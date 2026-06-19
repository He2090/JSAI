using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;

namespace JSAI.WinApp;

internal sealed class InitialServerSetupForm : Form
{
    private readonly ComboBox _schemeComboBox = new();
    private readonly TextBox _hostTextBox = new();
    private readonly NumericUpDown _portNumeric = new();
    private readonly Label _previewLabel = new();
    private readonly Label _statusLabel = new();
    private readonly Button _saveButton = new();
    private readonly Button _cancelButton = new();

    public InitialServerSetupForm(AppServerConfig config)
    {
        Text = "首次启动配置服务器";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Width = 520;
        Height = 340;
        BackColor = Color.FromArgb(20, 24, 34);
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);

        ServerBaseUrl = config.ApiBaseUrl;
        BuildLayout();
        LoadFromConfig(config);
        UpdatePreview();
    }

    public string ServerBaseUrl { get; private set; } = AppServerConfig.NormalizeBaseUrl(null);

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(20),
            BackColor = Color.Transparent
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));

        var title = new Label
        {
            Dock = DockStyle.Fill,
            Text = "客户端首次安装，请先配置服务器地址和端口",
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(title, 0, 0);

        var hint = new Label
        {
            Dock = DockStyle.Fill,
            Text = "建议填写服务端所在机器的 IP 地址。配置保存后，登录和自动更新都会使用这个地址。",
            ForeColor = Color.FromArgb(184, 196, 218),
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(hint, 0, 1);

        root.Controls.Add(CreateEndpointRow(), 0, 2);

        _previewLabel.Dock = DockStyle.Fill;
        _previewLabel.ForeColor = Color.FromArgb(255, 208, 115);
        _previewLabel.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(_previewLabel, 0, 3);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.ForeColor = Color.FromArgb(255, 148, 148);
        _statusLabel.TextAlign = ContentAlignment.TopLeft;
        root.Controls.Add(_statusLabel, 0, 4);

        var actionRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };

        ConfigureActionButton(_saveButton, "保存并继续", Color.FromArgb(255, 122, 0));
        _saveButton.Click += (_, _) => SaveAndClose();
        actionRow.Controls.Add(_saveButton);

        ConfigureActionButton(_cancelButton, "退出", Color.FromArgb(78, 86, 104));
        _cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        actionRow.Controls.Add(_cancelButton);
        root.Controls.Add(actionRow, 0, 5);

        Controls.Add(root);
    }

    private Control CreateEndpointRow()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));

        row.Controls.Add(CreateCaption("协议"), 0, 0);
        ConfigureComboBox(_schemeComboBox);
        _schemeComboBox.Items.AddRange(["https", "http"]);
        _schemeComboBox.SelectedIndexChanged += (_, _) => UpdatePreview();
        row.Controls.Add(_schemeComboBox, 1, 0);

        row.Controls.Add(CreateCaption("地址"), 2, 0);
        ConfigureTextBox(_hostTextBox);
        _hostTextBox.TextChanged += (_, _) => UpdatePreview();
        row.Controls.Add(_hostTextBox, 3, 0);

        row.Controls.Add(CreateCaption("端口"), 4, 0);
        _portNumeric.Minimum = 1;
        _portNumeric.Maximum = 65535;
        _portNumeric.Dock = DockStyle.Fill;
        _portNumeric.BackColor = Color.FromArgb(18, 22, 32);
        _portNumeric.ForeColor = Color.White;
        _portNumeric.ValueChanged += (_, _) => UpdatePreview();
        row.Controls.Add(_portNumeric, 5, 0);

        return row;
    }

    private void LoadFromConfig(AppServerConfig config)
    {
        try
        {
            var uri = new Uri(AppServerConfig.NormalizeBaseUrl(config.ApiBaseUrl));
            _schemeComboBox.SelectedItem = uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ? "http" : "https";
            _hostTextBox.Text = uri.Host;
            _portNumeric.Value = uri.Port > 0 ? uri.Port : (uri.Scheme == "http" ? 5157 : 7157);
        }
        catch
        {
            _schemeComboBox.SelectedItem = "http";
            _hostTextBox.Text = "127.0.0.1";
            _portNumeric.Value = 5157;
        }

        if (_schemeComboBox.SelectedIndex < 0)
        {
            _schemeComboBox.SelectedIndex = 0;
        }
    }

    private void UpdatePreview()
    {
        var scheme = _schemeComboBox.SelectedItem?.ToString() ?? "http";
        var host = _hostTextBox.Text.Trim();
        var port = (int)_portNumeric.Value;
        if (string.IsNullOrWhiteSpace(host))
        {
            _previewLabel.Text = "预览：请先填写服务器地址。";
            return;
        }

        ServerBaseUrl = $"{scheme}://{host}:{port}";
        _previewLabel.Text = $"当前将连接：{ServerBaseUrl}";
    }

    private void SaveAndClose()
    {
        var host = _hostTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            _statusLabel.Text = "请填写服务器地址。";
            return;
        }

        ServerBaseUrl = AppServerConfig.NormalizeBaseUrl($"{_schemeComboBox.SelectedItem}://{host}:{(int)_portNumeric.Value}");

        try
        {
            var config = new AppServerConfig
            {
                ApiBaseUrl = ServerBaseUrl,
                ConfigPath = AppServerConfig.GetConfigPath(AppDomain.CurrentDomain.BaseDirectory),
                IsConfigured = true
            };
            config.Save();
            SaveUpdateConfig(ServerBaseUrl);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"保存失败：{ex.Message}";
        }
    }

    private static void SaveUpdateConfig(string serverBaseUrl)
    {
        var updateConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update-config.json");
        var payload = new
        {
            manifestUrl = AppServerConfig.BuildManifestUrl(serverBaseUrl),
            channel = "stable",
            requestTimeoutSeconds = 8
        };
        File.WriteAllText(updateConfigPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }));
    }

    private static Label CreateCaption(string text)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = text,
            ForeColor = Color.FromArgb(208, 216, 235),
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static void ConfigureTextBox(TextBox textBox)
    {
        textBox.Dock = DockStyle.Fill;
        textBox.BackColor = Color.FromArgb(18, 22, 32);
        textBox.ForeColor = Color.White;
        textBox.BorderStyle = BorderStyle.FixedSingle;
    }

    private static void ConfigureComboBox(ComboBox comboBox)
    {
        comboBox.Dock = DockStyle.Fill;
        comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBox.FlatStyle = FlatStyle.Flat;
        comboBox.BackColor = Color.FromArgb(18, 22, 32);
        comboBox.ForeColor = Color.White;
    }

    private static void ConfigureActionButton(Button button, string text, Color backColor)
    {
        button.Text = text;
        button.Width = 132;
        button.Height = 38;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = backColor;
        button.ForeColor = Color.White;
        button.Margin = new Padding(10, 8, 0, 0);
    }
}
