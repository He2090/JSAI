using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public sealed class DirectStudioNodePanel : UserControl
    {
        private const int InputNumericWidth = 78;

        private static readonly string[] AspectRatioOptions =
        {
            "智能",
            "21:9",
            "16:9",
            "3:2",
            "4:3",
            "1:1",
            "3:4",
            "2:3",
            "9:16",
        };

        private static readonly (string Value, string Label)[] ResolutionOptions =
        {
            ("768", "普通 768"),
            ("1024", "普通 1024"),
            ("2K", "高清 2K"),
            ("4K", "超清 4K"),
        };

        private static readonly string[] QualityOptions = { "标清", "高清", "超清" };
        private static readonly string[] VideoAspectRatioOptions = { "16:9", "9:16" };
        private static readonly int[] VideoDurationOptions = { 5, 10, 15 };
        private static readonly (string Code, string Name)[] CloudVideoModelFamilyOptions =
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
        private static readonly Dictionary<string, string[]> CloudVideoSubModelOptions = new(StringComparer.OrdinalIgnoreCase)
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

        private readonly Label _headline = new();
        private readonly Label _modelLabel = new();
        private readonly TextBox _promptBox = new();
        private readonly Panel _referencePanel = new();
        private readonly PictureBox _referencePreview = new();
        private readonly Label _referencePathLabel = new();
        private readonly Button _pickReferenceButton = new();
        private readonly TableLayoutPanel _imageModeGrid = new();
        private readonly TableLayoutPanel _aspectGrid = new();
        private readonly TableLayoutPanel _resolutionGrid = new();
        private readonly Dictionary<string, Button> _imageModeButtons = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Button> _aspectButtons = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Button> _resolutionButtons = new(StringComparer.OrdinalIgnoreCase);
        private readonly NumericUpDown _widthNumeric = new();
        private readonly NumericUpDown _heightNumeric = new();
        private readonly TableLayoutPanel _videoSettingsGrid = new();
        private readonly TableLayoutPanel _videoAspectGrid = new();
        private readonly TableLayoutPanel _videoDurationGrid = new();
        private readonly TableLayoutPanel _videoQualityGrid = new();
        private readonly Dictionary<string, Button> _videoAspectButtons = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, Button> _videoDurationButtons = new();
        private readonly Dictionary<string, Button> _videoQualityButtons = new(StringComparer.OrdinalIgnoreCase);
        private readonly ComboBox _cloudVideoPlatformComboBox = new();
        private readonly ComboBox _cloudVideoModelFamilyComboBox = new();
        private readonly ComboBox _cloudVideoSubModelComboBox = new();
        private readonly NumericUpDown _durationNumeric = new();
        private readonly Label _videoDurationTitleLabel = new();
        private readonly ComboBox _qualityComboBox = new();
        private readonly Label _videoFrameRateTitleLabel = new();
        private readonly NumericUpDown _videoFrameRateNumeric = new();
        private readonly Label _videoPrefixTitleLabel = new();
        private readonly TextBox _videoPrefixBox = new();
        private readonly Button _generateButton = new();
        private readonly Label _statusLabel = new();
        private readonly PictureBox _resultPreview = new();
        private readonly Label _resultPlaceholderLabel = new();
        private readonly TextBox _artifactPathBox = new();
        private readonly Button _openArtifactButton = new();
        private readonly Button _openFolderButton = new();
        private readonly Button _saveArtifactButton = new();
        private readonly Button _clearArtifactButton = new();
        private readonly RichTextBox _positivePromptBox = new();
        private readonly RichTextBox _negativePromptBox = new();
        private readonly Label _negativePromptLabel = new();
        private readonly Label _referenceTitleLabel = new();
        private readonly Label _imageModeTitleLabel = new();
        private readonly Label _aspectTitleLabel = new();
        private readonly Label _resolutionTitleLabel = new();
        private readonly Label _sizeTitleLabel = new();
        private readonly TableLayoutPanel _sizePanel = new();

        private WorkflowNode? _node;
        private bool _busy;
        private bool _syncing;

        public DirectStudioNodePanel()
        {
            BackColor = Color.FromArgb(30, 35, 47);
            AutoScaleMode = AutoScaleMode.None;
            BuildLayout();
        }

        public event EventHandler? EntryChanged;
        public event EventHandler? InteractionStarted;
        public event EventHandler? GenerateRequested;

        public void Bind(WorkflowNode node, bool busy)
        {
            _node = node;
            _busy = busy;
            _node.Params ??= new WorkflowNodeParameters();
            _node.Params.EnsureDefaults(node.Type);
            SyncFromNode();
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = BackColor,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(16),
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57F));
            root.Controls.Add(BuildEditorColumn(), 0, 0);
            root.Controls.Add(BuildResultColumn(), 1, 0);
            Controls.Add(root);
        }

        private Control BuildEditorColumn()
        {
            var shell = CreateShellPanel();
            shell.AutoScroll = true;

            _headline.Dock = DockStyle.Top;
            _headline.Height = 38;
            _headline.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold, GraphicsUnit.Point);
            _headline.ForeColor = Color.White;
            _headline.TextAlign = ContentAlignment.MiddleLeft;

            _modelLabel.Dock = DockStyle.Top;
            _modelLabel.Height = 42;
            _modelLabel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            _modelLabel.ForeColor = Color.FromArgb(176, 190, 220);
            _modelLabel.TextAlign = ContentAlignment.MiddleLeft;

            _promptBox.Dock = DockStyle.Top;
            _promptBox.Height = 160;
            _promptBox.Multiline = true;
            _promptBox.ScrollBars = ScrollBars.Vertical;
            _promptBox.BorderStyle = BorderStyle.FixedSingle;
            _promptBox.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            _promptBox.BackColor = Color.FromArgb(23, 28, 40);
            _promptBox.ForeColor = Color.White;
            _promptBox.Enter += (_, _) => InteractionStarted?.Invoke(this, EventArgs.Empty);
            _promptBox.MouseDown += (_, _) => InteractionStarted?.Invoke(this, EventArgs.Empty);
            _promptBox.TextChanged += (_, _) =>
            {
                if (_syncing || _node?.Params == null)
                {
                    return;
                }

                _node.Params.Input = _promptBox.Text;
                _node.Params.DirectPositivePrompt = string.Empty;
                _node.Params.DirectNegativePrompt = string.Empty;
                EntryChanged?.Invoke(this, EventArgs.Empty);
            };

            ConfigureMiniButton(_pickReferenceButton, "选择参考图", (_, _) => PickReferenceImage());
            _pickReferenceButton.Dock = DockStyle.Bottom;

            _referencePreview.Size = new Size(132, 92);
            _referencePreview.SizeMode = PictureBoxSizeMode.Zoom;
            _referencePreview.BorderStyle = BorderStyle.FixedSingle;
            _referencePreview.BackColor = Color.FromArgb(23, 28, 40);
            _referencePreview.Click += (_, _) => OpenReferenceImage();

            _referencePathLabel.AutoSize = false;
            _referencePathLabel.Width = 220;
            _referencePathLabel.Height = 92;
            _referencePathLabel.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            _referencePathLabel.ForeColor = Color.FromArgb(186, 197, 216);
            _referencePathLabel.TextAlign = ContentAlignment.MiddleLeft;

            var referenceRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                AutoScroll = false,
                BackColor = Color.Transparent,
                Padding = new Padding(0),
                Margin = new Padding(0),
            };
            referenceRow.Controls.Add(_referencePreview);
            referenceRow.Controls.Add(_referencePathLabel);

            _referenceTitleLabel.Text = "参考图";
            _referenceTitleLabel.Dock = DockStyle.Top;
            _referenceTitleLabel.Height = 28;
            _referenceTitleLabel.ForeColor = Color.White;
            _referenceTitleLabel.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);

            _referencePanel.Dock = DockStyle.Top;
            _referencePanel.Height = 152;
            _referencePanel.BackColor = Color.Transparent;
            _referencePanel.Controls.Add(referenceRow);
            _referencePanel.Controls.Add(_pickReferenceButton);
            _referencePanel.Controls.Add(_referenceTitleLabel);

            _imageModeTitleLabel.Text = "生成模式";
            _imageModeTitleLabel.Dock = DockStyle.Top;
            _imageModeTitleLabel.Height = 28;
            _imageModeTitleLabel.ForeColor = Color.White;
            _imageModeTitleLabel.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);

            _imageModeGrid.Dock = DockStyle.Top;
            _imageModeGrid.Height = 58;
            _imageModeGrid.ColumnCount = 3;
            _imageModeGrid.RowCount = 1;
            _imageModeGrid.BackColor = Color.Transparent;
            _imageModeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3333F));
            _imageModeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3333F));
            _imageModeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3333F));
            AddImageModeButton("single", "普通", 0);
            AddImageModeButton("expression", "九宫格", 1);
            AddImageModeButton("threeview", "三视图", 2);

            _aspectTitleLabel.Text = "选择比例";
            _aspectTitleLabel.Dock = DockStyle.Top;
            _aspectTitleLabel.Height = 28;
            _aspectTitleLabel.ForeColor = Color.White;
            _aspectTitleLabel.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);

            _aspectGrid.Dock = DockStyle.Top;
            _aspectGrid.Height = 154;
            _aspectGrid.ColumnCount = 3;
            _aspectGrid.RowCount = 3;
            _aspectGrid.BackColor = Color.Transparent;
            for (var i = 0; i < 3; i++)
            {
                _aspectGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3333F));
                _aspectGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3333F));
            }

            for (var index = 0; index < AspectRatioOptions.Length; index++)
            {
                var option = AspectRatioOptions[index];
                var button = CreateOptionButton(option);
                button.Click += (_, _) => SetAspectRatio(option);
                _aspectButtons[option] = button;
                _aspectGrid.Controls.Add(button, index % 3, index / 3);
            }

            _resolutionTitleLabel.Text = "选择分辨率";
            _resolutionTitleLabel.Dock = DockStyle.Top;
            _resolutionTitleLabel.Height = 28;
            _resolutionTitleLabel.ForeColor = Color.White;
            _resolutionTitleLabel.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);

            _resolutionGrid.Dock = DockStyle.Top;
            _resolutionGrid.Height = 102;
            _resolutionGrid.ColumnCount = 2;
            _resolutionGrid.RowCount = 2;
            _resolutionGrid.BackColor = Color.Transparent;
            _resolutionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            _resolutionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            _resolutionGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            _resolutionGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            for (var index = 0; index < ResolutionOptions.Length; index++)
            {
                var option = ResolutionOptions[index];
                var button = CreateOptionButton(option.Label);
                button.Click += (_, _) => SetResolution(option.Value);
                _resolutionButtons[option.Value] = button;
                _resolutionGrid.Controls.Add(button, index % 2, index / 2);
            }

            _sizeTitleLabel.Text = "Width / Height 宽高 (px)";
            _sizeTitleLabel.Dock = DockStyle.Top;
            _sizeTitleLabel.Height = 28;
            _sizeTitleLabel.ForeColor = Color.White;
            _sizeTitleLabel.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);

            ConfigureNumeric(_widthNumeric, 256, 8192);
            ConfigureNumeric(_heightNumeric, 256, 8192);
            _widthNumeric.ValueChanged += (_, _) => CommitCanvasSize();
            _heightNumeric.ValueChanged += (_, _) => CommitCanvasSize();

            _sizePanel.Dock = DockStyle.Top;
            _sizePanel.Height = 58;
            _sizePanel.ColumnCount = 5;
            _sizePanel.BackColor = Color.Transparent;
            _sizePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 18F));
            _sizePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, InputNumericWidth));
            _sizePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 18F));
            _sizePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, InputNumericWidth));
            _sizePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28F));
            _sizePanel.Controls.Add(CreateInlineLabel("W"), 0, 0);
            _sizePanel.Controls.Add(_widthNumeric, 1, 0);
            _sizePanel.Controls.Add(CreateInlineLabel("H"), 2, 0);
            _sizePanel.Controls.Add(_heightNumeric, 3, 0);
            _sizePanel.Controls.Add(CreateInlineLabel("PX"), 4, 0);

            ConfigureNumeric(_durationNumeric, 5, 30);
            _durationNumeric.Dock = DockStyle.Top;
            _durationNumeric.Height = 34;
            _durationNumeric.ValueChanged += (_, _) =>
            {
                if (_syncing || _node?.Params == null)
                {
                    return;
                }

                _node.Params.DirectDurationSeconds = (int)_durationNumeric.Value;
                EntryChanged?.Invoke(this, EventArgs.Empty);
            };

            _videoDurationTitleLabel.Text = "Duration / 时长 (s)";
            _videoDurationTitleLabel.Dock = DockStyle.Top;
            _videoDurationTitleLabel.Height = 28;
            _videoDurationTitleLabel.ForeColor = Color.White;
            _videoDurationTitleLabel.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);

            _qualityComboBox.Dock = DockStyle.Fill;
            _qualityComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _qualityComboBox.FlatStyle = FlatStyle.Flat;
            _qualityComboBox.BackColor = Color.FromArgb(23, 28, 40);
            _qualityComboBox.ForeColor = Color.White;
            _qualityComboBox.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            _qualityComboBox.Items.AddRange(QualityOptions.Cast<object>().ToArray());
            _qualityComboBox.SelectedIndexChanged += (_, _) =>
            {
                if (_syncing || _node?.Params == null || _qualityComboBox.SelectedItem == null)
                {
                    return;
                }

                _node.Params.DirectQuality = _qualityComboBox.SelectedItem.ToString() ?? "高清";
                EntryChanged?.Invoke(this, EventArgs.Empty);
            };

            _videoFrameRateTitleLabel.Text = "Frame Rate / 帧率 (fps)";
            _videoFrameRateTitleLabel.Dock = DockStyle.Top;
            _videoFrameRateTitleLabel.Height = 28;
            _videoFrameRateTitleLabel.ForeColor = Color.White;
            _videoFrameRateTitleLabel.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);

            ConfigureNumeric(_videoFrameRateNumeric, 8, 60);
            _videoFrameRateNumeric.Dock = DockStyle.Top;
            _videoFrameRateNumeric.Height = 34;
            _videoFrameRateNumeric.ValueChanged += (_, _) =>
            {
                if (_syncing || _node?.Params == null)
                {
                    return;
                }

                _node.Params.DirectVideoFrameRate = (int)_videoFrameRateNumeric.Value;
                EntryChanged?.Invoke(this, EventArgs.Empty);
            };

            _videoPrefixTitleLabel.Text = "Filename Prefix / 保存前缀";
            _videoPrefixTitleLabel.Dock = DockStyle.Top;
            _videoPrefixTitleLabel.Height = 28;
            _videoPrefixTitleLabel.ForeColor = Color.White;
            _videoPrefixTitleLabel.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);

            _videoPrefixBox.Dock = DockStyle.Top;
            _videoPrefixBox.Height = 34;
            _videoPrefixBox.BorderStyle = BorderStyle.FixedSingle;
            _videoPrefixBox.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            _videoPrefixBox.BackColor = Color.FromArgb(23, 28, 40);
            _videoPrefixBox.ForeColor = Color.White;
            _videoPrefixBox.TextChanged += (_, _) =>
            {
                if (_syncing || _node?.Params == null)
                {
                    return;
                }

                _node.Params.DirectVideoFilenamePrefix = _videoPrefixBox.Text;
                EntryChanged?.Invoke(this, EventArgs.Empty);
            };

            ConfigureConfigComboBox(_cloudVideoPlatformComboBox);
            ConfigureConfigComboBox(_cloudVideoModelFamilyComboBox);
            ConfigureConfigComboBox(_cloudVideoSubModelComboBox);
            _cloudVideoPlatformComboBox.SelectionChangeCommitted += (_, _) => CommitCloudVideoPlatform();
            _cloudVideoModelFamilyComboBox.SelectionChangeCommitted += (_, _) => CommitCloudVideoModelFamily();
            _cloudVideoSubModelComboBox.SelectionChangeCommitted += (_, _) => CommitCloudVideoSubModel();

            _videoSettingsGrid.Dock = DockStyle.Top;
            _videoSettingsGrid.Height = 164;
            _videoSettingsGrid.ColumnCount = 3;
            _videoSettingsGrid.RowCount = 4;
            _videoSettingsGrid.BackColor = Color.Transparent;
            _videoSettingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
            _videoSettingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            _videoSettingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            _videoSettingsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
            _videoSettingsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            _videoSettingsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
            _videoSettingsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));

            ConfigureOptionGrid(_videoAspectGrid, VideoAspectRatioOptions.Length);
            for (var index = 0; index < VideoAspectRatioOptions.Length; index++)
            {
                AddVideoAspectButton(VideoAspectRatioOptions[index], index);
            }

            ConfigureOptionGrid(_videoDurationGrid, VideoDurationOptions.Length);
            for (var index = 0; index < VideoDurationOptions.Length; index++)
            {
                AddVideoDurationButton(VideoDurationOptions[index], index);
            }

            ConfigureOptionGrid(_videoQualityGrid, QualityOptions.Length);
            for (var index = 0; index < QualityOptions.Length; index++)
            {
                AddVideoQualityButton(QualityOptions[index], index);
            }

            _videoSettingsGrid.Controls.Add(CreateSectionLabel("画幅"), 0, 0);
            _videoSettingsGrid.Controls.Add(_videoAspectGrid, 0, 1);
            _videoSettingsGrid.Controls.Add(CreateSectionLabel("时长"), 0, 2);
            _videoSettingsGrid.Controls.Add(_videoDurationGrid, 0, 3);
            _videoSettingsGrid.Controls.Add(CreateSectionLabel("画质"), 0, 4);
            _videoSettingsGrid.Controls.Add(_videoQualityGrid, 0, 5);

            _videoSettingsGrid.Controls.Clear();
            _videoSettingsGrid.Controls.Add(CreateConfigLabel("视频接口 / Relay API"), 0, 0);
            _videoSettingsGrid.Controls.Add(CreateConfigLabel("视频大模型 / Model Family"), 1, 0);
            _videoSettingsGrid.Controls.Add(CreateConfigLabel("子模型 / Sub-model"), 2, 0);
            _videoSettingsGrid.Controls.Add(_cloudVideoPlatformComboBox, 0, 1);
            _videoSettingsGrid.Controls.Add(_cloudVideoModelFamilyComboBox, 1, 1);
            _videoSettingsGrid.Controls.Add(_cloudVideoSubModelComboBox, 2, 1);
            _videoSettingsGrid.Controls.Add(CreateConfigLabel("画幅 / Aspect Ratio"), 0, 2);
            _videoSettingsGrid.Controls.Add(CreateConfigLabel("时长 / Duration"), 1, 2);
            _videoSettingsGrid.Controls.Add(CreateConfigLabel("画质 / Quality"), 2, 2);
            _videoSettingsGrid.Controls.Add(_videoAspectGrid, 0, 3);
            _videoSettingsGrid.Controls.Add(_videoDurationGrid, 1, 3);
            _videoSettingsGrid.Controls.Add(_videoQualityGrid, 2, 3);

            _generateButton.Dock = DockStyle.Top;
            _generateButton.Height = 54;
            _generateButton.FlatStyle = FlatStyle.Flat;
            _generateButton.FlatAppearance.BorderSize = 0;
            _generateButton.BackColor = Color.FromArgb(255, 122, 0);
            _generateButton.ForeColor = Color.Black;
            _generateButton.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold, GraphicsUnit.Point);
            _generateButton.Click += (_, _) =>
            {
                InteractionStarted?.Invoke(this, EventArgs.Empty);
                GenerateRequested?.Invoke(this, EventArgs.Empty);
            };

            _statusLabel.Dock = DockStyle.Top;
            _statusLabel.Height = 38;
            _statusLabel.ForeColor = Color.FromArgb(176, 190, 220);
            _statusLabel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;

            shell.Controls.Add(_statusLabel);
            shell.Controls.Add(_generateButton);
            shell.Controls.Add(_videoPrefixBox);
            shell.Controls.Add(_videoPrefixTitleLabel);
            shell.Controls.Add(_videoFrameRateNumeric);
            shell.Controls.Add(_videoFrameRateTitleLabel);
            shell.Controls.Add(_durationNumeric);
            shell.Controls.Add(_videoDurationTitleLabel);
            shell.Controls.Add(_sizePanel);
            shell.Controls.Add(_sizeTitleLabel);
            shell.Controls.Add(_videoSettingsGrid);
            shell.Controls.Add(_resolutionGrid);
            shell.Controls.Add(_resolutionTitleLabel);
            shell.Controls.Add(_aspectGrid);
            shell.Controls.Add(_aspectTitleLabel);
            shell.Controls.Add(_imageModeGrid);
            shell.Controls.Add(_imageModeTitleLabel);
            shell.Controls.Add(_referencePanel);
            shell.Controls.Add(_promptBox);
            shell.Controls.Add(CreateSectionLabel("提示词 / Prompt"));
            shell.Controls.Add(_modelLabel);
            shell.Controls.Add(_headline);

            return shell;
        }

        private Control BuildResultColumn()
        {
            var shell = CreateShellPanel();

            _resultPreview.Dock = DockStyle.Top;
            _resultPreview.Height = 300;
            _resultPreview.SizeMode = PictureBoxSizeMode.Zoom;
            _resultPreview.BorderStyle = BorderStyle.FixedSingle;
            _resultPreview.BackColor = Color.FromArgb(23, 28, 40);
            _resultPreview.Click += (_, _) => OpenArtifact();

            _resultPlaceholderLabel.Dock = DockStyle.Fill;
            _resultPlaceholderLabel.ForeColor = Color.FromArgb(132, 150, 184);
            _resultPlaceholderLabel.Text = "生成后会在这里显示结果预览。";
            _resultPlaceholderLabel.TextAlign = ContentAlignment.MiddleCenter;
            _resultPlaceholderLabel.BackColor = Color.Transparent;

            var previewHost = new Panel
            {
                Dock = DockStyle.Top,
                Height = 300,
                BackColor = Color.Transparent,
            };
            previewHost.Controls.Add(_resultPlaceholderLabel);
            previewHost.Controls.Add(_resultPreview);

            ConfigureActionButton(_openArtifactButton, "打开文件", (_, _) => OpenArtifact());
            ConfigureActionButton(_openFolderButton, "打开目录", (_, _) => OpenArtifactFolder());
            ConfigureActionButton(_saveArtifactButton, "图片保存", (_, _) => SaveArtifactAs());
            ConfigureActionButton(_clearArtifactButton, "清空", (_, _) => ClearArtifact());

            var actionGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 46,
                ColumnCount = 4,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 10, 0, 4),
            };
            for (var i = 0; i < 4; i++)
            {
                actionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            }
            actionGrid.Controls.Add(_openArtifactButton, 0, 0);
            actionGrid.Controls.Add(_openFolderButton, 1, 0);
            actionGrid.Controls.Add(_saveArtifactButton, 2, 0);
            actionGrid.Controls.Add(_clearArtifactButton, 3, 0);

            _artifactPathBox.Dock = DockStyle.Top;
            _artifactPathBox.Height = 26;
            _artifactPathBox.ReadOnly = true;
            _artifactPathBox.BorderStyle = BorderStyle.FixedSingle;
            _artifactPathBox.BackColor = Color.FromArgb(23, 28, 40);
            _artifactPathBox.ForeColor = Color.White;

            _positivePromptBox.Dock = DockStyle.Top;
            _positivePromptBox.Height = 150;
            _positivePromptBox.BorderStyle = BorderStyle.FixedSingle;
            _positivePromptBox.BackColor = Color.FromArgb(23, 28, 40);
            _positivePromptBox.ForeColor = Color.White;
            _positivePromptBox.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
            _positivePromptBox.ReadOnly = false;
            _positivePromptBox.Enter += (_, _) => InteractionStarted?.Invoke(this, EventArgs.Empty);
            _positivePromptBox.MouseDown += (_, _) => InteractionStarted?.Invoke(this, EventArgs.Empty);
            _positivePromptBox.TextChanged += (_, _) =>
            {
                if (_syncing || _node?.Params == null)
                {
                    return;
                }

                _node.Params.DirectPositivePrompt = _positivePromptBox.Text;
                EntryChanged?.Invoke(this, EventArgs.Empty);
            };

            _negativePromptLabel.Text = "反向提示词";
            _negativePromptLabel.Text = "反向提示词 / Negative Prompt";
            _negativePromptLabel.Dock = DockStyle.Top;
            _negativePromptLabel.Height = 28;
            _negativePromptLabel.ForeColor = Color.White;
            _negativePromptLabel.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);

            _negativePromptBox.Dock = DockStyle.Top;
            _negativePromptBox.Height = 150;
            _negativePromptBox.BorderStyle = BorderStyle.FixedSingle;
            _negativePromptBox.BackColor = Color.FromArgb(23, 28, 40);
            _negativePromptBox.ForeColor = Color.White;
            _negativePromptBox.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
            _negativePromptBox.ReadOnly = false;
            _negativePromptBox.Enter += (_, _) => InteractionStarted?.Invoke(this, EventArgs.Empty);
            _negativePromptBox.MouseDown += (_, _) => InteractionStarted?.Invoke(this, EventArgs.Empty);
            _negativePromptBox.TextChanged += (_, _) =>
            {
                if (_syncing || _node?.Params == null)
                {
                    return;
                }

                _node.Params.DirectNegativePrompt = _negativePromptBox.Text;
                EntryChanged?.Invoke(this, EventArgs.Empty);
            };

            shell.Controls.Add(_negativePromptBox);
            shell.Controls.Add(_negativePromptLabel);
            shell.Controls.Add(_positivePromptBox);
            shell.Controls.Add(CreateSectionLabel("正向提示词 / Positive Prompt"));
            shell.Controls.Add(_artifactPathBox);
            shell.Controls.Add(actionGrid);
            shell.Controls.Add(previewHost);
            shell.Controls.Add(CreateSectionLabel("生成结果"));

            return shell;
        }

        private static Panel CreateShellPanel()
        {
            return new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(24, 31, 45),
                Padding = new Padding(16),
                Margin = new Padding(8),
            };
        }

        private static Label CreateSectionLabel(string text)
        {
            return new Label
            {
                Dock = DockStyle.Top,
                Height = 30,
                Text = text,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point),
                TextAlign = ContentAlignment.MiddleLeft,
            };
        }

        private static Label CreateInlineLabel(string text)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point),
                TextAlign = ContentAlignment.MiddleLeft,
            };
        }

        private static Label CreateConfigLabel(string text)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                Height = 18,
                Text = text,
                ForeColor = Color.FromArgb(148, 156, 176),
                Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point),
                TextAlign = ContentAlignment.BottomLeft,
                Margin = Padding.Empty,
            };
        }

        private static void ConfigureConfigComboBox(ComboBox comboBox)
        {
            comboBox.Dock = DockStyle.Fill;
            comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox.FlatStyle = FlatStyle.Flat;
            comboBox.BackColor = Color.FromArgb(23, 28, 40);
            comboBox.ForeColor = Color.White;
            comboBox.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            comboBox.Margin = Padding.Empty;
        }

        private static void ConfigureMiniButton(Button button, string text, EventHandler clickHandler)
        {
            button.Text = text;
            button.Height = 34;
            button.Width = 132;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(76, 95, 126);
            button.BackColor = Color.FromArgb(58, 71, 96);
            button.ForeColor = Color.White;
            button.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
            button.Click += clickHandler;
        }

        private static void ConfigureActionButton(Button button, string text, EventHandler clickHandler)
        {
            button.Text = text;
            button.Dock = DockStyle.Fill;
            button.Height = 38;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(76, 95, 126);
            button.BackColor = Color.FromArgb(58, 71, 96);
            button.ForeColor = Color.White;
            button.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
            button.Margin = new Padding(4, 0, 4, 0);
            button.Click += clickHandler;
        }

        private static Button CreateOptionButton(string text)
        {
            return new Button
            {
                Dock = DockStyle.Fill,
                Height = 42,
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(76, 95, 126) },
                BackColor = Color.FromArgb(239, 236, 230),
                ForeColor = Color.FromArgb(23, 28, 40),
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point),
                Text = text,
                Margin = new Padding(4),
            };
        }

        private static void ConfigureNumeric(NumericUpDown numeric, int minimum, int maximum)
        {
            numeric.Dock = DockStyle.Fill;
            numeric.Minimum = minimum;
            numeric.Maximum = maximum;
            numeric.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            numeric.BackColor = Color.FromArgb(23, 28, 40);
            numeric.ForeColor = Color.White;
            numeric.BorderStyle = BorderStyle.FixedSingle;
            numeric.ThousandsSeparator = false;
        }

        private void AddImageModeButton(string mode, string text, int columnIndex)
        {
            var button = CreateOptionButton(text);
            button.Click += (_, _) => SetImageMode(mode);
            _imageModeButtons[mode] = button;
            _imageModeGrid.Controls.Add(button, columnIndex, 0);
        }

        private static void ConfigureOptionGrid(TableLayoutPanel grid, int columnCount)
        {
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = columnCount;
            grid.RowCount = 1;
            grid.BackColor = Color.Transparent;
            grid.Margin = new Padding(0);
            grid.Padding = new Padding(0);
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            for (var index = 0; index < columnCount; index++)
            {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / columnCount));
            }
        }

        private void AddVideoAspectButton(string aspectRatio, int columnIndex)
        {
            var button = CreateOptionButton(aspectRatio);
            button.Click += (_, _) => SetVideoAspectRatio(aspectRatio);
            _videoAspectButtons[aspectRatio] = button;
            _videoAspectGrid.Controls.Add(button, columnIndex, 0);
        }

        private void AddVideoDurationButton(int seconds, int columnIndex)
        {
            var button = CreateOptionButton($"{seconds}秒");
            button.Click += (_, _) => SetVideoDuration(seconds);
            _videoDurationButtons[seconds] = button;
            _videoDurationGrid.Controls.Add(button, columnIndex, 0);
        }

        private void AddVideoQualityButton(string quality, int columnIndex)
        {
            var button = CreateOptionButton(quality);
            button.Click += (_, _) => SetVideoQuality(quality);
            _videoQualityButtons[quality] = button;
            _videoQualityGrid.Controls.Add(button, columnIndex, 0);
        }

        private void SyncFromNode()
        {
            if (_node?.Params == null)
            {
                return;
            }

            _syncing = true;
            try
            {
                var parameters = _node.Params;
                var nodeType = WorkflowNodeCatalog.NormalizeNodeType(_node.Type);
                var isImageNode = IsImageNode(nodeType);
                var isVideoNode = IsVideoNode(nodeType);
                EnsureCompatibleCloudVideoSelections(parameters);
                var useCloudVideoEditor = isVideoNode && ShouldUseCloudVideoEditor();
                var isLocalVideoExecution = isVideoNode && !useCloudVideoEditor;
                if (isLocalVideoExecution)
                {
                    ClampLocalVideoCanvas(parameters);
                    ConfigureLocalVideoDimensionBounds(parameters);
                }
                else
                {
                    ResetVideoDimensionBounds();
                }
                var requiresReference = RequiresReference(nodeType);
                var promptModelName = ResolvePromptModelName();
                var executionModelName = ResolveExecutionModelName();
                var modeLabel = isImageNode
                    ? WorkflowNodeParameters.GetDirectImageModeDisplayName(parameters.DirectImageMode)
                    : "视频 / Video";

                _headline.Text = $"{executionModelName} · {modeLabel} 创作模式";
                _modelLabel.Text = $"提示词模型 Prompt Model: {promptModelName}    执行模型 Execution Model: {executionModelName}";
                _promptBox.Text = parameters.Input ?? string.Empty;

                _referencePanel.Visible = requiresReference;
                if (requiresReference)
                {
                    _referenceTitleLabel.Text = nodeType == WorkflowNodeCatalog.ImageToImage ? "参考图片" : "参考素材";
                    _referencePathLabel.Text = string.IsNullOrWhiteSpace(parameters.DirectReferenceImagePath)
                        ? "尚未选择参考图。"
                        : parameters.DirectReferenceImagePath;
                    LoadImagePreview(_referencePreview, parameters.DirectReferenceImagePath);
                }
                else
                {
                    _referencePathLabel.Text = string.Empty;
                    LoadImagePreview(_referencePreview, null);
                }

                _imageModeTitleLabel.Visible = isImageNode;
                _imageModeGrid.Visible = isImageNode;
                _aspectTitleLabel.Visible = isImageNode;
                _aspectGrid.Visible = isImageNode;
                _resolutionTitleLabel.Visible = isImageNode;
                _resolutionGrid.Visible = isImageNode;
                _sizeTitleLabel.Visible = isImageNode || (isVideoNode && isLocalVideoExecution);
                _sizePanel.Visible = isImageNode || (isVideoNode && isLocalVideoExecution);
                _videoSettingsGrid.Visible = isVideoNode && useCloudVideoEditor;
                _videoDurationTitleLabel.Visible = isVideoNode && isLocalVideoExecution;
                _videoFrameRateTitleLabel.Visible = isVideoNode && isLocalVideoExecution;
                _videoFrameRateNumeric.Visible = isVideoNode && isLocalVideoExecution;
                _videoPrefixTitleLabel.Visible = isVideoNode && isLocalVideoExecution;
                _videoPrefixBox.Visible = isVideoNode && isLocalVideoExecution;
                _durationNumeric.Visible = isVideoNode && isLocalVideoExecution;
                _qualityComboBox.Visible = !isVideoNode;

                if (isImageNode)
                {
                    UpdateSelectionButtons(_imageModeButtons, parameters.DirectImageMode);
                    UpdateSelectionButtons(_aspectButtons, parameters.DirectAspectRatio);
                    UpdateSelectionButtons(_resolutionButtons, parameters.DirectResolutionPreset);
                }

                _widthNumeric.Value = ClampNumericValue(_widthNumeric, parameters.DirectWidth);
                _heightNumeric.Value = ClampNumericValue(_heightNumeric, parameters.DirectHeight);
                _durationNumeric.Value = ClampNumericValue(_durationNumeric, Math.Max(5, parameters.DirectDurationSeconds));
                _videoFrameRateNumeric.Value = ClampNumericValue(_videoFrameRateNumeric, Math.Max(8, parameters.DirectVideoFrameRate));
                _videoPrefixBox.Text = parameters.DirectVideoFilenamePrefix ?? string.Empty;

                var quality = QualityOptions.FirstOrDefault(item => string.Equals(item, parameters.DirectQuality, StringComparison.OrdinalIgnoreCase)) ?? "高清";
                _qualityComboBox.SelectedItem = quality;
                if (isVideoNode)
                {
                    var videoAspect = VideoAspectRatioOptions.FirstOrDefault(item => string.Equals(item, parameters.DirectAspectRatio, StringComparison.OrdinalIgnoreCase)) ?? "16:9";
                    var videoDuration = VideoDurationOptions.Contains(parameters.DirectDurationSeconds)
                        ? parameters.DirectDurationSeconds
                        : 10;
                    var videoQuality = QualityOptions.FirstOrDefault(item => string.Equals(item, parameters.DirectQuality, StringComparison.OrdinalIgnoreCase)) ?? "高清";
                    UpdateSelectionButtons(_videoAspectButtons, videoAspect);
                    UpdateSelectionButtons(_videoDurationButtons, videoDuration);
                    UpdateSelectionButtons(_videoQualityButtons, videoQuality);
                }

                if (isVideoNode)
                {
                    var allowedDurations = BuildAvailableCloudVideoDurations(parameters).ToList();
                    var videoAspect = VideoAspectRatioOptions.FirstOrDefault(item => string.Equals(item, parameters.DirectAspectRatio, StringComparison.OrdinalIgnoreCase)) ?? "16:9";
                    var videoDuration = allowedDurations.Contains(parameters.DirectDurationSeconds)
                        ? parameters.DirectDurationSeconds
                        : allowedDurations.LastOrDefault(10);
                    var videoQuality = QualityOptions.FirstOrDefault(item => string.Equals(item, parameters.DirectQuality, StringComparison.OrdinalIgnoreCase)) ?? "高清";
                    parameters.DirectDurationSeconds = videoDuration;
                    UpdateSelectionButtons(_videoAspectButtons, videoAspect);
                    UpdateSelectionButtons(_videoDurationButtons, videoDuration);
                    UpdateSelectionButtons(_videoQualityButtons, videoQuality);
                    if (useCloudVideoEditor)
                    {
                        PopulateCloudVideoEditors(parameters);
                    }
                }

                _artifactPathBox.Text = _node.ArtifactPath ?? string.Empty;
                _positivePromptBox.Text = parameters.DirectPositivePrompt ?? string.Empty;
                _negativePromptBox.Text = parameters.DirectNegativePrompt ?? string.Empty;
                _negativePromptLabel.Visible = isImageNode || (isVideoNode && isLocalVideoExecution) || !string.IsNullOrWhiteSpace(parameters.DirectNegativePrompt);
                _negativePromptBox.Visible = isImageNode || (isVideoNode && isLocalVideoExecution) || !string.IsNullOrWhiteSpace(parameters.DirectNegativePrompt);

                UpdateResultPreview(_node.ArtifactPath, _node.ArtifactKind);
                UpdateStatus(nodeType);
                UpdateButtonsState(nodeType);
            }
            finally
            {
                _syncing = false;
            }
        }

        private void UpdateButtonsState(string nodeType)
        {
            var canOpenArtifact = !string.IsNullOrWhiteSpace(_node?.ArtifactPath) && File.Exists(_node.ArtifactPath);
            var isVideoNode = IsVideoNode(nodeType);
            var useCloudVideoEditor = isVideoNode && ShouldUseCloudVideoEditor();
            var isLocalVideoExecution = isVideoNode && !useCloudVideoEditor;
            var allowedDurations = BuildAvailableCloudVideoDurations(_node?.Params).ToHashSet();

            _promptBox.ReadOnly = _busy;
            _positivePromptBox.ReadOnly = _busy;
            _negativePromptBox.ReadOnly = _busy;
            _pickReferenceButton.Enabled = !_busy;

            foreach (var button in _imageModeButtons.Values)
            {
                button.Enabled = !_busy;
            }

            foreach (var button in _aspectButtons.Values)
            {
                button.Enabled = !_busy;
            }

            foreach (var button in _resolutionButtons.Values)
            {
                button.Enabled = !_busy;
            }

            foreach (var button in _videoAspectButtons.Values)
            {
                button.Enabled = !_busy;
            }

            foreach (var button in _videoDurationButtons.Values)
            {
                button.Enabled = !_busy && (!isVideoNode || isLocalVideoExecution || allowedDurations.Contains(_videoDurationButtons.First(pair => pair.Value == button).Key));
            }

            foreach (var button in _videoQualityButtons.Values)
            {
                button.Enabled = !_busy;
            }

            _cloudVideoPlatformComboBox.Enabled = !_busy && isVideoNode && useCloudVideoEditor;
            _cloudVideoModelFamilyComboBox.Enabled = !_busy && isVideoNode && useCloudVideoEditor;
            _cloudVideoSubModelComboBox.Enabled = !_busy && isVideoNode && useCloudVideoEditor;
            _widthNumeric.Enabled = !_busy;
            _heightNumeric.Enabled = !_busy;
            _durationNumeric.Enabled = !_busy;
            _videoFrameRateNumeric.Enabled = !_busy;
            _videoPrefixBox.ReadOnly = _busy;
            _qualityComboBox.Enabled = !_busy;
            _openArtifactButton.Enabled = canOpenArtifact;
            _openFolderButton.Enabled = canOpenArtifact;
            _saveArtifactButton.Enabled = canOpenArtifact;
            _clearArtifactButton.Enabled = !_busy && (canOpenArtifact || !string.IsNullOrWhiteSpace(_positivePromptBox.Text) || !string.IsNullOrWhiteSpace(_negativePromptBox.Text));
            _generateButton.Enabled = !_busy;

            _generateButton.Text = nodeType switch
            {
                WorkflowNodeCatalog.ImageToImage => _busy ? "生成中..." : "生成图生图",
                WorkflowNodeCatalog.TextToVideo => _busy ? "生成中..." : "生成视频",
                WorkflowNodeCatalog.TextImageToVideo => _busy ? "生成中..." : "生成文图视频",
                _ => _busy ? "生成中..." : "生成图片",
            };
        }

        private void UpdateResultPreview(string? artifactPath, string? artifactKind)
        {
            var path = (artifactPath ?? string.Empty).Trim();
            var isImage = !string.IsNullOrWhiteSpace(path) && File.Exists(path) && IsImageFile(path);

            if (isImage)
            {
                LoadImagePreview(_resultPreview, path);
                _resultPlaceholderLabel.Visible = false;
                _resultPreview.Visible = true;
                return;
            }

            _resultPreview.Image?.Dispose();
            _resultPreview.Image = null;
            _resultPreview.Visible = false;
            _resultPlaceholderLabel.Visible = true;
            _resultPlaceholderLabel.Text = string.Equals(artifactKind, "video", StringComparison.OrdinalIgnoreCase)
                ? "视频已生成，请点击“打开文件”播放。"
                : "生成后会在这里显示结果预览。";
        }

        private void UpdateStatus(string nodeType)
        {
            if (_busy)
            {
                _statusLabel.Text = nodeType switch
                {
                    WorkflowNodeCatalog.ImageToImage => "正在调用图生图模型，请稍候...",
                    WorkflowNodeCatalog.TextToVideo => "正在调用文生视频模型，请稍候...",
                    WorkflowNodeCatalog.TextImageToVideo => "正在调用文图生视频模型，请稍候...",
                    _ => "正在调用图片模型生成，请稍候...",
                };
                return;
            }

            if (!string.IsNullOrWhiteSpace(_node?.ArtifactPath) && File.Exists(_node.ArtifactPath))
            {
                _statusLabel.Text = $"已生成：{Path.GetFileName(_node.ArtifactPath)}";
                return;
            }

            _statusLabel.Text = nodeType switch
            {
                WorkflowNodeCatalog.ImageToImage => "请先选择参考图片，然后开始图生图。",
                WorkflowNodeCatalog.TextToVideo => "请输入文字描述，生成视频提示词并输出视频。",
                WorkflowNodeCatalog.TextImageToVideo => "选择参考图片并输入提示词，生成文图视频。",
                _ => "准备就绪。",
            };
        }

        private void PickReferenceImage()
        {
            if (_node?.Params == null)
            {
                return;
            }

            using var dialog = new OpenFileDialog
            {
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.webp;*.bmp",
                Title = "选择参考图",
            };
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            {
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            _node.Params.DirectReferenceImagePath = dialog.FileName;
            EntryChanged?.Invoke(this, EventArgs.Empty);
            SyncFromNode();
        }

        private void OpenReferenceImage()
        {
            var path = _node?.Params?.DirectReferenceImagePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch
            {
                // ignore
            }
        }

        private void SetImageMode(string imageMode)
        {
            if (_node?.Params == null)
            {
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            _node.Params.DirectImageMode = NormalizeImageMode(imageMode);
            if (string.Equals(_node.Params.DirectImageMode, "threeview", StringComparison.Ordinal))
            {
                _node.Params.DirectAspectRatio = "16:9";
            }

            ApplyCanvasDefaults(_node.Params);
            EntryChanged?.Invoke(this, EventArgs.Empty);
            SyncFromNode();
        }

        private void SetAspectRatio(string aspectRatio)
        {
            if (_node?.Params == null)
            {
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            _node.Params.DirectAspectRatio = aspectRatio;
            ApplyCanvasDefaults(_node.Params);
            EntryChanged?.Invoke(this, EventArgs.Empty);
            SyncFromNode();
        }

        private void SetResolution(string preset)
        {
            if (_node?.Params == null)
            {
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            _node.Params.DirectResolutionPreset = preset;
            ApplyCanvasDefaults(_node.Params);
            EntryChanged?.Invoke(this, EventArgs.Empty);
            SyncFromNode();
        }

        private void SetVideoAspectRatio(string aspectRatio)
        {
            if (_node?.Params == null)
            {
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            _node.Params.DirectAspectRatio = VideoAspectRatioOptions.Contains(aspectRatio, StringComparer.OrdinalIgnoreCase)
                ? aspectRatio
                : "16:9";
            ApplyCanvasDefaults(_node.Params);
            EntryChanged?.Invoke(this, EventArgs.Empty);
            SyncFromNode();
        }

        private void SetVideoDuration(int seconds)
        {
            if (_node?.Params == null)
            {
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            var allowedDurations = BuildAvailableCloudVideoDurations(_node.Params).ToList();
            _node.Params.DirectDurationSeconds = allowedDurations.Contains(seconds)
                ? seconds
                : allowedDurations.LastOrDefault(10);
            EntryChanged?.Invoke(this, EventArgs.Empty);
            SyncFromNode();
        }

        private void SetVideoQuality(string quality)
        {
            if (_node?.Params == null)
            {
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            _node.Params.DirectQuality = QualityOptions.FirstOrDefault(item => string.Equals(item, quality, StringComparison.OrdinalIgnoreCase)) ?? "高清";
            EntryChanged?.Invoke(this, EventArgs.Empty);
            SyncFromNode();
        }

        private void CommitCanvasSize()
        {
            if (_syncing || _node?.Params == null)
            {
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            _node.Params.DirectWidth = (int)_widthNumeric.Value;
            _node.Params.DirectHeight = (int)_heightNumeric.Value;
            if (IsVideoNode(WorkflowNodeCatalog.NormalizeNodeType(_node.Type)) && IsLocalVideoExecution())
            {
                ClampLocalVideoCanvas(_node.Params);
                SyncFromNode();
            }
            EntryChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OpenArtifact()
        {
            var path = _node?.ArtifactPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch
            {
                // ignore
            }
        }

        private void OpenArtifactFolder()
        {
            var path = _node?.ArtifactPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            catch
            {
                // ignore
            }
        }

        private void SaveArtifactAs()
        {
            var path = _node?.ArtifactPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            using var dialog = new SaveFileDialog
            {
                FileName = Path.GetFileName(path),
                Filter = "所有文件|*.*",
                Title = "保存生成结果",
            };

            if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            {
                return;
            }

            File.Copy(path, dialog.FileName, true);
        }

        private void ClearArtifact()
        {
            if (_node?.Params == null)
            {
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            _node.ArtifactPath = string.Empty;
            _node.ArtifactKind = string.Empty;
            _node.Output = string.Empty;
            _node.Params.DirectPositivePrompt = string.Empty;
            _node.Params.DirectNegativePrompt = string.Empty;
            _node.Params.DirectExecutionModelName = string.Empty;
            _node.Params.DirectPromptModelName = string.Empty;
            EntryChanged?.Invoke(this, EventArgs.Empty);
            SyncFromNode();
        }

        private string ResolvePromptModelNameLegacy()
        {
            if (_node?.Params == null)
            {
                return "未配置";
            }

            if (!string.IsNullOrWhiteSpace(_node.Params.DirectPromptModelName))
            {
                return _node.Params.DirectPromptModelName;
            }

            return string.IsNullOrWhiteSpace(_node.Params.PreferredTextModelId)
                ? "跟随文本默认模型"
                : _node.Params.PreferredTextModelId;
        }

        private string ResolveExecutionModelNameLegacy()
        {
            if (_node?.Params == null)
            {
                return "未配置";
            }

            if (!string.IsNullOrWhiteSpace(_node.Params.DirectExecutionModelName))
            {
                return _node.Params.DirectExecutionModelName;
            }

            var nodeType = WorkflowNodeCatalog.NormalizeNodeType(_node.Type);
            return nodeType switch
            {
                WorkflowNodeCatalog.TextToVideo or WorkflowNodeCatalog.TextImageToVideo => string.IsNullOrWhiteSpace(_node.Params.PreferredVideoModelId)
                    ? "跟随视频默认模型"
                    : _node.Params.PreferredVideoModelId,
                _ => string.IsNullOrWhiteSpace(_node.Params.PreferredImageModelId)
                    ? "跟随图片默认模型"
                    : _node.Params.PreferredImageModelId,
            };
        }

        private bool IsLocalVideoExecutionLegacy()
        {
            var model = ResolveExecutionModelInfo();
            return model != null && ModelConfig.IsLocalEndpointUrl(model.Url);
        }

        private ModelInfo? ResolveExecutionModelInfoLegacy()
        {
            if (_node?.Params == null)
            {
                return null;
            }

            var settings = ModelConfig.Load();
            var nodeType = WorkflowNodeCatalog.NormalizeNodeType(_node.Type);
            var category = IsVideoNode(nodeType) ? ModelCategory.Video : ModelCategory.Image;
            var preferredModelId = _node.Params.GetPreferredModelId(category);

            if (string.IsNullOrWhiteSpace(preferredModelId))
            {
                preferredModelId = ModelConfig.GetDefaultModelForNodeType(settings, _node.Type);
            }

            if (!string.IsNullOrWhiteSpace(preferredModelId))
            {
                var selected = settings.Models.FirstOrDefault(model => ModelConfig.MatchesModelSelector(model, preferredModelId));
                if (selected != null)
                {
                    return selected;
                }
            }

            return category == ModelCategory.Video
                ? ModelConfig.GetPreferredLocalVideoModel(settings) ?? ModelConfig.GetPreferredCloudVideoModel(settings)
                : ModelConfig.GetPreferredLocalImageModel(settings) ?? ModelConfig.GetPreferredCloudImageModel(settings);
        }

        private void PopulateCloudVideoEditors(WorkflowNodeParameters parameters)
        {
            PopulateConfigComboBox(
                _cloudVideoPlatformComboBox,
                GetCloudVideoPlatformOptions(),
                NormalizeCloudVideoPlatform(parameters.StoryboardVideoPlatform),
                allowTextEntry: false);

            PopulateConfigComboBox(
                _cloudVideoModelFamilyComboBox,
                BuildCloudVideoModelFamilyOptions(),
                NormalizeCloudVideoModelFamily(parameters.StoryboardVideoModelFamily),
                allowTextEntry: false);

            PopulateConfigComboBox(
                _cloudVideoSubModelComboBox,
                BuildCloudVideoSubModelOptions(parameters.StoryboardVideoModelFamily),
                NormalizeCloudVideoSubModel(parameters.StoryboardVideoSubModel),
                allowTextEntry: false);
        }

        private void CommitCloudVideoPlatform()
        {
            if (_syncing || _node?.Params == null)
            {
                return;
            }

            var selectedValue = (_cloudVideoPlatformComboBox.SelectedItem as ConfigOptionItem)?.Value;
            if (string.IsNullOrWhiteSpace(selectedValue))
            {
                selectedValue = _cloudVideoPlatformComboBox.Text;
            }

            _node.Params.StoryboardVideoPlatform = NormalizeCloudVideoPlatform(selectedValue);
            EnsureCompatibleCloudVideoSelections(_node.Params);
            EntryChanged?.Invoke(this, EventArgs.Empty);
            SyncFromNode();
        }

        private void CommitCloudVideoModelFamily()
        {
            if (_syncing || _node?.Params == null)
            {
                return;
            }

            var selectedValue = (_cloudVideoModelFamilyComboBox.SelectedItem as ConfigOptionItem)?.Value;
            if (string.IsNullOrWhiteSpace(selectedValue))
            {
                selectedValue = _cloudVideoModelFamilyComboBox.Text;
            }

            _node.Params.StoryboardVideoModelFamily = NormalizeCloudVideoModelFamily(selectedValue);
            EnsureCompatibleCloudVideoSelections(_node.Params);
            EntryChanged?.Invoke(this, EventArgs.Empty);
            SyncFromNode();
        }

        private void CommitCloudVideoSubModel()
        {
            if (_syncing || _node?.Params == null)
            {
                return;
            }

            var selectedValue = (_cloudVideoSubModelComboBox.SelectedItem as ConfigOptionItem)?.Value;
            if (string.IsNullOrWhiteSpace(selectedValue))
            {
                selectedValue = _cloudVideoSubModelComboBox.Text;
            }

            _node.Params.StoryboardVideoSubModel = NormalizeCloudVideoSubModel(selectedValue);
            EnsureCompatibleCloudVideoSelections(_node.Params);
            EntryChanged?.Invoke(this, EventArgs.Empty);
            SyncFromNode();
        }

        private string ResolvePromptModelName()
        {
            var model = ResolvePromptModelInfo();
            return model == null ? "未配置文本模型 / Prompt Model Not Configured" : FormatModelDisplayName(model);
        }

        private string ResolveExecutionModelName()
        {
            var explicitVideoModeDisplayName = ResolveExplicitVideoModeDisplayName();
            if (!string.IsNullOrWhiteSpace(explicitVideoModeDisplayName))
            {
                return explicitVideoModeDisplayName;
            }

            var model = ResolveExecutionModelInfo();
            if (model == null)
            {
                return IsVideoNode(WorkflowNodeCatalog.NormalizeNodeType(_node?.Type ?? string.Empty))
                    ? "未配置视频模型 / Video Model Not Configured"
                    : "未配置图片模型 / Image Model Not Configured";
            }

            return FormatModelDisplayName(model);
        }

        private bool ShouldUseCloudVideoEditor()
        {
            if (ModelConfig.LocalOnlyMode)
            {
                return false;
            }

            if (_node?.Params == null)
            {
                return false;
            }

            var nodeType = WorkflowNodeCatalog.NormalizeNodeType(_node.Type);
            if (!IsVideoNode(nodeType))
            {
                return false;
            }

            var settings = ModelConfig.Load();
            var executionModel = WorkflowModelResolver.ResolveDirectStudioVideoExecutionModel(settings, _node);
            return executionModel != null && !ModelConfig.IsLocalEndpointUrl(executionModel.Url);
        }

        private bool IsLocalVideoExecution()
        {
            return !ShouldUseCloudVideoEditor();
        }

        private ModelInfo? ResolvePromptModelInfo()
        {
            if (_node == null)
            {
                return null;
            }

            var settings = ModelConfig.Load();
            return WorkflowModelResolver.ResolveDirectStudioPromptTextModel(settings, _node);
        }

        private ModelInfo? ResolveExecutionModelInfo()
        {
            if (_node == null)
            {
                return null;
            }

            var settings = ModelConfig.Load();
            var nodeType = WorkflowNodeCatalog.NormalizeNodeType(_node.Type);
            if (IsVideoNode(nodeType))
            {
                return WorkflowModelResolver.ResolveDirectStudioVideoExecutionModel(settings, _node);
            }

            return WorkflowModelResolver.ResolveDirectStudioImageExecutionModel(settings, _node);
        }

        private ModelInfo? ResolveExplicitNodePreferredModel(ModelSettings settings, ModelCategory category)
        {
            return _node == null
                ? null
                : WorkflowModelResolver.ResolveExplicitPreferredModel(settings, _node, category);
        }

        private ModelInfo? ResolveGlobalSelectedModel(ModelSettings settings, ModelCategory category)
        {
            return _node == null
                ? null
                : WorkflowModelResolver.ResolveGlobalSelectedModel(settings, _node, category);
        }

        private ModelInfo? BuildRelayVideoExecutionModel(ModelSettings settings)
        {
            return _node == null
                ? null
                : WorkflowModelResolver.BuildRelayVideoExecutionModel(settings, _node);
        }

        private string ResolveExplicitVideoModeDisplayName()
        {
            if (ModelConfig.LocalOnlyMode)
            {
                return string.Empty;
            }

            if (_node?.Params == null || !IsVideoNode(WorkflowNodeCatalog.NormalizeNodeType(_node.Type)))
            {
                return string.Empty;
            }

            var preferredVideoId = _node.Params.GetPreferredModelId(ModelCategory.Video);
            if (string.IsNullOrWhiteSpace(preferredVideoId))
            {
                preferredVideoId = _node.Params.PreferredModelId;
            }

            if (string.IsNullOrWhiteSpace(preferredVideoId))
            {
                return string.Empty;
            }

            var settings = ModelConfig.Load();
            if (string.Equals(preferredVideoId, ModelConfig.RelayVideoModeModelId, StringComparison.OrdinalIgnoreCase))
            {
                var relayExecutionModel = BuildRelayVideoExecutionModel(settings);
                return relayExecutionModel == null
                    ? "[云端 / Cloud] 云端视频模式 / Cloud Relay Video"
                    : FormatModelDisplayName(relayExecutionModel);
            }

            var model = settings.Models.FirstOrDefault(candidate =>
                candidate.Category == ModelCategory.Video &&
                ModelConfig.MatchesModelSelector(candidate, preferredVideoId));
            return model == null ? string.Empty : FormatModelDisplayName(ModelConfig.ApplyRelayOverrides(settings, model));
        }

        private static string FormatModelDisplayName(ModelInfo model)
        {
            return WorkflowModelResolver.FormatModelDisplayName(model);
        }

        private IEnumerable<ConfigOptionItem> GetCloudVideoPlatformOptions()
        {
            var settings = ModelConfig.Load();
            var configured = ModelConfig.GetRelayApis(settings)
                .Select(relay => new ConfigOptionItem(
                    relay.ProviderCode,
                    string.IsNullOrWhiteSpace(relay.Name)
                        ? WorkflowNodeParameters.GetStoryboardVideoPlatformDisplayName(relay.ProviderCode)
                        : relay.Name))
                .ToList();

            if (configured.Count == 0)
            {
                configured.Add(new ConfigOptionItem("yunwuapi", "云雾API"));
            }

            var current = NormalizeCloudVideoPlatform(_node?.Params?.StoryboardVideoPlatform);
            if (!configured.Any(option => string.Equals(option.Value, current, StringComparison.OrdinalIgnoreCase)))
            {
                configured.Add(new ConfigOptionItem(current, WorkflowNodeParameters.GetStoryboardVideoPlatformDisplayName(current)));
            }

            return configured
                .GroupBy(option => option.Value, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First());
        }

        private static IEnumerable<ConfigOptionItem> BuildCloudVideoModelFamilyOptions()
        {
            return CloudVideoModelFamilyOptions.Select(option => new ConfigOptionItem(option.Code, option.Name));
        }

        private static IEnumerable<ConfigOptionItem> BuildCloudVideoSubModelOptions(string? family)
        {
            var normalizedFamily = NormalizeCloudVideoModelFamily(family);
            if (!CloudVideoSubModelOptions.TryGetValue(normalizedFamily, out var values) || values.Length == 0)
            {
                values = new[] { WorkflowNodeParameters.GetDefaultStoryboardVideoSubModel(normalizedFamily) };
            }

            return values.Select(value => new ConfigOptionItem(value, WorkflowNodeParameters.GetStoryboardVideoSubModelDisplayName(value)));
        }

        private static IEnumerable<int> BuildAvailableCloudVideoDurations(WorkflowNodeParameters? parameters)
        {
            var platform = NormalizeCloudVideoPlatform(parameters?.StoryboardVideoPlatform);
            var family = NormalizeCloudVideoModelFamily(parameters?.StoryboardVideoModelFamily);
            if (string.Equals(platform, "yunwuapi", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(family, "grok", StringComparison.OrdinalIgnoreCase))
                {
                    return new[] { 10 };
                }

                return new[] { 5, 10 };
            }

            return new[] { 5, 10, 15 };
        }

        private static void EnsureCompatibleCloudVideoSelections(WorkflowNodeParameters parameters)
        {
            parameters.StoryboardVideoPlatform = NormalizeCloudVideoPlatform(parameters.StoryboardVideoPlatform);
            parameters.StoryboardVideoModelFamily = NormalizeCloudVideoModelFamily(parameters.StoryboardVideoModelFamily);

            var allowedSubModels = BuildCloudVideoSubModelOptions(parameters.StoryboardVideoModelFamily)
                .Select(option => option.Value)
                .ToList();
            var normalizedSubModel = NormalizeCloudVideoSubModel(parameters.StoryboardVideoSubModel);
            if (!allowedSubModels.Contains(normalizedSubModel, StringComparer.OrdinalIgnoreCase))
            {
                parameters.StoryboardVideoSubModel = allowedSubModels.FirstOrDefault()
                    ?? WorkflowNodeParameters.GetDefaultStoryboardVideoSubModel(parameters.StoryboardVideoModelFamily);
            }

            var allowedDurations = BuildAvailableCloudVideoDurations(parameters).ToList();
            if (!allowedDurations.Contains(parameters.DirectDurationSeconds))
            {
                parameters.DirectDurationSeconds = allowedDurations.LastOrDefault(10);
            }
        }

        private void PopulateConfigComboBox(ComboBox combo, IEnumerable<ConfigOptionItem> options, string selectedValue, bool allowTextEntry)
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

            var selectedOption = optionList.FirstOrDefault(option =>
                string.Equals(option.Value, normalizedSelectedValue, StringComparison.OrdinalIgnoreCase));
            combo.SelectedItem = selectedOption;
            combo.Text = selectedOption?.DisplayName ?? normalizedSelectedValue;
        }

        private static string NormalizeCloudVideoPlatform(string? platform)
        {
            return string.IsNullOrWhiteSpace(platform) ? "yunwuapi" : platform.Trim().ToLowerInvariant();
        }

        private static string NormalizeCloudVideoModelFamily(string? family)
        {
            return string.IsNullOrWhiteSpace(family) ? "luma" : family.Trim().ToLowerInvariant();
        }

        private static string NormalizeCloudVideoSubModel(string? subModel)
        {
            return string.IsNullOrWhiteSpace(subModel)
                ? WorkflowNodeParameters.GetDefaultStoryboardVideoSubModel("luma")
                : subModel.Trim().ToLowerInvariant();
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

        private static bool IsImageNode(string nodeType)
        {
            return string.Equals(nodeType, WorkflowNodeCatalog.TextToImage, StringComparison.Ordinal) ||
                   string.Equals(nodeType, WorkflowNodeCatalog.ImageToImage, StringComparison.Ordinal);
        }

        private static bool IsVideoNode(string nodeType)
        {
            return string.Equals(nodeType, WorkflowNodeCatalog.TextToVideo, StringComparison.Ordinal) ||
                   string.Equals(nodeType, WorkflowNodeCatalog.TextImageToVideo, StringComparison.Ordinal);
        }

        private static bool RequiresReference(string nodeType)
        {
            return string.Equals(nodeType, WorkflowNodeCatalog.ImageToImage, StringComparison.Ordinal) ||
                   string.Equals(nodeType, WorkflowNodeCatalog.TextImageToVideo, StringComparison.Ordinal);
        }

        private static void UpdateSelectionButtons(IDictionary<string, Button> buttons, string? selectedValue)
        {
            var selected = (selectedValue ?? string.Empty).Trim();
            foreach (var pair in buttons)
            {
                var active = string.Equals(pair.Key, selected, StringComparison.OrdinalIgnoreCase);
                pair.Value.BackColor = active ? Color.FromArgb(255, 122, 0) : Color.FromArgb(239, 236, 230);
                pair.Value.ForeColor = active ? Color.White : Color.FromArgb(23, 28, 40);
            }
        }

        private static void UpdateSelectionButtons(IDictionary<int, Button> buttons, int selectedValue)
        {
            foreach (var pair in buttons)
            {
                var active = pair.Key == selectedValue;
                pair.Value.BackColor = active ? Color.FromArgb(255, 122, 0) : Color.FromArgb(239, 236, 230);
                pair.Value.ForeColor = active ? Color.White : Color.FromArgb(23, 28, 40);
            }
        }

        private static decimal ClampNumericValue(NumericUpDown numeric, int value)
        {
            if (value < numeric.Minimum)
            {
                return numeric.Minimum;
            }

            if (value > numeric.Maximum)
            {
                return numeric.Maximum;
            }

            return value;
        }

        private static string NormalizeImageMode(string? imageMode)
        {
            return (imageMode ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "expression" => "expression",
                "threeview" => "threeview",
                _ => "single",
            };
        }

        private static void LoadImagePreview(PictureBox pictureBox, string? imagePath)
        {
            pictureBox.Image?.Dispose();
            pictureBox.Image = null;

            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath) || !IsImageFile(imagePath))
            {
                return;
            }

            using var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var image = Image.FromStream(fs);
            pictureBox.Image = new Bitmap(image);
        }

        private static bool IsImageFile(string path)
        {
            var extension = Path.GetExtension(path)?.ToLowerInvariant();
            return extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp";
        }

        private static void ApplyCanvasDefaults(WorkflowNodeParameters parameters)
        {
            var preset = (parameters.DirectResolutionPreset ?? "2K").Trim();
            var aspectRatio = (parameters.DirectAspectRatio ?? "1:1").Trim();

            var longSide = preset switch
            {
                "768" => 768,
                "1024" => 1024,
                "4K" => 3840,
                _ => 2048,
            };

            var shortSide = preset switch
            {
                "768" => 432,
                "1024" => 576,
                "4K" => 2160,
                _ => 1152,
            };

            (parameters.DirectWidth, parameters.DirectHeight) = aspectRatio switch
            {
                "21:9" => (longSide, (int)Math.Round(longSide / 2.3333)),
                "16:9" => (longSide, shortSide),
                "3:2" => (longSide, (int)Math.Round(longSide / 1.5)),
                "4:3" => (longSide, (int)Math.Round(longSide / 1.3333)),
                "3:4" => ((int)Math.Round(longSide / 1.3333), longSide),
                "2:3" => ((int)Math.Round(longSide / 1.5), longSide),
                "9:16" => (shortSide, longSide),
                _ => (longSide, longSide),
            };
        }

        private void ConfigureLocalVideoDimensionBounds(WorkflowNodeParameters parameters)
        {
            var portrait = string.Equals(parameters.DirectAspectRatio, "9:16", StringComparison.OrdinalIgnoreCase);
            _widthNumeric.Minimum = 256;
            _heightNumeric.Minimum = 256;
            _widthNumeric.Maximum = portrait ? 720 : 1280;
            _heightNumeric.Maximum = portrait ? 1280 : 720;
        }

        private void ResetVideoDimensionBounds()
        {
            _widthNumeric.Minimum = 256;
            _heightNumeric.Minimum = 256;
            _widthNumeric.Maximum = 8192;
            _heightNumeric.Maximum = 8192;
        }

        private static void ClampLocalVideoCanvas(WorkflowNodeParameters parameters)
        {
            var portrait = string.Equals(parameters.DirectAspectRatio, "9:16", StringComparison.OrdinalIgnoreCase);
            var maxWidth = portrait ? 720 : 1280;
            var maxHeight = portrait ? 1280 : 720;

            parameters.DirectWidth = Math.Clamp(parameters.DirectWidth <= 0 ? maxWidth : parameters.DirectWidth, 256, maxWidth);
            parameters.DirectHeight = Math.Clamp(parameters.DirectHeight <= 0 ? maxHeight : parameters.DirectHeight, 256, maxHeight);
        }
    }
}
