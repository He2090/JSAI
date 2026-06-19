using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public sealed class WorkflowNodeCard : UserControl
    {
        public const int CardWidth = 320;
        public const int CardHeight = 360;
        public const int OutlineCardWidth = 428;
        public const int OutlineCardHeight = 724;
        public const int ScriptCardWidth = 428;
        public const int ScriptCardHeight = 612;
        public const int CharacterCardWidth = 428;
        public const int CharacterCardHeight = 820;
        public const int StoryboardBreakdownCardWidth = 408;
        public const int StoryboardBreakdownCardHeight = 760;
        public const int StoryboardImageCardWidth = 408;
        public const int StoryboardImageCardHeight = 824;
        public const int StoryboardVideoCardWidth = 640;
        public const int StoryboardVideoCardHeight = 836;
        public const int VideoCollectionCardWidth = 980;
        public const int VideoCollectionCardHeight = 840;
        public const int VideoPreviewCardWidth = 360;
        public const int VideoPreviewCardHeight = 280;
        public const int CreativeCardWidth = 428;
        public const int CreativeCardHeight = 540;
        public const int TextToImageCardWidth = 860;
        public const int TextToImageCardHeight = 960;
        public const int DirectStudioCardWidth = 900;
        public const int DirectStudioCardHeight = 880;
        public const int MinimizedCardHeight = 54;

        private readonly Panel _header;
        private readonly Label _titleLabel;
        private readonly Button _runButton;
        private readonly Button _deleteButton;
        private readonly Button _saveButton;
        private readonly Button _minimizeButton;
        private readonly WorkflowPortHandle _inputPort;
        private readonly WorkflowPortHandle _outputPort;
        private readonly TextBox _inputTextBox;
        private readonly RichTextBox _outputTextBox;
        private readonly TextBox _outlineIdeaTextBox;
        private readonly ComboBox _genreComboBox;
        private readonly ComboBox _settingComboBox;
        private readonly Label _outlineOutputPlaceholderLabel;
        private readonly Label _scriptOutputPlaceholderLabel;
        private readonly Label _outlineStatusLabel;
        private readonly Label _scriptStatusLabel;
        private readonly Button _outlineGenerateButton;
        private readonly Button _outlineResetButton;
        private readonly ComboBox _scriptChapterComboBox;
        private readonly NumericUpDown _scriptEpisodesToGenerateNumeric;
        private readonly TextBox _scriptRevisionNotesTextBox;
        private readonly Button _scriptGenerateButton;
        private readonly ScriptEpisodePanel _scriptEpisodePanel;
        private readonly CreativeDescriptionPanel _creativeDescriptionPanel;
        private readonly DirectStudioNodePanel _directStudioNodePanel;
        private readonly CharacterDesignPanel _characterDesignPanel;
        private readonly StoryboardBreakdownPanel _storyboardBreakdownPanel;
        private readonly StoryboardVideoPanel _storyboardVideoPanel;
        private readonly VideoCollectionPanel _videoCollectionPanel;
        private readonly PictureBox _storyboardPreviewPictureBox;
        private readonly Label _storyboardPreviewPlaceholderLabel;
        private readonly Label _storyboardConnectionLabel;
        private readonly Label _storyboardModelLabel;
        private readonly Label _storyboardPageLabel;
        private readonly Button _storyboardPrevPageButton;
        private readonly Button _storyboardNextPageButton;
        private readonly Button _storyboardOpenButton;
        private readonly Button _storyboardEditButton;
        private readonly Button _storyboardGrid3x3Button;
        private readonly Button _storyboardGrid2x3Button;
        private readonly Button _storyboardLandscapeButton;
        private readonly Button _storyboardPortraitButton;
        private readonly Button _storyboardGenerateButton;
        private readonly List<OutlineChapterPlan> _scriptChapterPlans = new();
        private readonly Dictionary<string, Button> _styleButtons = new(StringComparer.Ordinal);
        private readonly NumericUpDown _episodesNumeric;
        private readonly NumericUpDown _durationNumeric;
        private readonly Size _baseSize;
        private readonly Control _body;
        private int _storyboardConnectionCount;
        private string _loadedStoryboardPreviewPath = string.Empty;
        private bool _syncing;
        private bool _busy;
        private bool _dragging;
        private bool _selected;
        private Point _dragOrigin;
        private Point _cardOrigin;
        private WorkflowPortKind? _pendingPortKind;
        private float _zoom = 1F;
        private bool _collapsed;

        public string ProjectName { get; set; } = string.Empty;

        public WorkflowDocument? Document { get; set; }

        public WorkflowNodeCard(WorkflowNode node)
        {
            Node = node;
            Node.Params ??= new WorkflowNodeParameters();
            Node.Params.EnsureDefaults(node.Type);

            var defaultSize = GetCardSize(node.Type);
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.Gainsboro;
            Margin = Padding.Empty;
            Cursor = Cursors.Default;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            _baseSize = defaultSize;

            _header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 42,
                BackColor = Color.FromArgb(43, 43, 47),
                Padding = new Padding(12, 7, 10, 7),
                Cursor = Cursors.SizeAll,
            };

            _titleLabel = new Label
            {
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.SizeAll,
            };

            _runButton = new Button
            {
                Dock = DockStyle.Right,
                Width = 58,
                FlatStyle = FlatStyle.Flat,
                Text = "生成",
                BackColor = Color.FromArgb(74, 161, 255),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
            };
            _runButton.FlatAppearance.BorderSize = 0;
            _runButton.Click += (_, _) => RunHeaderButtonAction();

            _deleteButton = new Button
            {
                Dock = DockStyle.Right,
                Width = 58,
                FlatStyle = FlatStyle.Flat,
                Text = "删除",
                BackColor = Color.FromArgb(86, 30, 30),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
            };
            _deleteButton.FlatAppearance.BorderSize = 0;
            _deleteButton.Click += (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty);

            _saveButton = new Button
            {
                Dock = DockStyle.Right,
                Width = 58,
                FlatStyle = FlatStyle.Flat,
                Text = "保存",
                BackColor = Color.FromArgb(56, 60, 74),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Visible = false,
                Enabled = false,
            };
            _saveButton.FlatAppearance.BorderSize = 0;
            _saveButton.Click += (_, _) =>
            {
                if (Node.Type == WorkflowNodeCatalog.StoryboardVideo)
                {
                    NodeActionRequested?.Invoke(this, new WorkflowNodeActionEventArgs(Node, "storyboard-video.save-assets"));
                }
            };

            _minimizeButton = new Button
            {
                Dock = DockStyle.Right,
                Width = 36,
                FlatStyle = FlatStyle.Flat,
                Text = "－",
                BackColor = Color.FromArgb(58, 64, 78),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                TabStop = false,
            };
            _minimizeButton.FlatAppearance.BorderSize = 0;
            _minimizeButton.Click += (_, _) => ToggleMinimized();

            _header.Controls.Add(_titleLabel);
            _header.Controls.Add(_deleteButton);
            _header.Controls.Add(_saveButton);
            _header.Controls.Add(_runButton);
            _header.Controls.Add(_minimizeButton);

            _inputPort = CreatePort(WorkflowPortKind.Input);
            _outputPort = CreatePort(WorkflowPortKind.Output);

            _inputTextBox = CreateEditorTextBox();
            _inputTextBox.TextChanged += InputTextBox_TextChanged;
            _inputTextBox.Enter += (_, _) => SelectRequested?.Invoke(this, EventArgs.Empty);

            _outputTextBox = CreateOutputTextBox();
            _outputTextBox.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
            _outputTextBox.Enter += (_, _) => SelectRequested?.Invoke(this, EventArgs.Empty);

            _outlineIdeaTextBox = CreateEditorTextBox();
            _outlineIdeaTextBox.Height = 76;
            _outlineIdeaTextBox.TextChanged += OutlineIdeaTextBox_TextChanged;
            _outlineIdeaTextBox.Enter += (_, _) => SelectRequested?.Invoke(this, EventArgs.Empty);

            _genreComboBox = CreateComboBox();
            _genreComboBox.SelectedIndexChanged += GenreComboBox_SelectedIndexChanged;

            _settingComboBox = CreateComboBox();
            _settingComboBox.SelectedIndexChanged += SettingComboBox_SelectedIndexChanged;

            _outlineOutputPlaceholderLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = "生成后的故事大纲会显示在这里。",
                ForeColor = Color.FromArgb(118, 128, 146),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
            };

            _scriptOutputPlaceholderLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = "请先连接故事大纲并生成可解析的章节。",
                ForeColor = Color.FromArgb(118, 128, 146),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
            };

            _outlineStatusLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(142, 156, 180),
                TextAlign = ContentAlignment.MiddleLeft,
            };

            _scriptStatusLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(142, 156, 180),
                TextAlign = ContentAlignment.MiddleLeft,
            };

            _outlineGenerateButton = CreateActionButton(Color.FromArgb(255, 140, 34), "生成大纲");
            _outlineGenerateButton.Click += (_, _) => RunRequested?.Invoke(this, EventArgs.Empty);

            _outlineResetButton = CreateActionButton(Color.FromArgb(58, 64, 78), "重置大纲");
            _outlineResetButton.Click += (_, _) => ResetOutlineOutput();

            _scriptChapterComboBox = CreateComboBox();
            _scriptChapterComboBox.SelectedIndexChanged += ScriptChapterComboBox_SelectedIndexChanged;

            _scriptEpisodesToGenerateNumeric = CreateNumericUpDown(1, 20, 1);
            _scriptEpisodesToGenerateNumeric.ValueChanged += ScriptEpisodesToGenerateNumeric_ValueChanged;

            _scriptRevisionNotesTextBox = CreateEditorTextBox();
            _scriptRevisionNotesTextBox.Height = 68;
            _scriptRevisionNotesTextBox.TextChanged += ScriptRevisionNotesTextBox_TextChanged;
            _scriptRevisionNotesTextBox.Enter += (_, _) => SelectRequested?.Invoke(this, EventArgs.Empty);

            _scriptGenerateButton = CreateActionButton(Color.FromArgb(24, 196, 159), "生成分集剧本");
            _scriptGenerateButton.Click += (_, _) => RunRequested?.Invoke(this, EventArgs.Empty);

            _scriptEpisodePanel = new ScriptEpisodePanel
            {
                Dock = DockStyle.Fill,
            };
            _scriptEpisodePanel.InteractionStarted += (_, _) => SelectRequested?.Invoke(this, EventArgs.Empty);
            _scriptEpisodePanel.EpisodeSelectionChanged += (_, _) => NodeChanged?.Invoke(this, EventArgs.Empty);

            _creativeDescriptionPanel = new CreativeDescriptionPanel
            {
                Dock = DockStyle.Fill,
            };
            _creativeDescriptionPanel.InteractionStarted += (_, _) => SelectRequested?.Invoke(this, EventArgs.Empty);
            _creativeDescriptionPanel.EntryChanged += (_, _) => NodeChanged?.Invoke(this, EventArgs.Empty);
            _creativeDescriptionPanel.SplitRequested += (_, _) => RunRequested?.Invoke(this, EventArgs.Empty);

            _directStudioNodePanel = new DirectStudioNodePanel
            {
                Dock = DockStyle.Fill,
            };
            _directStudioNodePanel.InteractionStarted += (_, _) => SelectRequested?.Invoke(this, EventArgs.Empty);
            _directStudioNodePanel.EntryChanged += (_, _) => NodeChanged?.Invoke(this, EventArgs.Empty);
            _directStudioNodePanel.GenerateRequested += (_, _) => RunRequested?.Invoke(this, EventArgs.Empty);

            _characterDesignPanel = new CharacterDesignPanel
            {
                Dock = DockStyle.Fill,
            };
            _characterDesignPanel.InteractionStarted += (_, _) => SelectRequested?.Invoke(this, EventArgs.Empty);
            _characterDesignPanel.EntryChanged += (_, _) => NodeChanged?.Invoke(this, EventArgs.Empty);
            _characterDesignPanel.CharacterActionRequested += (_, e) => CharacterActionRequested?.Invoke(this, e);

            _storyboardBreakdownPanel = new StoryboardBreakdownPanel
            {
                Dock = DockStyle.Fill,
            };
            _storyboardBreakdownPanel.InteractionStarted += (_, _) => SelectRequested?.Invoke(this, EventArgs.Empty);
            _storyboardBreakdownPanel.EntryChanged += (_, _) => NodeChanged?.Invoke(this, EventArgs.Empty);
            _storyboardBreakdownPanel.SplitRequested += (_, _) => RunRequested?.Invoke(this, EventArgs.Empty);
            _storyboardBreakdownPanel.ShotActionRequested += (_, action) => NodeActionRequested?.Invoke(this, new WorkflowNodeActionEventArgs(Node, action));

            _storyboardVideoPanel = new StoryboardVideoPanel
            {
                Dock = DockStyle.Fill,
            };
            _storyboardVideoPanel.InteractionStarted += (_, _) => SelectRequested?.Invoke(this, EventArgs.Empty);
            _storyboardVideoPanel.EntryChanged += (_, _) => NodeChanged?.Invoke(this, EventArgs.Empty);
            _storyboardVideoPanel.ActionRequested += (_, action) => NodeActionRequested?.Invoke(this, new WorkflowNodeActionEventArgs(Node, action));

            _videoCollectionPanel = new VideoCollectionPanel
            {
                Dock = DockStyle.Fill,
            };
            _videoCollectionPanel.InteractionStarted += (_, _) => SelectRequested?.Invoke(this, EventArgs.Empty);
            _videoCollectionPanel.EntryChanged += (_, _) => NodeChanged?.Invoke(this, EventArgs.Empty);
            _videoCollectionPanel.ActionRequested += (_, action) => NodeActionRequested?.Invoke(this, new WorkflowNodeActionEventArgs(Node, action));

            _storyboardPreviewPictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(18, 19, 24),
                SizeMode = PictureBoxSizeMode.Zoom,
                Visible = false,
                Cursor = Cursors.Hand,
            };
            _storyboardPreviewPictureBox.Enter += (_, _) => SelectRequested?.Invoke(this, EventArgs.Empty);
            _storyboardPreviewPictureBox.Click += (_, _) => OpenStoryboardPreview();
            _storyboardPreviewPictureBox.DoubleClick += (_, _) => OpenStoryboardPreview();

            _storyboardPreviewPlaceholderLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(124, 130, 158),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
            };

            _storyboardConnectionLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(116, 185, 255),
                TextAlign = ContentAlignment.MiddleLeft,
            };

            _storyboardModelLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(196, 164, 255),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
            };

            _storyboardPageLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(214, 220, 232),
                TextAlign = ContentAlignment.MiddleCenter,
            };

            _storyboardPrevPageButton = CreateToggleButton("上一页");
            _storyboardPrevPageButton.Margin = Padding.Empty;
            _storyboardPrevPageButton.ForeColor = Color.White;
            _storyboardPrevPageButton.Click += (_, _) => ChangeStoryboardPage(-1);

            _storyboardNextPageButton = CreateToggleButton("下一页");
            _storyboardNextPageButton.Margin = Padding.Empty;
            _storyboardNextPageButton.ForeColor = Color.White;
            _storyboardNextPageButton.Click += (_, _) => ChangeStoryboardPage(1);

            _storyboardOpenButton = CreateToggleButton("放大查看");
            _storyboardOpenButton.Margin = Padding.Empty;
            _storyboardOpenButton.ForeColor = Color.White;
            _storyboardOpenButton.Click += (_, _) => OpenStoryboardPreview();

            _storyboardEditButton = CreateToggleButton("编辑描述");
            _storyboardEditButton.Margin = Padding.Empty;
            _storyboardEditButton.ForeColor = Color.White;
            _storyboardEditButton.Click += (_, _) => OpenStoryboardPageEditor();

            _storyboardGrid3x3Button = CreateToggleButton("九宫格 (3×3)");
            _storyboardGrid3x3Button.Click += (_, _) => SetStoryboardGridLayout("3x3");

            _storyboardGrid2x3Button = CreateToggleButton("六宫格 (2×3)");
            _storyboardGrid2x3Button.Click += (_, _) => SetStoryboardGridLayout("2x3");

            _storyboardLandscapeButton = CreateToggleButton("横屏 (16:9)");
            _storyboardLandscapeButton.Click += (_, _) => SetStoryboardOrientation("16:9");

            _storyboardPortraitButton = CreateToggleButton("竖屏 (9:16)");
            _storyboardPortraitButton.Click += (_, _) => SetStoryboardOrientation("9:16");

            _storyboardGenerateButton = CreateActionButton(Color.FromArgb(176, 74, 255), "生成九宫格分镜图");
            _storyboardGenerateButton.Margin = new Padding(0, 8, 0, 0);
            _storyboardGenerateButton.Click += (_, _) => RunRequested?.Invoke(this, EventArgs.Empty);

            _episodesNumeric = CreateNumericUpDown(5, 100, 1);
            _episodesNumeric.ValueChanged += EpisodesNumeric_ValueChanged;

            _durationNumeric = CreateNumericUpDown(1, 5, 0.5M);
            _durationNumeric.DecimalPlaces = 1;
            _durationNumeric.ValueChanged += DurationNumeric_ValueChanged;

            _body = BuildBody();
            Controls.Add(_body);
            Controls.Add(_header);
            Controls.Add(_inputPort);
            Controls.Add(_outputPort);

            Size = defaultSize;
            MinimumSize = defaultSize;
            MaximumSize = defaultSize;
            Location = new Point(node.X, node.Y);

            AttachSelection(this);
            AttachDrag(this);
            AttachDrag(_header);
            AttachDrag(_titleLabel);
            AttachDragSurface(_body);
            AttachViewportWheel(this);
            MouseDoubleClick += NodeCard_MouseDoubleClick;
            _header.MouseDoubleClick += NodeCard_MouseDoubleClick;
            _titleLabel.MouseDoubleClick += NodeCard_MouseDoubleClick;
            SyncFromNode();
        }

        public WorkflowNode Node { get; }

        public WorkflowPortKind? PendingPortKind
        {
            get => _pendingPortKind;
            set
            {
                if (_pendingPortKind == value)
                {
                    return;
                }

                _pendingPortKind = value;
                _inputPort.Pending = value == WorkflowPortKind.Input;
                _outputPort.Pending = value == WorkflowPortKind.Output;
            }
        }

        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value)
                {
                    return;
                }

                _selected = value;
                Invalidate();
                _inputPort.Selected = value;
                _outputPort.Selected = value;
            }
        }

        public event EventHandler? RunRequested;
        public event EventHandler? DeleteRequested;
        public event EventHandler? SelectRequested;
        public event EventHandler? PositionChanged;
        public event EventHandler? MoveCompleted;
        public event EventHandler? NodeChanged;
        public event EventHandler<WorkflowPortEventArgs>? PortClicked;
        public event EventHandler<WorkflowCharacterActionEventArgs>? CharacterActionRequested;
        public event EventHandler<WorkflowNodeActionEventArgs>? NodeActionRequested;

        private void RunHeaderButtonAction()
        {
            if (Node.Type == WorkflowNodeCatalog.StoryboardVideo)
            {
                var action = ResolveStoryboardVideoHeaderAction();
                if (!string.IsNullOrWhiteSpace(action))
                {
                    NodeActionRequested?.Invoke(this, new WorkflowNodeActionEventArgs(Node, action));
                }

                return;
            }

            RunRequested?.Invoke(this, EventArgs.Empty);
        }

        private string ResolveStoryboardVideoHeaderAction()
        {
            var stage = (Node.Params?.StoryboardVideoStage ?? "idle").Trim().ToLowerInvariant();
            var hasPrompt = !string.IsNullOrWhiteSpace(Node.Params?.StoryboardVideoPrompt);
            var hasShots = Node.Params?.StoryboardShots != null && Node.Params.StoryboardShots.Count > 0;
            var hasSelectedShots = Node.Params?.StoryboardVideoSelectedShotIds != null &&
                                   Node.Params.StoryboardVideoSelectedShotIds.Count > 0;

            if (!hasShots)
            {
                return "storyboard-video.fetch-shots";
            }

            if (stage == "prompting" && hasPrompt)
            {
                return "storyboard-video.generate-video";
            }

            if (stage == "completed" && hasPrompt)
            {
                return "storyboard-video.generate-video";
            }

            return hasSelectedShots
                ? "storyboard-video.generate-prompt"
                : string.Empty;
        }

        public static Size GetCardSize(string nodeType)
        {
            return nodeType switch
            {
                var type when type == WorkflowNodeCatalog.Outline => new Size(OutlineCardWidth, OutlineCardHeight),
                var type when type == WorkflowNodeCatalog.Script => new Size(ScriptCardWidth, ScriptCardHeight),
                var type when type == WorkflowNodeCatalog.CreativeDescription => new Size(CreativeCardWidth, CreativeCardHeight),
                var type when type == WorkflowNodeCatalog.TextToImage
                    => new Size(TextToImageCardWidth, TextToImageCardHeight),
                var type when WorkflowNodeCatalog.IsDirectStudioNodeType(type)
                    => new Size(DirectStudioCardWidth, DirectStudioCardHeight),
                var type when type == WorkflowNodeCatalog.CharacterView || type == WorkflowNodeCatalog.CharacterDescription
                    => new Size(CharacterCardWidth, CharacterCardHeight),
                var type when type == WorkflowNodeCatalog.StoryboardBreakdown
                    => new Size(StoryboardBreakdownCardWidth, StoryboardBreakdownCardHeight),
                var type when type == WorkflowNodeCatalog.StoryboardImage
                    => new Size(StoryboardImageCardWidth, StoryboardImageCardHeight),
                var type when type == WorkflowNodeCatalog.StoryboardVideo
                    => new Size(StoryboardVideoCardWidth, StoryboardVideoCardHeight),
                var type when type == WorkflowNodeCatalog.VideoCollection
                    => new Size(VideoCollectionCardWidth, VideoCollectionCardHeight),
                var type when type == WorkflowNodeCatalog.VideoPreview
                    => new Size(VideoPreviewCardWidth, VideoPreviewCardHeight),
                _ => new Size(CardWidth, CardHeight),
            };
        }

        public void SetScriptChapterOptions(IReadOnlyList<OutlineChapterPlan> chapterPlans)
        {
            _scriptChapterPlans.Clear();
            if (chapterPlans != null)
            {
                _scriptChapterPlans.AddRange(chapterPlans);
            }

            if (Node.Type == WorkflowNodeCatalog.Script)
            {
                SyncFromNode();
            }
        }

        public Point GetPortCenter(WorkflowPortKind portKind)
        {
            var port = portKind == WorkflowPortKind.Input ? _inputPort : _outputPort;
            return new Point(Left + port.Left + port.Width / 2, Top + port.Top + port.Height / 2);
        }

        public void ApplyZoom(float zoom)
        {
            var effectiveZoom = Math.Max(1F, zoom);

            if (effectiveZoom <= 0F || Math.Abs(_zoom - effectiveZoom) < 0.001F)
            {
                return;
            }

            _zoom = effectiveZoom;

            SuspendLayout();
            try
            {
                MinimumSize = Size.Empty;
                MaximumSize = Size.Empty;

                // WinForms child controls do not survive repeated cumulative Scale() calls well.
                // We only resize the card container itself here so zooming stays visually stable.
                ApplySizeForCurrentState(effectiveZoom);
            }
            finally
            {
                ResumeLayout(true);
            }

            PositionPorts();
            Invalidate(true);
        }

        public void SyncFromNode()
        {
            _syncing = true;
            Node.Params ??= new WorkflowNodeParameters();
            Node.Params.EnsureDefaults(Node.Type);
            var nodeParams = Node.Params;
            var displayType = Node.Type == WorkflowNodeCatalog.CharacterDescription
                ? WorkflowNodeCatalog.CharacterView
                : Node.Type;
            _titleLabel.Text = Node.Type == WorkflowNodeCatalog.CreativeDescription && !string.IsNullOrWhiteSpace(Node.Params?.CoreIdea)
                ? $"{displayType} · {nodeParams.CoreIdea}"
                : $"{Node.Id} · {displayType}";
            _runButton.Text = GetRunButtonText(Node.Type);
            ApplyCollapsedState(persist: false);

            if (Node.Type == WorkflowNodeCatalog.Outline)
            {
                if (_outlineIdeaTextBox.Text != nodeParams.CoreIdea)
                {
                    _outlineIdeaTextBox.Text = nodeParams.CoreIdea;
                }

                SyncComboBox(_genreComboBox, WorkflowNodeCatalog.OutlineGenres, nodeParams.Genre, "选择类型 (Genre)");
                SyncComboBox(_settingComboBox, WorkflowNodeCatalog.OutlineSettings, nodeParams.Setting, "选择背景 (Setting)");
                _episodesNumeric.Value = Math.Min(_episodesNumeric.Maximum, Math.Max(_episodesNumeric.Minimum, nodeParams.Episodes));

                var duration = nodeParams.DurationMinutes <= 0 ? 1M : nodeParams.DurationMinutes;
                _durationNumeric.Value = Math.Min(_durationNumeric.Maximum, Math.Max(_durationNumeric.Minimum, duration));
                UpdateStyleButtons(nodeParams.VisualStyle);
            }
            else if (Node.Type == WorkflowNodeCatalog.Script)
            {
                SyncScriptChapterComboBox();

                if (_scriptRevisionNotesTextBox.Text != nodeParams.ScriptRevisionNotes)
                {
                    _scriptRevisionNotesTextBox.Text = nodeParams.ScriptRevisionNotes;
                }

                var selectedChapter = GetSelectedScriptChapterPlan();
                var episodeCount = WorkflowExecutor.ResolveScriptEpisodeCount(Node, selectedChapter);
                UpdateScriptEpisodeEditor(selectedChapter, episodeCount);
                _scriptEpisodePanel.Bind(Node, _busy);
            }
            else if (Node.Type == WorkflowNodeCatalog.CharacterView || Node.Type == WorkflowNodeCatalog.CharacterDescription)
            {
                _characterDesignPanel.Bind(Node, _busy, ProjectName);
            }
            else if (Node.Type == WorkflowNodeCatalog.CreativeDescription)
            {
                _creativeDescriptionPanel.Bind(Node, _busy);
            }
            else if (WorkflowNodeCatalog.IsDirectStudioNodeType(Node.Type))
            {
                _directStudioNodePanel.Bind(Node, _busy);
            }
            else if (Node.Type == WorkflowNodeCatalog.StoryboardBreakdown)
            {
                _storyboardBreakdownPanel.Bind(Document, Node, _busy);
            }
            else if (Node.Type == WorkflowNodeCatalog.StoryboardImage)
            {
                if (_inputTextBox.Text != nodeParams.Input)
                {
                    _inputTextBox.Text = nodeParams.Input;
                }

                UpdateStoryboardOptionButtons();
                UpdateStoryboardPreview();
            }
            else if (Node.Type == WorkflowNodeCatalog.StoryboardVideo)
            {
                _storyboardVideoPanel.Bind(Document, Node, _busy);
            }
            else if (Node.Type == WorkflowNodeCatalog.VideoCollection)
            {
                _videoCollectionPanel.Bind(Document, Node, _busy);
            }
            else if (_inputTextBox.Text != nodeParams.Input)
            {
                _inputTextBox.Text = nodeParams.Input;
            }

            var displayOutput = WorkflowExecutor.NormalizeTextResult(Node.Type, Node.Output);
            if (_outputTextBox.Text != displayOutput)
            {
                _outputTextBox.Text = displayOutput;
            }

            UpdateNodePresentation();
            _syncing = false;
        }

        public void SetBusy(bool busy)
        {
            if (_busy == busy)
            {
                return;
            }

            _busy = busy;
            if (Node.Type == WorkflowNodeCatalog.Script)
            {
                _scriptEpisodePanel.Bind(Node, _busy);
            }
            if (Node.Type == WorkflowNodeCatalog.CreativeDescription)
            {
                _creativeDescriptionPanel.Bind(Node, _busy);
            }
            if (WorkflowNodeCatalog.IsDirectStudioNodeType(Node.Type))
            {
                _directStudioNodePanel.Bind(Node, _busy);
            }
            if (Node.Type == WorkflowNodeCatalog.CharacterView || Node.Type == WorkflowNodeCatalog.CharacterDescription)
            {
                _characterDesignPanel.Bind(Node, _busy, ProjectName);
            }
            if (Node.Type == WorkflowNodeCatalog.StoryboardBreakdown)
            {
                _storyboardBreakdownPanel.Bind(Document, Node, _busy);
            }
            if (Node.Type == WorkflowNodeCatalog.StoryboardVideo)
            {
                _storyboardVideoPanel.Bind(Document, Node, _busy);
            }
            if (Node.Type == WorkflowNodeCatalog.VideoCollection)
            {
                _videoCollectionPanel.Bind(Document, Node, _busy);
            }
            UpdateNodePresentation();
        }

        private void NodeCard_MouseDoubleClick(object? sender, MouseEventArgs e)
        {
            if (_collapsed)
            {
                ToggleMinimized();
            }
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            PositionPorts();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (Parent is WorkflowCanvasControl canvas)
            {
                canvas.ZoomAt(canvas.PointToClient(PointToScreen(e.Location)), e.Delta);
                return;
            }

            base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = CreateRoundedRectangle(rect, 18);
            using var fillBrush = new SolidBrush(Color.FromArgb(28, 28, 30));
            using var borderPen = new Pen(
                Selected ? Color.FromArgb(34, 211, 238) : Color.FromArgb(63, 73, 94),
                Selected ? 2F : 1.2F);

            e.Graphics.FillPath(fillBrush, path);
            e.Graphics.DrawPath(borderPen, path);

            if (Selected)
            {
                using var glowPen = new Pen(Color.FromArgb(70, 34, 211, 238), 6F);
                e.Graphics.DrawPath(glowPen, path);
            }
        }

        private Control BuildBody()
        {
            return Node.Type switch
            {
                var type when type == WorkflowNodeCatalog.Outline => BuildOutlineBody(),
                var type when type == WorkflowNodeCatalog.Script => BuildScriptBody(),
                var type when type == WorkflowNodeCatalog.CreativeDescription => BuildCreativeDescriptionBody(),
                var type when WorkflowNodeCatalog.IsDirectStudioNodeType(type) => BuildDirectStudioBody(),
                var type when type == WorkflowNodeCatalog.CharacterView || type == WorkflowNodeCatalog.CharacterDescription => BuildCharacterBody(),
                var type when type == WorkflowNodeCatalog.StoryboardBreakdown => BuildStoryboardBreakdownBody(),
                var type when type == WorkflowNodeCatalog.StoryboardImage => BuildStoryboardImageBody(),
                var type when type == WorkflowNodeCatalog.StoryboardVideo => BuildStoryboardVideoBody(),
                var type when type == WorkflowNodeCatalog.VideoCollection => BuildVideoCollectionBody(),
                var type when type == WorkflowNodeCatalog.VideoPreview => BuildVideoPreviewBody(),
                _ => BuildGenericBody(),
            };
        }

        private Control BuildGenericBody()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(24, 12, 24, 16),
                BackColor = Color.FromArgb(30, 30, 30),
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 116F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            layout.Controls.Add(CreateSectionLabel("输入参数"), 0, 0);
            layout.Controls.Add(_inputTextBox, 0, 1);
            layout.Controls.Add(CreateSectionLabel("节点输出"), 0, 2);
            layout.Controls.Add(_outputTextBox, 0, 3);
            return layout;
        }

        private Control BuildOutlineBody()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(18, 12, 18, 18),
                BackColor = Color.FromArgb(30, 30, 30),
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 252F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            layout.Controls.Add(BuildOutlinePreviewSection(), 0, 0);
            layout.Controls.Add(BuildOutlineComposerSection(), 0, 1);
            return layout;
        }

        private Control BuildScriptBody()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(18, 12, 18, 18),
                BackColor = Color.FromArgb(30, 30, 30),
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 248F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            layout.Controls.Add(BuildScriptPreviewSection(), 0, 0);
            layout.Controls.Add(BuildScriptComposerSection(), 0, 1);
            return layout;
        }

        private Control BuildCharacterBody()
        {
            return _characterDesignPanel;
        }

        private Control BuildDirectStudioBody()
        {
            return _directStudioNodePanel;
        }

        private Control BuildStoryboardImageBody()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(18, 12, 18, 18),
                BackColor = Color.FromArgb(30, 30, 30),
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 392F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            layout.Controls.Add(BuildStoryboardImagePreviewSection(), 0, 0);
            layout.Controls.Add(BuildStoryboardImageComposerSection(), 0, 1);
            return layout;
        }

        private Control BuildStoryboardBreakdownBody()
        {
            return _storyboardBreakdownPanel;
        }

        private Control BuildStoryboardVideoBody()
        {
            return _storyboardVideoPanel;
        }

        private Control BuildVideoCollectionBody()
        {
            return _videoCollectionPanel;
        }

        private Control BuildVideoPreviewBody()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(18, 12, 18, 18),
                BackColor = Color.FromArgb(30, 30, 30),
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            layout.Controls.Add(CreateSectionLabel("视频预览"), 0, 0);

            var actionRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 6, 0, 10),
            };
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            var openVideoButton = CreateActionButton(Color.FromArgb(74, 161, 255), "打开视频");
            openVideoButton.Margin = new Padding(0, 0, 8, 0);
            openVideoButton.Click += (_, _) => OpenFileOrFolder(Node.ArtifactPath, openDirectory: false);

            var openFolderButton = CreateActionButton(Color.FromArgb(58, 64, 78), "打开目录");
            openFolderButton.Margin = new Padding(0);
            openFolderButton.Click += (_, _) => OpenFileOrFolder(Node.ArtifactPath, openDirectory: true);

            actionRow.Controls.Add(openVideoButton, 0, 0);
            actionRow.Controls.Add(openFolderButton, 1, 0);

            layout.Controls.Add(actionRow, 0, 1);
            layout.Controls.Add(CreateSectionLabel("节点输出"), 0, 2);
            layout.Controls.Add(_outputTextBox, 0, 3);
            return layout;
        }

        private Control BuildCreativeDescriptionBody()
        {
            return _creativeDescriptionPanel;
        }

        private Control BuildOutlinePreviewSection()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            layout.Controls.Add(CreateSectionLabel("故事大纲"), 0, 0);

            var shell = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 10, 12, 10),
                Margin = new Padding(0, 6, 0, 10),
                BackColor = Color.FromArgb(20, 21, 26),
            };

            _outlineOutputPlaceholderLabel.BringToFront();
            shell.Controls.Add(_outputTextBox);
            shell.Controls.Add(_outlineOutputPlaceholderLabel);
            layout.Controls.Add(shell, 0, 1);
            return layout;
        }

        private Control BuildStoryboardImagePreviewSection()
        {
            var shell = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(24, 25, 30),
                Padding = new Padding(14),
                Margin = new Padding(0, 0, 0, 12),
                ColumnCount = 1,
                RowCount = 2,
            };
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var toolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 2,
                Margin = new Padding(0, 0, 0, 10),
                BackColor = Color.FromArgb(24, 25, 30),
            };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104F));
            toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            toolbar.Controls.Add(_storyboardPrevPageButton, 0, 0);
            toolbar.Controls.Add(_storyboardPageLabel, 1, 0);
            toolbar.Controls.Add(_storyboardNextPageButton, 2, 0);
            toolbar.Controls.Add(_storyboardOpenButton, 0, 1);
            toolbar.Controls.Add(_storyboardEditButton, 2, 1);

            var inner = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(20, 21, 26),
            };

            inner.Controls.Add(_storyboardPreviewPictureBox);
            inner.Controls.Add(_storyboardPreviewPlaceholderLabel);
            shell.Controls.Add(toolbar, 0, 0);
            shell.Controls.Add(inner, 0, 1);
            return shell;
        }

        private Control BuildScriptPreviewSection()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            layout.Controls.Add(CreateSectionLabel("分集剧本"), 0, 0);

            var shell = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 10, 12, 10),
                Margin = new Padding(0, 6, 0, 10),
                BackColor = Color.FromArgb(20, 21, 26),
            };

            shell.Controls.Add(_scriptEpisodePanel);
            layout.Controls.Add(shell, 0, 1);
            return layout;
        }

        private Control BuildOutlineComposerSection()
        {
            var shell = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14),
                Margin = Padding.Empty,
                BackColor = Color.FromArgb(34, 35, 40),
                AutoScroll = true,
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7,
                Margin = Padding.Empty,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            layout.Controls.Add(_outlineStatusLabel, 0, 0);
            layout.Controls.Add(_outlineIdeaTextBox, 0, 1);
            layout.Controls.Add(BuildOutlineSelectors(), 0, 2);
            layout.Controls.Add(CreateSectionLabel("视觉风格与时长"), 0, 3);
            layout.Controls.Add(BuildOutlineMetrics(), 0, 4);
            layout.Controls.Add(BuildOutlineActionRow(), 0, 5);
            shell.Controls.Add(layout);
            return shell;
        }

        private Control BuildScriptComposerSection()
        {
            var shell = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14),
                Margin = Padding.Empty,
                BackColor = Color.FromArgb(34, 35, 40),
                AutoScroll = true,
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 8,
                Margin = Padding.Empty,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));

            layout.Controls.Add(_scriptStatusLabel, 0, 0);
            layout.Controls.Add(CreateSectionLabel("选择章节 (Source Chapter)"), 0, 1);
            layout.Controls.Add(_scriptChapterComboBox, 0, 2);
            layout.Controls.Add(CreateSectionLabel("拆分集数 (Episodes)"), 0, 3);
            layout.Controls.Add(_scriptEpisodesToGenerateNumeric, 0, 4);
            layout.Controls.Add(CreateSectionLabel("修改建议 (Optional)"), 0, 5);
            layout.Controls.Add(_scriptRevisionNotesTextBox, 0, 6);
            layout.Controls.Add(_scriptGenerateButton, 0, 7);

            shell.Controls.Add(layout);
            return shell;
        }

        private Control BuildStoryboardImageComposerSection()
        {
            var shell = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14),
                Margin = Padding.Empty,
                BackColor = Color.FromArgb(34, 35, 40),
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 10,
                Margin = Padding.Empty,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            layout.Controls.Add(CreateSectionLabel("分镜描述 (DESCRIPTION)"), 0, 0);
            layout.Controls.Add(_inputTextBox, 0, 1);
            layout.Controls.Add(_storyboardConnectionLabel, 0, 2);
            layout.Controls.Add(_storyboardModelLabel, 0, 3);
            layout.Controls.Add(CreateSectionLabel("网格布局 (Grid Layout)"), 0, 4);
            layout.Controls.Add(BuildStoryboardGridLayoutRow(), 0, 5);
            layout.Controls.Add(CreateSectionLabel("画板方向 (Panel Orientation)"), 0, 6);
            layout.Controls.Add(BuildStoryboardOrientationRow(), 0, 7);
            layout.Controls.Add(_storyboardGenerateButton, 0, 8);

            shell.Controls.Add(layout);
            return shell;
        }

        private Control BuildOutlineSelectors()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            layout.Controls.Add(_genreComboBox, 0, 0);
            layout.Controls.Add(_settingComboBox, 1, 0);
            return layout;
        }

        private Control BuildOutlineMetrics()
        {
            var container = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
            };
            container.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            container.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var stylePanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 12),
            };
            stylePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            stylePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            stylePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));

            foreach (var style in WorkflowNodeCatalog.OutlineVisualStyles)
            {
                var button = CreateStyleButton(style);
                _styleButtons[style] = button;
                stylePanel.Controls.Add(button);
            }

            var metricsPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 6, 0, 0),
            };
            metricsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            metricsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            metricsPanel.Controls.Add(BuildMetricEditor("总集数", _episodesNumeric), 0, 0);
            metricsPanel.Controls.Add(BuildMetricEditor("单集时长 (分钟)", _durationNumeric), 1, 0);

            container.Controls.Add(stylePanel, 0, 0);
            container.Controls.Add(metricsPanel, 0, 1);
            return container;
        }

        private Control BuildMetricEditor(string title, NumericUpDown editor)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
            };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            panel.Controls.Add(CreateSectionLabel(title), 0, 0);
            panel.Controls.Add(editor, 0, 1);
            return panel;
        }

        private Control BuildOutlineActionRow()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 12, 0, 0),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            layout.Controls.Add(_outlineGenerateButton, 0, 0);
            layout.Controls.Add(_outlineResetButton, 1, 0);
            return layout;
        }

        private Control BuildStoryboardGridLayoutRow()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.Controls.Add(_storyboardGrid3x3Button, 0, 0);
            layout.Controls.Add(_storyboardGrid2x3Button, 1, 0);
            return layout;
        }

        private Control BuildStoryboardOrientationRow()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.Controls.Add(_storyboardLandscapeButton, 0, 0);
            layout.Controls.Add(_storyboardPortraitButton, 1, 0);
            return layout;
        }

        private void SyncScriptChapterComboBox()
        {
            _scriptChapterComboBox.BeginUpdate();
            _scriptChapterComboBox.Items.Clear();

            if (_scriptChapterPlans.Count == 0)
            {
                _scriptChapterComboBox.Items.Add("请先连接故事大纲");
                _scriptChapterComboBox.SelectedIndex = 0;
                _scriptChapterComboBox.Enabled = false;
                _scriptChapterComboBox.EndUpdate();
                return;
            }

            foreach (var plan in _scriptChapterPlans)
            {
                _scriptChapterComboBox.Items.Add(plan);
            }

            _scriptChapterComboBox.Enabled = true;
            var selectedPlan = WorkflowExecutor.ResolveScriptChapterSelection(Node, _scriptChapterPlans);
            var selectedIndex = selectedPlan == null
                ? 0
                : _scriptChapterPlans.FindIndex(plan => string.Equals(plan.DisplayText, selectedPlan.DisplayText, StringComparison.Ordinal));
            _scriptChapterComboBox.SelectedIndex = selectedIndex < 0 ? 0 : selectedIndex;
            _scriptChapterComboBox.EndUpdate();
        }

        private OutlineChapterPlan? GetSelectedScriptChapterPlan()
        {
            return _scriptChapterComboBox.SelectedItem as OutlineChapterPlan
                   ?? WorkflowExecutor.ResolveScriptChapterSelection(Node, _scriptChapterPlans);
        }

        private void UpdateScriptEpisodeEditor(OutlineChapterPlan? selectedChapter, int episodeCount)
        {
            var maximum = selectedChapter == null
                ? 20
                : Math.Max(1, selectedChapter.EpisodeCount);
            _scriptEpisodesToGenerateNumeric.Maximum = maximum;
            _scriptEpisodesToGenerateNumeric.Minimum = 1;
            _scriptEpisodesToGenerateNumeric.Value = Math.Min(_scriptEpisodesToGenerateNumeric.Maximum, Math.Max(_scriptEpisodesToGenerateNumeric.Minimum, episodeCount));
        }

        private WorkflowPortHandle CreatePort(WorkflowPortKind portKind)
        {
            var port = new WorkflowPortHandle(portKind);
            port.MouseDown += (_, e) => PortPanel_MouseDown(e, portKind);
            return port;
        }

        private static TextBox CreateEditorTextBox()
        {
            return new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.WhiteSmoke,
                BorderStyle = BorderStyle.FixedSingle,
            };
        }

        private static RichTextBox CreateOutputTextBox()
        {
            return new RichTextBox
            {
                Dock = DockStyle.Fill,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                BackColor = Color.FromArgb(20, 21, 26),
                ForeColor = Color.FromArgb(222, 227, 236),
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                WordWrap = true,
                DetectUrls = false,
                ShortcutsEnabled = true,
            };
        }

        private static ComboBox CreateComboBox()
        {
            return new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.WhiteSmoke,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 0, 8, 0),
            };
        }

        private static NumericUpDown CreateNumericUpDown(decimal minimum, decimal maximum, decimal increment)
        {
            return new NumericUpDown
            {
                Dock = DockStyle.Fill,
                Minimum = minimum,
                Maximum = maximum,
                Increment = increment,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.WhiteSmoke,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0),
            };
        }

        private static Label CreateSectionLabel(string text)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                ForeColor = Color.Gainsboro,
                TextAlign = ContentAlignment.MiddleLeft,
            };
        }

        private static Button CreateActionButton(Color backColor, string text)
        {
            var button = new Button
            {
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                Text = text,
                BackColor = backColor,
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 8, 0),
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private static Button CreateToggleButton(string text)
        {
            var button = new Button
            {
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                Text = text,
                BackColor = Color.FromArgb(38, 39, 44),
                ForeColor = Color.Gainsboro,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 8, 0),
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(76, 82, 98);
            button.FlatAppearance.BorderSize = 1;
            return button;
        }

        private Button CreateStyleButton(string style)
        {
            var button = new Button
            {
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                Text = WorkflowNodeParameters.GetVisualStyleDisplayName(style),
                Margin = new Padding(0, 0, 6, 0),
                Cursor = Cursors.Hand,
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (_, _) => SelectStyle(style);
            return button;
        }

        private void UpdateStyleButtons(string activeStyle)
        {
            foreach (var pair in _styleButtons)
            {
                var isActive = string.Equals(pair.Key, activeStyle, StringComparison.Ordinal);
                pair.Value.BackColor = isActive ? Color.FromArgb(255, 122, 0) : Color.FromArgb(44, 44, 44);
                pair.Value.ForeColor = isActive ? Color.White : Color.Gainsboro;
            }
        }

        private void UpdateStoryboardOptionButtons()
        {
            var gridLayout = Node.Params?.StoryboardGridLayout == "2x3" ? "2x3" : "3x3";
            ApplyToggleButtonStyle(_storyboardGrid3x3Button, gridLayout == "3x3");
            ApplyToggleButtonStyle(_storyboardGrid2x3Button, gridLayout == "2x3");

            var orientation = Node.Params?.StoryboardPanelOrientation == "9:16" ? "9:16" : "16:9";
            ApplyToggleButtonStyle(_storyboardLandscapeButton, orientation == "16:9");
            ApplyToggleButtonStyle(_storyboardPortraitButton, orientation == "9:16");
        }

        private static void ApplyToggleButtonStyle(Button button, bool active)
        {
            button.BackColor = active ? Color.FromArgb(136, 72, 255) : Color.FromArgb(38, 39, 44);
            button.ForeColor = active ? Color.White : Color.Gainsboro;
            button.FlatAppearance.BorderColor = active ? Color.FromArgb(196, 137, 255) : Color.FromArgb(76, 82, 98);
        }

        private void SyncComboBox(ComboBox comboBox, IEnumerable<string> values, string selectedValue, string placeholder)
        {
            comboBox.BeginUpdate();
            comboBox.Items.Clear();
            comboBox.Items.Add(placeholder);
            foreach (var value in values)
            {
                comboBox.Items.Add(value);
            }

            comboBox.SelectedIndex = 0;
            for (var index = 1; index < comboBox.Items.Count; index++)
            {
                if (string.Equals(comboBox.Items[index]?.ToString(), selectedValue, StringComparison.Ordinal))
                {
                    comboBox.SelectedIndex = index;
                    break;
                }
            }
            comboBox.EndUpdate();
        }

        private void UpdateStoryboardPreview()
        {
            var imagePath = GetCurrentStoryboardPreviewPath();

            if (!string.Equals(_loadedStoryboardPreviewPath, imagePath, StringComparison.OrdinalIgnoreCase))
            {
                _storyboardPreviewPictureBox.Image?.Dispose();
                _storyboardPreviewPictureBox.Image = null;
                _loadedStoryboardPreviewPath = imagePath;

                if (!string.IsNullOrWhiteSpace(imagePath))
                {
                    _storyboardPreviewPictureBox.Image = LoadImageCopy(imagePath);
                }
            }

            var hasPreview = _storyboardPreviewPictureBox.Image != null;
            _storyboardPreviewPictureBox.Visible = hasPreview;
            _storyboardPreviewPlaceholderLabel.Visible = !hasPreview;
            _storyboardOpenButton.Enabled = hasPreview;
            _storyboardOpenButton.Cursor = hasPreview ? Cursors.Hand : Cursors.Default;
            UpdateStoryboardPageControls();
        }

        private List<string> GetStoryboardPreviewPaths()
        {
            Node.Params ??= new WorkflowNodeParameters();
            Node.Params.EnsureDefaults(Node.Type);

            var paths = (Node.Params.StoryboardGridPagePaths ?? new List<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (paths.Count == 0 && !string.IsNullOrWhiteSpace(Node.ArtifactPath) && File.Exists(Node.ArtifactPath))
            {
                paths.Add(Node.ArtifactPath);
            }

            return paths;
        }

        private string GetCurrentStoryboardPreviewPath()
        {
            var paths = GetStoryboardPreviewPaths();
            if (paths.Count == 0)
            {
                return string.Empty;
            }

            var pageIndex = Math.Max(0, Math.Min(Node.Params?.StoryboardCurrentPage ?? 0, paths.Count - 1));
            if (Node.Params != null)
            {
                Node.Params.StoryboardCurrentPage = pageIndex;
                Node.Params.StoryboardTotalPages = paths.Count;
            }

            return paths[pageIndex];
        }

        private void UpdateStoryboardPageControls()
        {
            var paths = GetStoryboardPreviewPaths();
            var total = paths.Count;
            var pageIndex = total == 0 ? 0 : Math.Max(0, Math.Min(Node.Params?.StoryboardCurrentPage ?? 0, total - 1));
            var hasPages = total > 0;

            _storyboardPageLabel.Text = hasPages
                ? $"第 {pageIndex + 1} / {total} 页"
                : "暂无分镜图";
            _storyboardPageLabel.ForeColor = hasPages
                ? Color.WhiteSmoke
                : Color.FromArgb(214, 220, 232);
            _storyboardPrevPageButton.Enabled = hasPages && pageIndex > 0;
            _storyboardPrevPageButton.Cursor = _storyboardPrevPageButton.Enabled ? Cursors.Hand : Cursors.Default;
            _storyboardNextPageButton.Enabled = hasPages && pageIndex < total - 1;
            _storyboardNextPageButton.Cursor = _storyboardNextPageButton.Enabled ? Cursors.Hand : Cursors.Default;
            _storyboardPrevPageButton.ForeColor = _storyboardPrevPageButton.Enabled
                ? Color.WhiteSmoke
                : Color.FromArgb(198, 204, 218);
            _storyboardNextPageButton.ForeColor = _storyboardNextPageButton.Enabled
                ? Color.WhiteSmoke
                : Color.FromArgb(198, 204, 218);
            _storyboardOpenButton.ForeColor = _storyboardOpenButton.Enabled
                ? Color.WhiteSmoke
                : Color.FromArgb(198, 204, 218);
        }

        private void ChangeStoryboardPage(int delta)
        {
            var paths = GetStoryboardPreviewPaths();
            if (paths.Count == 0 || Node.Params == null)
            {
                return;
            }

            var nextIndex = Math.Max(0, Math.Min(Node.Params.StoryboardCurrentPage + delta, paths.Count - 1));
            if (nextIndex == Node.Params.StoryboardCurrentPage)
            {
                return;
            }

            Node.Params.StoryboardCurrentPage = nextIndex;
            Node.Params.StoryboardTotalPages = paths.Count;
            Node.ArtifactPath = paths[nextIndex];
            _loadedStoryboardPreviewPath = string.Empty;
            UpdateStoryboardPreview();
            NodeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OpenStoryboardPreview()
        {
            var paths = GetStoryboardPreviewPaths();
            if (paths.Count == 0)
            {
                return;
            }

            var pageIndex = Math.Max(0, Math.Min(Node.Params?.StoryboardCurrentPage ?? 0, paths.Count - 1));
            using var form = new ImageGalleryForm(paths, pageIndex, $"{Node.Id} - {Node.Type}");
            form.ShowDialog(FindForm());
        }

        private (List<StoryboardShot> Shots, int StartIndex) GetCurrentStoryboardPageShots()
        {
            Node.Params ??= new WorkflowNodeParameters();
            Node.Params.EnsureDefaults(Node.Type);

            var allShots = Node.Params.StoryboardShots ?? new List<StoryboardShot>();
            var columns = string.Equals(Node.Params.StoryboardGridLayout, "2x3", StringComparison.Ordinal) ? 2 : 3;
            var shotsPerPage = columns * 3;
            var currentPage = Math.Max(0, Node.Params.StoryboardCurrentPage);
            var startIndex = currentPage * shotsPerPage;
            if (startIndex >= allShots.Count)
            {
                return (new List<StoryboardShot>(), startIndex);
            }

            var pageShots = allShots
                .Skip(startIndex)
                .Take(shotsPerPage)
                .Select(shot => shot.Clone())
                .ToList();
            return (pageShots, startIndex);
        }

        private void OpenStoryboardPageEditor()
        {
            if (_busy)
            {
                return;
            }

            var currentPage = GetCurrentStoryboardPageShots();
            if (currentPage.Shots.Count == 0)
            {
                return;
            }

            using var form = new StoryboardPageEditForm(Node.Params?.StoryboardCurrentPage ?? 0, currentPage.Shots);
            if (form.ShowDialog(FindForm()) != DialogResult.OK)
            {
                return;
            }

            Node.Params ??= new WorkflowNodeParameters();
            Node.Params.EnsureDefaults(Node.Type);
            for (var index = 0; index < form.ResultShots.Count; index++)
            {
                var targetIndex = currentPage.StartIndex + index;
                if (targetIndex < 0 || targetIndex >= Node.Params.StoryboardShots.Count)
                {
                    continue;
                }

                Node.Params.StoryboardShots[targetIndex] = form.ResultShots[index].Clone();
            }

            NodeChanged?.Invoke(this, EventArgs.Empty);
            RunRequested?.Invoke(this, EventArgs.Empty);
        }

        private static Image? LoadImageCopy(string filePath)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var image = Image.FromStream(stream);
                return new Bitmap(image);
            }
            catch
            {
                return null;
            }
        }

        private void SelectStyle(string style)
        {
            if (_syncing)
            {
                return;
            }

            Node.Params ??= new WorkflowNodeParameters();
            Node.Params.VisualStyle = style;
            UpdateStyleButtons(style);
            NodeChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetStoryboardConnectionCount(int count)
        {
            if (_storyboardConnectionCount == count)
            {
                return;
            }

            _storyboardConnectionCount = Math.Max(0, count);
            if (Node.Type == WorkflowNodeCatalog.StoryboardImage)
            {
                UpdateNodePresentation();
            }
        }

        private void SetStoryboardGridLayout(string layout)
        {
            if (_syncing)
            {
                return;
            }

            Node.Params ??= new WorkflowNodeParameters();
            Node.Params.StoryboardGridLayout = string.Equals(layout, "2x3", StringComparison.Ordinal) ? "2x3" : "3x3";
            UpdateStoryboardOptionButtons();
            UpdateNodePresentation();
            NodeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SetStoryboardOrientation(string orientation)
        {
            if (_syncing)
            {
                return;
            }

            Node.Params ??= new WorkflowNodeParameters();
            Node.Params.StoryboardPanelOrientation = string.Equals(orientation, "9:16", StringComparison.Ordinal) ? "9:16" : "16:9";
            UpdateStoryboardOptionButtons();
            UpdateNodePresentation();
            NodeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void InputTextBox_TextChanged(object? sender, EventArgs e)
        {
            if (_syncing)
            {
                return;
            }

            Node.Params ??= new WorkflowNodeParameters();
            Node.Params.Input = _inputTextBox.Text;
            NodeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OutlineIdeaTextBox_TextChanged(object? sender, EventArgs e)
        {
            if (_syncing)
            {
                return;
            }

            Node.Params ??= new WorkflowNodeParameters();
            Node.Params.CoreIdea = _outlineIdeaTextBox.Text;
            Node.Params.Input = _outlineIdeaTextBox.Text;
            NodeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void GenreComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_syncing)
            {
                return;
            }

            Node.Params ??= new WorkflowNodeParameters();
            Node.Params.Genre = _genreComboBox.SelectedIndex <= 0 ? string.Empty : _genreComboBox.SelectedItem?.ToString() ?? string.Empty;
            NodeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SettingComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_syncing)
            {
                return;
            }

            Node.Params ??= new WorkflowNodeParameters();
            Node.Params.Setting = _settingComboBox.SelectedIndex <= 0 ? string.Empty : _settingComboBox.SelectedItem?.ToString() ?? string.Empty;
            NodeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void EpisodesNumeric_ValueChanged(object? sender, EventArgs e)
        {
            if (_syncing)
            {
                return;
            }

            Node.Params ??= new WorkflowNodeParameters();
            Node.Params.Episodes = decimal.ToInt32(_episodesNumeric.Value);
            NodeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void DurationNumeric_ValueChanged(object? sender, EventArgs e)
        {
            if (_syncing)
            {
                return;
            }

            Node.Params ??= new WorkflowNodeParameters();
            Node.Params.DurationMinutes = _durationNumeric.Value;
            NodeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ScriptChapterComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_syncing)
            {
                return;
            }

            Node.Params ??= new WorkflowNodeParameters();
            if (_scriptChapterComboBox.SelectedItem is not OutlineChapterPlan chapterPlan)
            {
                Node.Params.ScriptSourceChapter = string.Empty;
                NodeChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            Node.Params.ScriptSourceChapter = chapterPlan.DisplayText;
            var recommendedCount = Math.Max(1, chapterPlan.EpisodeCount);
            Node.Params.ScriptEpisodesToGenerate = recommendedCount;

            _syncing = true;
            _scriptEpisodesToGenerateNumeric.Maximum = recommendedCount;
            _scriptEpisodesToGenerateNumeric.Value = Math.Min(_scriptEpisodesToGenerateNumeric.Maximum, recommendedCount);
            _syncing = false;

            UpdateNodePresentation();
            NodeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ScriptEpisodesToGenerateNumeric_ValueChanged(object? sender, EventArgs e)
        {
            if (_syncing)
            {
                return;
            }

            Node.Params ??= new WorkflowNodeParameters();
            Node.Params.ScriptEpisodesToGenerate = decimal.ToInt32(_scriptEpisodesToGenerateNumeric.Value);
            UpdateNodePresentation();
            NodeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ScriptRevisionNotesTextBox_TextChanged(object? sender, EventArgs e)
        {
            if (_syncing)
            {
                return;
            }

            Node.Params ??= new WorkflowNodeParameters();
            Node.Params.ScriptRevisionNotes = _scriptRevisionNotesTextBox.Text;
            NodeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ResetOutlineOutput()
        {
            if (_syncing || _busy)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(Node.ArtifactPath) && File.Exists(Node.ArtifactPath))
            {
                try
                {
                    File.Delete(Node.ArtifactPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            Node.Output = string.Empty;
            Node.ArtifactPath = string.Empty;
            Node.ArtifactKind = string.Empty;
            Node.Params ??= new WorkflowNodeParameters();
            Node.Params.Input = Node.Params.CoreIdea;
            _outputTextBox.Text = string.Empty;
            UpdateNodePresentation();
            SyncFromNode();
            NodeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void AttachSelection(Control control)
        {
            control.MouseDown += (_, _) => SelectRequested?.Invoke(this, EventArgs.Empty);
            foreach (Control child in control.Controls)
            {
                AttachSelection(child);
            }
        }

        private void AttachDragSurface(Control control)
        {
            if (CanStartDragFrom(control))
            {
                AttachDrag(control);
            }

            foreach (Control child in control.Controls)
            {
                AttachDragSurface(child);
            }
        }

        private static bool CanStartDragFrom(Control control)
        {
            return control is not TextBoxBase
                && control is not ComboBox
                && control is not NumericUpDown
                && control is not Button
                && control is not WorkflowPortHandle;
        }

        private void AttachViewportWheel(Control control)
        {
            if (control is TextBoxBase || control is ComboBox || control is NumericUpDown || control is CharacterDesignPanel)
            {
                return;
            }

            control.MouseWheel += ForwardViewportWheel;
            foreach (Control child in control.Controls)
            {
                AttachViewportWheel(child);
            }
        }

        private void ForwardViewportWheel(object? sender, MouseEventArgs e)
        {
            if (Parent == null)
            {
                return;
            }

            var source = sender as Control ?? this;
            if (Parent is WorkflowCanvasControl canvas)
            {
                canvas.ZoomAt(canvas.PointToClient(source.PointToScreen(e.Location)), e.Delta);
            }
        }

        private void AttachDrag(Control control)
        {
            control.MouseDown += DragControl_MouseDown;
            control.MouseMove += DragControl_MouseMove;
            control.MouseUp += DragControl_MouseUp;
        }

        private void DragControl_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            SelectRequested?.Invoke(this, EventArgs.Empty);
            _dragging = true;
            _dragOrigin = Cursor.Position;
            _cardOrigin = new Point(Node.X, Node.Y);
            Capture = true;
        }

        private void DragControl_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!_dragging || Parent == null)
            {
                return;
            }

             if ((Control.MouseButtons & MouseButtons.Left) == 0)
             {
                 EndDragging();
                 return;
             }

            if (Parent is WorkflowCanvasControl canvas)
            {
                var cursor = canvas.PointToClient(Cursor.Position);
                var start = canvas.PointToClient(_dragOrigin);
                var deltaX = (cursor.X - start.X) / canvas.ZoomFactor;
                var deltaY = (cursor.Y - start.Y) / canvas.ZoomFactor;
                var nextX = (int)Math.Round(_cardOrigin.X + deltaX);
                var nextY = (int)Math.Round(_cardOrigin.Y + deltaY);
                if (nextX == Node.X && nextY == Node.Y)
                {
                    return;
                }

                Node.X = nextX;
                Node.Y = nextY;
                PositionChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            var parentCursor = Parent.PointToClient(Cursor.Position);
            var parentStart = Parent.PointToClient(_dragOrigin);
            var parentDeltaX = parentCursor.X - parentStart.X;
            var parentDeltaY = parentCursor.Y - parentStart.Y;
            var fallbackX = _cardOrigin.X + parentDeltaX;
            var fallbackY = _cardOrigin.Y + parentDeltaY;
            Location = new Point(fallbackX, fallbackY);
            Node.X = fallbackX;
            Node.Y = fallbackY;
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void DragControl_MouseUp(object? sender, MouseEventArgs e)
        {
            if (!_dragging)
            {
                return;
            }

            EndDragging();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (_dragging && !Capture)
            {
                EndDragging();
            }
        }

        private void EndDragging()
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            Capture = false;
            MoveCompleted?.Invoke(this, EventArgs.Empty);
        }

        private void ToggleMinimized()
        {
            Node.IsMinimized = !Node.IsMinimized;
            ApplyCollapsedState(persist: true);
        }

        private void ApplyCollapsedState(bool persist)
        {
            var collapsed = Node.IsMinimized;
            if (_collapsed == collapsed && _body.Visible == !collapsed)
            {
                UpdateMinimizeButtonVisual();
                return;
            }

            _collapsed = collapsed;
            SuspendLayout();
            try
            {
                _body.Visible = !collapsed;
                ApplySizeForCurrentState(_zoom);
                UpdateMinimizeButtonVisual();
            }
            finally
            {
                ResumeLayout(true);
            }

            PositionPorts();
            Invalidate(true);

            if (!persist)
            {
                return;
            }

            PositionChanged?.Invoke(this, EventArgs.Empty);
            NodeChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateMinimizeButtonVisual()
        {
            _minimizeButton.Text = _collapsed ? "＋" : "－";
            _minimizeButton.BackColor = _collapsed
                ? Color.FromArgb(48, 96, 62)
                : Color.FromArgb(58, 64, 78);
            _minimizeButton.ForeColor = Color.White;
        }

        private void ApplySizeForCurrentState(float zoom)
        {
            var width = Math.Max(220, (int)Math.Round(_baseSize.Width * zoom));
            var baseHeight = _collapsed ? MinimizedCardHeight : _baseSize.Height;
            var minHeight = _collapsed ? MinimizedCardHeight : 180;
            var height = Math.Max(minHeight, (int)Math.Round(baseHeight * zoom));
            var scaledSize = new Size(width, height);
            Size = scaledSize;
            MinimumSize = scaledSize;
            MaximumSize = scaledSize;
        }

        private void PortPanel_MouseDown(MouseEventArgs e, WorkflowPortKind portKind)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            SelectRequested?.Invoke(this, EventArgs.Empty);
            PortClicked?.Invoke(this, new WorkflowPortEventArgs(Node, portKind));
        }

        private void PositionPorts()
        {
            if (_header == null || _inputPort == null || _outputPort == null)
            {
                return;
            }

            var showPorts = !WorkflowNodeCatalog.IsDirectStudioNodeType(Node.Type);
            _inputPort.Visible = showPorts;
            _outputPort.Visible = showPorts;
            _inputPort.Enabled = showPorts;
            _outputPort.Enabled = showPorts;
            if (!showPorts)
            {
                return;
            }

            var centerY = Math.Max(_header.Bottom + 28, Height / 2 - _inputPort.Height / 2);
            _inputPort.Location = new Point(4, centerY);
            _outputPort.Location = new Point(Width - _outputPort.Width - 4, centerY);
            _inputPort.BringToFront();
            _outputPort.BringToFront();
        }

        private static string GetRunButtonText(string nodeType)
        {
            return nodeType switch
            {
                WorkflowNodeCatalog.Outline => "大纲",
                WorkflowNodeCatalog.Script => "剧本",
                WorkflowNodeCatalog.CreativeDescription => "拆分",
                WorkflowNodeCatalog.TextToImage => "生图",
                WorkflowNodeCatalog.TextToVideo => "视频",
                WorkflowNodeCatalog.TextImageToVideo => "图视频",
                WorkflowNodeCatalog.CharacterDescription => "同步",
                WorkflowNodeCatalog.CharacterView => "同步",
                WorkflowNodeCatalog.StoryboardImage => "分镜图",
                WorkflowNodeCatalog.StoryboardBreakdown => "拆解",
                WorkflowNodeCatalog.StoryboardVideo => "分镜",
                WorkflowNodeCatalog.VideoPreview => "预览",
                WorkflowNodeCatalog.VideoCollection => "合集",
                _ => "运行",
            };
        }

        private void UpdateNodePresentation()
        {
            _saveButton.Visible = false;
            _saveButton.Enabled = false;
            _saveButton.Cursor = Cursors.Default;
            _saveButton.BackColor = Color.FromArgb(56, 60, 74);
            _saveButton.ForeColor = Color.White;

            if (Node.Type == WorkflowNodeCatalog.Outline)
            {
                var hasOutline = !string.IsNullOrWhiteSpace(Node.Output);
                _outlineOutputPlaceholderLabel.Visible = !hasOutline;
                _outlineStatusLabel.Text = _busy
                    ? "正在生成大纲，请稍候..."
                    : hasOutline
                        ? "大纲已生成，如需重新生成请先重置大纲。"
                        : "填写故事简介、类型、背景、风格后生成细纲。";
                _outlineGenerateButton.Text = _busy ? "生成中..." : "生成大纲";
                _outlineGenerateButton.Enabled = !_busy && !hasOutline;
                _outlineGenerateButton.Cursor = _outlineGenerateButton.Enabled ? Cursors.Hand : Cursors.Default;
                _outlineGenerateButton.BackColor = _outlineGenerateButton.Enabled
                    ? Color.FromArgb(255, 140, 34)
                    : Color.FromArgb(92, 98, 112);
                _outlineGenerateButton.ForeColor = _outlineGenerateButton.Enabled
                    ? Color.White
                    : Color.FromArgb(174, 178, 188);
                _outlineResetButton.Enabled = !_busy && hasOutline;
                _outlineResetButton.Cursor = _outlineResetButton.Enabled ? Cursors.Hand : Cursors.Default;
                _outlineResetButton.BackColor = _outlineResetButton.Enabled ? Color.FromArgb(58, 64, 78) : Color.FromArgb(44, 48, 58);
                _outlineResetButton.ForeColor = _outlineResetButton.Enabled ? Color.White : Color.FromArgb(128, 132, 142);
                _runButton.Text = _busy ? "生成中" : GetRunButtonText(Node.Type);
                _runButton.Enabled = !_busy && !hasOutline;
                _runButton.Cursor = _runButton.Enabled ? Cursors.Hand : Cursors.Default;
                _runButton.BackColor = _runButton.Enabled ? Color.FromArgb(74, 161, 255) : Color.FromArgb(80, 88, 102);
                _runButton.ForeColor = _runButton.Enabled ? Color.White : Color.FromArgb(176, 182, 194);
                return;
            }

            if (Node.Type == WorkflowNodeCatalog.Script)
            {
                var selectedChapter = GetSelectedScriptChapterPlan();
                var hasScriptOutput = !string.IsNullOrWhiteSpace(Node.Output);
                var canGenerate = !_busy && selectedChapter != null;
                var episodeCount = WorkflowExecutor.ResolveScriptEpisodeCount(Node, selectedChapter);
                var rangeText = WorkflowExecutor.BuildScriptTargetEpisodeRange(selectedChapter, episodeCount);

                _scriptOutputPlaceholderLabel.Visible = !hasScriptOutput;
                _scriptOutputPlaceholderLabel.Text = _busy
                    ? "正在生成分集剧本..."
                    : selectedChapter == null
                        ? "请先连接故事大纲并生成可解析的章节。"
                        : $"已选择：{selectedChapter.DisplayText}{Environment.NewLine}本次生成：{rangeText}";
                _scriptStatusLabel.Text = _busy
                    ? "正在生成分集剧本，请稍候..."
                    : selectedChapter == null
                        ? "等待上游故事大纲提供章节。"
                        : $"当前章节：{selectedChapter.DisplayText} | 本次生成 {episodeCount} 集";
                _scriptGenerateButton.Text = _busy ? "生成中..." : "生成分集剧本";
                _scriptGenerateButton.Enabled = canGenerate;
                _scriptGenerateButton.Cursor = canGenerate ? Cursors.Hand : Cursors.Default;
                _scriptGenerateButton.BackColor = canGenerate ? Color.FromArgb(24, 196, 159) : Color.FromArgb(76, 92, 96);
                _scriptGenerateButton.ForeColor = canGenerate ? Color.White : Color.FromArgb(176, 182, 194);
                _runButton.Text = _busy ? "生成中" : GetRunButtonText(Node.Type);
                _runButton.Enabled = canGenerate;
                _runButton.Cursor = canGenerate ? Cursors.Hand : Cursors.Default;
                _runButton.BackColor = canGenerate ? Color.FromArgb(74, 161, 255) : Color.FromArgb(80, 88, 102);
                _runButton.ForeColor = canGenerate ? Color.White : Color.FromArgb(176, 182, 194);
                return;
            }

            if (Node.Type == WorkflowNodeCatalog.CharacterView || Node.Type == WorkflowNodeCatalog.CharacterDescription)
            {
                _runButton.Text = _busy ? "生成中" : GetRunButtonText(Node.Type);
                _runButton.Enabled = !_busy;
                _runButton.Cursor = _runButton.Enabled ? Cursors.Hand : Cursors.Default;
                _runButton.BackColor = _runButton.Enabled ? Color.FromArgb(74, 161, 255) : Color.FromArgb(80, 88, 102);
                _runButton.ForeColor = _runButton.Enabled ? Color.White : Color.FromArgb(176, 182, 194);
                return;
            }

            if (Node.Type == WorkflowNodeCatalog.StoryboardImage)
            {
                UpdateStoryboardPreview();
                var hasInput = !string.IsNullOrWhiteSpace(Node.Params?.Input);
                var canGenerate = !_busy && (hasInput || _storyboardConnectionCount > 0);
                var hasPreview = _storyboardPreviewPictureBox.Image != null;
                var gridIsTwoByThree = string.Equals(Node.Params?.StoryboardGridLayout, "2x3", StringComparison.Ordinal);
                var actionText = gridIsTwoByThree ? "生成六宫格分镜图" : "生成九宫格分镜图";
                var previewPaths = GetStoryboardPreviewPaths();
                if (previewPaths.Count > 0 && Node.Params != null)
                {
                    var pageIndex = Math.Max(0, Math.Min(Node.Params.StoryboardCurrentPage, previewPaths.Count - 1));
                    Node.Params.StoryboardCurrentPage = pageIndex;
                    Node.Params.StoryboardTotalPages = previewPaths.Count;
                    Node.ArtifactPath = previewPaths[pageIndex];
                }

                _storyboardConnectionLabel.Text = _storyboardConnectionCount > 0
                    ? $"已连接 {_storyboardConnectionCount} 个节点"
                    : "输入分镜描述，或连接剧本分集 / 创意描述节点";
                _storyboardConnectionLabel.ForeColor = _storyboardConnectionCount > 0
                    ? Color.FromArgb(116, 185, 255)
                    : Color.FromArgb(154, 162, 184);
                _storyboardModelLabel.Text = BuildStoryboardImageModelStatusText(Node);

                _storyboardPreviewPlaceholderLabel.Text = _busy
                    ? "正在生成分镜图...\r\n\r\n保持当前布局与画板方向，请稍候。"
                    : hasPreview
                        ? string.Empty
                        : "等待生成分镜图...\r\n\r\n输入分镜描述或连接剧本分集节点\r\n可连多个节点保持角色一致性\r\n选择九宫格 / 六宫格布局\r\n支持横屏 / 竖屏画板";

                _storyboardGenerateButton.Text = _busy ? "生成中..." : actionText;
                _storyboardGenerateButton.Enabled = canGenerate;
                _storyboardGenerateButton.Cursor = canGenerate ? Cursors.Hand : Cursors.Default;
                _storyboardGenerateButton.BackColor = canGenerate ? Color.FromArgb(176, 74, 255) : Color.FromArgb(84, 74, 104);
                _storyboardGenerateButton.ForeColor = canGenerate ? Color.White : Color.FromArgb(176, 182, 194);
                var hasShots = Node.Params?.StoryboardShots != null && Node.Params.StoryboardShots.Count > 0;
                _storyboardEditButton.Enabled = !_busy && hasShots;
                _storyboardEditButton.Cursor = _storyboardEditButton.Enabled ? Cursors.Hand : Cursors.Default;
                ApplyToggleButtonStyle(_storyboardEditButton, false);
                _storyboardEditButton.ForeColor = _storyboardEditButton.Enabled
                    ? Color.WhiteSmoke
                    : Color.FromArgb(198, 204, 218);
                UpdateStoryboardPageControls();

                _runButton.Text = _busy ? "生成中" : GetRunButtonText(Node.Type);
                _runButton.Enabled = canGenerate;
                _runButton.Cursor = _runButton.Enabled ? Cursors.Hand : Cursors.Default;
                _runButton.BackColor = _runButton.Enabled ? Color.FromArgb(74, 161, 255) : Color.FromArgb(80, 88, 102);
                _runButton.ForeColor = _runButton.Enabled ? Color.White : Color.FromArgb(176, 182, 194);
                return;
            }

            if (Node.Type == WorkflowNodeCatalog.CreativeDescription)
            {
                var hasCreativeText = !string.IsNullOrWhiteSpace(WorkflowExecutor.NormalizeTextResult(Node.Type, Node.Output));
                _runButton.Text = _busy ? "拆分中" : GetRunButtonText(Node.Type);
                _runButton.Enabled = !_busy && hasCreativeText;
                _runButton.Cursor = _runButton.Enabled ? Cursors.Hand : Cursors.Default;
                _runButton.BackColor = _runButton.Enabled ? Color.FromArgb(114, 78, 255) : Color.FromArgb(84, 74, 104);
                _runButton.ForeColor = _runButton.Enabled ? Color.White : Color.FromArgb(176, 182, 194);
                return;
            }

            if (WorkflowNodeCatalog.IsDirectStudioNodeType(Node.Type))
            {
                _runButton.Text = _busy ? "生成中" : GetRunButtonText(Node.Type);
                _runButton.Enabled = !_busy;
                _runButton.Cursor = _runButton.Enabled ? Cursors.Hand : Cursors.Default;
                _runButton.BackColor = _runButton.Enabled ? Color.FromArgb(74, 161, 255) : Color.FromArgb(80, 88, 102);
                _runButton.ForeColor = _runButton.Enabled ? Color.White : Color.FromArgb(176, 182, 194);
                return;
            }

            if (Node.Type == WorkflowNodeCatalog.StoryboardVideo)
            {
                var hasPrompt = !string.IsNullOrWhiteSpace(Node.Params?.StoryboardVideoPrompt);
                var hasSelectedShots = Node.Params?.StoryboardVideoSelectedShotIds != null && Node.Params.StoryboardVideoSelectedShotIds.Count > 0;
                var hasFusedImage = !string.IsNullOrWhiteSpace(Node.Params?.StoryboardVideoFusedImagePath) &&
                                    File.Exists(Node.Params.StoryboardVideoFusedImagePath);
                var stage = (Node.Params?.StoryboardVideoStage ?? "idle").Trim().ToLowerInvariant();
                var canSaveStoryboardVideo = !_busy && hasPrompt && hasFusedImage;
                _saveButton.Visible = true;
                _saveButton.Enabled = canSaveStoryboardVideo;
                _saveButton.Cursor = canSaveStoryboardVideo ? Cursors.Hand : Cursors.Default;
                _saveButton.BackColor = canSaveStoryboardVideo ? Color.FromArgb(44, 108, 74) : Color.FromArgb(56, 60, 74);
                _saveButton.ForeColor = canSaveStoryboardVideo ? Color.White : Color.FromArgb(176, 182, 194);
                _runButton.Text = _busy
                    ? "生成中"
                    : stage switch
                    {
                        "prompting" when hasPrompt => "视频",
                        "selecting" when hasSelectedShots => "提示词",
                        _ => GetRunButtonText(Node.Type),
                    };
                _runButton.Enabled = !_busy;
                _runButton.Cursor = _runButton.Enabled ? Cursors.Hand : Cursors.Default;
                _runButton.BackColor = _runButton.Enabled ? Color.FromArgb(74, 161, 255) : Color.FromArgb(80, 88, 102);
                _runButton.ForeColor = _runButton.Enabled ? Color.White : Color.FromArgb(176, 182, 194);
                return;
            }

            if (Node.Type == WorkflowNodeCatalog.VideoPreview)
            {
                _runButton.Text = "预览";
                _runButton.Enabled = false;
                _runButton.Cursor = Cursors.Default;
                _runButton.BackColor = Color.FromArgb(80, 88, 102);
                _runButton.ForeColor = Color.FromArgb(176, 182, 194);
                return;
            }

            if (Node.Type == WorkflowNodeCatalog.VideoCollection)
            {
                var canRun = !_busy && Node.Params != null &&
                             (Node.Params.VideoCollectionSelectedArtifactPaths?.Count ?? 0) > 0;
                _runButton.Text = _busy ? "合集中" : GetRunButtonText(Node.Type);
                _runButton.Enabled = canRun;
                _runButton.Cursor = canRun ? Cursors.Hand : Cursors.Default;
                _runButton.BackColor = canRun ? Color.FromArgb(74, 161, 255) : Color.FromArgb(80, 88, 102);
                _runButton.ForeColor = canRun ? Color.White : Color.FromArgb(176, 182, 194);
                return;
            }

            _runButton.Text = _busy ? "生成中" : GetRunButtonText(Node.Type);
            _runButton.Enabled = !_busy;
            _runButton.Cursor = _runButton.Enabled ? Cursors.Hand : Cursors.Default;
            _runButton.BackColor = _runButton.Enabled ? Color.FromArgb(74, 161, 255) : Color.FromArgb(80, 88, 102);
            _runButton.ForeColor = _runButton.Enabled ? Color.White : Color.FromArgb(176, 182, 194);
        }

        private static string BuildStoryboardImageModelStatusText(WorkflowNode node)
        {
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);
            var settings = ModelConfig.Load();
            var textToImageModel = WorkflowModelResolver.ResolveStoryboardTextToImageModel(settings, node);
            var imageToImageModel = WorkflowModelResolver.ResolveStoryboardImageToImageModel(settings, node);
            if (textToImageModel == null && imageToImageModel == null)
            {
                return "当前调用模型：未配置";
            }

            if (textToImageModel != null &&
                imageToImageModel != null &&
                string.Equals(textToImageModel.Id, imageToImageModel.Id, StringComparison.OrdinalIgnoreCase))
            {
                return $"当前调用模型：文生/图生 {FormatStoryboardModelName(textToImageModel)}";
            }

            return $"当前调用模型：文生图 {FormatStoryboardModelName(textToImageModel)}  /  图生图 {FormatStoryboardModelName(imageToImageModel)}";
        }

        private static string FormatStoryboardModelName(ModelInfo? model)
        {
            if (model == null)
            {
                return "未配置";
            }

            if (!string.IsNullOrWhiteSpace(model.Name) && !string.IsNullOrWhiteSpace(model.Id))
            {
                return $"{model.Name} ({model.Id})";
            }

            return (model.Name ?? model.Id ?? "未配置").Trim();
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            var diameter = radius * 2;

            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _storyboardPreviewPictureBox.Image?.Dispose();
            }

            base.Dispose(disposing);
        }

        private static void OpenFileOrFolder(string filePath, bool openDirectory)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            var targetPath = openDirectory
                ? (File.Exists(filePath) ? Path.GetDirectoryName(filePath) : filePath)
                : filePath;
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(targetPath) { UseShellExecute = true });
            }
            catch
            {
            }
        }
    }
}
