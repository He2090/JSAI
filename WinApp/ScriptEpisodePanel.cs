using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public sealed class ScriptEpisodePanel : UserControl
    {
        private readonly Label _statusLabel;
        private readonly FlowLayoutPanel _listPanel;
        private WorkflowNode? _node;
        private bool _busy;

        public ScriptEpisodePanel()
        {
            BackColor = Color.FromArgb(20, 21, 26);
            AutoScaleMode = AutoScaleMode.None;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(0),
                Margin = Padding.Empty,
                BackColor = Color.FromArgb(20, 21, 26),
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(118, 128, 146),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0),
            };

            _listPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = false,
                FlowDirection = FlowDirection.TopDown,
                Padding = new Padding(0, 4, 0, 0),
                Margin = Padding.Empty,
                BackColor = Color.FromArgb(20, 21, 26),
            };

            layout.Controls.Add(_statusLabel, 0, 0);
            layout.Controls.Add(_listPanel, 0, 1);
            Controls.Add(layout);

            SizeChanged += (_, _) => Rebuild();
        }

        public event EventHandler? EpisodeSelectionChanged;

        public event EventHandler? InteractionStarted;

        public void Bind(WorkflowNode node, bool busy)
        {
            _node = node;
            _busy = busy;
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);
            Rebuild();
        }

        private void Rebuild()
        {
            if (_node?.Params == null)
            {
                return;
            }

            var episodes = _node.Params.GeneratedScriptEpisodes ?? new System.Collections.Generic.List<GeneratedScriptEpisode>();
            _statusLabel.Text = _busy
                ? "正在生成分集剧本..."
                : episodes.Count == 0
                    ? "请先选择章节并生成分集剧本。"
                    : $"已生成 {episodes.Count} 集，点击条目可展开查看创意描述。";

            _listPanel.SuspendLayout();
            _listPanel.Controls.Clear();

            if (_node.Params.SelectedScriptEpisodeIndex >= episodes.Count)
            {
                _node.Params.SelectedScriptEpisodeIndex = episodes.Count == 0 ? 0 : episodes.Count - 1;
            }

            foreach (var item in episodes.Select((episode, index) => (episode, index)))
            {
                _listPanel.Controls.Add(BuildEpisodeCard(item.episode, item.index));
            }

            _listPanel.ResumeLayout();
        }

        private Control BuildEpisodeCard(GeneratedScriptEpisode episode, int index)
        {
            var isExpanded = _node?.Params != null && _node.Params.SelectedScriptEpisodeIndex == index;
            var width = Math.Max(320, _listPanel.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 10);

            var shell = new Panel
            {
                Width = width,
                Height = isExpanded ? 242 : 52,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(0),
                BackColor = isExpanded ? Color.FromArgb(12, 35, 44) : Color.FromArgb(34, 35, 40),
            };

            var headerButton = new Button
            {
                Dock = DockStyle.Top,
                Height = 52,
                FlatStyle = FlatStyle.Flat,
                BackColor = shell.BackColor,
                ForeColor = isExpanded ? Color.FromArgb(208, 248, 244) : Color.WhiteSmoke,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = $"  {index + 1}  {episode.DisplayTitle}",
                Cursor = Cursors.Hand,
            };
            headerButton.FlatAppearance.BorderSize = 0;
            headerButton.Click += (_, _) => SelectEpisode(index);
            shell.Controls.Add(headerButton);

            if (!isExpanded)
            {
                return shell;
            }

            var bodyShell = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 8, 12, 12),
                BackColor = Color.FromArgb(20, 21, 26),
            };

            var metaLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 18,
                ForeColor = Color.FromArgb(87, 201, 187),
                Text = string.IsNullOrWhiteSpace(episode.Characters) ? "角色：待生成" : $"角色：{episode.Characters}",
            };

            var contentBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                BackColor = Color.FromArgb(20, 21, 26),
                ForeColor = Color.FromArgb(222, 227, 236),
                Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point),
                DetectUrls = false,
                WordWrap = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Text = episode.Content,
            };
            contentBox.MouseDown += (_, _) => InteractionStarted?.Invoke(this, EventArgs.Empty);

            bodyShell.Controls.Add(contentBox);
            bodyShell.Controls.Add(metaLabel);
            shell.Controls.Add(bodyShell);
            return shell;
        }

        private void SelectEpisode(int index)
        {
            if (_node?.Params == null)
            {
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            _node.Params.SelectedScriptEpisodeIndex = index;
            EpisodeSelectionChanged?.Invoke(this, EventArgs.Empty);
            Rebuild();
        }
    }
}
