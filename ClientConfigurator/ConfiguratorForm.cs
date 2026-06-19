using System.Drawing;
using System.Windows.Forms;

namespace JSAI.ClientConfigurator;

internal sealed class ConfiguratorForm : Form
{
    private readonly TextBox _targetDirectoryTextBox = new();
    private readonly TextBox _serverBaseUrlTextBox = new();
    private readonly TextBox _manifestUrlTextBox = new();
    private readonly NumericUpDown _timeoutNumeric = new();
    private readonly Label _statusLabel = new();

    public ConfiguratorForm(string? targetDirectory = null)
    {
        Text = "JSAI 客户端设置程序";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 720;
        Height = 420;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Color.FromArgb(20, 24, 34);
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);

        BuildLayout();

        var defaultDirectory = string.IsNullOrWhiteSpace(targetDirectory)
            ? Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "WinApp", "bin", "Debug", "net8.0-windows")
            : targetDirectory;
        _targetDirectoryTextBox.Text = Path.GetFullPath(defaultDirectory);
        LoadExistingConfig();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(20),
            BackColor = Color.Transparent,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "设置客户端发布目录中的服务器地址与更新地址",
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);

        root.Controls.Add(CreatePathRow(), 0, 1);
        root.Controls.Add(CreateFieldRow("服务器 API 地址", _serverBaseUrlTextBox), 0, 2);
        root.Controls.Add(CreateFieldRow("更新清单地址", _manifestUrlTextBox), 0, 3);
        root.Controls.Add(CreateTimeoutRow(), 0, 4);
        root.Controls.Add(CreateActionRow(), 0, 5);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.ForeColor = Color.FromArgb(255, 208, 115);
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(_statusLabel, 0, 6);

        Controls.Add(root);
    }

    private Control CreatePathRow()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));

        panel.Controls.Add(CreateCaption("客户端目录"), 0, 0);

        ConfigureTextBox(_targetDirectoryTextBox);
        panel.Controls.Add(_targetDirectoryTextBox, 1, 0);

        var browseButton = CreateButton("浏览目录", Color.FromArgb(57, 126, 222));
        browseButton.Click += (_, _) => BrowseDirectory();
        panel.Controls.Add(browseButton, 2, 0);
        return panel;
    }

    private Control CreateFieldRow(string label, TextBox textBox)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        panel.Controls.Add(CreateCaption(label), 0, 0);
        ConfigureTextBox(textBox);
        panel.Controls.Add(textBox, 1, 0);
        return panel;
    }

    private Control CreateTimeoutRow()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        panel.Controls.Add(CreateCaption("超时秒数"), 0, 0);

        _timeoutNumeric.Minimum = 3;
        _timeoutNumeric.Maximum = 60;
        _timeoutNumeric.Value = 8;
        _timeoutNumeric.Dock = DockStyle.Fill;
        _timeoutNumeric.BackColor = Color.FromArgb(15, 20, 30);
        _timeoutNumeric.ForeColor = Color.White;
        panel.Controls.Add(_timeoutNumeric, 1, 0);

        var autoBuildButton = CreateButton("按服务器地址生成", Color.FromArgb(92, 98, 112));
        autoBuildButton.Click += (_, _) =>
        {
            var baseUrl = ClientPackageConfig.NormalizeServerBaseUrl(_serverBaseUrlTextBox.Text);
            _serverBaseUrlTextBox.Text = baseUrl;
            _manifestUrlTextBox.Text = ClientPackageConfig.BuildManifestUrl(baseUrl);
        };
        panel.Controls.Add(autoBuildButton, 3, 0);
        return panel;
    }

    private Control CreateActionRow()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
        };

        var saveButton = CreateButton("保存到客户端目录", Color.FromArgb(255, 122, 0));
        saveButton.Click += (_, _) => SaveConfig();
        panel.Controls.Add(saveButton);

        var loadButton = CreateButton("读取现有配置", Color.FromArgb(57, 126, 222));
        loadButton.Click += (_, _) => LoadExistingConfig();
        panel.Controls.Add(loadButton);
        return panel;
    }

    private void BrowseDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择客户端发布目录（包含 WinApp.exe 的目录）",
            SelectedPath = Directory.Exists(_targetDirectoryTextBox.Text) ? _targetDirectoryTextBox.Text : AppDomain.CurrentDomain.BaseDirectory,
        };
        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        _targetDirectoryTextBox.Text = dialog.SelectedPath;
        LoadExistingConfig();
    }

    private void LoadExistingConfig()
    {
        try
        {
            var config = ClientPackageConfig.Load(_targetDirectoryTextBox.Text.Trim());
            _serverBaseUrlTextBox.Text = config.ApiBaseUrl;
            _manifestUrlTextBox.Text = config.ManifestUrl;
            _timeoutNumeric.Value = Math.Max(_timeoutNumeric.Minimum, Math.Min(_timeoutNumeric.Maximum, config.RequestTimeoutSeconds));
            SetStatus("已读取客户端配置。", Color.FromArgb(128, 224, 170));
        }
        catch (Exception ex)
        {
            SetStatus($"读取配置失败：{ex.Message}", Color.FromArgb(255, 148, 148));
        }
    }

    private void SaveConfig()
    {
        try
        {
            var targetDirectory = _targetDirectoryTextBox.Text.Trim();
            var serverBaseUrl = ClientPackageConfig.NormalizeServerBaseUrl(_serverBaseUrlTextBox.Text);
            var manifestUrl = string.IsNullOrWhiteSpace(_manifestUrlTextBox.Text)
                ? ClientPackageConfig.BuildManifestUrl(serverBaseUrl)
                : _manifestUrlTextBox.Text.Trim();

            var config = new ClientPackageConfig
            {
                ApiBaseUrl = serverBaseUrl,
                ManifestUrl = manifestUrl,
                Channel = "stable",
                RequestTimeoutSeconds = (int)_timeoutNumeric.Value,
            };
            config.Save(targetDirectory);
            _serverBaseUrlTextBox.Text = config.ApiBaseUrl;
            _manifestUrlTextBox.Text = config.ManifestUrl;
            SetStatus("客户端服务器地址与更新地址已保存。", Color.FromArgb(128, 224, 170));
        }
        catch (Exception ex)
        {
            SetStatus($"保存失败：{ex.Message}", Color.FromArgb(255, 148, 148));
        }
    }

    private static Label CreateCaption(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            ForeColor = Color.FromArgb(208, 216, 235),
            TextAlign = ContentAlignment.MiddleLeft,
        };
    }

    private static Button CreateButton(string text, Color backColor)
    {
        return new Button
        {
            Text = text,
            Width = 148,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
            BackColor = backColor,
            ForeColor = Color.White,
            Margin = new Padding(8, 6, 0, 0),
        };
    }

    private static void ConfigureTextBox(TextBox textBox)
    {
        textBox.Dock = DockStyle.Fill;
        textBox.BackColor = Color.FromArgb(15, 20, 30);
        textBox.ForeColor = Color.White;
        textBox.BorderStyle = BorderStyle.FixedSingle;
    }

    private void SetStatus(string text, Color color)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = color;
    }
}
