using System;
using System.Drawing;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public sealed class CreativeDescriptionPanel : UserControl
    {
        private readonly TableLayoutPanel _rawLayout;
        private readonly TableLayoutPanel _storyboardLayout;
        private readonly RichTextBox _creativeTextBox;
        private readonly Label _creativePlaceholderLabel;
        private readonly Button _splitButton;
        private readonly Button _resplitButton;
        private readonly StoryboardBreakdownPanel _storyboardPanel;
        private WorkflowNode? _node;
        private bool _busy;

        public CreativeDescriptionPanel()
        {
            BackColor = Color.FromArgb(30, 30, 30);
            AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

            _storyboardPanel = new StoryboardBreakdownPanel
            {
                Dock = DockStyle.Fill,
                Visible = false,
            };
            _storyboardPanel.InteractionStarted += (_, _) => InteractionStarted?.Invoke(this, EventArgs.Empty);
            _storyboardPanel.EntryChanged += (_, _) => EntryChanged?.Invoke(this, EventArgs.Empty);

            _creativeTextBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(20, 21, 26),
                ForeColor = Color.WhiteSmoke,
                DetectUrls = false,
                Margin = Padding.Empty,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
            };
            _creativeTextBox.Enter += (_, _) => InteractionStarted?.Invoke(this, EventArgs.Empty);
            _creativeTextBox.MouseDown += (_, _) => InteractionStarted?.Invoke(this, EventArgs.Empty);

            _creativePlaceholderLabel = new Label
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(118, 128, 146),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
                Text = "等待剧本节点生成创意描述...\r\n\r\n生成后可在这里拆分为影视分镜。",
            };

            var textShell = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(20, 21, 26),
                Padding = new Padding(12, 10, 12, 10),
                Margin = new Padding(0, 0, 0, 12),
            };
            textShell.Controls.Add(_creativeTextBox);
            textShell.Controls.Add(_creativePlaceholderLabel);
            textShell.MouseDown += (_, _) => InteractionStarted?.Invoke(this, EventArgs.Empty);

            _splitButton = CreateSplitButton();
            _resplitButton = CreateSplitButton();

            var actionShell = CreateActionShell(_splitButton);
            var storyboardActionShell = CreateActionShell(_resplitButton);

            _rawLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(16, 12, 16, 14),
                BackColor = Color.FromArgb(30, 30, 30),
                Margin = Padding.Empty,
            };
            _rawLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _rawLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
            _rawLayout.Controls.Add(textShell, 0, 0);
            _rawLayout.Controls.Add(actionShell, 0, 1);

            _storyboardLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(16, 12, 16, 14),
                BackColor = Color.FromArgb(30, 30, 30),
                Margin = Padding.Empty,
                Visible = false,
            };
            _storyboardLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _storyboardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
            _storyboardLayout.Controls.Add(_storyboardPanel, 0, 0);
            _storyboardLayout.Controls.Add(storyboardActionShell, 0, 1);

            Controls.Add(_storyboardLayout);
            Controls.Add(_rawLayout);
        }

        public event EventHandler? SplitRequested;

        public event EventHandler? EntryChanged;

        public event EventHandler? InteractionStarted;

        public void Bind(WorkflowNode node, bool busy)
        {
            _node = node;
            _busy = busy;
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);
            RefreshView();
        }

        private Button CreateSplitButton()
        {
            var button = new Button
            {
                Dock = DockStyle.Fill,
                Height = 42,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(114, 78, 255),
                ForeColor = Color.White,
                Text = "拆分为影视分镜",
                Cursor = Cursors.Hand,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
                Margin = Padding.Empty,
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (_, _) => SplitRequested?.Invoke(this, EventArgs.Empty);
            return button;
        }

        private static Panel CreateActionShell(Control control)
        {
            var shell = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(34, 35, 40),
                Padding = new Padding(14, 10, 14, 10),
                Margin = Padding.Empty,
            };
            shell.Controls.Add(control);
            return shell;
        }

        private void RefreshView()
        {
            if (_node == null)
            {
                return;
            }

            var shots = _node.Params?.StoryboardShots;
            var hasShots = shots != null && shots.Count > 0;
            var creativeText = WorkflowExecutor.NormalizeTextResult(WorkflowNodeCatalog.CreativeDescription, _node.Output ?? string.Empty);
            var hasCreativeText = !string.IsNullOrWhiteSpace(creativeText);

            _rawLayout.Visible = !hasShots;
            _storyboardLayout.Visible = hasShots;
            _storyboardPanel.Visible = hasShots;

            if (_creativeTextBox.Text != creativeText)
            {
                _creativeTextBox.Text = creativeText;
                _creativeTextBox.SelectionLength = 0;
            }

            _creativePlaceholderLabel.Visible = !hasCreativeText;

            if (hasShots)
            {
                _storyboardPanel.Bind(null, _node, _busy);
                _storyboardPanel.BringToFront();
            }

            UpdateSplitButton(_splitButton, hasCreativeText, false);
            UpdateSplitButton(_resplitButton, hasCreativeText, true);
        }

        private void UpdateSplitButton(Button button, bool hasCreativeText, bool resplit)
        {
            var enabled = !_busy && hasCreativeText;
            button.Enabled = enabled;
            button.Cursor = enabled ? Cursors.Hand : Cursors.Default;
            button.Text = _busy
                ? "处理中..."
                : resplit
                    ? "重新拆分为影视分镜"
                    : "拆分为影视分镜";
            button.BackColor = enabled ? Color.FromArgb(114, 78, 255) : Color.FromArgb(84, 74, 104);
            button.ForeColor = enabled ? Color.White : Color.FromArgb(176, 182, 194);
        }
    }
}
