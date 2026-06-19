using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public sealed class StoryboardVideoPanel : UserControl
    {
        private static readonly string[] QualityOptions = { "标清", "高清", "超清" };
        private static readonly ConfigOptionItem[] PromptLanguageOptions =
        {
            new("zh", "中文显示 / 英文执行"),
        };
        private static readonly (string Code, string Name)[] ModelFamilyOptions =
        {
            ("veo", "Veo"),
            ("luma", "Luma Dream Machine"),
            ("runway", "Runway Gen-3"),
            ("minimax", "海螺"),
            ("volcengine", "豆包"),
            ("grok", "Grok"),
            ("qwen", "通义万相"),
            ("sora", "Sora"),
        };

        private static readonly Dictionary<string, string[]> SubModelOptions = new(StringComparer.OrdinalIgnoreCase)
        {
            ["veo"] = new[] { "veo3.1-fast", "veo3.1-4k", "veo3.1-pro-4k" },
            ["luma"] = new[] { "ray-v2", "photon", "photon-flash" },
            ["runway"] = new[] { "gen3-alpha-turbo", "gen3-alpha", "gen3-alpha-extreme" },
            ["minimax"] = new[] { "video-01", "video-01-live" },
            ["volcengine"] = new[] { "doubao-video-1", "doubao-video-pro" },
            ["grok"] = new[] { "grok-video-3-10s" },
            ["qwen"] = new[] { "qwen-video", "qwen-video-plus" },
            ["sora"] = new[] { "sora", "sora-2" },
        };
        private WorkflowDocument? _document;
        private WorkflowNode? _node;
        private bool _busy;
        private bool _syncingConfigEditors;
        private FlowLayoutPanel? _selectedShotsHost;
        private FlowLayoutPanel? _allShotsHost;

        public StoryboardVideoPanel()
        {
            BackColor = Color.FromArgb(30, 30, 30);
            AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        public event EventHandler? EntryChanged;
        public event EventHandler? InteractionStarted;
        public event EventHandler<string>? ActionRequested;

        public void Bind(WorkflowDocument? document, WorkflowNode node, bool busy)
        {
            _document = document;
            _node = node;
            _busy = busy;
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);
            EnsureCompatibleSelections();
            SyncAvailableShots();
            Rebuild();
        }

        private void EnsureCompatibleSelections()
        {
            if (_node?.Params == null)
            {
                return;
            }

            var allowedDurations = BuildAvailableDurationOptions().ToList();
            if (allowedDurations.Count == 0)
            {
                return;
            }

            var currentDuration = Math.Max(1, _node.Params.StoryboardVideoDurationSeconds).ToString();
            if (!allowedDurations.Contains(currentDuration, StringComparer.Ordinal))
            {
                _node.Params.StoryboardVideoDurationSeconds = int.Parse(allowedDurations[^1]);
            }
        }

        private void SyncAvailableShots()
        {
            if (_document == null || _node?.Params == null)
            {
                return;
            }

            var upstreamShots = WorkflowExecutor.CollectStoryboardShots(_document, _node, 96);
            var selectedIds = new HashSet<string>((_node.Params.StoryboardVideoSelectedShotIds ?? new List<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);

            _node.Params.StoryboardShots = upstreamShots;
            _node.Params.StoryboardVideoSelectedShotIds = upstreamShots
                .Where(shot => selectedIds.Contains(shot.Id))
                .Select(shot => shot.Id)
                .ToList();

            if (upstreamShots.Count == 0)
            {
                _node.Params.StoryboardVideoStage = "idle";
                _node.Params.StoryboardVideoSelectedShotIds.Clear();
                _node.Params.StoryboardVideoPrompt = string.Empty;
                _node.Params.StoryboardVideoModelPrompt = string.Empty;
                _node.Params.StoryboardVideoGeneratedClips.Clear();
                _node.Params.StoryboardVideoFusedImagePath = string.Empty;
                return;
            }

            if (string.IsNullOrWhiteSpace(_node.Params.StoryboardVideoStage) ||
                string.Equals(_node.Params.StoryboardVideoStage, "idle", StringComparison.OrdinalIgnoreCase))
            {
                _node.Params.StoryboardVideoStage = "selecting";
            }
        }

        private void Rebuild(bool preserveScroll = false)
        {
            var selectedScrollY = preserveScroll ? GetScrollY(_selectedShotsHost) : 0;
            var allScrollY = preserveScroll ? GetScrollY(_allShotsHost) : 0;

            SuspendLayout();
            Controls.Clear();
            _selectedShotsHost = null;
            _allShotsHost = null;
            if (_node?.Params == null)
            {
                ResumeLayout();
                return;
            }

            var stage = (_node.Params.StoryboardVideoStage ?? "idle").Trim().ToLowerInvariant();
            Controls.Add(stage is "prompting" or "generating" or "completed" ? BuildPromptingView() : BuildSelectingView());
            ResumeLayout(true);

            if (preserveScroll)
            {
                RestoreShotListScrollPositions(selectedScrollY, allScrollY);
            }
        }

        private Control BuildSelectingView()
        {
            var shots = _node!.Params!.StoryboardShots ?? new List<StoryboardShot>();
            var selectedShots = GetSelectedShots();

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(16, 12, 16, 14),
                BackColor = BackColor,
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118F));

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72F));
            header.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(214, 220, 232),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = $"已选择 {selectedShots.Count} / {shots.Count} 个分镜",
            }, 0, 0);

            var toggleLink = new LinkLabel
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                LinkColor = Color.FromArgb(165, 116, 255),
                ActiveLinkColor = Color.FromArgb(195, 156, 255),
                VisitedLinkColor = Color.FromArgb(165, 116, 255),
                Text = selectedShots.Count > 0 && selectedShots.Count == shots.Count ? "取消全选" : "全选",
            };
            toggleLink.LinkClicked += (_, _) =>
            {
                InteractionStarted?.Invoke(this, EventArgs.Empty);
                _node.Params!.StoryboardVideoSelectedShotIds = selectedShots.Count > 0 && selectedShots.Count == shots.Count
                    ? new List<string>()
                    : shots.Select(shot => shot.Id).ToList();
                InvalidateGeneratedPromptForSelectionChange();
                EntryChanged?.Invoke(this, EventArgs.Empty);
                Rebuild(preserveScroll: true);
            };
            header.Controls.Add(toggleLink, 1, 0);
            root.Controls.Add(header, 0, 0);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 8, 0, 10) };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
            body.Controls.Add(BuildShotListPanel("已选择的分镜", selectedShots, false, "请从右侧勾选分镜"), 0, 0);
            body.Controls.Add(BuildShotListPanel("所有分镜", shots, true, "请先连接“分镜图拆解”节点后再获取分镜。"), 1, 0);
            root.Controls.Add(body, 0, 1);

            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.FromArgb(34, 35, 40),
                Padding = new Padding(14, 10, 14, 10),
            };
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            footer.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(154, 162, 184),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
                Text = shots.Count == 0 ? "请先连接“分镜图拆解”节点。" : "请在右侧勾选需要输出的分镜镜头。",
            }, 0, 0);

            footer.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(186, 170, 238),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 8.7F, FontStyle.Regular, GraphicsUnit.Point),
                Text = BuildModelHintText(),
            }, 0, 1);

            var actionRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = Padding.Empty,
            };
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108F));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280F));

            var languageCombo = CreateConfigComboBox();
            languageCombo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            languageCombo.Margin = new Padding(0, 4, 10, 0);
            PopulateConfigComboBox(languageCombo, PromptLanguageOptions, NormalizePromptLanguage(_node!.Params!.StoryboardVideoPromptLanguage), allowTextEntry: false);
            languageCombo.SelectionChangeCommitted += (_, _) => CommitPromptLanguage(languageCombo);
            actionRow.Controls.Add(languageCombo, 1, 0);

            var soundCheckBox = CreateNeedSoundCheckBox();
            actionRow.Controls.Add(soundCheckBox, 2, 0);

            var actionButton = CreatePrimaryButton(shots.Count == 0 ? "获取分镜" : "生成选中分镜提示词");
            actionButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            actionButton.Margin = Padding.Empty;
            actionButton.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            actionButton.Enabled = !_busy && (shots.Count == 0 || selectedShots.Count > 0);
            actionButton.BackColor = actionButton.Enabled ? Color.FromArgb(114, 78, 255) : Color.FromArgb(74, 74, 92);
            actionButton.ForeColor = actionButton.Enabled ? Color.White : Color.FromArgb(176, 182, 194);
            actionButton.Click += (_, _) =>
            {
                InteractionStarted?.Invoke(this, EventArgs.Empty);
                ActionRequested?.Invoke(this, shots.Count == 0 ? "storyboard-video.fetch-shots" : "storyboard-video.generate-prompt");
            };
            actionRow.Controls.Add(actionButton, 3, 0);
            footer.Controls.Add(actionRow, 0, 2);
            root.Controls.Add(footer, 0, 2);
            return root;
        }
        private Control BuildPromptingView()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(16, 12, 16, 14),
                BackColor = BackColor,
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 252F));

            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46F));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54F));
            content.Controls.Add(BuildPromptImageColumn(), 0, 0);
            content.Controls.Add(BuildPromptEditorColumn(), 1, 0);
            root.Controls.Add(content, 0, 0);
            root.Controls.Add(BuildPromptFooter(), 0, 1);
            return root;
        }

        private Control BuildShotListPanel(string title, IReadOnlyList<StoryboardShot> shots, bool selectable, string emptyText)
        {
            var shell = CreatePanelShell();
            shell.Controls.Add(new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                ForeColor = Color.FromArgb(154, 162, 184),
                Text = title,
                TextAlign = ContentAlignment.MiddleLeft,
            });

            var host = CreateScrollableHost();
            host.Dock = DockStyle.Fill;
            host.Padding = new Padding(0, 8, 0, 0);
            if (selectable)
            {
                _allShotsHost = host;
            }
            else
            {
                _selectedShotsHost = host;
            }

            if (shots.Count == 0)
            {
                host.Controls.Add(new Label
                {
                    Dock = DockStyle.Top,
                    Height = 180,
                    ForeColor = Color.FromArgb(102, 110, 132),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Text = emptyText,
                });
            }
            else
            {
                foreach (var shot in shots)
                {
                    host.Controls.Add(selectable ? BuildShotSelectionCard(shot) : BuildShotThumbCard(shot, false));
                }
            }

            shell.Controls.Add(host);
            return shell;
        }

        private Control BuildPromptImageColumn()
        {
            var shell = CreatePanelShell();
            shell.Controls.Add(new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                ForeColor = Color.FromArgb(154, 162, 184),
                Text = $"分镜图（已选 {GetSelectedShots().Count} / {_node!.Params!.StoryboardShots?.Count ?? 0}）",
                TextAlign = ContentAlignment.MiddleLeft,
            });

            var host = CreateScrollableHost();
            host.Dock = DockStyle.Fill;
            host.Padding = new Padding(0, 8, 0, 0);
            _selectedShotsHost = host;

            var fusedPath = _node.Params.StoryboardVideoFusedImagePath;
            if (!string.IsNullOrWhiteSpace(fusedPath) && File.Exists(fusedPath))
            {
                host.Controls.Add(BuildFusedImageCard(fusedPath));
            }

            foreach (var shot in _node.Params.StoryboardShots ?? new List<StoryboardShot>())
            {
                host.Controls.Add(BuildPromptShotSelectionCard(shot));
            }

            shell.Controls.Add(host);
            return shell;
        }

        private Control BuildPromptEditorColumn()
        {
            var shell = CreatePanelShell();
            shell.Controls.Add(new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                ForeColor = Color.FromArgb(154, 162, 184),
                Text = "视频生成提示词",
                TextAlign = ContentAlignment.MiddleLeft,
            });

            var editor = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(20, 21, 26),
                ForeColor = Color.WhiteSmoke,
                DetectUrls = false,
                Margin = Padding.Empty,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point),
                Text = _node!.Params!.StoryboardVideoPrompt ?? string.Empty,
            };
            editor.Enter += (_, _) => InteractionStarted?.Invoke(this, EventArgs.Empty);
            editor.TextChanged += (_, _) =>
            {
                if (_node?.Params == null)
                {
                    return;
                }

                _node.Params.StoryboardVideoPrompt = editor.Text;
                _node.Params.StoryboardVideoModelPrompt = string.Empty;
                EntryChanged?.Invoke(this, EventArgs.Empty);
            };
            shell.Controls.Add(editor);
            return shell;
        }

        private Control BuildPromptFooter()
        {
            var shell = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.FromArgb(34, 35, 40),
                Padding = new Padding(14, 10, 14, 10),
            };
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 132F));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

            var headerRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
            };
            headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
            headerRow.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(154, 162, 184),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = BuildPromptFooterHint(),
            }, 0, 0);
            headerRow.Controls.Add(CreateNeedSoundCheckBox(), 1, 0);
            shell.Controls.Add(headerRow, 0, 0);

            var configGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 3,
                Margin = new Padding(0, 8, 0, 8),
            };
            configGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
            configGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            configGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            configGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
            configGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            configGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));

            var selectedVideoModel = ResolveSelectedStoryboardVideoModel();
            var selectedIsCloud = selectedVideoModel != null &&
                                  ModelConfig.GetModelSource(selectedVideoModel) == ModelEndpointSource.Cloud;
            var selectedIsComfyUi = selectedVideoModel == null || ModelConfig.IsComfyUiEndpointUrl(selectedVideoModel.Url);

            configGrid.Controls.Add(CreateConfigLabel(selectedIsCloud ? "模型平台名称" : selectedIsComfyUi ? "API名称" : "模型名称"), 0, 0);
            configGrid.Controls.Add(CreateConfigLabel(selectedIsCloud ? "模型名称" : "模型ID"), 1, 0);
            configGrid.Controls.Add(CreateConfigLabel(selectedIsCloud ? "清晰度" : selectedIsComfyUi ? "JSON名称" : "工作流"), 2, 0);
            configGrid.Controls.Add(BuildPlatformEditor(), 0, 1);
            configGrid.Controls.Add(BuildModelFamilyEditor(), 1, 1);
            configGrid.Controls.Add(selectedIsCloud ? BuildCloudQualityEditor() : BuildSubModelEditor(), 2, 1);
            configGrid.Controls.Add(BuildAutoVideoRuleHint(), 0, 2);
            configGrid.SetColumnSpan(configGrid.GetControlFromPosition(0, 2)!, 3);
            shell.Controls.Add(configGrid, 0, 1);

            shell.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(196, 164, 255),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Text = BuildModelHintText(),
            }, 0, 2);

            var actionRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84F));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104F));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128F));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var backButton = CreateSecondaryButton("返回");
            backButton.Enabled = !_busy;
            backButton.Click += (_, _) =>
            {
                InteractionStarted?.Invoke(this, EventArgs.Empty);
                ActionRequested?.Invoke(this, "storyboard-video.back-to-selecting");
            };

            var repromptButton = CreateSecondaryButton("重新生成提示词");
            repromptButton.Enabled = !_busy && GetSelectedShots().Count > 0;
            repromptButton.Click += (_, _) =>
            {
                InteractionStarted?.Invoke(this, EventArgs.Empty);
                ActionRequested?.Invoke(this, "storyboard-video.generate-prompt");
            };

            var fetchByTaskIdButton = CreateSecondaryButton("按ID获取");
            fetchByTaskIdButton.Enabled = !_busy;
            fetchByTaskIdButton.Click += (_, _) =>
            {
                InteractionStarted?.Invoke(this, EventArgs.Empty);
                ActionRequested?.Invoke(this, "storyboard-video.fetch-video-by-task-id");
            };

            var selectedShotCount = GetSelectedShots().Count;
            var generateButton = CreatePrimaryButton(_busy ? "生成中..." : $"生成选中分镜视频（{selectedShotCount}）");
            generateButton.Enabled = !_busy && selectedShotCount > 0 && !string.IsNullOrWhiteSpace(_node!.Params!.StoryboardVideoPrompt);
            generateButton.BackColor = generateButton.Enabled ? Color.FromArgb(255, 112, 34) : Color.FromArgb(96, 86, 84);
            generateButton.ForeColor = generateButton.Enabled ? Color.White : Color.FromArgb(176, 182, 194);
            generateButton.Click += (_, _) =>
            {
                InteractionStarted?.Invoke(this, EventArgs.Empty);
                ActionRequested?.Invoke(this, "storyboard-video.generate-video");
            };

            actionRow.Controls.Add(backButton, 0, 0);
            actionRow.Controls.Add(fetchByTaskIdButton, 1, 0);
            actionRow.Controls.Add(repromptButton, 2, 0);
            actionRow.Controls.Add(generateButton, 3, 0);
            shell.Controls.Add(actionRow, 0, 3);
            return shell;
        }
        private Control BuildShotSelectionCard(StoryboardShot shot)
        {
            var selected = (_node!.Params!.StoryboardVideoSelectedShotIds ?? new List<string>())
                .Contains(shot.Id, StringComparer.OrdinalIgnoreCase);

            var shell = new Panel
            {
                Width = 290,
                Height = 102,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(8),
                BackColor = selected ? Color.FromArgb(44, 56, 84) : Color.FromArgb(24, 25, 30),
                Cursor = _busy ? Cursors.Default : Cursors.Hand,
            };
            shell.Enabled = !_busy;
            shell.Click += (_, _) => ToggleShotSelection(shot.Id);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var checkBox = new CheckBox { Dock = DockStyle.Fill, Checked = selected, Margin = Padding.Empty };
            checkBox.CheckedChanged += (_, _) => ToggleShotSelection(shot.Id, checkBox.Checked);
            root.Controls.Add(checkBox, 0, 0);

            var thumb = CreateThumbnail(shot.SplitImagePath, 118, 72);
            thumb.Click += (_, _) => ToggleShotSelection(shot.Id);
            root.Controls.Add(thumb, 1, 0);

            var info = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(14, 2, 0, 0) };
            info.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            info.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            info.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(214, 220, 232),
                AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold, GraphicsUnit.Point),
                Text = $"#{Math.Max(1, shot.ShotNumber)}  {shot.DisplayTitle}",
            }, 0, 0);
            info.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(154, 162, 184),
                Font = new Font("Microsoft YaHei UI", 8.4F, FontStyle.Regular, GraphicsUnit.Point),
                Text = shot.VisualPreview,
            }, 0, 1);
            info.Click += (_, _) => ToggleShotSelection(shot.Id);
            root.Controls.Add(info, 2, 0);

            shell.Controls.Add(root);
            return shell;
        }

        private Control BuildShotThumbCard(StoryboardShot shot, bool showMeta)
        {
            var shell = new Panel
            {
                Width = 184,
                Height = showMeta ? 132 : 108,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(8),
                BackColor = Color.FromArgb(24, 25, 30),
            };

            var thumb = CreateThumbnail(shot.SplitImagePath, 168, 82);
            thumb.Dock = DockStyle.Top;
            shell.Controls.Add(thumb);

            shell.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(214, 220, 232),
                AutoEllipsis = true,
                TextAlign = ContentAlignment.TopLeft,
                Text = showMeta
                    ? $"#{Math.Max(1, shot.ShotNumber)} {shot.DisplayTitle}{Environment.NewLine}{shot.DurationLabel}"
                    : $"#{Math.Max(1, shot.ShotNumber)} {shot.DisplayTitle}",
            });

            return shell;
        }

        private Control BuildPromptShotSelectionCard(StoryboardShot shot)
        {
            var selected = (_node!.Params!.StoryboardVideoSelectedShotIds ?? new List<string>())
                .Contains(shot.Id, StringComparer.OrdinalIgnoreCase);
            var shell = new Panel
            {
                Width = 184,
                Height = 150,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(8),
                BackColor = selected ? Color.FromArgb(44, 56, 84) : Color.FromArgb(24, 25, 30),
                Cursor = _busy ? Cursors.Default : Cursors.Hand,
            };
            shell.Enabled = !_busy;

            var header = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1, Height = 24, Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62F));
            header.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(214, 220, 232),
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = $"#{Math.Max(1, shot.ShotNumber)}",
            }, 0, 0);

            var checkBox = new CheckBox
            {
                Dock = DockStyle.Fill,
                Checked = selected,
                Text = "生成",
                ForeColor = Color.WhiteSmoke,
                TextAlign = ContentAlignment.MiddleRight,
                CheckAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty,
            };
            checkBox.CheckedChanged += (_, _) => ToggleShotSelection(shot.Id, checkBox.Checked, invalidatePrompt: false);
            header.Controls.Add(checkBox, 1, 0);
            shell.Controls.Add(header);

            var thumb = CreateThumbnail(shot.SplitImagePath, 168, 82);
            thumb.Dock = DockStyle.Top;
            thumb.Top = header.Bottom;
            thumb.Click += (_, _) => ToggleShotSelection(shot.Id, null, invalidatePrompt: false);
            shell.Controls.Add(thumb);

            shell.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = selected ? Color.White : Color.FromArgb(154, 162, 184),
                AutoEllipsis = true,
                TextAlign = ContentAlignment.TopLeft,
                Text = $"{shot.DisplayTitle}{Environment.NewLine}{shot.DurationLabel}",
            });

            shell.Click += (_, _) => ToggleShotSelection(shot.Id, null, invalidatePrompt: false);
            return shell;
        }

        private Control BuildFusedImageCard(string filePath)
        {
            var shell = new Panel
            {
                Width = 164,
                Height = 180,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(8),
                BackColor = Color.FromArgb(24, 25, 30),
            };

            var header = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1, Height = 24, Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52F));
            header.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(196, 164, 255),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "融合参考图",
            }, 0, 0);

            var openButton = CreateMiniButton("放大");
            openButton.Click += (_, _) => OpenImageGallery(new List<string> { filePath }, 0, "分镜视频参考图");
            header.Controls.Add(openButton, 1, 0);
            shell.Controls.Add(header);

            var picture = CreateThumbnail(filePath, 148, 138);
            picture.Dock = DockStyle.Fill;
            picture.Click += (_, _) => OpenImageGallery(new List<string> { filePath }, 0, "分镜视频参考图");
            shell.Controls.Add(picture);
            return shell;
        }

        private Control BuildAspectRatioRow()
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            var landscapeButton = CreateToggleButton("16:9", string.Equals(_node!.Params!.StoryboardVideoAspectRatio, "16:9", StringComparison.Ordinal));
            landscapeButton.Click += (_, _) => SetAspectRatio("16:9");
            row.Controls.Add(landscapeButton, 0, 0);

            var portraitButton = CreateToggleButton("9:16", string.Equals(_node.Params.StoryboardVideoAspectRatio, "9:16", StringComparison.Ordinal));
            portraitButton.Click += (_, _) => SetAspectRatio("9:16");
            row.Controls.Add(portraitButton, 1, 0);
            return row;
        }

        private static Control BuildAutoVideoRuleHint()
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(196, 164, 255),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = false,
                Text = "生成规则：使用 1080P 高清分镜参考图；时长按每个分镜提示词；横竖屏按传入参考图宽高自动判断；生成时按分镜顺序逐段输出。",
            };
        }

        private Control BuildPlatformEditor()
        {
            var combo = CreateConfigComboBox();
            var options = GetPlatformOptions().ToList();
            var selectedValue = GetSelectedStoryboardVideoModelSelector();
            PopulateConfigComboBox(combo, options, selectedValue, allowTextEntry: false);
            combo.SelectionChangeCommitted += (_, _) =>
            {
                CommitStoryboardVideoPlatform(combo);
            };
            return combo;
        }

        private Control BuildModelFamilyEditor()
        {
            var combo = CreateConfigComboBox();
            var options = BuildModelFamilyOptions().ToList();
            var selectedValue = GetSelectedStoryboardVideoModelSelector();
            PopulateConfigComboBox(combo, options, selectedValue, allowTextEntry: false);
            combo.SelectionChangeCommitted += (_, _) =>
            {
                CommitStoryboardVideoModelFamily(combo);
            };
            return combo;
        }

        private Control BuildSubModelEditor()
        {
            var combo = CreateConfigComboBox();
            var options = BuildWorkflowOptions().ToList();
            var selected = GetSelectedStoryboardVideoModelSelector();
            PopulateConfigComboBox(combo, options, selected, allowTextEntry: false);
            combo.SelectionChangeCommitted += (_, _) =>
            {
                CommitStoryboardVideoSubModel(combo);
            };
            return combo;
        }

        private Control BuildDurationEditor()
        {
            var durationOptions = BuildAvailableDurationOptions().ToList();
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = Math.Max(1, durationOptions.Count), RowCount = 1, Margin = Padding.Empty };
            for (var index = 0; index < durationOptions.Count; index++)
            {
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / Math.Max(1, durationOptions.Count)));
            }

            foreach (var duration in durationOptions)
            {
                var selected = string.Equals(duration, Math.Max(1, _node!.Params!.StoryboardVideoDurationSeconds).ToString(), StringComparison.Ordinal);
                var button = CreateToggleButton($"{duration}秒", selected);
                var value = int.Parse(duration);
                button.Click += (_, _) => SetStoryboardVideoDurationSeconds(value);
                row.Controls.Add(button);
            }

            return row;
        }

        private IEnumerable<string> BuildAvailableDurationOptions()
        {
            return new[] { "5", "10", "15" };
        }

        private Control BuildQualityEditor()
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = QualityOptions.Length, RowCount = 1, Margin = Padding.Empty };
            for (var index = 0; index < QualityOptions.Length; index++)
            {
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / QualityOptions.Length));
            }

            foreach (var quality in QualityOptions)
            {
                var selected = string.Equals(_node!.Params!.StoryboardVideoQuality, quality, StringComparison.Ordinal);
                var button = CreateToggleButton(quality, selected);
                button.Click += (_, _) => SetStoryboardVideoQuality(quality);
                row.Controls.Add(button);
            }

            return row;
        }

        private Control BuildCloudQualityEditor()
        {
            var combo = CreateConfigComboBox();
            PopulateConfigComboBox(combo, new[]
            {
                new ConfigOptionItem("1080P", "1080P"),
                new ConfigOptionItem("720P", "720P"),
            }, NormalizeCloudVideoQuality(_node?.Params?.StoryboardVideoQuality), allowTextEntry: false);
            combo.SelectionChangeCommitted += (_, _) =>
            {
                if (_node?.Params == null)
                {
                    return;
                }

                _node.Params.StoryboardVideoQuality = combo.SelectedValue?.ToString().OrDefault(combo.Text) ?? "1080P";
                EntryChanged?.Invoke(this, EventArgs.Empty);
                Rebuild(preserveScroll: true);
            };
            return combo;
        }

        private IEnumerable<ConfigOptionItem> GetPlatformOptions()
        {
            return BuildVideoModelOptions(model => string.IsNullOrWhiteSpace(model.Name) ? model.Id : model.Name);
        }

        private IEnumerable<ConfigOptionItem> BuildModelFamilyOptions()
        {
            return BuildVideoModelOptions(model => string.IsNullOrWhiteSpace(model.Id) ? "未填写模型ID" : model.Id);
        }

        private IEnumerable<ConfigOptionItem> BuildWorkflowOptions()
        {
            return BuildVideoModelOptions(FormatVideoWorkflowName);
        }

        private IEnumerable<ConfigOptionItem> BuildVideoModelOptions(Func<ModelInfo, string> displaySelector)
        {
            var settings = ModelConfig.Load();
            var models = settings.Models?
                .Where(model => model.Category == ModelCategory.Video)
                .Where(model => !string.IsNullOrWhiteSpace(ModelConfig.GetModelSelector(model)))
                .ToList() ?? new List<ModelInfo>();

            foreach (var model in models)
            {
                var displayName = displaySelector(model);
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = ModelConfig.GetModelSelector(model);
                }

                yield return new ConfigOptionItem(ModelConfig.GetModelSelector(model), displayName);
            }
        }

        private ModelInfo? ResolveSelectedStoryboardVideoModel()
        {
            var selector = GetSelectedStoryboardVideoModelSelector();
            if (string.IsNullOrWhiteSpace(selector))
            {
                return null;
            }

            var settings = ModelConfig.Load();
            return ModelConfig.FindModel(settings, ModelCategory.Video, selector);
        }

        private static string NormalizeCloudVideoQuality(string? quality)
        {
            var value = (quality ?? string.Empty).Trim();
            return string.Equals(value, "720P", StringComparison.OrdinalIgnoreCase) ? "720P" : "1080P";
        }

        private string GetSelectedStoryboardVideoModelSelector()
        {
            if (_node?.Params == null)
            {
                return string.Empty;
            }

            var settings = ModelConfig.Load();
            var preferred = _node.Params.GetPreferredModelId(ModelCategory.Video);
            if (!string.IsNullOrWhiteSpace(preferred) &&
                settings.Models.Any(model => model.Category == ModelCategory.Video && ModelConfig.MatchesModelSelector(model, preferred)))
            {
                return preferred;
            }

            var resolved = WorkflowModelResolver.ResolveStoryboardVideoExecutionModel(settings, _node);
            return ModelConfig.GetModelSelector(resolved);
        }

        private static string FormatVideoWorkflowName(ModelInfo model)
        {
            var workflow = ModelConfig.ResolveComfyUiWorkflowJson(model);
            return string.IsNullOrWhiteSpace(workflow) ? "未设置工作流" : workflow;
        }

        private void CommitStoryboardVideoPlatform(ComboBox combo)
        {
            if (_syncingConfigEditors)
            {
                return;
            }

            var selectedValue = (combo.SelectedItem as ConfigOptionItem)?.Value;
            if (string.IsNullOrWhiteSpace(selectedValue))
            {
                selectedValue = combo.Text;
            }

            SetStoryboardVideoModelSelector(selectedValue);
        }

        private void CommitStoryboardVideoModelFamily(ComboBox combo)
        {
            if (_syncingConfigEditors)
            {
                return;
            }

            var selectedValue = (combo.SelectedItem as ConfigOptionItem)?.Value;
            if (string.IsNullOrWhiteSpace(selectedValue))
            {
                selectedValue = combo.Text;
            }

            SetStoryboardVideoModelSelector(selectedValue);
        }

        private void CommitStoryboardVideoSubModel(ComboBox combo)
        {
            if (_syncingConfigEditors)
            {
                return;
            }

            var selectedValue = (combo.SelectedItem as ConfigOptionItem)?.Value;
            if (string.IsNullOrWhiteSpace(selectedValue))
            {
                selectedValue = combo.Text;
            }

            SetStoryboardVideoModelSelector(selectedValue);
        }

        private static string NormalizeStoryboardVideoPlatform(string? platform)
        {
            return "local";
        }

        private static string NormalizeStoryboardVideoModelFamily(string? family)
        {
            return "comfyui";
        }

        private static string NormalizeStoryboardVideoSubModel(string? subModel)
        {
            return "workflow";
        }

        private static string NormalizePromptLanguage(string? language)
        {
            return string.Equals((language ?? string.Empty).Trim(), "en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh";
        }

        private void PopulateConfigComboBox(ComboBox combo, IEnumerable<ConfigOptionItem> options, string selectedValue, bool allowTextEntry)
        {
            _syncingConfigEditors = true;
            try
            {
                var normalizedSelectedValue = (selectedValue ?? string.Empty).Trim();
                combo.Items.Clear();
                var optionList = options?
                    .Where(option => option != null && !string.IsNullOrWhiteSpace(option.Value))
                    .GroupBy(option => option.Value, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList() ?? new List<ConfigOptionItem>();

                foreach (var option in optionList)
                {
                    combo.Items.Add(option);
                }

                if (allowTextEntry &&
                    !string.IsNullOrWhiteSpace(normalizedSelectedValue) &&
                    !optionList.Any(option => string.Equals(option.Value, normalizedSelectedValue, StringComparison.OrdinalIgnoreCase)))
                {
                    var customOption = new ConfigOptionItem(normalizedSelectedValue, normalizedSelectedValue);
                    combo.Items.Add(customOption);
                    optionList.Add(customOption);
                }

                var matchedIndex = optionList.FindIndex(option => string.Equals(option.Value, normalizedSelectedValue, StringComparison.OrdinalIgnoreCase));
                combo.SelectedIndex = matchedIndex;
                if (matchedIndex >= 0)
                {
                    combo.Text = optionList[matchedIndex].DisplayName;
                }
                else if (!string.IsNullOrWhiteSpace(normalizedSelectedValue))
                {
                    combo.Text = normalizedSelectedValue;
                }
            }
            finally
            {
                _syncingConfigEditors = false;
            }
        }

        private void SetStoryboardVideoModelSelector(string selector)
        {
            if (_node?.Params == null)
            {
                return;
            }

            var settings = ModelConfig.Load();
            var selectedModel = settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Video &&
                ModelConfig.MatchesModelSelector(model, selector));
            if (selectedModel == null)
            {
                return;
            }

            _node.Params.SetPreferredModelId(ModelCategory.Video, ModelConfig.GetModelSelector(selectedModel));
            _node.Params.StoryboardVideoPlatform = string.IsNullOrWhiteSpace(selectedModel.Name) ? "本地视频模型" : selectedModel.Name;
            _node.Params.StoryboardVideoModelFamily = selectedModel.Id.OrDefault("未填写模型ID");
            _node.Params.StoryboardVideoSubModel = FormatVideoWorkflowName(selectedModel);
            EnsureCompatibleSelections();
            EntryChanged?.Invoke(this, EventArgs.Empty);
            Rebuild();
        }

        private void CommitPromptLanguage(ComboBox combo)
        {
            if (_syncingConfigEditors)
            {
                return;
            }

            var selectedValue = (combo.SelectedItem as ConfigOptionItem)?.Value;
            if (string.IsNullOrWhiteSpace(selectedValue))
            {
                selectedValue = combo.Text;
            }

            SetPromptLanguage(selectedValue);
        }

        private void SetPromptLanguage(string language)
        {
            if (_node?.Params == null)
            {
                return;
            }

            _node.Params.StoryboardVideoPromptLanguage = NormalizePromptLanguage(language);
            EntryChanged?.Invoke(this, EventArgs.Empty);
            Rebuild();
        }

        private void SetNeedSound(bool needSound)
        {
            if (_node?.Params == null)
            {
                return;
            }

            _node.Params.StoryboardVideoNeedSound = needSound;
            EntryChanged?.Invoke(this, EventArgs.Empty);
            Rebuild();
        }

        private void SetStoryboardVideoDurationSeconds(int seconds)
        {
            if (_node?.Params == null)
            {
                return;
            }

            var allowedDurations = BuildAvailableDurationOptions()
                .Select(int.Parse)
                .ToList();
            _node.Params.StoryboardVideoDurationSeconds = allowedDurations.Contains(seconds)
                ? seconds
                : allowedDurations.LastOrDefault(5);
            EntryChanged?.Invoke(this, EventArgs.Empty);
            Rebuild();
        }

        private void SetStoryboardVideoQuality(string quality)
        {
            if (_node?.Params == null)
            {
                return;
            }

            _node.Params.StoryboardVideoQuality = QualityOptions.Contains(quality, StringComparer.Ordinal)
                ? quality
                : "高清";
            EntryChanged?.Invoke(this, EventArgs.Empty);
            Rebuild();
        }
        private void ToggleShotSelection(string shotId, bool? value = null, bool invalidatePrompt = true)
        {
            if (_node?.Params == null || string.IsNullOrWhiteSpace(shotId))
            {
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            var selectedIds = (_node.Params.StoryboardVideoSelectedShotIds ?? new List<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var contains = selectedIds.Contains(shotId, StringComparer.OrdinalIgnoreCase);
            var shouldSelect = value ?? !contains;
            var changed = false;

            if (shouldSelect && !contains)
            {
                selectedIds.Add(shotId);
                changed = true;
            }
            else if (!shouldSelect && contains)
            {
                selectedIds.RemoveAll(id => string.Equals(id, shotId, StringComparison.OrdinalIgnoreCase));
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            _node.Params.StoryboardVideoSelectedShotIds = selectedIds;
            if (invalidatePrompt)
            {
                InvalidateGeneratedPromptForSelectionChange();
            }

            EntryChanged?.Invoke(this, EventArgs.Empty);
            Rebuild(preserveScroll: true);
        }

        private void InvalidateGeneratedPromptForSelectionChange()
        {
            if (_node?.Params == null)
            {
                return;
            }

            _node.Params.StoryboardVideoStage = "selecting";
            _node.Params.StoryboardVideoPrompt = string.Empty;
            _node.Params.StoryboardVideoModelPrompt = string.Empty;
            _node.Params.StoryboardVideoGeneratedClips.Clear();
            _node.Params.StoryboardVideoFusedImagePath = string.Empty;
            _node.Params.StoryboardVideoTaskId = string.Empty;
            _node.Params.StoryboardVideoTaskQueryUrl = string.Empty;
            _node.Params.StoryboardVideoLastError = string.Empty;
            _node.Output = string.Empty;
        }

        private static int GetScrollY(ScrollableControl? host)
        {
            return host == null ? 0 : Math.Abs(host.AutoScrollPosition.Y);
        }

        private void RestoreShotListScrollPositions(int selectedScrollY, int allScrollY)
        {
            void Restore()
            {
                RestoreScrollY(_selectedShotsHost, selectedScrollY);
                RestoreScrollY(_allShotsHost, allScrollY);
            }

            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke((Action)Restore);
            }
            else
            {
                Restore();
            }
        }

        private static void RestoreScrollY(ScrollableControl? host, int scrollY)
        {
            if (host == null || scrollY <= 0)
            {
                return;
            }

            var maximum = Math.Max(0, host.VerticalScroll.Maximum);
            host.AutoScrollPosition = new Point(0, Math.Min(scrollY, maximum));
        }

        private List<StoryboardShot> GetSelectedShots()
        {
            if (_node?.Params == null)
            {
                return new List<StoryboardShot>();
            }

            var selectedIds = (_node.Params.StoryboardVideoSelectedShotIds ?? new List<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return (_node.Params.StoryboardShots ?? new List<StoryboardShot>())
                .Where(shot => selectedIds.Contains(shot.Id))
                .Select(shot => shot.Clone())
                .ToList();
        }

        private string BuildPromptFooterHint()
        {
            if (_node?.Params == null)
            {
                return string.Empty;
            }

            var stageText = WorkflowNodeParameters.GetStoryboardVideoStageDisplayName(_node.Params.StoryboardVideoStage);
            return $"当前阶段：{stageText}  |  已选分镜：{(_node.Params.StoryboardVideoSelectedShotIds?.Count ?? 0)}  |  提示词：中文显示 / 英文执行  |  声音：{(_node.Params.StoryboardVideoNeedSound ? "需要" : "关闭")}";
        }

        private string BuildModelHintText()
        {
            if (_node == null)
            {
                return "当前调用：文本模型 未配置  |  视频接口 未配置";
            }

            var settings = ModelConfig.Load();
            var preferredTextId = _node.Params?.GetPreferredModelId(ModelCategory.Text);
            var textModel = settings.Models.FirstOrDefault(model =>
                                model.Category == ModelCategory.Text &&
                                ModelConfig.MatchesModelSelector(model, preferredTextId))
                            ?? ModelConfig.GetPreferredLocalTextModel(settings)
                            ?? settings.Models.FirstOrDefault(model =>
                                model.Category == ModelCategory.Text &&
                                ModelConfig.MatchesModelSelector(model, settings.SelectedTextModel))
                            ?? ModelConfig.GetPreferredCloudTextModel(settings);
            var videoModel = WorkflowModelResolver.ResolveStoryboardVideoExecutionModel(settings, _node);
            var videoWorkflow = videoModel == null ? "未配置" : FormatVideoWorkflowName(videoModel);
            return $"当前调用：文本模型 {textModel?.Name ?? "未配置"}  |  视频模型 {videoModel?.Name ?? "未配置"}  |  模型ID {videoModel?.Id ?? "未配置"}  |  工作流 {videoWorkflow}";
        }

        private static FlowLayoutPanel CreateScrollableHost()
        {
            return new FlowLayoutPanel
            {
                AutoScroll = true,
                WrapContents = false,
                FlowDirection = FlowDirection.TopDown,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.Transparent,
            };
        }

        private static Panel CreatePanelShell()
        {
            return new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(24, 25, 30),
                Padding = new Padding(10),
                Margin = Padding.Empty,
            };
        }

        private static Button CreatePrimaryButton(string text)
        {
            var button = new Button
            {
                Dock = DockStyle.Fill,
                Height = 40,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(114, 78, 255),
                ForeColor = Color.White,
                Text = text,
                Cursor = Cursors.Hand,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
                Margin = Padding.Empty,
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private static Button CreateSecondaryButton(string text)
        {
            var button = CreatePrimaryButton(text);
            button.BackColor = Color.FromArgb(56, 60, 74);
            return button;
        }

        private static Button CreateMiniButton(string text)
        {
            var button = new Button
            {
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(56, 60, 74),
                ForeColor = Color.WhiteSmoke,
                Text = text,
                Cursor = Cursors.Hand,
                Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular, GraphicsUnit.Point),
                Margin = Padding.Empty,
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private static Button CreateToggleButton(string text, bool selected)
        {
            var button = new Button
            {
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                Text = text,
                Cursor = Cursors.Hand,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point),
                BackColor = selected ? Color.FromArgb(114, 78, 255) : Color.FromArgb(44, 48, 60),
                ForeColor = Color.White,
                Margin = Padding.Empty,
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private static Label CreateConfigLabel(string text)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(148, 156, 176),
                TextAlign = ContentAlignment.BottomLeft,
                Text = text,
            };
        }

        private static ComboBox CreateConfigComboBox()
        {
            return new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(20, 21, 26),
                ForeColor = Color.WhiteSmoke,
                Margin = Padding.Empty,
            };
        }

        private CheckBox CreateNeedSoundCheckBox()
        {
            var checkBox = new CheckBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(8, 2, 0, 0),
                ForeColor = Color.FromArgb(214, 220, 232),
                Text = "需要声音",
                TextAlign = ContentAlignment.MiddleLeft,
                Checked = _node?.Params?.StoryboardVideoNeedSound == true,
            };
            checkBox.CheckedChanged += (_, _) =>
            {
                if (_syncingConfigEditors)
                {
                    return;
                }

                InteractionStarted?.Invoke(this, EventArgs.Empty);
                SetNeedSound(checkBox.Checked);
            };
            return checkBox;
        }

        private sealed class ConfigOptionItem
        {
            public ConfigOptionItem(string value, string displayName)
            {
                Value = value;
                DisplayName = displayName;
            }

            public string Value { get; }

            public string DisplayName { get; }

            public override string ToString()
            {
                return DisplayName;
            }
        }

        private static PictureBox CreateThumbnail(string filePath, int width, int height)
        {
            var pictureBox = new PictureBox
            {
                Width = width,
                Height = height,
                BackColor = Color.FromArgb(18, 19, 24),
                SizeMode = PictureBoxSizeMode.Zoom,
                Margin = Padding.Empty,
                Cursor = File.Exists(filePath) ? Cursors.Hand : Cursors.Default,
            };

            if (File.Exists(filePath))
            {
                pictureBox.Image = LoadImageCopy(filePath);
                pictureBox.Disposed += (_, _) => pictureBox.Image?.Dispose();
            }

            return pictureBox;
        }

        private void SetAspectRatio(string aspectRatio)
        {
            if (_node?.Params == null)
            {
                return;
            }

            _node.Params.StoryboardVideoAspectRatio = string.Equals(aspectRatio, "9:16", StringComparison.Ordinal) ? "9:16" : "16:9";
            EntryChanged?.Invoke(this, EventArgs.Empty);
            Rebuild();
        }

        private void OpenImageGallery(IReadOnlyList<string> imagePaths, int startIndex, string title)
        {
            if (imagePaths == null || imagePaths.Count == 0)
            {
                return;
            }

            using var form = new ImageGalleryForm(imagePaths.ToList(), Math.Max(0, Math.Min(startIndex, imagePaths.Count - 1)), title);
            var owner = FindForm();
            if (owner == null)
            {
                form.ShowDialog();
            }
            else
            {
                form.ShowDialog(owner);
            }
        }

        private static Image? LoadImageCopy(string filePath)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return Image.FromStream(stream);
            }
            catch
            {
                return null;
            }
        }
    }
}
