using System.Diagnostics;
using System.Drawing;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace JSAI.Installer;

internal sealed class InstallerForm : Form
{
    private static readonly byte[] EmbeddedPayloadMarker = Encoding.ASCII.GetBytes("JSAI_PAYLOAD_V1");

    private readonly TextBox _installDirectoryTextBox = new();
    private readonly CheckBox _desktopShortcutCheckBox = new();
    private readonly CheckBox _launchAfterInstallCheckBox = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Label _statusLabel = new();
    private readonly Button _installButton;
    private readonly Button _cancelButton = new();
    private readonly Button _browseButton;

    private bool _installing;
    private string? _materializedPayloadPath;

    public InstallerForm()
    {
        Text = InstallerProfile.WindowTitle;
        StartPosition = FormStartPosition.CenterScreen;
        Width = 780;
        Height = 560;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        BackColor = Color.FromArgb(18, 23, 34);
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);

        _browseButton = CreatePrimaryButton("浏览...", Color.FromArgb(67, 122, 214));
        _browseButton.Dock = DockStyle.Fill;
        _browseButton.Click += (_, _) => BrowseInstallDirectory();

        _installButton = CreatePrimaryButton("开始安装", Color.FromArgb(255, 122, 0));
        _installButton.Dock = DockStyle.Fill;
        _installButton.Click += async (_, _) => await InstallAsync();

        BuildLayout();

