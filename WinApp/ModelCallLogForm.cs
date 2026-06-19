using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public sealed class ModelCallLogForm : Form
    {
        private readonly TextBox _logTextBox = new();
        private readonly Button _refreshButton = new();
        private readonly Button _openFileButton = new();
        private readonly Button _openFolderButton = new();

        public ModelCallLogForm()
        {
            InitializeLayout();
            Load += (_, _) => ReloadLog();
        }

        private void InitializeLayout()
        {
            Text = "模型调用日志";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(980, 660);
            MinimumSize = new Size(840, 540);
            BackColor = Color.FromArgb(18, 22, 30);
            ForeColor = Color.White;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(12),
                BackColor = BackColor,
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = false,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };

            ConfigureButton(_refreshButton, "刷新", (_, _) => ReloadLog(), Color.FromArgb(52, 73, 106));
            ConfigureButton(_openFileButton, "打开日志文件", (_, _) => OpenPath(ModelCallLogService.LogFilePath), Color.FromArgb(52, 73, 106));
            ConfigureButton(_openFolderButton, "打开日志目录", (_, _) => OpenPath(ModelCallLogService.LogFolderPath), Color.FromArgb(255, 122, 0));

            toolbar.Controls.Add(_refreshButton);
            toolbar.Controls.Add(_openFileButton);
            toolbar.Controls.Add(_openFolderButton);

            _logTextBox.Dock = DockStyle.Fill;
            _logTextBox.Multiline = true;
            _logTextBox.ReadOnly = true;
            _logTextBox.ScrollBars = ScrollBars.Both;
            _logTextBox.WordWrap = false;
            _logTextBox.BorderStyle = BorderStyle.FixedSingle;
            _logTextBox.BackColor = Color.FromArgb(14, 18, 24);
            _logTextBox.ForeColor = Color.FromArgb(230, 234, 242);
            _logTextBox.Font = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point);

            root.Controls.Add(toolbar, 0, 0);
            root.Controls.Add(_logTextBox, 0, 1);
            Controls.Add(root);
        }

        private void ReloadLog()
        {
            try
            {
                var path = ModelCallLogService.LogFilePath;
                _logTextBox.Text = File.Exists(path)
                    ? File.ReadAllText(path)
                    : "暂无成功模型调用日志。";
                _logTextBox.SelectionStart = _logTextBox.TextLength;
                _logTextBox.ScrollToCaret();
            }
            catch (Exception ex)
            {
                _logTextBox.Text = $"读取日志失败：{ex.Message}";
            }
        }

        private static void OpenPath(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                var targetPath = File.Exists(path) || Directory.Exists(path)
                    ? path
                    : Path.GetDirectoryName(path) ?? path;

                if (!Directory.Exists(targetPath) && !File.Exists(targetPath))
                {
                    Directory.CreateDirectory(targetPath);
                }

                Process.Start(new ProcessStartInfo(targetPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开失败：{ex.Message}", "模型调用日志", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static void ConfigureButton(Button button, string text, EventHandler clickHandler, Color backColor)
        {
            button.Text = text;
            button.Width = 118;
            button.Height = 34;
            button.Margin = new Padding(0, 0, 8, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(72, 87, 116);
            button.BackColor = backColor;
            button.ForeColor = Color.White;
            button.UseVisualStyleBackColor = false;
            button.Click += clickHandler;
        }
    }
}
