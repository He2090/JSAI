using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public sealed class StoryboardShotEditForm : Form
    {
        private readonly StoryboardShot _workingCopy;
        private readonly TextBox _sceneTextBox;
        private readonly TextBox _charactersTextBox;
        private readonly NumericUpDown _durationNumeric;
        private readonly TextBox _visualDescriptionTextBox;
        private readonly TextBox _dialogueTextBox;
        private readonly TextBox _visualEffectsTextBox;
        private readonly TextBox _audioEffectsTextBox;
        private readonly Dictionary<string, Button> _shotSizeButtons = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Button> _cameraAngleButtons = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Button> _cameraMovementButtons = new(StringComparer.Ordinal);

        public StoryboardShotEditForm(StoryboardShot shot)
        {
            _workingCopy = shot?.Clone() ?? new StoryboardShot();
            Text = $"编辑分镜 #{Math.Max(1, _workingCopy.ShotNumber)}";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(560, 860);
            MinimumSize = new Size(520, 760);
            BackColor = Color.FromArgb(28, 30, 36);
            ForeColor = Color.WhiteSmoke;
            AutoScaleMode = AutoScaleMode.None;

            _sceneTextBox = CreateEditorTextBox(_workingCopy.Scene, 40);
            _charactersTextBox = CreateEditorTextBox(string.Join("、", _workingCopy.Characters ?? new List<string>()), 40);
            _durationNumeric = new NumericUpDown
            {
                Dock = DockStyle.Top,
                Height = 38,
                Minimum = 1,
                Maximum = 15,
                Value = Math.Max(1, Math.Min(15, _workingCopy.DurationSeconds)),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(16, 17, 22),
                ForeColor = Color.WhiteSmoke,
                Font = new Font("Microsoft YaHei", 10F, FontStyle.Regular, GraphicsUnit.Point),
            };
            _visualDescriptionTextBox = CreateEditorTextBox(_workingCopy.VisualDescription, 120);
            _dialogueTextBox = CreateEditorTextBox(_workingCopy.Dialogue, 72);
            _visualEffectsTextBox = CreateEditorTextBox(_workingCopy.VisualEffects, 56);
            _audioEffectsTextBox = CreateEditorTextBox(_workingCopy.AudioEffects, 56);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(14),
                BackColor = BackColor,
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));

            var scrollHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.Transparent,
            };

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 10,
                Margin = Padding.Empty,
                BackColor = Color.Transparent,
            };

            content.Controls.Add(CreateFieldSection("场景", _sceneTextBox), 0, 0);
            content.Controls.Add(CreateFieldSection("角色（逗号分隔）", _charactersTextBox), 0, 1);
            content.Controls.Add(CreateFieldSection("时长（秒）", _durationNumeric), 0, 2);
            content.Controls.Add(CreateOptionSection("景别", StoryboardShotCatalog.ShotSizes, _workingCopy.ShotSize, _shotSizeButtons), 0, 3);
            content.Controls.Add(CreateOptionSection("拍摄角度", StoryboardShotCatalog.CameraAngles, _workingCopy.CameraAngle, _cameraAngleButtons), 0, 4);
            content.Controls.Add(CreateOptionSection("运镜方式", StoryboardShotCatalog.CameraMovements, _workingCopy.CameraMovement, _cameraMovementButtons), 0, 5);
            content.Controls.Add(CreateFieldSection("画面描述", _visualDescriptionTextBox), 0, 6);
            content.Controls.Add(CreateFieldSection("对白", _dialogueTextBox), 0, 7);
            content.Controls.Add(CreateFieldSection("视觉效果", _visualEffectsTextBox), 0, 8);
            content.Controls.Add(CreateFieldSection("音效提示", _audioEffectsTextBox), 0, 9);

            scrollHost.Controls.Add(content);

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

            var saveButton = CreateFooterButton("保存分镜", Color.FromArgb(114, 78, 255));
            saveButton.Click += (_, _) =>
            {
                ApplyChanges();
                DialogResult = DialogResult.OK;
            };

            footer.Controls.Add(cancelButton, 0, 0);
            footer.Controls.Add(saveButton, 1, 0);

            root.Controls.Add(scrollHost, 0, 0);
            root.Controls.Add(footer, 0, 1);
            Controls.Add(root);

            AcceptButton = saveButton;
            CancelButton = cancelButton;
        }

        public StoryboardShot Result => _workingCopy.Clone();

        private static TextBox CreateEditorTextBox(string text, int height)
        {
            return new TextBox
            {
                Dock = DockStyle.Top,
                Height = height,
                Multiline = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(16, 17, 22),
                ForeColor = Color.WhiteSmoke,
                Font = new Font("Microsoft YaHei", 10F, FontStyle.Regular, GraphicsUnit.Point),
                Text = text ?? string.Empty,
                ScrollBars = ScrollBars.Vertical,
            };
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

        private static Control CreateFieldSection(string title, Control editor)
        {
            var shell = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0, 0, 0, 14),
                BackColor = Color.Transparent,
            };
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            shell.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(180, 230, 255),
                Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold, GraphicsUnit.Point),
                Text = title,
                TextAlign = ContentAlignment.MiddleLeft,
            }, 0, 0);
            shell.Controls.Add(editor, 0, 1);
            return shell;
        }

        private Control CreateOptionSection(
            string title,
            IReadOnlyList<string> options,
            string selectedValue,
            IDictionary<string, Button> buttonMap)
        {
            var shell = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0, 0, 0, 14),
                BackColor = Color.Transparent,
            };
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            shell.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(180, 230, 255),
                Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold, GraphicsUnit.Point),
                Text = title,
                TextAlign = ContentAlignment.MiddleLeft,
            }, 0, 0);

            var optionPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = Padding.Empty,
                BackColor = Color.Transparent,
            };

            foreach (var option in options)
            {
                var currentOption = option;
                var button = new Button
                {
                    Width = 98,
                    Height = 42,
                    Margin = new Padding(0, 0, 8, 8),
                    FlatStyle = FlatStyle.Flat,
                    Text = currentOption,
                    Cursor = Cursors.Hand,
                };
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.BorderColor = Color.FromArgb(88, 94, 112);
                button.Click += (_, _) =>
                {
                    foreach (var pair in buttonMap)
                    {
                        ApplyOptionButtonStyle(pair.Value, string.Equals(pair.Key, currentOption, StringComparison.Ordinal));
                    }
                };
                buttonMap[currentOption] = button;
                ApplyOptionButtonStyle(button, string.Equals(currentOption, selectedValue, StringComparison.Ordinal));
                optionPanel.Controls.Add(button);
            }

            shell.Controls.Add(optionPanel, 0, 1);
            return shell;
        }

        private static void ApplyOptionButtonStyle(Button button, bool selected)
        {
            button.BackColor = selected ? Color.FromArgb(94, 71, 240) : Color.FromArgb(22, 24, 30);
            button.ForeColor = selected ? Color.White : Color.Gainsboro;
            button.FlatAppearance.BorderColor = selected ? Color.FromArgb(168, 140, 255) : Color.FromArgb(88, 94, 112);
        }

        private void ApplyChanges()
        {
            _workingCopy.Scene = _sceneTextBox.Text.Trim();
            _workingCopy.Characters = _charactersTextBox.Text
                .Split(new[] { '、', ',', '，', '/', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _workingCopy.DurationSeconds = (int)_durationNumeric.Value;
            _workingCopy.ShotSize = ResolveSelectedOption(_shotSizeButtons, _workingCopy.ShotSize, "中景");
            _workingCopy.CameraAngle = ResolveSelectedOption(_cameraAngleButtons, _workingCopy.CameraAngle, "平视");
            _workingCopy.CameraMovement = ResolveSelectedOption(_cameraMovementButtons, _workingCopy.CameraMovement, "固定");
            _workingCopy.VisualDescription = _visualDescriptionTextBox.Text.Trim();
            _workingCopy.Dialogue = string.IsNullOrWhiteSpace(_dialogueTextBox.Text) ? "无" : _dialogueTextBox.Text.Trim();
            _workingCopy.VisualEffects = string.IsNullOrWhiteSpace(_visualEffectsTextBox.Text) ? "无" : _visualEffectsTextBox.Text.Trim();
            _workingCopy.AudioEffects = string.IsNullOrWhiteSpace(_audioEffectsTextBox.Text) ? "无" : _audioEffectsTextBox.Text.Trim();
            _workingCopy.EndTime = _workingCopy.StartTime + Math.Max(1, _workingCopy.DurationSeconds);
        }

        private static string ResolveSelectedOption(IDictionary<string, Button> buttonMap, string currentValue, string fallbackValue)
        {
            return buttonMap.FirstOrDefault(pair => pair.Value.BackColor == Color.FromArgb(94, 71, 240)).Key
                   ?? currentValue
                   ?? fallbackValue;
        }
    }
}
