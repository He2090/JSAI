using System;
using System.Drawing;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public sealed class ProjectLauncherForm : Form
    {
        public ProjectLaunchMode SelectedMode { get; private set; }

        public ProjectLauncherForm()
        {
            Text = "创建项目";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(760, 360);
            BackColor = Color.FromArgb(16, 20, 29);
            ForeColor = Color.White;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Padding = new Padding(24),
                BackColor = BackColor,
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var title = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                Text = "请选择项目模式",
                Font = new Font("Microsoft YaHei", 16F, FontStyle.Bold, GraphicsUnit.Point),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.White,
            };

            var subtitle = new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                Text = "AI 动漫项目会进入当前节点工作台，其余三种模式会打开快捷创作窗口。",
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(170, 180, 198),
            };

            var header = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
            };
            header.Controls.Add(subtitle);
            header.Controls.Add(title);
            subtitle.Top = title.Bottom + 4;
            root.Controls.Add(header, 0, 0);
            root.SetColumnSpan(header, 2);

            var choices = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 18, 0, 18),
            };
            choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            choices.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            choices.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            choices.Controls.Add(CreateModeCard("AI 动漫项目", "使用当前节点式创作工作台。", ProjectLaunchMode.AiAnimeProject), 0, 0);
            choices.Controls.Add(CreateModeCard("文生图", "输入提示词，调用本地优先的图片生成链路。", ProjectLaunchMode.TextToImage), 1, 0);
            choices.Controls.Add(CreateModeCard("文生视频", "输入提示词，调用本地优先的视频生成链路。", ProjectLaunchMode.TextToVideo), 0, 1);
            choices.Controls.Add(CreateModeCard("文图生视频", "上传参考图并输入提示词，直接生成视频。", ProjectLaunchMode.TextImageToVideo), 1, 1);

            root.Controls.Add(choices, 0, 1);
            root.SetColumnSpan(choices, 2);

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Color.Transparent,
                AutoSize = true,
            };

            footer.Controls.Add(CreateFooterButton("取消", DialogResult.Cancel, null, Color.FromArgb(60, 68, 84), Color.White));
            footer.Controls.Add(CreateFooterButton("载入项目", DialogResult.OK, ProjectLaunchMode.LoadProject, Color.FromArgb(36, 56, 94), Color.White));

            root.Controls.Add(footer, 0, 2);
            root.SetColumnSpan(footer, 2);

            Controls.Add(root);
        }

        private Control CreateModeCard(string title, string subtitle, ProjectLaunchMode mode)
        {
            var button = new Button
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 16, 16),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(22, 28, 40),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(61, 76, 104);
            button.FlatAppearance.BorderSize = 1;
            button.Click += (_, _) =>
            {
                SelectedMode = mode;
                DialogResult = DialogResult.OK;
                Close();
            };

            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(18),
            };
            var titleLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                Text = title,
                Font = new Font("Microsoft YaHei", 14F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            var subtitleLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = subtitle,
                Font = new Font("Microsoft YaHei", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(175, 186, 206),
                TextAlign = ContentAlignment.TopLeft,
            };
            panel.Controls.Add(subtitleLabel);
            panel.Controls.Add(titleLabel);
            button.Controls.Add(panel);
            return button;
        }

        private Button CreateFooterButton(string text, DialogResult result, ProjectLaunchMode? mode, Color backColor, Color foreColor)
        {
            var button = new Button
            {
                Text = text,
                Width = 108,
                Height = 34,
                DialogResult = result,
                BackColor = backColor,
                ForeColor = foreColor,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(10, 0, 0, 0),
            };
            button.FlatAppearance.BorderSize = 0;
            if (mode.HasValue)
            {
                button.Click += (_, _) => SelectedMode = mode.Value;
            }

            return button;
        }
    }
}