        _installDirectoryTextBox.Text = InstallerProfile.DefaultInstallDirectory;
        _desktopShortcutCheckBox.Checked = true;
        _launchAfterInstallCheckBox.Checked = true;
        SetStatus($"准备安装{InstallerProfile.TargetDisplayName}。");
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        TryDeleteMaterializedPayload();
        base.OnFormClosed(e);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(24),
            BackColor = Color.Transparent,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68F));

        root.Controls.Add(CreateHeaderPanel(), 0, 0);
        root.Controls.Add(CreateDirectoryPanel(), 0, 1);
        root.Controls.Add(CreateOptionsPanel(), 0, 2);
        root.Controls.Add(CreateProgressPanel(), 0, 3);
        root.Controls.Add(CreateActionPanel(), 0, 4);

        Controls.Add(root);
    }

    private Control CreateHeaderPanel()
    {
        var panel = CreateCardPanel(18);

        panel.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 38,
            Text = InstallerProfile.HeaderTitle,
            Font = new Font("Microsoft YaHei UI", 19F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
        });

        panel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = InstallerProfile.HeaderDescription,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(189, 202, 224),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 40, 0, 0),
        });

        return panel;
    }

    private Control CreateDirectoryPanel()
    {
        var panel = CreateCardPanel();
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Color.Transparent,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

        var caption = CreateCaptionLabel("安装目录");
        table.Controls.Add(caption, 0, 0);
        table.SetColumnSpan(caption, 2);

        _installDirectoryTextBox.Dock = DockStyle.Fill;
        _installDirectoryTextBox.BackColor = Color.FromArgb(14, 19, 29);
        _installDirectoryTextBox.ForeColor = Color.White;
        _installDirectoryTextBox.BorderStyle = BorderStyle.FixedSingle;
        _installDirectoryTextBox.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        table.Controls.Add(_installDirectoryTextBox, 0, 1);
        table.Controls.Add(_browseButton, 1, 1);

        panel.Controls.Add(table);
        return panel;
    }

    private Control CreateOptionsPanel()
    {
        var panel = CreateCardPanel();
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0),
            Margin = new Padding(0),
        };

        _desktopShortcutCheckBox.Text = $"创建桌面快捷方式（{InstallerProfile.ShortcutBaseName}）";
        _desktopShortcutCheckBox.ForeColor = Color.White;
        _desktopShortcutCheckBox.AutoSize = true;
        _desktopShortcutCheckBox.Margin = new Padding(0, 0, 0, 12);

        _launchAfterInstallCheckBox.Text = $"安装完成后立即启动{InstallerProfile.TargetDisplayName}";
        _launchAfterInstallCheckBox.ForeColor = Color.White;
        _launchAfterInstallCheckBox.AutoSize = true;
        _launchAfterInstallCheckBox.Margin = new Padding(0);

        flow.Controls.Add(_desktopShortcutCheckBox);
        flow.Controls.Add(_launchAfterInstallCheckBox);
        panel.Controls.Add(flow);
        return panel;
    }

    private Control CreateProgressPanel()
    {
        var panel = CreateCardPanel();
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        layout.Controls.Add(CreateCaptionLabel("安装进度"), 0, 0);

        _progressBar.Dock = DockStyle.Fill;
        _progressBar.Minimum = 0;
        _progressBar.Maximum = 100;
        _progressBar.Style = ProgressBarStyle.Continuous;
        layout.Controls.Add(_progressBar, 0, 1);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.ForeColor = Color.FromArgb(192, 203, 220);
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(_statusLabel, 0, 2);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control CreateActionPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            BackColor = Color.Transparent,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));

        _cancelButton.Text = "取消";
        _cancelButton.Dock = DockStyle.Fill;
        _cancelButton.FlatStyle = FlatStyle.Flat;
        _cancelButton.FlatAppearance.BorderColor = Color.FromArgb(86, 99, 123);
        _cancelButton.FlatAppearance.BorderSize = 1;
        _cancelButton.BackColor = Color.FromArgb(41, 49, 66);
        _cancelButton.ForeColor = Color.White;
        _cancelButton.Click += (_, _) => Close();

        panel.Controls.Add(_cancelButton, 1, 0);
        panel.Controls.Add(_installButton, 2, 0);
        return panel;
    }

    private static Panel CreateCardPanel(int padding = 16)
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(26, 33, 47),
            Padding = new Padding(padding),
            Margin = new Padding(0, 0, 0, 16),
        };
    }

    private static Label CreateCaptionLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
        };
    }

    private static Button CreatePrimaryButton(string text, Color color)
    {
        var button = new Button
        {
            Text = text,
            Height = 42,
            FlatStyle = FlatStyle.Flat,
            BackColor = color,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point),
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private void BrowseInstallDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = $"选择{InstallerProfile.TargetDisplayName}安装目录",
            SelectedPath = Directory.Exists(_installDirectoryTextBox.Text)
                ? _installDirectoryTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _installDirectoryTextBox.Text = dialog.SelectedPath;
        }
    }

    private async Task InstallAsync()
    {
        if (_installing)
        {
            return;
        }

        var installDirectory = (_installDirectoryTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            MessageBox.Show(this, "请先选择安装目录。", "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _installing = true;
        ToggleInteractiveControls(false);
        SetStatus("正在准备安装...");
        _progressBar.Value = 0;

        try
        {
            var payloadPath = ResolvePayloadZipPath();
            await Task.Run(() => ExtractPayload(payloadPath, installDirectory));

            if (_desktopShortcutCheckBox.Checked)
            {
                CreateDesktopShortcut(installDirectory);
            }

            _progressBar.Value = 100;
            SetStatus("安装完成。");

            if (_launchAfterInstallCheckBox.Checked)
            {
                var exePath = Path.Combine(installDirectory, InstallerProfile.MainExecutableName);
                if (File.Exists(exePath))
                {
                    Process.Start(new ProcessStartInfo(exePath)
                    {
                        UseShellExecute = true,
                        WorkingDirectory = installDirectory,
                    });
                }
            }

            MessageBox.Show(
                this,
                InstallerProfile.CompletionMessage,
                "安装完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus($"安装失败：{ex.Message}");
        }
        finally
        {
            _installing = false;
            ToggleInteractiveControls(true);
            TryDeleteMaterializedPayload();
        }
    }

    private string ResolvePayloadZipPath()
    {
        var adjacentPayload = Path.Combine(AppContext.BaseDirectory, "client-payload.zip");
        if (File.Exists(adjacentPayload))
        {
            return adjacentPayload;
        }

        if (!string.IsNullOrWhiteSpace(_materializedPayloadPath) && File.Exists(_materializedPayloadPath))
        {
            return _materializedPayloadPath;
        }

        _materializedPayloadPath = MaterializeEmbeddedPayload();
        return _materializedPayloadPath;
    }

    private static string MaterializeEmbeddedPayload()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            throw new FileNotFoundException("未找到安装器主程序，无法读取内置安装包。");
        }

        using var stream = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var footerSize = sizeof(long) + EmbeddedPayloadMarker.Length;
        if (stream.Length <= footerSize)
        {
            throw new FileNotFoundException("安装器内未发现安装包，请重新下载安装包。");
        }

        stream.Seek(-footerSize, SeekOrigin.End);
        Span<byte> lengthBytes = stackalloc byte[sizeof(long)];
        stream.ReadExactly(lengthBytes);
        var payloadLength = BitConverter.ToInt64(lengthBytes);

        var markerBytes = new byte[EmbeddedPayloadMarker.Length];
        stream.ReadExactly(markerBytes);
        if (!markerBytes.SequenceEqual(EmbeddedPayloadMarker))
        {
            throw new FileNotFoundException("安装器内未发现安装包，请重新下载安装包。");
        }

        if (payloadLength <= 0 || payloadLength > stream.Length - footerSize)
        {
            throw new InvalidOperationException("安装器内置安装包损坏，请重新下载。");
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), "JSAIInstaller");
        Directory.CreateDirectory(tempDirectory);

        var payloadPath = Path.Combine(tempDirectory, $"{InstallerProfile.PayloadFilePrefix}-{Process.GetCurrentProcess().Id}.zip");
        stream.Seek(-(footerSize + payloadLength), SeekOrigin.End);

        using var output = new FileStream(payloadPath, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.CopyRangeTo(output, payloadLength);
        output.Flush(true);

        return payloadPath;
    }

    private void ExtractPayload(string payloadPath, string installDirectory)
    {
        Directory.CreateDirectory(installDirectory);

        using var archive = ZipFile.OpenRead(payloadPath);
        var entries = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.FullName)).ToList();
        if (entries.Count == 0)
        {
            throw new InvalidOperationException("安装包内容为空。");
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var destinationPath = Path.Combine(installDirectory, entry.FullName);
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            entry.ExtractToFile(destinationPath, true);

            var progress = Math.Max(1, (int)Math.Round((index + 1d) * 100d / entries.Count));
            BeginInvoke(new Action(() =>
            {
                _progressBar.Value = Math.Min(100, progress);
                SetStatus($"正在安装：{entry.Name}");
            }));
        }
    }

    private static void CreateDesktopShortcut(string installDirectory)
    {
        var targetPath = Path.Combine(installDirectory, InstallerProfile.MainExecutableName);
        if (!File.Exists(targetPath))
        {
            return;
        }

        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var shortcutPath = Path.Combine(desktopPath, InstallerProfile.ShortcutBaseName + ".lnk");
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
        {
            return;
        }

        var shell = Activator.CreateInstance(shellType);
        try
        {
            if (shell == null)
            {
                return;
            }

            var shortcut = shell.GetType().InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                null,
                shell,
                new object[] { shortcutPath });

            if (shortcut == null)
            {
                return;
            }

            shortcut.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
            shortcut.GetType().InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { installDirectory });
            shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }
        finally
        {
            if (shell != null)
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private void ToggleInteractiveControls(bool enabled)
    {
        _installDirectoryTextBox.Enabled = enabled;
        _browseButton.Enabled = enabled;
        _desktopShortcutCheckBox.Enabled = enabled;
        _launchAfterInstallCheckBox.Enabled = enabled;
        _installButton.Enabled = enabled;
        _cancelButton.Enabled = enabled;
    }

    private void SetStatus(string message)
    {
        _statusLabel.Text = message;
    }

    private void TryDeleteMaterializedPayload()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_materializedPayloadPath) && File.Exists(_materializedPayloadPath))
            {
                File.Delete(_materializedPayloadPath);
            }
        }
        catch
        {
        }
    }
}

internal static class StreamExtensions
{
    public static void CopyRangeTo(this Stream source, Stream destination, long bytesToCopy)
    {
        var buffer = new byte[1024 * 128];
        long remaining = bytesToCopy;
        while (remaining > 0)
        {
            var chunkSize = (int)Math.Min(buffer.Length, remaining);
            var read = source.Read(buffer, 0, chunkSize);
            if (read <= 0)
            {
                throw new EndOfStreamException("读取安装包时提前结束。");
            }

            destination.Write(buffer, 0, read);
            remaining -= read;
        }
    }
}
