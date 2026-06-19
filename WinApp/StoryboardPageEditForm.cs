using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public sealed class StoryboardPageEditForm : Form
    {
        private readonly List<TextBox> _descriptionEditors = new();
        private readonly List<StoryboardShot> _shots;

        public StoryboardPageEditForm(int pageIndex, IReadOnlyList<StoryboardShot> shots)
        {
            _shots = (shots ?? Array.Empty<StoryboardShot>())
                .Select(shot => shot.Clone())
                .ToList();

            Text = $"编辑分镜描述 - 第 {pageIndex + 1} 页";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(560, 760);
            MinimumSize = new Size(520, 680);
            BackColor = Color.FromArgb(28, 30, 36);
            ForeColor = Color.WhiteSmoke;
            AutoScaleMode = AutoScaleMode.None;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(14),
                BackColor = BackColor,
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));

            var scrollHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.Transparent,
            };

            var list = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = Math.Max(1, _shots.Count),
                Margin = Padding.Empty,
                BackColor = Color.Transparent,
            };

            for (var index = 0; index < _shots.Count; index++)
            {
                list.Controls.Add(BuildShotEditorCard(index, _shots[index]), 0, index);
            }

            scrollHost.Controls.Add(list);

            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            var cancelButton = CreateFooterButton("取消", Color.FromArgb(72, 78, 92));
            cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;

            var regenerateButton = CreateFooterButton("重新生成", Color.FromArgb(114, 78, 255));
            regenerateButton.Click += (_, _) =>
            {
                ApplyChanges();
                DialogResult = DialogResult.OK;
            };

            footer.Controls.Add(cancelButton, 0, 0);
            footer.Controls.Add(regenerateButton, 1, 0);

            root.Controls.Add(scrollHost, 0, 0);
            root.Controls.Add(footer, 0, 1);
            Controls.Add(root);

            AcceptButton = regenerateButton;
            CancelButton = cancelButton;
        }

        public IReadOnlyList<StoryboardShot> ResultShots => _shots;

        private Control BuildShotEditorCard(int index, StoryboardShot shot)
        {
            var card = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0, 0, 0, 12),
                Padding = new Padding(14),
                BackColor = Color.FromArgb(20, 21, 26),
            };
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            card.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.WhiteSmoke,
                Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold, GraphicsUnit.Point),
                Text = $"分镜 {Math.Max(1, shot.ShotNumber == 0 ? index + 1 : shot.ShotNumber)}",
                TextAlign = ContentAlignment.MiddleLeft,
            }, 0, 0);

            var editor = new TextBox
            {
                Dock = DockStyle.Top,
                Height = 92,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(16, 17, 22),
                ForeColor = Color.WhiteSmoke,
                Font = new Font("Microsoft YaHei", 10F, FontStyle.Regular, GraphicsUnit.Point),
                Text = shot.VisualDescription ?? string.Empty,
            };
            _descriptionEditors.Add(editor);
            card.Controls.Add(editor, 0, 1);

            card.Controls.Add(new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0),
                ForeColor = Color.FromArgb(130, 138, 156),
                Font = new Font("Microsoft YaHei", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
                Text = string.IsNullOrWhiteSpace(shot.Scene)
                    ? "场景：未填写"
                    : $"场景：{shot.Scene.Trim()}",
            }, 0, 2);

            return card;
        }

        private static Button CreateFooterButton(string text, Color backColor)
        {
            var button = new Button
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 8, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = backColor,
                ForeColor = Color.WhiteSmoke,
                Text = text,
                Cursor = Cursors.Hand,
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private void ApplyChanges()
        {
            for (var index = 0; index < _shots.Count && index < _descriptionEditors.Count; index++)
            {
                _shots[index].VisualDescription = _descriptionEditors[index].Text?.Trim() ?? string.Empty;
            }
        }
    }
}
