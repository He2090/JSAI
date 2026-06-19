using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public partial class MainForm
    {
        private readonly WorkflowCanvasControl _canvas = new();
        private readonly SplitContainer _workspaceSplit = new();
        private readonly SplitContainer _canvasSplit = new();
        private readonly TextBox _projectNameTextBox = new();
        private readonly Label _statsLabel = new();
        private readonly Label _statusLabel = new();
        private readonly ListView _assetListView = new();
        private readonly ListView _connectionListView = new();
        private readonly TextBox _latestPromptTextBox = new();
        private readonly Label _selectedNodeTitleLabel = new();
        private readonly Label _selectedNodeHintLabel = new();
        private readonly Label _modelHintLabel = new();
        private readonly FlowLayoutPanel _modelSelectorPanel = new();
        private readonly TextBox _outputTextBox = new();
        private readonly Button _runNodeButton = new();
        private readonly Button _deleteNodeButton = new();
        private readonly Button _removeConnectionButton = new();
        private readonly Button _importAssetButton = new();
        private readonly Button _newProjectButton = new();
        private readonly Button _loadProjectButton = new();
        private readonly Button _saveProjectButton = new();
        private readonly Button _saveProjectAsButton = new();
        private readonly Button _importWorkflowButton = new();
        private readonly Button _exportWorkflowButton = new();
        private readonly Button _modelCallLogButton = new();
        private readonly Button _runWorkflowButton = new();
        private readonly Button _modelSettingsButton = new();
        private readonly Panel _workspaceHostPanel = new();
        private readonly Panel _homePanel = new();
        private readonly FlowLayoutPanel _toolbarActionsPanel = new();
        private readonly Panel _inspectorActionCard = new();
        private readonly Panel _inspectorOutputCard = new();
        private readonly Button _toggleLeftSidebarButton = new();
        private readonly Button _toggleRightInspectorButton = new();
        private Label? _nodeLibraryTitleLabel;
        private readonly TableLayoutPanel _nodeLibraryButtonGrid = new();
        private readonly Label _statusRightLabel = new();
        private Label? _assetCardTitleLabel;
        private Label? _assetCardHintLabel;
        private Control? _assetCard;
        private Control? _connectionCard;
        private Control? _hintCard;
        private int _leftSidebarExpandedWidth = 310;
        private int _rightInspectorExpandedWidth = 360;
        private bool _leftSidebarCollapsed;
        private bool _rightInspectorCollapsed;

        private void BuildLayout()
        {
            SuspendLayout();

            BackColor = Color.FromArgb(13, 16, 24);
            ForeColor = Color.White;
            Text = "JSAI 工作助手";
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Normal;
            Size = new Size(1440, 900);
            MinimumSize = new Size(1360, 860);
            Text = "JSAI 工作助手";

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = BackColor,
                ColumnCount = 1,
                RowCount = 4,
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));

            root.Controls.Add(BuildTopBar(), 0, 0);
            root.Controls.Add(BuildProjectTabsBar(), 0, 1);
            root.Controls.Add(BuildWorkspaceHost(), 0, 2);
            root.Controls.Add(BuildStatusBar(), 0, 3);

            Controls.Add(root);
            ResumeLayout();
        }

        private Control BuildTopBar()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(17, 21, 31),
                Padding = new Padding(18, 12, 18, 12),
                ColumnCount = 2,
                RowCount = 1,
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var titlePanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
            };
            titlePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 58F));
            titlePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 42F));

            var titleLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei", 15F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.White,
                Text = "JSAI 工作助手",
                TextAlign = ContentAlignment.MiddleLeft,
            };

            _statsLabel.Dock = DockStyle.Fill;
            _statsLabel.ForeColor = Color.FromArgb(166, 178, 200);
            _statsLabel.TextAlign = ContentAlignment.MiddleLeft;

            titlePanel.Controls.Add(titleLabel, 0, 0);
            titlePanel.Controls.Add(_statsLabel, 0, 1);
            titleLabel.Text = "JSAI 工作助手";

            _toolbarActionsPanel.BackColor = Color.FromArgb(22, 28, 40);
            _toolbarActionsPanel.Padding = new Padding(12, 5, 12, 5);
            _toolbarActionsPanel.Margin = new Padding(24, 0, 0, 0);
            _toolbarActionsPanel.AutoSize = true;
            _toolbarActionsPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _toolbarActionsPanel.WrapContents = false;
            _toolbarActionsPanel.FlowDirection = FlowDirection.LeftToRight;
            _toolbarActionsPanel.Anchor = AnchorStyles.Right;
            _toolbarActionsPanel.BackColor = Color.Transparent;
            _toolbarActionsPanel.Controls.Clear();

            ConfigureToolbarButton(_newProjectButton, "新建项目", (_, _) => ShowHomeScreen(force: true), Color.FromArgb(40, 50, 72), Color.White);
            ConfigureToolbarButton(_loadProjectButton, "载入项目", (_, _) => LoadProjectFile(), Color.FromArgb(40, 50, 72), Color.White);
            ConfigureToolbarButton(_saveProjectButton, "保存项目", (_, _) => SaveProjectFile(), Color.FromArgb(40, 50, 72), Color.White);
            ConfigureToolbarButton(_saveProjectAsButton, "另存项目", (_, _) => SaveProjectFileAs(), Color.FromArgb(40, 50, 72), Color.White);
            ConfigureToolbarButton(_importWorkflowButton, "导入 JSON", (_, _) => ImportWorkflow(), Color.FromArgb(40, 50, 72), Color.White);
            ConfigureToolbarButton(_exportWorkflowButton, "导出 JSON", (_, _) => ExportWorkflow(), Color.FromArgb(40, 50, 72), Color.White);
            ConfigureToolbarButton(_modelCallLogButton, "模型调用日志", (_, _) => OpenModelCallLog(), Color.FromArgb(40, 50, 72), Color.White);
            ConfigureToolbarButton(_runWorkflowButton, "执行工作流", async (_, _) => await RunWorkflowAsync(), Color.FromArgb(255, 122, 0), Color.Black);
            ConfigureToolbarButton(_modelSettingsButton, "模型设置", (_, _) => OpenModelSettings(), Color.FromArgb(255, 214, 102), Color.Black);

            _toolbarActionsPanel.Controls.Add(_newProjectButton);
            _toolbarActionsPanel.Controls.Add(_loadProjectButton);
            _toolbarActionsPanel.Controls.Add(_saveProjectButton);
            _toolbarActionsPanel.Controls.Add(_saveProjectAsButton);
            _toolbarActionsPanel.Controls.Add(_importWorkflowButton);
            _toolbarActionsPanel.Controls.Add(_exportWorkflowButton);
            _toolbarActionsPanel.Controls.Add(_modelCallLogButton);
            _toolbarActionsPanel.Controls.Add(_runWorkflowButton);
            _toolbarActionsPanel.Controls.Add(_modelSettingsButton);

            _newProjectButton.Text = "新建项目";
            _loadProjectButton.Text = "载入项目";
            _saveProjectButton.Text = "保存项目";
            _saveProjectAsButton.Text = "另存项目";
            _importWorkflowButton.Text = "导入 JSON";
            _exportWorkflowButton.Text = "导出 JSON";
            _modelCallLogButton.Text = "模型调用日志";
            _runWorkflowButton.Text = "执行工作流";
            _modelSettingsButton.Text = "模型设置";

            panel.Controls.Add(titlePanel, 0, 0);
            panel.Controls.Add(_toolbarActionsPanel, 1, 0);
            return panel;
        }

        private void HookStartupVisibility()
        {
            Shown -= MainForm_ShownEnsureVisible;
            Shown += MainForm_ShownEnsureVisible;
        }

        private void MainForm_ShownEnsureVisible(object? sender, EventArgs e)
        {
            var workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(80, 80, 1440, 900);
            var targetWidth = Math.Max(MinimumSize.Width, Math.Min(workingArea.Width - 80, 1600));
            var targetHeight = Math.Max(MinimumSize.Height, Math.Min(workingArea.Height - 80, 980));
            var targetLeft = workingArea.Left + Math.Max(0, (workingArea.Width - targetWidth) / 2);
            var targetTop = workingArea.Top + Math.Max(0, (workingArea.Height - targetHeight) / 2);

            StartPosition = FormStartPosition.Manual;
            WindowState = FormWindowState.Normal;
            Bounds = new Rectangle(targetLeft, targetTop, targetWidth, targetHeight);

            if (!Visible)
            {
                Show();
            }

            Activate();
            BringToFront();
            TopMost = true;
            TopMost = false;
            Focus();
        }

        private Control BuildWorkspaceHost()
        {
            _workspaceHostPanel.Dock = DockStyle.Fill;
            _workspaceHostPanel.BackColor = BackColor;

            var workspace = BuildWorkspace();
            workspace.Dock = DockStyle.Fill;
            _homePanel.Dock = DockStyle.Fill;
            _homePanel.Visible = false;

            _workspaceHostPanel.Controls.Add(workspace);
            _workspaceHostPanel.Controls.Add(BuildHomePanel());
            return _workspaceHostPanel;
        }

        private Control BuildWorkspace()
        {
            _workspaceSplit.Dock = DockStyle.Fill;
            _workspaceSplit.BackColor = BackColor;
            _workspaceSplit.FixedPanel = FixedPanel.Panel1;
            _workspaceSplit.SplitterWidth = 8;
            _workspaceSplit.Panel1.Padding = new Padding(10, 0, 4, 10);
            _workspaceSplit.Panel2.Padding = new Padding(4, 0, 10, 10);

            _workspaceSplit.Panel1.Controls.Add(BuildSidebar());
            _workspaceSplit.Panel2.Controls.Add(BuildCanvasAndInspector());
            ConfigureSidebarToggleButton(_toggleLeftSidebarButton, (_, _) => ToggleLeftSidebar());
            _workspaceSplit.Panel2.Controls.Add(_toggleLeftSidebarButton);
            _workspaceSplit.Panel2.Resize += (_, _) => PositionLeftSidebarToggleButton();
            PositionLeftSidebarToggleButton();
            return _workspaceSplit;
        }

        private Control BuildHomePanel()
        {
            _homePanel.BackColor = Color.Black;
            _homePanel.Padding = new Padding(48);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                ColumnCount = 1,
                RowCount = 3,
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var title = new Label
            {
                Dock = DockStyle.Fill,
                Text = "选择创作类型",
                Font = new Font("Microsoft YaHei", 28F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
            };

            var subtitle = new Label
            {
                Dock = DockStyle.Fill,
                Text = "从下方四种模式中进入对应操作界面",
                Font = new Font("Microsoft YaHei", 11F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(155, 165, 182),
                TextAlign = ContentAlignment.TopCenter,
            };

            var cardsHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
            };

            var cards = new TableLayoutPanel
            {
                BackColor = Color.Transparent,
                ColumnCount = 2,
                RowCount = 2,
                Width = 880,
                Height = 400,
            };
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            cards.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            cards.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            cards.Controls.Add(CreateHomeModeCard("AI 动漫项目", "进入漫剧节点式创作工作台", ProjectLaunchMode.AiAnimeProject), 0, 0);
            cards.Controls.Add(CreateHomeModeCard("文生图", "输入文字提示词，优先调用本地图片模型", ProjectLaunchMode.TextToImage), 1, 0);
            cards.Controls.Add(CreateHomeModeCard("文生视频", "输入文字提示词，优先调用本地视频模型", ProjectLaunchMode.TextToVideo), 0, 1);
            cards.Controls.Add(CreateHomeModeCard("文图生视频", "上传参考图并输入提示词生成视频", ProjectLaunchMode.TextImageToVideo), 1, 1);

            void CenterCards()
            {
                cards.Left = Math.Max(0, (cardsHost.ClientSize.Width - cards.Width) / 2);
                cards.Top = Math.Max(0, (cardsHost.ClientSize.Height - cards.Height) / 2) - 24;
            }

            cardsHost.Resize += (_, _) => CenterCards();
            cardsHost.HandleCreated += (_, _) => CenterCards();
            cardsHost.Controls.Add(cards);

            root.Controls.Add(title, 0, 0);
            root.Controls.Add(subtitle, 0, 1);
            root.Controls.Add(cardsHost, 0, 2);

            _homePanel.Controls.Add(root);
            return _homePanel;
        }

        private Control CreateHomeModeCard(string title, string description, ProjectLaunchMode mode)
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(18),
                BackColor = Color.FromArgb(16, 16, 16),
                Cursor = Cursors.Hand,
                Padding = new Padding(24),
            };
            card.Paint += (_, e) =>
            {
                using var pen = new Pen(Color.FromArgb(46, 46, 46), 1F);
                e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
            };

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
            };
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 58F));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 42F));

            var titleLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = title,
                Font = new Font("Microsoft YaHei", 19F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = Padding.Empty,
            };

            var descriptionLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = description,
                Font = new Font("Microsoft YaHei", 10.5F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(176, 186, 206),
                TextAlign = ContentAlignment.TopCenter,
                Margin = Padding.Empty,
            };

            void OpenMode(object? _, EventArgs __) => HandleProjectLaunchMode(mode);

            card.Click += OpenMode;
            content.Click += OpenMode;
            titleLabel.Click += OpenMode;
            descriptionLabel.Click += OpenMode;

            content.Controls.Add(titleLabel, 0, 0);
            content.Controls.Add(descriptionLabel, 0, 1);
            card.Controls.Add(content);
            return card;
        }

        private Control BuildSidebar()
        {
            var panel = CreatePanel(new Padding(14));
            panel.Dock = DockStyle.Fill;
            panel.AutoScroll = true;

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 6,
                BackColor = Color.Transparent,
            };

            content.Controls.Add(BuildProjectCard(), 0, 0);
            content.Controls.Add(BuildNodeLibraryCard(), 0, 1);
            _directHistoryCard = BuildDirectImageHistoryCard();
            _assetCard = BuildAssetCard();
            _connectionCard = BuildConnectionCard();
            _hintCard = BuildHintCard();
            content.Controls.Add(_directHistoryCard, 0, 2);
            content.Controls.Add(_assetCard, 0, 3);
            content.Controls.Add(_connectionCard, 0, 4);
            content.Controls.Add(_hintCard, 0, 5);

            panel.Controls.Add(content);
            return panel;
        }

        private Control BuildProjectCard()
        {
            var panel = CreateCard("项目");
            var body = CreateCardBody();

            _projectNameTextBox.Dock = DockStyle.Top;
            _projectNameTextBox.BorderStyle = BorderStyle.FixedSingle;
            _projectNameTextBox.BackColor = Color.FromArgb(23, 28, 40);
            _projectNameTextBox.ForeColor = Color.White;

            body.Controls.Add(_projectNameTextBox);
            body.Controls.Add(CreateSectionLabel("项目名称"));
            panel.Controls.Add(body);
            return panel;
        }

        private Control BuildNodeLibraryCard()
        {
            var panel = CreateCard("节点库");
            _nodeLibraryTitleLabel = panel.Controls.OfType<Label>().FirstOrDefault();
            var body = CreateCardBody();

            _nodeLibraryButtonGrid.Dock = DockStyle.Top;
            _nodeLibraryButtonGrid.AutoSize = true;
            _nodeLibraryButtonGrid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _nodeLibraryButtonGrid.ColumnCount = 2;
            _nodeLibraryButtonGrid.BackColor = Color.Transparent;
            _nodeLibraryButtonGrid.ColumnStyles.Clear();
            _nodeLibraryButtonGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            _nodeLibraryButtonGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            body.Controls.Add(_nodeLibraryButtonGrid);
            panel.Controls.Add(body);
            return panel;
        }

        private void RebuildNodeLibraryButtons()
        {
            _nodeLibraryButtonGrid.SuspendLayout();
            _nodeLibraryButtonGrid.Controls.Clear();

            var nodeTypes = WorkflowNodeCatalog.GetNodeTypesForMode(_document.ProjectMode);
            var index = 0;
            foreach (var nodeType in nodeTypes)
            {
                var button = new Button
                {
                    Width = _document.ProjectMode == ProjectWorkspaceMode.DirectStudio ? 118 : 122,
                    Height = _document.ProjectMode == ProjectWorkspaceMode.DirectStudio ? 48 : 42,
                    Text = nodeType,
                    BackColor = Color.FromArgb(24, 31, 44),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Margin = new Padding(0, 0, 10, 10),
                    Cursor = Cursors.Hand,
                };
                button.FlatAppearance.BorderColor = Color.FromArgb(53, 69, 99);
                button.Click += (_, _) => AddNode(nodeType);
                _nodeLibraryButtonGrid.Controls.Add(button, index % 2, index / 2);
                index++;
            }

            _nodeLibraryButtonGrid.ResumeLayout();
        }

        private Control BuildAssetCard()
        {
            var panel = CreateCard("本地资产");
            _assetCardTitleLabel = panel.Controls.OfType<Label>().FirstOrDefault();
            var body = CreateCardBody();

            ConfigureActionButton(_importAssetButton, "导入素材", (_, _) => ImportAssets(), Color.FromArgb(36, 56, 94), Color.White);
            _importAssetButton.Dock = DockStyle.Top;

            _assetListView.Dock = DockStyle.Top;
            _assetListView.Height = 170;
            _assetListView.View = View.Details;
            _assetListView.FullRowSelect = true;
            _assetListView.HideSelection = false;
            _assetListView.MultiSelect = false;
            _assetListView.BorderStyle = BorderStyle.FixedSingle;
            _assetListView.BackColor = Color.FromArgb(20, 24, 34);
            _assetListView.ForeColor = Color.White;
            _assetListView.Columns.Add("名称", 150);
            _assetListView.Columns.Add("类型", 70);
            _assetListView.Columns.Add("大小", 70);

            _assetCardHintLabel = CreateHintLabel("双击素材会创建一个“本地资产”节点，并自动绑定文件路径。");
            body.Controls.Add(_assetCardHintLabel);
            body.Controls.Add(_assetListView);
            body.Controls.Add(_importAssetButton);
            panel.Controls.Add(body);
            return panel;
        }

        private Control BuildConnectionCard()
        {
            var panel = CreateCard("生成提示词");
            var body = CreateCardBody();

            ConfigureActionButton(_removeConnectionButton, "删除所选连线", (_, _) => RemoveSelectedConnection(), Color.FromArgb(91, 46, 46), Color.White);
            _removeConnectionButton.Dock = DockStyle.Top;

            _connectionListView.Dock = DockStyle.Top;
            _connectionListView.Height = 170;
            _connectionListView.View = View.Details;
            _connectionListView.FullRowSelect = true;
            _connectionListView.HideSelection = false;
            _connectionListView.MultiSelect = false;
            _connectionListView.BorderStyle = BorderStyle.FixedSingle;
            _connectionListView.BackColor = Color.FromArgb(20, 24, 34);
            _connectionListView.ForeColor = Color.White;
            _connectionListView.Columns.Add("起点", 110);
            _connectionListView.Columns.Add("终点", 110);

            _latestPromptTextBox.Dock = DockStyle.Top;
            _latestPromptTextBox.Height = 220;
            _latestPromptTextBox.Multiline = true;
            _latestPromptTextBox.ScrollBars = ScrollBars.Vertical;
            _latestPromptTextBox.ReadOnly = true;
            _latestPromptTextBox.BorderStyle = BorderStyle.FixedSingle;
            _latestPromptTextBox.BackColor = Color.FromArgb(20, 24, 34);
            _latestPromptTextBox.ForeColor = Color.WhiteSmoke;
            _latestPromptTextBox.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            _latestPromptTextBox.Text = "等待生成。这里会显示最近一次发送给模型的完整提示词。";

            body.Controls.Add(CreateHintLabel("每次生成内容时，这里会自动切换为最新发送给模型的提示词。"));
            body.Controls.Add(_latestPromptTextBox);
            panel.Controls.Add(body);
            return panel;
        }

        private Control BuildHintCard()
        {
            var panel = CreateCard("使用说明");
            var body = CreateCardBody();
            body.Controls.Add(CreateHintLabel("节点拖动：按住节点标题栏左键拖动。"));
            body.Controls.Add(CreateHintLabel("节点模型：右侧可为当前节点单独指定模型。"));
            body.Controls.Add(CreateHintLabel("执行顺序：按连线拓扑顺序执行。"));
            panel.Controls.Add(body);
            return panel;
        }

        private Control BuildCanvasAndInspector()
        {
            _canvasSplit.Dock = DockStyle.Fill;
            _canvasSplit.BackColor = BackColor;
            _canvasSplit.FixedPanel = FixedPanel.Panel2;
            _canvasSplit.SplitterWidth = 8;
            _canvasSplit.Panel2.Padding = new Padding(6, 0, 0, 0);

            var canvasHost = CreatePanel(new Padding(8));
            canvasHost.Dock = DockStyle.Fill;
            _canvas.Dock = DockStyle.Fill;
            canvasHost.Controls.Add(_canvas);

            _canvasSplit.Panel1.Controls.Add(canvasHost);
            _canvasSplit.Panel2.Controls.Add(BuildInspector());
            ConfigureSidebarToggleButton(_toggleRightInspectorButton, (_, _) => ToggleRightInspector());
            _canvasSplit.Panel1.Controls.Add(_toggleRightInspectorButton);
            _canvasSplit.Panel1.Resize += (_, _) => PositionRightInspectorToggleButton();
            PositionRightInspectorToggleButton();
            return _canvasSplit;
        }

        private Control BuildInspector()
        {
            var panel = CreatePanel(new Padding(14));
            panel.Dock = DockStyle.Fill;

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.Transparent,
            };
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var infoCard = CreateCard("节点详情");
            var infoBody = CreateCardBody();
            _selectedNodeTitleLabel.Dock = DockStyle.Top;
            _selectedNodeTitleLabel.Font = new Font("Microsoft YaHei", 11F, FontStyle.Bold, GraphicsUnit.Point);
            _selectedNodeTitleLabel.ForeColor = Color.White;
            _selectedNodeTitleLabel.Height = 28;
            _selectedNodeHintLabel.Dock = DockStyle.Top;
            _selectedNodeHintLabel.ForeColor = Color.FromArgb(171, 183, 205);
            _selectedNodeHintLabel.Height = 40;
            infoBody.Controls.Add(_selectedNodeHintLabel);
            infoBody.Controls.Add(_selectedNodeTitleLabel);
            infoCard.Controls.Add(infoBody);
            SetInspectorCardTitle(infoCard, "节点详情");

            var modelCard = CreateCard("节点模型");
            var modelBody = CreateCardBody();
            _modelHintLabel.Dock = DockStyle.Top;
            _modelHintLabel.Height = 40;
            _modelHintLabel.ForeColor = Color.FromArgb(171, 183, 205);
            _modelHintLabel.TextAlign = ContentAlignment.MiddleLeft;
            _modelHintLabel.Text = "当前节点优先使用这里选择的模型，留空则使用节点或类别默认模型。";

            _modelSelectorPanel.Dock = DockStyle.Top;
            _modelSelectorPanel.AutoSize = true;
            _modelSelectorPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _modelSelectorPanel.WrapContents = false;
            _modelSelectorPanel.FlowDirection = FlowDirection.TopDown;
            _modelSelectorPanel.BackColor = Color.Transparent;
            _modelSelectorPanel.Margin = Padding.Empty;
            _modelSelectorPanel.Padding = Padding.Empty;

            modelBody.Controls.Add(_modelSelectorPanel);
            modelBody.Controls.Add(_modelHintLabel);
            modelCard.Controls.Add(modelBody);
            SetInspectorCardTitle(modelCard, "节点模型");
            _modelHintLabel.Text = "当前节点优先使用这里选择的模型，留空则使用节点或类别默认模型。";

            var actionCard = _inspectorActionCard;
            InitializeInspectorCard(actionCard, "节点操作");
            actionCard.Dock = DockStyle.Fill;
            var actionBody = CreateCardBody();
            actionBody.Dock = DockStyle.Fill;
            actionBody.AutoSize = false;
            ConfigureActionButton(_runNodeButton, "运行当前节点", async (_, _) => await RunSelectedNodeAsync(), Color.FromArgb(255, 122, 0), Color.Black);
            ConfigureActionButton(_deleteNodeButton, "删除当前节点", (_, _) => DeleteSelectedNode(), Color.FromArgb(91, 46, 46), Color.White);
            _runNodeButton.Dock = DockStyle.Top;
            _deleteNodeButton.Dock = DockStyle.Top;
            _runNodeButton.Margin = new Padding(0, 0, 0, 8);
            actionBody.Controls.Add(_deleteNodeButton);
            actionBody.Controls.Add(_runNodeButton);
            actionCard.Controls.Add(actionBody);
            actionCard.MinimumSize = new Size(0, 196);
            SetInspectorCardTitle(actionCard, "节点操作");

            var outputCard = _inspectorOutputCard;
            InitializeInspectorCard(outputCard, "节点输出");
            outputCard.Dock = DockStyle.Fill;
            var outputBody = CreateCardBody();
            outputBody.Dock = DockStyle.Fill;
            outputBody.AutoSize = false;
            _outputTextBox.Dock = DockStyle.Fill;
            _outputTextBox.Multiline = true;
            _outputTextBox.ScrollBars = ScrollBars.Vertical;
            _outputTextBox.ReadOnly = true;
            _outputTextBox.BorderStyle = BorderStyle.FixedSingle;
            _outputTextBox.BackColor = Color.FromArgb(20, 24, 34);
            _outputTextBox.ForeColor = Color.FromArgb(232, 236, 244);
            outputBody.Controls.Add(_outputTextBox);
            outputCard.Controls.Add(outputBody);
            outputCard.Controls.Clear();
            InitializeInspectorCard(outputCard, "本机授权");
            outputCard.Dock = DockStyle.Fill;
            outputCard.Controls.Add(BuildMembershipInfoBody());
            outputCard.MinimumSize = new Size(0, 256);

            content.Controls.Add(infoCard, 0, 0);
            content.Controls.Add(modelCard, 0, 1);
            content.Controls.Add(actionCard, 0, 2);
            content.Controls.Add(outputCard, 0, 3);

            panel.Controls.Add(content);
            return panel;
        }

        private Control BuildStatusBar()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(14, 18, 28),
                Padding = new Padding(16, 0, 16, 0),
                ColumnCount = 2,
                RowCount = 1,
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.ForeColor = Color.FromArgb(165, 179, 204);
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.Text = "准备就绪。";

            _statusRightLabel.AutoSize = true;
            _statusRightLabel.Dock = DockStyle.Fill;
            _statusRightLabel.ForeColor = Color.FromArgb(118, 132, 160);
            _statusRightLabel.TextAlign = ContentAlignment.MiddleRight;
            _statusRightLabel.Text = "TerryHe20900  ·  27911515@qq.com";

            panel.Controls.Add(_statusLabel, 0, 0);
            panel.Controls.Add(_statusRightLabel, 1, 0);
            return panel;
        }

        private static Panel CreatePanel(Padding padding)
        {
            return new Panel
            {
                Padding = padding,
                BackColor = Color.FromArgb(18, 22, 32),
            };
        }

        private static Panel CreateCard(string title)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 0,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 14),
                Padding = new Padding(1),
                BackColor = Color.FromArgb(49, 63, 92),
            };

            var header = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                Text = title,
                Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0),
                BackColor = Color.FromArgb(23, 28, 40),
            };

            panel.Controls.Add(header);
            return panel;
        }

        private static void InitializeInspectorCard(Panel panel, string title)
        {
            panel.Controls.Clear();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Color.FromArgb(20, 24, 34);
            panel.BorderStyle = BorderStyle.FixedSingle;
            panel.Margin = new Padding(0, 0, 0, 12);
            panel.Padding = Padding.Empty;
            panel.MinimumSize = new Size(0, 148);

            var header = new Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                Text = title,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(24, 30, 43),
                Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold, GraphicsUnit.Point),
                Padding = new Padding(12, 2, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
            };

            panel.Controls.Add(header);
        }

        private static void SetInspectorCardTitle(Panel panel, string title)
        {
            if (panel.Controls.OfType<Label>().FirstOrDefault() is Label header)
            {
                header.Text = title;
            }
        }

        private void ConfigureSidebarToggleButton(Button button, EventHandler clickHandler)
        {
            button.Size = new Size(26, 56);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(78, 92, 124);
            button.BackColor = Color.FromArgb(28, 34, 47);
            button.ForeColor = Color.White;
            button.Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold, GraphicsUnit.Point);
            button.Cursor = Cursors.Hand;
            button.TabStop = false;
            button.Click -= clickHandler;
            button.Click += clickHandler;
            button.BringToFront();
        }

        private void ToggleLeftSidebar()
        {
            if (_leftSidebarCollapsed)
            {
                _workspaceSplit.Panel1Collapsed = false;
                ApplySplitDistance(_workspaceSplit, _leftSidebarExpandedWidth);
                _leftSidebarCollapsed = false;
            }
            else
            {
                _leftSidebarExpandedWidth = Math.Max(220, _workspaceSplit.SplitterDistance);
                _workspaceSplit.Panel1Collapsed = true;
                _leftSidebarCollapsed = true;
            }

            PositionLeftSidebarToggleButton();
        }

        private void ToggleRightInspector()
        {
            if (_rightInspectorCollapsed)
            {
                _canvasSplit.Panel2Collapsed = false;
                ApplySplitDistance(_canvasSplit, Math.Max(640, _canvasSplit.Width - _rightInspectorExpandedWidth));
                _rightInspectorCollapsed = false;
            }
            else
            {
                _rightInspectorExpandedWidth = Math.Max(260, _canvasSplit.Panel2.Width);
                _canvasSplit.Panel2Collapsed = true;
                _rightInspectorCollapsed = true;
            }

            PositionRightInspectorToggleButton();
        }

        private void PositionLeftSidebarToggleButton()
        {
            _toggleLeftSidebarButton.Text = _leftSidebarCollapsed ? "▶" : "◀";
            _toggleLeftSidebarButton.Location = new Point(4, 18);
            _toggleLeftSidebarButton.BringToFront();
        }

        private void PositionRightInspectorToggleButton()
        {
            _toggleRightInspectorButton.Text = _rightInspectorCollapsed ? "◀" : "▶";
            _toggleRightInspectorButton.Location = new Point(Math.Max(4, _canvasSplit.Panel1.ClientSize.Width - _toggleRightInspectorButton.Width - 4), 18);
            _toggleRightInspectorButton.BringToFront();
        }

        private static Panel CreateCardBody()
        {
            return new Panel
            {
                Dock = DockStyle.Top,
                Height = 0,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12),
                BackColor = Color.FromArgb(18, 22, 32),
            };
        }

        private static Label CreateSectionLabel(string text)
        {
            return new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                ForeColor = Color.FromArgb(177, 190, 214),
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft,
            };
        }

        private static Label CreateHintLabel(string text)
        {
            return new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                MaximumSize = new Size(280, 0),
                Margin = new Padding(0, 0, 0, 8),
                ForeColor = Color.FromArgb(148, 159, 181),
                Text = text,
            };
        }

        private static void ConfigureToolbarButton(Button button, string text, EventHandler onClick, Color backColor, Color foreColor)
        {
            button.Text = text;
            button.Width = 96;
            button.Height = 34;
            button.FlatStyle = FlatStyle.Flat;
            button.Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold, GraphicsUnit.Point);
            button.Margin = new Padding(6, 0, 0, 0);
            button.BackColor = backColor;
            button.ForeColor = foreColor;
            button.UseVisualStyleBackColor = false;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(72, 87, 116);
            button.Click += onClick;
        }

        private static void ConfigureActionButton(Button button, string text, EventHandler onClick, Color backColor, Color foreColor)
        {
            button.Text = text;
            button.Height = 38;
            button.FlatStyle = FlatStyle.Flat;
            button.Margin = new Padding(0, 0, 0, 10);
            button.BackColor = backColor;
            button.ForeColor = foreColor;
            button.UseVisualStyleBackColor = false;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(72, 87, 116);
            button.Click += onClick;
        }

        private sealed class ModelPickerItem
        {
            public ModelPickerItem(string modelId, string displayName)
            {
                ModelId = modelId;
                DisplayName = displayName;
            }

            public string ModelId { get; }

            public string DisplayName { get; }

            public override string ToString()
            {
                return DisplayName;
            }
        }
    }
}
