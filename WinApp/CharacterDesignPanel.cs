using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public sealed class CharacterDesignPanel : UserControl
    {
        private readonly Label _statusLabel;
        private readonly Label _footerLeftLabel;
        private readonly Label _footerRightLabel;
        private readonly FlowLayoutPanel _entryListPanel;
        private WorkflowNode? _node;
        private bool _busy;
        private string _projectName = string.Empty;

        public CharacterDesignPanel()
        {
            BackColor = Color.FromArgb(30, 30, 30);
            AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(16, 12, 16, 14),
                BackColor = Color.FromArgb(30, 30, 30),
                Margin = Padding.Empty,
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(142, 156, 180),
                TextAlign = ContentAlignment.MiddleLeft,
            };

            _entryListPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = false,
                FlowDirection = FlowDirection.TopDown,
                Margin = new Padding(0, 8, 0, 10),
                Padding = Padding.Empty,
                BackColor = Color.FromArgb(30, 30, 30),
            };

            var footer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(34, 35, 40),
                Padding = new Padding(8, 4, 8, 4),
                Margin = Padding.Empty,
            };

            _footerLeftLabel = new Label
            {
                Dock = DockStyle.Left,
                Width = 220,
                ForeColor = Color.FromArgb(154, 164, 182),
                TextAlign = ContentAlignment.MiddleLeft,
            };

            _footerRightLabel = new Label
            {
                Dock = DockStyle.Right,
                Width = 100,
                ForeColor = Color.FromArgb(255, 180, 56),
                TextAlign = ContentAlignment.MiddleRight,
            };

            footer.Controls.Add(_footerRightLabel);
            footer.Controls.Add(_footerLeftLabel);

            root.Controls.Add(_statusLabel, 0, 0);
            root.Controls.Add(_entryListPanel, 0, 1);
            root.Controls.Add(footer, 0, 2);

            Controls.Add(root);

            AttachScrollSupport(this);
            SizeChanged += (_, _) => RebuildEntries();
        }

        public event EventHandler? EntryChanged;

        public event EventHandler<WorkflowCharacterActionEventArgs>? CharacterActionRequested;

        public event EventHandler? InteractionStarted;

        public void Bind(WorkflowNode node, bool busy, string projectName)
        {
            _node = node;
            _busy = busy;
            _projectName = projectName ?? string.Empty;
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);
            RebuildEntries();
        }

        private void RebuildEntries()
        {
            if (_node?.Params == null)
            {
                return;
            }

            var entries = _node.Params.CharacterEntries ??= new System.Collections.Generic.List<CharacterDesignEntry>();
            if (string.IsNullOrWhiteSpace(_node.Params.SelectedCharacterName) ||
                entries.All(entry => !string.Equals(entry.Name, _node.Params.SelectedCharacterName, StringComparison.OrdinalIgnoreCase)))
            {
                _node.Params.SelectedCharacterName = entries.FirstOrDefault()?.Name ?? string.Empty;
            }

            _statusLabel.Text = _busy
                ? "正在处理角色设计，请稍候..."
                : entries.Count == 0
                    ? "等待故事大纲同步角色列表..."
                    : $"已同步 {entries.Count} 个角色，每一行一个角色卡片。";

            _footerLeftLabel.Text = $"已同步角色：{entries.Count}";
            _footerRightLabel.Text = _busy ? "处理中" : "Ready";

            _entryListPanel.SuspendLayout();
            _entryListPanel.Controls.Clear();

            if (entries.Count == 0)
            {
                _entryListPanel.Controls.Add(BuildEmptyStateCard());
            }
            else
            {
                foreach (var item in entries.Select((entry, index) => (entry, index)))
                {
                    _entryListPanel.Controls.Add(BuildEntryCard(item.entry, item.index));
                }
            }

            _entryListPanel.ResumeLayout();
        }

        private Control BuildEmptyStateCard()
        {
            var width = Math.Max(320, _entryListPanel.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 12);
            return new Panel
            {
                Width = width,
                Height = 140,
                Margin = new Padding(0, 0, 0, 10),
                BackColor = Color.FromArgb(24, 25, 30),
                Padding = new Padding(18),
                Controls =
                {
                    new Label
                    {
                        Dock = DockStyle.Fill,
                        ForeColor = Color.FromArgb(118, 128, 146),
                        Font = new Font("Microsoft YaHei", 10F, FontStyle.Regular, GraphicsUnit.Point),
                        TextAlign = ContentAlignment.MiddleCenter,
                        Text = "等待故事大纲同步角色列表...\r\n连接大纲节点后会自动生成角色卡片。",
                    },
                },
            };
        }

        private Control BuildEntryCard(CharacterDesignEntry entry, int index)
        {
            var isSelected = _node?.Params != null &&
                             string.Equals(_node.Params.SelectedCharacterName, entry.Name, StringComparison.OrdinalIgnoreCase);
            var expanded = isSelected ||
                           entry.HasProfileData ||
                           entry.ProfileStatus == CharacterAssetStatus.Generating ||
                           entry.ExpressionStatus == CharacterAssetStatus.Generating ||
                           entry.ThreeViewStatus == CharacterAssetStatus.Generating ||
                           !string.IsNullOrWhiteSpace(entry.LastError);
            var width = Math.Max(320, _entryListPanel.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 12);

            var shell = new Panel
            {
                Width = width,
                Height = expanded ? 238 : 64,
                Margin = new Padding(0, 0, 0, 10),
                Padding = new Padding(12, 10, 12, 10),
                BackColor = isSelected ? Color.FromArgb(36, 38, 46) : Color.FromArgb(24, 25, 30),
                Cursor = Cursors.Hand,
                Tag = entry.Name,
            };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = expanded ? 4 : 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            if (expanded)
            {
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            }

            root.Controls.Add(BuildEntryHeader(entry, index), 0, 0);
            if (expanded)
            {
                root.Controls.Add(BuildEntrySummary(entry), 0, 1);
                root.Controls.Add(BuildEntryActionRow(entry), 0, 2);
                root.Controls.Add(BuildEntryHintRow(entry), 0, 3);
            }

            shell.Controls.Add(root);
            shell.Click += (_, _) => SelectEntry(entry.Name);
            AttachScrollSupport(shell);
            return shell;
        }

        private Control BuildEntryHeader(CharacterDesignEntry entry, int index)
        {
            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));

            var indexBadge = new Label
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Text = (index + 1).ToString(),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = entry.AssetsSaved ? Color.FromArgb(228, 255, 236) : Color.White,
                BackColor = Color.FromArgb(179, 98, 18),
            };

            var nameLink = new LinkLabel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(10, 0, 8, 0),
                AutoEllipsis = true,
                LinkColor = Color.WhiteSmoke,
                ActiveLinkColor = Color.White,
                VisitedLinkColor = Color.WhiteSmoke,
                Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold, GraphicsUnit.Point),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = entry.Name,
            };
            nameLink.Click += (_, _) =>
            {
                SelectEntry(entry.Name);
                ShowDetail(entry);
            };

            var roleComboBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 8, 0),
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(19, 20, 24),
                ForeColor = Color.WhiteSmoke,
            };
            roleComboBox.Items.Add(CharacterDesignRoleType.Main.ToLabel());
            roleComboBox.Items.Add(CharacterDesignRoleType.Supporting.ToLabel());
            roleComboBox.SelectedItem = CharacterDesignRoleTypeExtensions.Parse(entry.RoleType).ToLabel();
            roleComboBox.SelectedIndexChanged += (_, _) =>
            {
                InteractionStarted?.Invoke(this, EventArgs.Empty);
                entry.RoleType = roleComboBox.SelectedItem?.ToString() ?? CharacterDesignRoleType.Main.ToLabel();
                EntryChanged?.Invoke(this, EventArgs.Empty);
            };

            var quickButton = new Button
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(118, 73, 26),
                ForeColor = Color.White,
                Text = GetProfileButtonText(entry),
                Enabled = !_busy && entry.ProfileStatus != CharacterAssetStatus.Generating,
                Cursor = !_busy && entry.ProfileStatus != CharacterAssetStatus.Generating ? Cursors.Hand : Cursors.Default,
            };
            quickButton.FlatAppearance.BorderSize = 0;
            quickButton.Click += (_, _) => TriggerAction(entry, CharacterDesignActionType.GenerateProfile);

            header.Controls.Add(indexBadge, 0, 0);
            header.Controls.Add(nameLink, 1, 0);
            header.Controls.Add(roleComboBox, 2, 0);
            header.Controls.Add(quickButton, 3, 0);
            return header;
        }

        private Control BuildEntrySummary(CharacterDesignEntry entry)
        {
            var shell = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(28, 30, 36),
                Padding = new Padding(10),
                Margin = new Padding(0, 8, 0, 8),
            };

            var preview = BuildPreviewBox(entry);
            preview.Location = new Point(0, 0);
            preview.Size = new Size(72, 72);

            var titleLabel = new Label
            {
                Location = new Point(86, 0),
                Size = new Size(Math.Max(180, shell.Width - 96), 18),
                AutoEllipsis = true,
                ForeColor = Color.FromArgb(255, 190, 92),
                Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold, GraphicsUnit.Point),
                Text = string.IsNullOrWhiteSpace(entry.CompactSummary) ? "等待生成角色档案..." : entry.CompactSummary,
            };

            var metaLabel = new Label
            {
                Location = new Point(86, 24),
                Size = new Size(280, 32),
                AutoEllipsis = true,
                ForeColor = Color.FromArgb(200, 206, 216),
                Text = BuildMetaLine(entry),
            };

            var stateLabel = new Label
            {
                Location = new Point(86, 58),
                Size = new Size(280, 16),
                AutoEllipsis = true,
                ForeColor = string.IsNullOrWhiteSpace(entry.LastError)
                    ? Color.FromArgb(142, 156, 180)
                    : Color.FromArgb(255, 140, 140),
                Text = BuildEntryStatusLine(entry),
            };

            shell.Resize += (_, _) =>
            {
                var width = Math.Max(200, shell.ClientSize.Width - 96);
                titleLabel.Width = width;
                metaLabel.Width = width;
                stateLabel.Width = width;
            };

            shell.Controls.Add(preview);
            shell.Controls.Add(titleLabel);
            shell.Controls.Add(metaLabel);
            shell.Controls.Add(stateLabel);
            return shell;
        }

        private Control BuildPreviewBox(CharacterDesignEntry entry)
        {
            var host = new Panel
            {
                BackColor = Color.FromArgb(16, 17, 22),
                BorderStyle = BorderStyle.FixedSingle,
            };

            var artifactPath = entry.LatestArtifactPath;
            if (!string.IsNullOrWhiteSpace(artifactPath) && File.Exists(artifactPath))
            {
                var picture = new PictureBox
                {
                    Dock = DockStyle.Fill,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.FromArgb(16, 17, 22),
                };
                picture.Image = LoadImageCopy(artifactPath);
                host.Controls.Add(picture);
                host.Disposed += (_, _) => picture.Image?.Dispose();
                return host;
            }

            host.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "角色",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(118, 128, 146),
            });
            return host;
        }

        private Control BuildEntryActionRow(CharacterDesignEntry entry)
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));

            var profileButton = CreateCardActionButton(
                entry.HasProfileData ? "重新生成档案" : "生成角色档案",
                entry.HasProfileData ? Color.FromArgb(28, 111, 67) : Color.FromArgb(147, 83, 34),
                !_busy);
            profileButton.Click += (_, _) => TriggerAction(entry, CharacterDesignActionType.GenerateProfile);

            var expressionButton = CreateCardActionButton(
                entry.HasExpressionSheet ? "重新生成九宫格" : "生成九宫格",
                Color.FromArgb(56, 106, 196),
                !_busy && entry.HasProfileData && entry.ProfileStatus != CharacterAssetStatus.Generating);
            expressionButton.Click += (_, _) => TriggerAction(entry, CharacterDesignActionType.GenerateExpression);

            var threeViewButton = CreateCardActionButton(
                entry.HasThreeViewSheet ? "重新生成三视图" : "生成三视图",
                Color.FromArgb(72, 90, 120),
                !_busy && entry.HasExpressionSheet && entry.ProfileStatus != CharacterAssetStatus.Generating);
            threeViewButton.Click += (_, _) => TriggerAction(entry, CharacterDesignActionType.GenerateThreeView);

            row.Controls.Add(profileButton, 0, 0);
            row.Controls.Add(expressionButton, 1, 0);
            row.Controls.Add(threeViewButton, 2, 0);
            return row;
        }

        private Control BuildEntryHintRow(CharacterDesignEntry entry)
        {
            var shell = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.FromArgb(30, 30, 30),
                Margin = new Padding(0, 8, 0, 0),
                Padding = Padding.Empty,
            };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            var promptButton = new Button
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 6, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(88, 52, 18),
                ForeColor = Color.FromArgb(255, 205, 120),
                Text = "查看角色提示词（中文）",
                Cursor = Cursors.Hand,
            };
            promptButton.FlatAppearance.BorderSize = 0;
            promptButton.Click += (_, _) =>
            {
                SelectEntry(entry.Name);
                ShowPromptPreview(entry);
            };

            var saveButton = new Button
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(6, 0, 0, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = entry.AssetsSaved ? Color.FromArgb(36, 104, 63) : Color.FromArgb(46, 76, 34),
                ForeColor = Color.White,
                Text = "保存角色资产",
                Cursor = Cursors.Hand,
            };
            saveButton.FlatAppearance.BorderSize = 0;
            saveButton.Enabled = !entry.AssetsSaved;
            saveButton.Cursor = saveButton.Enabled ? Cursors.Hand : Cursors.Default;
            saveButton.Text = entry.AssetsSaved ? "已保存" : "保存角色资产";
            saveButton.ForeColor = entry.AssetsSaved ? Color.FromArgb(228, 255, 236) : Color.White;
            saveButton.Click += (_, _) =>
            {
                SelectEntry(entry.Name);
                SaveCharacterAssets(entry);
            };

            shell.Controls.Add(promptButton, 0, 0);
            shell.Controls.Add(saveButton, 1, 0);
            return shell;
        }

        private static Button CreateCardActionButton(string text, Color backColor, bool enabled)
        {
            var button = new Button
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 8, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = enabled ? backColor : Color.FromArgb(63, 68, 82),
                ForeColor = enabled ? Color.White : Color.FromArgb(160, 166, 180),
                Text = text,
                Enabled = enabled,
                Cursor = enabled ? Cursors.Hand : Cursors.Default,
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private void AttachScrollSupport(Control control)
        {
            if (control is ComboBox)
            {
                return;
            }

            control.MouseWheel -= ScrollSupport_MouseWheel;
            control.MouseWheel += ScrollSupport_MouseWheel;
            foreach (Control child in control.Controls)
            {
                AttachScrollSupport(child);
            }
        }

        private void ScrollSupport_MouseWheel(object? sender, MouseEventArgs e)
        {
            ScrollEntryList(e.Delta);
        }

        private void ScrollEntryList(int delta)
        {
            if (!_entryListPanel.VerticalScroll.Visible)
            {
                return;
            }

            var scrollLines = Math.Max(1, SystemInformation.MouseWheelScrollLines);
            var step = Math.Max(28, scrollLines * 16);
            var current = Math.Abs(_entryListPanel.AutoScrollPosition.Y);
            var target = delta > 0
                ? Math.Max(0, current - step)
                : current + step;

            _entryListPanel.AutoScrollPosition = new Point(0, target);
        }

        private void SelectEntry(string name)
        {
            if (_node?.Params == null)
            {
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            _node.Params.SelectedCharacterName = name;
            EntryChanged?.Invoke(this, EventArgs.Empty);
            RebuildEntries();
        }

        private void TriggerAction(CharacterDesignEntry entry, CharacterDesignActionType action)
        {
            if (_node == null)
            {
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            entry.AssetsSaved = false;
            entry.SavedAssetFolderPath = string.Empty;
            CharacterActionRequested?.Invoke(this, new WorkflowCharacterActionEventArgs(_node, entry.Name, action));
        }

        private void ShowDetail(CharacterDesignEntry entry)
        {
            InteractionStarted?.Invoke(this, EventArgs.Empty);
            using var form = new CharacterDetailForm(entry);
            form.ShowDialog(FindForm());
        }

        private void ShowPromptPreview(CharacterDesignEntry entry)
        {
            InteractionStarted?.Invoke(this, EventArgs.Empty);

            using var form = new Form
            {
                Text = $"角色提示词 - {entry.Name}",
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                ClientSize = new Size(760, 620),
                BackColor = Color.FromArgb(24, 25, 30),
                Font = new Font("Microsoft YaHei", 9F, FontStyle.Regular, GraphicsUnit.Point),
            };

            var textBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = false,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(24, 25, 30),
                ForeColor = Color.WhiteSmoke,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
                DetectUrls = false,
                Text = BuildPromptPreviewText(entry),
            };

            var saveButton = new Button
            {
                Text = "保存并关闭",
                Dock = DockStyle.Right,
                Width = 118,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(255, 122, 0),
                ForeColor = Color.Black,
            };
            saveButton.FlatAppearance.BorderSize = 0;
            saveButton.Click += (_, _) =>
            {
                ApplyPromptPreviewEdits(entry, textBox.Text);
                EntryChanged?.Invoke(this, EventArgs.Empty);
                RebuildEntries();
                form.DialogResult = DialogResult.OK;
                form.Close();
            };

            var closeButton = new Button
            {
                Text = "关闭",
                Dock = DockStyle.Right,
                Width = 96,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(62, 69, 84),
                ForeColor = Color.White,
                DialogResult = DialogResult.OK,
            };
            closeButton.FlatAppearance.BorderSize = 0;

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                Padding = new Padding(16, 8, 16, 8),
                BackColor = Color.FromArgb(28, 30, 36),
            };
            footer.Controls.Add(saveButton);
            footer.Controls.Add(closeButton);

            var host = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
            };
            host.Controls.Add(textBox);

            form.Controls.Add(host);
            form.Controls.Add(footer);
            form.AcceptButton = saveButton;
            form.ShowDialog(FindForm());
        }

        private string BuildPromptPreviewText(CharacterDesignEntry entry)
        {
            var styleLine = _node != null
                ? WorkflowExecutor.ResolveCharacterDesignStyleDescriptorChinese(_node)
                : "2D动漫，赛璐珞勾线，洁净线稿";
            var sections = new[]
            {
                ("角色视觉风格", styleLine),
                ("表情九宫格中文提示词", _node != null ? CharacterPromptTextBuilder.BuildChineseExpressionPrompt(_node, entry) : CharacterPromptTextBuilder.BuildChineseExpressionPrompt(entry)),
                ("三视图中文提示词", _node != null ? CharacterPromptTextBuilder.BuildChineseThreeViewPrompt(_node, entry) : CharacterPromptTextBuilder.BuildChineseThreeViewPrompt(entry)),
                ("角色名", entry.Name),
                ("别名", entry.Alias),
                ("角色类型", entry.RoleType),
                ("基础外形", entry.BasicStats),
                ("角色摘要", entry.Summary),
                ("服装说明", entry.CostumeNotes),
                ("表演说明", entry.ActingNotes),
                ("角色外观提示词", entry.AppearancePrompt),
                ("九宫格提示词", entry.ExpressionPrompt),
                ("三视图提示词", entry.ThreeViewPrompt),
            };

            var builder = new StringBuilder();
            foreach (var section in sections)
            {
                if (string.IsNullOrWhiteSpace(section.Item2))
                {
                    continue;
                }

                builder.AppendLine(section.Item1);
                builder.AppendLine(section.Item2.Trim());
                builder.AppendLine();
            }

            return builder.ToString().Trim();
        }

        private static void ApplyPromptPreviewEdits(CharacterDesignEntry entry, string text)
        {
            if (entry == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var sections = ParsePromptPreviewSections(text);
            if (sections.TryGetValue("角色外观提示词", out var appearancePrompt))
            {
                entry.AppearancePrompt = appearancePrompt.Trim();
            }

            if (sections.TryGetValue("九宫格提示词", out var expressionPrompt))
            {
                entry.ExpressionPrompt = expressionPrompt.Trim();
            }

            if (sections.TryGetValue("三视图提示词", out var threeViewPrompt))
            {
                entry.ThreeViewPrompt = threeViewPrompt.Trim();
            }
        }

        private static Dictionary<string, string> ParsePromptPreviewSections(string text)
        {
            var knownHeadings = new HashSet<string>(new[]
            {
                "角色视觉风格",
                "表情九宫格中文提示词",
                "三视图中文提示词",
                "角色名",
                "别名",
                "角色类型",
                "基础外形",
                "角色摘要",
                "服装说明",
                "表演说明",
                "角色外观提示词",
                "九宫格提示词",
                "三视图提示词",
            }, StringComparer.Ordinal);

            var sections = new Dictionary<string, string>(StringComparer.Ordinal);
            string? currentHeading = null;
            var builder = new StringBuilder();

            foreach (var rawLine in (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Split('\n'))
            {
                var line = rawLine.Trim();
                if (knownHeadings.Contains(line))
                {
                    FlushSection();
                    currentHeading = line;
                    continue;
                }

                if (currentHeading != null)
                {
                    builder.AppendLine(rawLine);
                }
            }

            FlushSection();
            return sections;

            void FlushSection()
            {
                if (currentHeading == null)
                {
                    builder.Clear();
                    return;
                }

                sections[currentHeading] = builder.ToString().Trim();
                builder.Clear();
            }
        }

        private void SaveCharacterAssets(CharacterDesignEntry entry)
        {
            try
            {
                var result = CharacterAssetExportService.Export(
                    entry,
                    CharacterAssetExportService.GetProjectRootPath(_projectName),
                    _node);
                entry.AssetsSaved = true;
                entry.SavedAssetFolderPath = result.FolderPath;
                EntryChanged?.Invoke(this, EventArgs.Empty);
                RebuildEntries();
                MessageBox.Show(
                    FindForm(),
                    $"角色资产已保存到：{result.FolderPath}",
                    "保存成功",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    FindForm(),
                    ex.Message,
                    "保存失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private static string GetProfileButtonText(CharacterDesignEntry entry)
        {
            return entry.ProfileStatus switch
            {
                CharacterAssetStatus.Generating => "生成中...",
                CharacterAssetStatus.Success when entry.HasProfileData => "已完成",
                CharacterAssetStatus.Failed => "重试",
                _ => "生成",
            };
        }

        private static string BuildEntryStatusLine(CharacterDesignEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(entry.LastError))
            {
                return $"失败：{entry.LastError}";
            }

            if (entry.ProfileStatus == CharacterAssetStatus.Generating)
            {
                return "正在生成角色档案...";
            }

            if (!entry.HasProfileData)
            {
                return "待生成角色档案";
            }

            if (entry.ThreeViewStatus == CharacterAssetStatus.Generating)
            {
                return "三视图生成中...";
            }

            if (entry.ExpressionStatus == CharacterAssetStatus.Generating)
            {
                return "九宫格生成中...";
            }

            if (entry.HasThreeViewSheet)
            {
                return "角色档案、九宫格、三视图均已完成";
            }

            if (entry.HasExpressionSheet)
            {
                return "角色档案和九宫格已完成，可继续生成三视图";
            }

            return "角色档案已完成，可继续生成九宫格";
        }

        private static string BuildMetaLine(CharacterDesignEntry entry)
        {
            var parts = new[] { entry.BasicStats, entry.Profession, entry.Personality }
                .Where(value => !string.IsNullOrWhiteSpace(value) && !CharacterDesignEntry.LooksLikeRawStructuredText(value))
                .Select(value => value.Trim())
                .Take(3);

            return string.Join("，", parts);
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
    }
}
