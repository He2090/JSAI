using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public sealed class CharacterDetailForm : Form
    {
        private readonly CharacterDesignEntry _entry;
        private PictureBox _expressionPictureBox = null!;
        private PictureBox _threeViewPictureBox = null!;

        public CharacterDetailForm(CharacterDesignEntry entry)
        {
            _entry = entry;
            Text = $"角色详情 - {entry.Name}";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(980, 700);
            Size = new Size(1120, 780);
            BackColor = Color.FromArgb(34, 36, 42);
            ForeColor = Color.WhiteSmoke;
            AutoScaleMode = AutoScaleMode.None;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = BackColor,
                Margin = Padding.Empty,
                Padding = new Padding(10),
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 345F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var leftPanel = BuildLeftPanel();
            var rightPanel = BuildRightPanel();

            root.Controls.Add(leftPanel, 0, 0);
            root.Controls.Add(rightPanel, 1, 0);

            Controls.Add(root);
            LoadImages();
        }

        private Control BuildLeftPanel()
        {
            var shell = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = new Padding(0, 0, 10, 0),
                BackColor = Color.FromArgb(34, 36, 42),
            };
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            _expressionPictureBox = CreatePreviewPictureBox();
            _threeViewPictureBox = CreatePreviewPictureBox();

            shell.Controls.Add(
                CreateImageSection("九宫格表情板", _entry.ExpressionSheetPath, _expressionPictureBox, highlight: true),
                0,
                0);
            shell.Controls.Add(
                CreateImageSection("三视图", _entry.ThreeViewSheetPath, _threeViewPictureBox, highlight: false),
                0,
                1);
            return shell;
        }

        private Control BuildRightPanel()
        {
            var container = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(28, 30, 36),
                Padding = new Padding(18, 16, 18, 16),
                Margin = Padding.Empty,
            };

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 8,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.Transparent,
            };

            content.Controls.Add(BuildHeader(), 0, 0);
            content.Controls.Add(CreateInfoSection("基础设定", "基础外形", _entry.BasicStats), 0, 1);
            content.Controls.Add(CreateInfoSection("性格特征", string.Empty, _entry.Personality), 0, 2);
            content.Controls.Add(CreateInfoSection("核心动机", string.Empty, _entry.Motivation), 0, 3);
            content.Controls.Add(CreateInfoSection("弱点 / 恐惧", string.Empty, _entry.Weakness), 0, 4);
            content.Controls.Add(CreateInfoSection("核心关系", string.Empty, _entry.Relationships), 0, 5);
            content.Controls.Add(CreateInfoSection("习惯与兴趣", string.Empty, _entry.Habits), 0, 6);
            content.Controls.Add(CreateInfoSection("生成提示词", string.Empty, BuildPromptInfo()), 0, 7);

            var scrollHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.Transparent,
            };
            scrollHost.Controls.Add(content);
            container.Controls.Add(scrollHost);
            return container;
        }

        private Control BuildHeader()
        {
            var shell = new Panel
            {
                Dock = DockStyle.Top,
                Height = 74,
                BackColor = Color.FromArgb(30, 32, 38),
                Padding = new Padding(16, 12, 16, 12),
                Margin = new Padding(0, 0, 0, 18),
            };

            var titleLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 28,
                ForeColor = Color.WhiteSmoke,
                Font = new Font("Microsoft YaHei", 15F, FontStyle.Bold, GraphicsUnit.Point),
                Text = _entry.Name,
                TextAlign = ContentAlignment.MiddleLeft,
            };

            var summaryLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(255, 190, 92),
                Font = new Font("Microsoft YaHei", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
                Text = BuildHeaderSummary(),
                TextAlign = ContentAlignment.MiddleLeft,
            };

            shell.Controls.Add(summaryLabel);
            shell.Controls.Add(titleLabel);
            return shell;
        }

        private Control CreateImageSection(string title, string filePath, PictureBox pictureBox, bool highlight)
        {
            var shell = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0, 0, 0, 14),
                Padding = Padding.Empty,
                BackColor = Color.Transparent,
            };
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));

            var titleLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(0, 232, 182),
                Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold, GraphicsUnit.Point),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = title,
            };

            var previewHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(18, 19, 24),
                Padding = new Padding(1),
                Margin = new Padding(0, 6, 0, 8),
            };

            var inner = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(15, 16, 21),
                Padding = new Padding(10, highlight ? 10 : 0, 10, 10),
            };

            if (highlight)
            {
                var accent = new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 4,
                    BackColor = Color.FromArgb(255, 122, 0),
                };
                inner.Controls.Add(pictureBox);
                inner.Controls.Add(accent);
            }
            else
            {
                inner.Controls.Add(pictureBox);
            }

            previewHost.Controls.Add(inner);

            var openButton = new Button
            {
                Dock = DockStyle.Left,
                Width = 92,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(74, 80, 96),
                ForeColor = Color.WhiteSmoke,
                Text = "打开文件",
                Enabled = !string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath),
                Margin = Padding.Empty,
            };
            openButton.FlatAppearance.BorderSize = 0;
            openButton.Click += (_, _) => OpenFile(filePath);

            var buttonHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
            };
            buttonHost.Controls.Add(openButton);

            shell.Controls.Add(titleLabel, 0, 0);
            shell.Controls.Add(previewHost, 0, 1);
            shell.Controls.Add(buttonHost, 0, 2);
            return shell;
        }

        private static PictureBox CreatePreviewPictureBox()
        {
            return new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(14, 15, 20),
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.None,
            };
        }

        private static Control CreateInfoSection(string title, string subTitle, string content)
        {
            var shell = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0, 0, 0, 16),
                Padding = Padding.Empty,
            };
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var titleLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(0, 232, 182),
                Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold, GraphicsUnit.Point),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = title,
            };

            var body = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.FromArgb(34, 36, 42),
                Padding = new Padding(14, 12, 14, 12),
                Margin = Padding.Empty,
            };

            var contentLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = string.IsNullOrWhiteSpace(subTitle) ? 1 : 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };

            if (!string.IsNullOrWhiteSpace(subTitle))
            {
                var subTitleLabel = new Label
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    ForeColor = Color.FromArgb(225, 231, 241),
                    Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold, GraphicsUnit.Point),
                    Margin = new Padding(0, 0, 0, 8),
                    Text = subTitle,
                };
                contentLayout.Controls.Add(subTitleLabel, 0, 0);
            }

            var displayContent = CharacterDesignEntry.LooksLikeRawStructuredText(content) ? string.Empty : content;
            var bodyLabel = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                MaximumSize = new Size(620, 0),
                ForeColor = Color.WhiteSmoke,
                Font = new Font("Microsoft YaHei", 10F, FontStyle.Regular, GraphicsUnit.Point),
                Text = string.IsNullOrWhiteSpace(displayContent) ? "暂无内容" : displayContent.Trim(),
            };

            contentLayout.Controls.Add(bodyLabel, 0, contentLayout.Controls.Count);
            body.Controls.Add(contentLayout);

            shell.Controls.Add(titleLabel, 0, 0);
            shell.Controls.Add(body, 0, 1);
            return shell;
        }

        private string BuildHeaderSummary()
        {
            var parts = new[]
            {
                _entry.BasicStats,
                _entry.Personality,
            }
            .Where(value => !string.IsNullOrWhiteSpace(value) && !CharacterDesignEntry.LooksLikeRawStructuredText(value))
            .Select(value => value.Trim());

            var summary = string.Join("，", parts);
            if (!string.IsNullOrWhiteSpace(summary))
            {
                return summary;
            }

            return string.IsNullOrWhiteSpace(_entry.CompactSummary) ? _entry.RoleType : _entry.CompactSummary;
        }

        private string BuildPromptInfo()
        {
            var parts = new[]
            {
                _entry.AppearancePrompt,
                _entry.ExpressionPrompt,
                _entry.ThreeViewPrompt,
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToList();

            return parts.Count == 0 ? string.Empty : string.Join(Environment.NewLine + Environment.NewLine, parts);
        }

        private void LoadImages()
        {
            _expressionPictureBox.Image = LoadImageCopy(_entry.ExpressionSheetPath);
            _threeViewPictureBox.Image = LoadImageCopy(_entry.ThreeViewSheetPath);
        }

        private static Image? LoadImageCopy(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var image = Image.FromStream(stream);
            return new Bitmap(image);
        }

        private static void OpenFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            }
            catch
            {
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _expressionPictureBox.Image?.Dispose();
                _threeViewPictureBox.Image?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
