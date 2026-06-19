using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace JSAI.WinApp;

internal sealed class MachineActivationForm : Form
{
    private readonly AppServerConfig _serverConfig;
    private readonly MachineActivationStatus _status;
    private readonly Label _statusLabel = new();
    private readonly TextBox _machineCodeTextBox = new();
    private readonly TextBox _registrationCodeTextBox = new();

    public MachineActivationForm(AppServerConfig serverConfig, MachineActivationStatus status)
    {
        _serverConfig = serverConfig;
        _status = status;
        ResolvedSession = MachineActivationService.CreateUnlicensedSession(status.MachineCode);

        Text = "软件注册码";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 420);
        MinimumSize = new Size(560, 420);
        BackColor = Color.FromArgb(18, 23, 35);
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        BuildLayout();
    }

    public UserSessionResponse? ResolvedSession { get; private set; }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 22, 24, 20),
            ColumnCount = 1,
            RowCount = 9,
            BackColor = Color.Transparent,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));

        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = "本机注册码授权",
            Font = new Font(Font.FontFamily, 18F, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var hint = new Label
        {
            Dock = DockStyle.Fill,
            Text = "复制机器码，到服务端后台的“注册码生成”输入机器码生成注册码。未激活可以进入软件，但不能保存工程。",
            ForeColor = Color.FromArgb(190, 205, 232),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        _machineCodeTextBox.Dock = DockStyle.Fill;
        _machineCodeTextBox.ReadOnly = true;
        _machineCodeTextBox.Text = _status.MachineCode;
        ConfigureTextBox(_machineCodeTextBox);

        _registrationCodeTextBox.Dock = DockStyle.Fill;
        _registrationCodeTextBox.PlaceholderText = "粘贴网站生成的注册码";
        ConfigureTextBox(_registrationCodeTextBox);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.ForeColor = Color.FromArgb(255, 208, 115);
        _statusLabel.TextAlign = ContentAlignment.TopLeft;
        _statusLabel.Text = BuildStatusText();

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(hint, 0, 1);
        root.Controls.Add(CreateLabel("机器码"), 0, 2);
        root.Controls.Add(CreateTextBoxRow(_machineCodeTextBox, CreateButton("复制机器码", Color.FromArgb(57, 126, 222), CopyMachineCode)), 0, 3);
        root.Controls.Add(CreateServerRow(), 0, 4);
        root.Controls.Add(CreateLabel("注册码"), 0, 5);
        root.Controls.Add(_registrationCodeTextBox, 0, 6);
        root.Controls.Add(_statusLabel, 0, 7);
        root.Controls.Add(CreateActionRow(), 0, 8);

        Controls.Add(root);
    }

    private string BuildStatusText()
    {
        var cpuText = _status.HasCpuId ? "CPU序列号已读取" : "CPU序列号未读取，已使用备用硬件信息";
        var diskText = _status.HasDiskSerial ? "硬盘序列号已读取" : "硬盘序列号未读取，已使用备用硬件信息";
        return $"{_status.Message}\r\n{cpuText}；{diskText}。";
    }

    private Control CreateServerRow()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty,
            BackColor = Color.Transparent,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136F));

        var label = new Label
        {
            Dock = DockStyle.Fill,
            Text = $"注册码生成网站：{_serverConfig.ApiBaseUrl.TrimEnd('/')}/admin",
            ForeColor = Color.FromArgb(155, 177, 216),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };

        row.Controls.Add(label, 0, 0);
        row.Controls.Add(CreateButton("打开网站", Color.FromArgb(64, 78, 112), OpenAdminWebsite), 1, 0);
        return row;
    }

    private Control CreateTextBoxRow(TextBox textBox, Button button)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty,
            BackColor = Color.Transparent,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136F));
        row.Controls.Add(textBox, 0, 0);
        row.Controls.Add(button, 1, 0);
        return row;
    }

    private Control CreateActionRow()
    {
        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
        };

        row.Controls.Add(CreateButton("激活并进入", Color.FromArgb(255, 122, 0), ActivateAndEnter, 126));
        row.Controls.Add(CreateButton("进入软件（不能保存）", Color.FromArgb(64, 78, 112), ContinueWithoutActivation, 172));
        return row;
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            ForeColor = Color.FromArgb(214, 224, 245),
            TextAlign = ContentAlignment.MiddleLeft,
        };
    }

    private static Button CreateButton(string text, Color color, Action action, int width = 120)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 34,
            BackColor = color,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(8, 0, 0, 0),
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => action();
        return button;
    }

    private static void ConfigureTextBox(TextBox textBox)
    {
        textBox.Height = 32;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.BackColor = Color.FromArgb(12, 17, 28);
        textBox.ForeColor = Color.White;
        textBox.Margin = Padding.Empty;
    }

    private void CopyMachineCode()
    {
        Clipboard.SetText(_status.MachineCode);
        _statusLabel.Text = "机器码已复制。请到服务端后台生成注册码。";
        _statusLabel.ForeColor = Color.FromArgb(128, 224, 170);
    }

    private void OpenAdminWebsite()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"{_serverConfig.ApiBaseUrl.TrimEnd('/')}/admin",
                UseShellExecute = true,
            });
        }
        catch
        {
            _statusLabel.Text = "无法自动打开网站，请手动访问：" + $"{_serverConfig.ApiBaseUrl.TrimEnd('/')}/admin";
            _statusLabel.ForeColor = Color.FromArgb(255, 148, 148);
        }
    }

    private void ActivateAndEnter()
    {
        if (MachineActivationService.TryActivate(_registrationCodeTextBox.Text, out var status, out var message))
        {
            ResolvedSession = MachineActivationService.CreateSession(status);
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        _statusLabel.Text = message;
        _statusLabel.ForeColor = Color.FromArgb(255, 148, 148);
        _registrationCodeTextBox.Focus();
        _registrationCodeTextBox.SelectAll();
    }

    private void ContinueWithoutActivation()
    {
        ResolvedSession = MachineActivationService.CreateUnlicensedSession(_status.MachineCode);
        DialogResult = DialogResult.Ignore;
        Close();
    }
}
