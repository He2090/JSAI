using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public partial class MainForm
    {
        private sealed class GeneratedVideoListItem
        {
            public string Name { get; init; } = string.Empty;

            public string FullPath { get; init; } = string.Empty;

            public long Size { get; init; }

            public DateTime LastWriteTime { get; init; }
        }

        private enum CharacterDesignModelSlot
        {
            Text,
            TextToImage,
            ImageToImage,
        }

        private enum StoryboardImageModelSlot
        {
            TextToImage,
            ImageToImage,
        }

        private sealed class CharacterDesignModelPickerTag
        {
            public CharacterDesignModelPickerTag(CharacterDesignModelSlot slot, ModelCategory category)
            {
                Slot = slot;
                Category = category;
            }

            public CharacterDesignModelSlot Slot { get; }

            public ModelCategory Category { get; }
        }

        private sealed class StoryboardImageModelPickerTag
        {
            public StoryboardImageModelPickerTag(StoryboardImageModelSlot slot, ModelCategory category)
            {
                Slot = slot;
                Category = category;
            }

            public StoryboardImageModelSlot Slot { get; }

            public ModelCategory Category { get; }
        }

        private readonly WorkflowRuntimeService _runtimeService = new();
        private readonly System.Collections.Generic.Dictionary<ModelCategory, ComboBox> _inspectorModelComboBoxes = new();
        private WorkflowDocument _document = WorkflowDocument.CreateEmpty();
        private WorkflowNode? _selectedNode;
        private bool _syncingInspector;
        private bool _running;

        private void HookEvents()
        {
            _runtimeService.ConfirmStoryboardVideoContinueAsync = ConfirmStoryboardVideoContinueAsync;
            WorkflowRuntimeService.PromptDispatched += WorkflowRuntimeService_PromptDispatched;
            FormClosed += (_, _) =>
            {
                _runtimeService.ConfirmStoryboardVideoContinueAsync = null;
                WorkflowRuntimeService.PromptDispatched -= WorkflowRuntimeService_PromptDispatched;
            };

            Shown += (_, _) =>
            {
                _workspaceSplit.Panel1MinSize = 280;
                _workspaceSplit.Panel2MinSize = 780;
                _canvasSplit.Panel1MinSize = 640;
                _canvasSplit.Panel2MinSize = 300;
                ApplySplitDistance(_workspaceSplit, 310);
                ApplySplitDistance(_canvasSplit, Math.Max(640, _canvasSplit.Width - 360));
                PositionLeftSidebarToggleButton();
                PositionRightInspectorToggleButton();
            };

            _projectNameTextBox.TextChanged += (_, _) =>
            {
                if (_switchingSession)
                {
                    return;
                }

                if (_document.ProjectName == _projectNameTextBox.Text.Trim())
                {
                    return;
                }

                _document.ProjectName = string.IsNullOrWhiteSpace(_projectNameTextBox.Text) ? "新项目" : _projectNameTextBox.Text.Trim();
                MarkActiveSessionDirty();
                SaveWorkingCopy();
            };

            _assetListView.DoubleClick += (_, _) => CreateAssetNodeFromSelection();
            _connectionListView.DoubleClick += (_, _) => RemoveSelectedConnection();

            _canvas.DocumentChanged += (_, _) => OnDocumentChanged();
            _canvas.SelectedNodeChanged += (_, e) => SelectNode(e.Node);
            _canvas.NodeRunRequested += async (_, e) =>
            {
                if (e.Node != null)
                {
                    try
                    {
                        await RunNodeAsync(e.Node);
                    }
                    catch (Exception ex)
                    {
                        UpdateStatus($"节点执行失败：{ex.Message}", Color.DarkOrange);
                        MessageBox.Show(this, ex.Message, "执行失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            };
            _canvas.NodeCharacterActionRequested += async (_, e) =>
            {
                await RunCharacterDesignActionWorkflowAsync(e.Node, e.CharacterName, e.Action);
            };
            _canvas.NodeActionRequested += async (_, e) =>
            {
                await RunNodeActionAsync(e.Node, e.Action);
            };
            _canvas.StatusChanged += (_, e) => UpdateStatus(e.Message, e.Color);
        }

        private void WorkflowRuntimeService_PromptDispatched(object? sender, WorkflowPromptDispatchedEventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            void UpdatePromptBox()
            {
                if (_latestPromptTextBox.IsDisposed)
                {
                    return;
                }

                var header = $"[{e.DispatchedAt:HH:mm:ss}] {e.ModuleName}";
                if (!string.IsNullOrWhiteSpace(e.ModelName))
                {
                    header += $" / {e.ModelName}";
                }

                _latestPromptTextBox.Text = $"{header}{Environment.NewLine}{Environment.NewLine}{e.Prompt}";
                _latestPromptTextBox.SelectionStart = 0;
                _latestPromptTextBox.SelectionLength = 0;
                _latestPromptTextBox.ScrollToCaret();
            }

            if (InvokeRequired)
            {
                BeginInvoke((Action)UpdatePromptBox);
            }
            else
            {
                UpdatePromptBox();
            }
        }

        private void LoadInitialDocument()
        {
            _currentProjectFilePath = string.Empty;
            _document = WorkflowDocument.CreateEmpty();
            _canvas.SetDocument(_document);
            _projectNameTextBox.Text = _document.ProjectName;
            SelectNode(null);
            RefreshWorkspaceForCurrentMode();
            RebuildProjectTabs();
        }

        private void CreateNewProject()
        {
            CreateNewProject(ProjectWorkspaceMode.AiAnimeProject);
        }

        private void CreateNewProject(ProjectWorkspaceMode mode, string? initialNodeType = null)
        {
            if (_running)
            {
                return;
            }

            var session = AddProjectSession(
                WorkflowDocument.CreateEmpty(mode: mode),
                isDirty: true,
                workspaceLabel: GetWorkspaceLabel(mode, initialNodeType));
            ActivateProjectSession(session);

            if (!string.IsNullOrWhiteSpace(initialNodeType))
            {
                AddNode(initialNodeType);
            }

            SaveWorkingCopy();
            UpdateStatus("已创建新项目。", Color.FromArgb(90, 176, 255));
        }

        private void AddNode(string nodeType)
        {
            var normalizedType = WorkflowNodeCatalog.NormalizeNodeType(nodeType);
            var location = _canvas.GetSuggestedNodeLocation(_document.Nodes.Count, normalizedType);
            var node = new WorkflowNode
            {
                Id = _document.CreateNextNodeId(),
                Type = normalizedType,
                X = location.X,
                Y = location.Y,
                Params = new WorkflowNodeParameters
                {
                    PreferredModelId = GetDefaultModelIdForNodeType(normalizedType),
                },
            };

            node.Params.EnsureDefaults(normalizedType);
            _document.Nodes.Add(node);
            TryAutoConnectNewNode(node);
            _canvas.SetDocument(_document);
            SelectNode(node);
            OnDocumentChanged();
            UpdateStatus($"已添加节点：{node.Type}", Color.FromArgb(90, 176, 255));
        }

        private void TryAutoConnectNewNode(WorkflowNode node)
        {
            if (node.Type != WorkflowNodeCatalog.Script)
            {
                return;
            }

            var outlineNode = _document.Nodes
                .Where(candidate => candidate.Id != node.Id && candidate.Type == WorkflowNodeCatalog.Outline)
                .OrderBy(candidate => string.IsNullOrWhiteSpace(candidate.Output) ? 0 : 1)
                .ThenBy(candidate => _document.Nodes.IndexOf(candidate))
                .LastOrDefault();

            if (outlineNode == null ||
                _document.Edges.Any(edge =>
                    string.Equals(edge.From, outlineNode.Id, StringComparison.Ordinal) &&
                    string.Equals(edge.To, node.Id, StringComparison.Ordinal)))
            {
                return;
            }

            _document.Edges.Add(new WorkflowEdge
            {
                From = outlineNode.Id,
                To = node.Id,
            });
        }

        private void ImportAssets()
        {
            using var dialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "支持的素材|*.png;*.jpg;*.jpeg;*.webp;*.mp4;*.mov;*.mkv;*.mp3;*.wav|全部文件|*.*",
                Title = "导入本地素材",
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            foreach (var filePath in dialog.FileNames)
            {
                if (_document.Assets.Any(asset => string.Equals(asset.FilePath, filePath, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var info = new FileInfo(filePath);
                _document.Assets.Add(new WorkflowAsset
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = info.Name,
                    FilePath = info.FullName,
                    Size = info.Exists ? info.Length : 0,
                    Kind = GetAssetKind(info.Extension),
                    Mime = GetAssetMime(info.Extension),
                    LastModified = info.Exists ? info.LastWriteTimeUtc.Ticks : DateTime.UtcNow.Ticks,
                });
            }

            OnDocumentChanged();
            UpdateStatus("素材已导入。", Color.FromArgb(90, 176, 255));
        }

        private void CreateAssetNodeFromSelection()
        {
            if (_assetListView.SelectedItems.Count == 0)
            {
                return;
            }

            if (_assetListView.SelectedItems[0].Tag is GeneratedVideoListItem videoItem)
            {
                try
                {
                    using var player = new VideoPlaybackForm(videoItem.FullPath, $"视频预览 - {videoItem.Name}");
                    player.ShowDialog(this);
                }
                catch
                {
                    Process.Start(new ProcessStartInfo(videoItem.FullPath)
                    {
                        UseShellExecute = true,
                    });
                }

                return;
            }

            if (_assetListView.SelectedItems[0].Tag is not WorkflowAsset asset)
            {
                return;
            }

            var location = _canvas.GetSuggestedNodeLocation(_document.Nodes.Count, WorkflowNodeCatalog.LocalAsset);
            var node = new WorkflowNode
            {
                Id = _document.CreateNextNodeId(),
                Type = WorkflowNodeCatalog.LocalAsset,
                X = location.X,
                Y = location.Y,
                ArtifactPath = asset.FilePath,
                ArtifactKind = asset.Kind,
                Params = new WorkflowNodeParameters
                {
                    Input = asset.FilePath,
                },
            };

            _document.Nodes.Add(node);
            _canvas.SetDocument(_document);
            SelectNode(node);
            OnDocumentChanged();
        }

        private void RemoveSelectedConnection()
        {
            if (_connectionListView.SelectedItems.Count == 0 || _connectionListView.SelectedItems[0].Tag is not WorkflowEdge edge)
            {
                return;
            }

            _document.Edges.Remove(edge);
            _canvas.SetDocument(_document);
            OnDocumentChanged();
            UpdateStatus("已删除连线。", Color.DarkOrange);
        }

        private void SelectNode(WorkflowNode? node)
        {
            _selectedNode = node;
            _syncingInspector = true;
            SetInspectorCardTitle(_inspectorActionCard, "节点操作");
            SetInspectorCardTitle(_inspectorOutputCard, "本机授权");

            if (node == null)
            {
                _selectedNodeTitleLabel.Text = "未选择节点";
                _selectedNodeHintLabel.Text = "点击画布中的节点可查看输出，并给当前节点单独选择模型。";
                _modelHintLabel.Text = "当前未选择节点。";
                _modelSelectorPanel.Controls.Clear();
                _inspectorModelComboBoxes.Clear();
                _runNodeButton.Enabled = false;
                _deleteNodeButton.Enabled = false;
                var showInspectorExtras = !IsDirectStudioMode;
                _inspectorActionCard.Visible = showInspectorExtras;
                _inspectorOutputCard.Visible = showInspectorExtras;
                RefreshMembershipInspector();
                _syncingInspector = false;
                return;
            }

            SyncCharacterDesignNodeVisualStyle(node);
            _selectedNodeTitleLabel.Text = $"{node.Id} 路 {node.Type}";
            _selectedNodeHintLabel.Text = $"坐标：({node.X}, {node.Y})";
            _runNodeButton.Enabled = !_running &&
                                     (node.Type != WorkflowNodeCatalog.CreativeDescription ||
                                      !string.IsNullOrWhiteSpace(node.Output));
            _deleteNodeButton.Enabled = !_running;
            var showDirectStudioExtras = !WorkflowNodeCatalog.IsDirectStudioNodeType(node.Type);
            _inspectorActionCard.Visible = showDirectStudioExtras;
            _inspectorOutputCard.Visible = showDirectStudioExtras;
            PopulateModelOptions(node);
            RefreshMembershipInspector();
            _syncingInspector = false;
        }

        private void SyncCharacterDesignNodeVisualStyle(WorkflowNode node)
        {
            if (node == null ||
                (node.Type != WorkflowNodeCatalog.CharacterView &&
                 node.Type != WorkflowNodeCatalog.CharacterDescription &&
                 node.Type != WorkflowNodeCatalog.StoryboardImage &&
                 node.Type != WorkflowNodeCatalog.StoryboardVideo))
            {
                return;
            }

            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);
            var outlineNode = WorkflowExecutor.CollectUpstreamNodes(_document, node)
                .FirstOrDefault(candidate => candidate.Type == WorkflowNodeCatalog.Outline);
            outlineNode ??= _document.Nodes
                .Where(candidate => candidate.Type == WorkflowNodeCatalog.Outline)
                .OrderBy(candidate => string.IsNullOrWhiteSpace(candidate.Output) ? 0 : 1)
                .ThenBy(candidate => _document.Nodes.IndexOf(candidate))
                .LastOrDefault();

            var style = outlineNode?.Params?.VisualStyle.OrDefault(string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(style) &&
                WorkflowNodeCatalog.OutlineVisualStyles.Contains(style, StringComparer.OrdinalIgnoreCase) &&
                !string.Equals(node.Params.VisualStyle, style, StringComparison.OrdinalIgnoreCase))
            {
                node.Params.VisualStyle = style;
                _canvas.RefreshNode(node.Id);
            }
        }

        private void PopulateModelOptions(WorkflowNode node)
        {
            var settings = ModelConfig.Load();
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);

            var categories = WorkflowExecutor.GetRequiredModelCategories(node.Type);
            _modelSelectorPanel.SuspendLayout();
            _modelSelectorPanel.Controls.Clear();
            _inspectorModelComboBoxes.Clear();

            if (categories.Count == 0)
            {
                _modelHintLabel.Text = "当前节点不需要单独指定模型。";
                _modelSelectorPanel.ResumeLayout();
                return;
            }

            if (IsCharacterDesignModelNode(node))
            {
                PopulateCharacterDesignModelOptions(settings, node);
                _modelSelectorPanel.ResumeLayout();
                return;
            }

            if (IsStoryboardImageModelNode(node))
            {
                PopulateStoryboardImageModelOptions(settings, node);
                _modelSelectorPanel.ResumeLayout();
                return;
            }

            _modelHintLabel.Text = categories.Count == 1
                ? $"当前节点使用 {GetModelCategoryDisplayName(categories[0])} 模型。"
                : $"当前节点会同时使用 {string.Join(" + ", categories.Select(GetModelCategoryDisplayName))} 模型。";

            foreach (var category in categories)
            {
                var comboBox = CreateInspectorModelComboBox();
                var entries = new List<ModelPickerItem>
                {
                    new(string.Empty, $"使用{GetModelCategoryDisplayName(category)}默认模型"),
                };

                var availableModels = settings.Models
                    .Where(model => model.Category == category)
                    .OrderBy(model => ModelConfig.GetModelSource(model) == ModelEndpointSource.Local ? 0 : 1)
                    .ThenBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                entries.AddRange(availableModels.Select(model => new ModelPickerItem(
                    ModelConfig.GetModelSelector(model),
                    FormatInspectorModelOptionLabel(model))));

                comboBox.Tag = category;
                comboBox.Enabled = !_running;
                comboBox.SelectedIndexChanged += ModelCategoryComboBox_SelectedIndexChanged;
                comboBox.Items.Clear();
                comboBox.Items.AddRange(entries.Cast<object>().ToArray());

                var preferredId = node.Params.GetPreferredModelId(category);
                var selectedIndex = -1;
                if (!string.IsNullOrWhiteSpace(preferredId))
                {
                    var modelOffset = 1;
                    var modelIndex = availableModels.FindIndex(model => ModelConfig.MatchesModelSelector(model, preferredId));
                    if (selectedIndex < 0 && modelIndex >= 0)
                    {
                        selectedIndex = modelOffset + modelIndex;
                    }
                }
                comboBox.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;

                _inspectorModelComboBoxes[category] = comboBox;
                _modelSelectorPanel.Controls.Add(CreateInspectorModelRow(category, comboBox));
            }

            _modelSelectorPanel.ResumeLayout();
        }

        private void PopulateCharacterDesignModelOptions(ModelSettings settings, WorkflowNode node)
        {
            _modelHintLabel.Text = "角色节点会使用：文本模型 + 文生图模型 + 图生图模型。";

            AddCharacterDesignModelComboBox(
                settings,
                node,
                "文本模型 / Text Model",
                ModelCategory.Text,
                CharacterDesignModelSlot.Text,
                node.Params.GetPreferredModelId(ModelCategory.Text),
                "使用文本默认模型");

            AddCharacterDesignModelComboBox(
                settings,
                node,
                "文生图模型 / Text-to-Image Model",
                ModelCategory.Image,
                CharacterDesignModelSlot.TextToImage,
                node.Params.CharacterTextToImageModelId.OrDefault(node.Params.GetPreferredModelId(ModelCategory.Image)),
                "使用图片默认模型");

            AddCharacterDesignModelComboBox(
                settings,
                node,
                "图生图模型 / Image-to-Image Model",
                ModelCategory.Image,
                CharacterDesignModelSlot.ImageToImage,
                node.Params.CharacterImageToImageModelId.OrDefault(node.Params.GetPreferredModelId(ModelCategory.Image)),
                "使用文生图模型或图片默认模型");
        }

        private static bool IsCharacterDesignModelNode(WorkflowNode node)
        {
            var normalizedType = WorkflowNodeCatalog.NormalizeNodeType(node.Type);
            return string.Equals(normalizedType, WorkflowNodeCatalog.CharacterView, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalizedType, WorkflowNodeCatalog.CharacterDescription, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStoryboardImageModelNode(WorkflowNode node)
        {
            var normalizedType = WorkflowNodeCatalog.NormalizeNodeType(node.Type);
            return string.Equals(normalizedType, WorkflowNodeCatalog.StoryboardImage, StringComparison.OrdinalIgnoreCase);
        }

        private void PopulateStoryboardImageModelOptions(ModelSettings settings, WorkflowNode node)
        {
            _modelHintLabel.Text = "分镜图片节点会自动路由：无参考图用文生图模型，有参考图用图生图模型。";

            AddStoryboardImageModelComboBox(
                settings,
                node,
                "文生图选择模型 / Text-to-Image Model",
                ModelCategory.Image,
                StoryboardImageModelSlot.TextToImage,
                node.Params.StoryboardTextToImageModelId.OrDefault(node.Params.GetPreferredModelId(ModelCategory.Image)),
                "使用图片默认模型");

            AddStoryboardImageModelComboBox(
                settings,
                node,
                "图生图选择模型 / Image-to-Image Model",
                ModelCategory.Image,
                StoryboardImageModelSlot.ImageToImage,
                node.Params.StoryboardImageToImageModelId
                    .OrDefault(node.Params.StoryboardTextToImageModelId)
                    .OrDefault(node.Params.GetPreferredModelId(ModelCategory.Image)),
                "使用文生图模型或图片默认模型");
        }

        private void AddCharacterDesignModelComboBox(
            ModelSettings settings,
            WorkflowNode node,
            string labelText,
            ModelCategory category,
            CharacterDesignModelSlot slot,
            string preferredId,
            string defaultLabel)
        {
            var comboBox = CreateInspectorModelComboBox();
            var entries = new List<ModelPickerItem>
            {
                new(string.Empty, defaultLabel),
            };

            var availableModels = settings.Models
                .Where(model => model.Category == category)
                .OrderBy(model => ModelConfig.GetModelSource(model) == ModelEndpointSource.Local ? 0 : 1)
                .ThenBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            entries.AddRange(availableModels.Select(model => new ModelPickerItem(
                ModelConfig.GetModelSelector(model),
                FormatInspectorModelOptionLabel(model))));

            comboBox.Tag = new CharacterDesignModelPickerTag(slot, category);
            comboBox.Enabled = !_running;
            comboBox.SelectedIndexChanged += ModelCategoryComboBox_SelectedIndexChanged;
            comboBox.Items.Clear();
            comboBox.Items.AddRange(entries.Cast<object>().ToArray());

            var selectedIndex = -1;
            if (!string.IsNullOrWhiteSpace(preferredId))
            {
                var modelIndex = availableModels.FindIndex(model => ModelConfig.MatchesModelSelector(model, preferredId));
                if (modelIndex >= 0)
                {
                    selectedIndex = 1 + modelIndex;
                }
            }

            comboBox.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
            if (slot == CharacterDesignModelSlot.Text || slot == CharacterDesignModelSlot.ImageToImage)
            {
                _inspectorModelComboBoxes[category] = comboBox;
            }

            _modelSelectorPanel.Controls.Add(CreateInspectorModelRow(labelText, comboBox));
        }

        private void AddStoryboardImageModelComboBox(
            ModelSettings settings,
            WorkflowNode node,
            string labelText,
            ModelCategory category,
            StoryboardImageModelSlot slot,
            string preferredId,
            string defaultLabel)
        {
            var comboBox = CreateInspectorModelComboBox();
            var entries = new List<ModelPickerItem>
            {
                new(string.Empty, defaultLabel),
            };

            var availableModels = settings.Models
                .Where(model => model.Category == category)
                .OrderBy(model => ModelConfig.GetModelSource(model) == ModelEndpointSource.Local ? 0 : 1)
                .ThenBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            entries.AddRange(availableModels.Select(model => new ModelPickerItem(
                ModelConfig.GetModelSelector(model),
                FormatInspectorModelOptionLabel(model))));

            comboBox.Tag = new StoryboardImageModelPickerTag(slot, category);
            comboBox.Enabled = !_running;
            comboBox.SelectedIndexChanged += ModelCategoryComboBox_SelectedIndexChanged;
            comboBox.Items.Clear();
            comboBox.Items.AddRange(entries.Cast<object>().ToArray());

            var selectedIndex = -1;
            if (!string.IsNullOrWhiteSpace(preferredId))
            {
                var modelIndex = availableModels.FindIndex(model => ModelConfig.MatchesModelSelector(model, preferredId));
                if (modelIndex >= 0)
                {
                    selectedIndex = 1 + modelIndex;
                }
            }

            comboBox.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
            if (slot == StoryboardImageModelSlot.ImageToImage)
            {
                _inspectorModelComboBoxes[category] = comboBox;
            }

            _modelSelectorPanel.Controls.Add(CreateInspectorModelRow(labelText, comboBox));
        }

        private static bool SupportsRelayVideoMode(string nodeType)
        {
            return false;
        }

        private async Task RunWorkflowAsync()
        {
            if (_running)
            {
                return;
            }

            if (!WorkflowExecutor.ValidateBeforeRun(_document, out var errorMessage))
            {
                MessageBox.Show(this, errorMessage, "执行前检查失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _running = true;
            SetRunState(false);
            UpdateStatus("正在执行工作流...", Color.FromArgb(90, 176, 255));

            try
            {
                using var processingScope = BeginProcessingScope("正在执行工作流...");
                foreach (var node in WorkflowExecutor.ComputeExecutionOrder(_document))
                {
                    SetProcessingDetail($"正在执行节点：{node.Type}");
                    await RunNodeAsync(node, saveAfterRun: false);
                }

                SaveWorkingCopy();
                UpdateStatus("工作流执行完成。", Color.FromArgb(90, 176, 255));
            }
            catch (Exception ex)
            {
                UpdateStatus($"工作流执行失败：{ex.Message}", Color.DarkOrange);
                MessageBox.Show(this, ex.Message, "执行失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _running = false;
                SetRunState(true);
                SelectNode(_selectedNode);
            }
        }

        private async Task RunSelectedNodeAsync()
        {
            if (_selectedNode == null || _running)
            {
                return;
            }

            _running = true;
            SetRunState(false);
            try
            {
                using var processingScope = BeginProcessingScope($"正在执行节点：{_selectedNode.Type}");
                await RunNodeAsync(_selectedNode);
            }
            catch (Exception ex)
            {
                UpdateStatus($"节点执行失败：{ex.Message}", Color.DarkOrange);
                MessageBox.Show(this, ex.Message, "执行失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _running = false;
                SetRunState(true);
                SelectNode(_selectedNode);
            }
        }

        private async Task RunNodeActionAsync(WorkflowNode node, string action)
        {
            if (_running || node == null || string.IsNullOrWhiteSpace(action))
            {
                return;
            }

            if (string.Equals(action, "storyboard-video.save-assets", StringComparison.OrdinalIgnoreCase))
            {
                SaveStoryboardVideoAssets(node);
                return;
            }

            if (string.Equals(action, "storyboard-video.fetch-video-by-task-id", StringComparison.OrdinalIgnoreCase))
            {
                node.Params ??= new WorkflowNodeParameters();
                string? input = PromptDialog.Show(this, "按任务ID获取视频", "请输入云端任务ID或完整查询地址：", node.Params.StoryboardVideoTaskId.OrDefault(node.Params.StoryboardVideoTaskQueryUrl));
                if (string.IsNullOrWhiteSpace(input))
                {
                    return;
                }

                string trimmed = input.Trim();
                if (Uri.TryCreate(trimmed, UriKind.Absolute, out var queryUri) &&
                    (string.Equals(queryUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(queryUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
                {
                    node.Params.StoryboardVideoTaskQueryUrl = queryUri.ToString();
                    string idFromUri = ExtractYunWuTaskId(queryUri);
                    if (!string.IsNullOrWhiteSpace(idFromUri))
                    {
                        node.Params.StoryboardVideoTaskId = idFromUri;
                    }
                }
                else
                {
                    node.Params.StoryboardVideoTaskId = trimmed;
                    node.Params.StoryboardVideoTaskQueryUrl = string.Empty;
                }

                node.Params.StoryboardVideoLastError = string.Empty;
            }

            _running = true;
            SetRunState(false);
            _canvas.SetNodeBusy(node.Id, true);
            try
            {
                UpdateStatus($"正在处理节点动作：{node.Type} / {action}", Color.FromArgb(90, 176, 255));
                using var processingScope = BeginProcessingScope(BuildProcessingDetailText(node, action));

                await _runtimeService.ExecuteNodeActionAsync(_document, node, action, CancellationToken.None);
                var documentChanged = SyncCreativeDescriptionNodes(node)
                    | SyncStoryboardVideoPreviewNodes(node)
                    | SyncVideoCollectionNodes(node);
                ExportGeneratedProjectAssets(node);
                if (documentChanged)
                {
                    _canvas.SetDocument(_document);
                }
                else if (RequiresCanvasWideRefresh(node))
                {
                    _canvas.RefreshAllNodes();
                }
                else
                {
                    _canvas.RefreshNode(node.Id);
                }

                RefreshGeneratedMediaUi(node);

                if (_selectedNode?.Id == node.Id)
                {
                    SelectNode(node);
                }

                SaveWorkingCopy();
                if (string.Equals(node.Type, WorkflowNodeCatalog.StoryboardVideo, StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(action, "storyboard-video.generate-video", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(action, "storyboard-video.fetch-video-by-task-id", StringComparison.OrdinalIgnoreCase)))
                {
                    UpdateStatus("分镜视频已完成，已刷新已连接的视频合集。", Color.FromArgb(90, 176, 255));
                }
            }
            catch (Exception ex)
            {
                _canvas.RefreshNode(node.Id);
                if (_selectedNode?.Id == node.Id)
                {
                    SelectNode(node);
                }

                UpdateStatus($"节点动作执行失败：{ex.Message}", Color.DarkOrange);
                MessageBox.Show(this, ex.Message, "执行失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _canvas.SetNodeBusy(node.Id, false);
                _running = false;
                SetRunState(true);
            }
        }

        private static string BuildProcessingDetailText(WorkflowNode node, string action)
        {
            if (string.Equals(node.Type, WorkflowNodeCatalog.StoryboardVideo, StringComparison.OrdinalIgnoreCase))
            {
                return action.Trim().ToLowerInvariant() switch
                {
                    "storyboard-video.fetch-shots" => "正在获取可选分镜...",
                    "storyboard-video.generate-prompt" => "正在生成分镜视频提示词...",
                    "storyboard-video.generate-video" => "正在生成选中的分镜视频...",
                    "storyboard-video.fetch-video-by-task-id" => "正在按任务ID获取视频...",
                    _ => $"正在处理：{node.Type}",
                };
            }

            if (string.Equals(node.Type, WorkflowNodeCatalog.VideoCollection, StringComparison.OrdinalIgnoreCase))
            {
                return action.Trim().ToLowerInvariant() switch
                {
                    "video-collection.generate-video" => "正在生成视频合集...",
                    "video-collection.extract-audio" => "正在剥离视频音频...",
                    _ => $"正在处理：{node.Type}",
                };
            }

            return $"正在处理：{node.Type}";
        }

        private static string ExtractYunWuTaskId(Uri queryUri)
        {
            if (queryUri == null)
            {
                return string.Empty;
            }

            var query = queryUri.Query.OrDefault(string.Empty).TrimStart('?');
            foreach (var segment in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = segment.Split('=', 2);
                if (kv.Length == 2 && string.Equals(kv[0], "id", StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(kv[1]);
                }
            }

            var pathSegments = queryUri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (pathSegments.Length >= 2 && string.Equals(pathSegments[0], "v1", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(pathSegments[1], "videos", StringComparison.OrdinalIgnoreCase) && pathSegments.Length >= 3)
            {
                return Uri.UnescapeDataString(pathSegments[2]);
            }

            return string.Empty;
        }

        private async Task RunNodeAsync(WorkflowNode node, bool saveAfterRun = true)
        {
            _canvas.SetNodeBusy(node.Id, true);
            using var processingScope = BeginProcessingScope($"正在执行节点：{node.Type}");
            try
            {
                var input = WorkflowExecutor.CollectUpstreamOutput(_document, node);
                UpdateStatus($"正在执行节点：{node.Type}", Color.FromArgb(90, 176, 255));
                try
                {
                    await _runtimeService.ExecuteNodeAsync(_document, node, input, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    if (!await TryRetryNodeWithCloudImageModelAsync(node, input, ex))
                    {
                        throw;
                    }
                }

                ArchiveDirectStudioArtifacts(node);
                var documentChanged = SyncCreativeDescriptionNodes(node)
                    | SyncStoryboardVideoPreviewNodes(node)
                    | SyncVideoCollectionNodes(node);
                ExportGeneratedProjectAssets(node);
                if (documentChanged)
                {
                    _canvas.SetDocument(_document);
                }
                else
                {
                    _canvas.RefreshAllNodes();
                }

                RefreshGeneratedMediaUi(node);

                if (_selectedNode?.Id == node.Id)
                {
                    SelectNode(node);
                }

                if (saveAfterRun)
                {
                    SaveWorkingCopy();
                }
            }
            finally
            {
                _canvas.SetNodeBusy(node.Id, false);
            }
        }

        private void ExportGeneratedProjectAssets(WorkflowNode node)
        {
            try
            {
                if (node == null)
                {
                    return;
                }

                if (node.Type == WorkflowNodeCatalog.StoryboardImage ||
                    node.Type == WorkflowNodeCatalog.StoryboardBreakdown ||
                    node.Type == WorkflowNodeCatalog.StoryboardVideo)
                {
                    ProjectLibraryExportService.ExportStoryboardNodeToProjectFolder(_document.ProjectName, node);
                }
            }
            catch
            {
            }
        }

        private static bool RequiresCanvasWideRefresh(WorkflowNode node)
        {
            return node != null &&
                   (string.Equals(node.Type, WorkflowNodeCatalog.StoryboardVideo, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(node.Type, WorkflowNodeCatalog.VideoPreview, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(node.Type, WorkflowNodeCatalog.VideoCollection, StringComparison.OrdinalIgnoreCase));
        }

        private void RefreshGeneratedMediaUi(WorkflowNode node)
        {
            if (!RequiresCanvasWideRefresh(node))
            {
                return;
            }

            RefreshAssets();
            RefreshConnections();
            _canvas.RefreshAllNodes();

            if (_selectedNode != null)
            {
                SelectNode(_selectedNode);
            }
        }

        private bool SyncVideoCollectionNodes(WorkflowNode node)
        {
            if (node == null)
            {
                return false;
            }

            var storyboardVideoId = ResolveStoryboardVideoOwnerId(node);
            if (string.IsNullOrWhiteSpace(storyboardVideoId))
            {
                return false;
            }

            var affectedCollections = _document.Nodes
                .Where(candidate =>
                    string.Equals(candidate.Type, WorkflowNodeCatalog.VideoCollection, StringComparison.OrdinalIgnoreCase) &&
                    IsVideoCollectionConnectedToStoryboardVideo(candidate, storyboardVideoId))
                .ToList();
            var storyboardVideoNode = _document.Nodes.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, storyboardVideoId, StringComparison.OrdinalIgnoreCase));

            var changed = false;
            foreach (var collectionNode in affectedCollections)
            {
                collectionNode.Params ??= new WorkflowNodeParameters();
                collectionNode.Params.EnsureDefaults(collectionNode.Type);

                var previousCurrentPath = collectionNode.Params.VideoCollectionCurrentArtifactPath.OrDefault(string.Empty);
                var previousSelected = string.Join(
                    "|",
                    (collectionNode.Params.VideoCollectionSelectedArtifactPaths ?? new List<string>())
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
                var previousOutput = collectionNode.Output.OrDefault(string.Empty);

                if (storyboardVideoNode != null)
                {
                    changed |= AddStoryboardVideoSourcesToCollection(collectionNode, storyboardVideoNode);
                }

                var selectedSources = VideoCollectionSupport.GetTimelineSources(_document, collectionNode);
                var currentArtifactPath = collectionNode.Params.VideoCollectionCurrentArtifactPath.OrDefault(string.Empty);
                var selectedJoined = string.Join(
                    "|",
                    (collectionNode.Params.VideoCollectionSelectedArtifactPaths ?? new List<string>())
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));

                if (!string.Equals(previousCurrentPath, currentArtifactPath, StringComparison.OrdinalIgnoreCase))
                {
                    changed = true;
                }

                if (!string.Equals(previousSelected, selectedJoined, StringComparison.Ordinal))
                {
                    changed = true;
                }

                var summary = BuildVideoCollectionSummary(collectionNode, selectedSources);
                if (!string.Equals(previousOutput, summary, StringComparison.Ordinal))
                {
                    collectionNode.Output = summary;
                    changed = true;
                }
            }

            return changed;
        }

        private static bool AddStoryboardVideoSourcesToCollection(WorkflowNode collectionNode, WorkflowNode storyboardVideoNode)
        {
            collectionNode.Params ??= new WorkflowNodeParameters();
            collectionNode.Params.EnsureDefaults(collectionNode.Type);
            storyboardVideoNode.Params ??= new WorkflowNodeParameters();
            storyboardVideoNode.Params.EnsureDefaults(storyboardVideoNode.Type);

            var selectedPaths = collectionNode.Params.VideoCollectionSelectedArtifactPaths ??= new List<string>();
            var existing = new HashSet<string>(
                selectedPaths
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(NormalizeCollectionSourcePath),
                StringComparer.OrdinalIgnoreCase);

            var changed = false;
            foreach (var sourcePath in EnumerateStoryboardVideoSourcePaths(storyboardVideoNode))
            {
                var normalizedPath = NormalizeCollectionSourcePath(sourcePath);
                if (string.IsNullOrWhiteSpace(normalizedPath) || !File.Exists(normalizedPath) || !existing.Add(normalizedPath))
                {
                    continue;
                }

                selectedPaths.Add(normalizedPath);
                changed = true;
            }

            if (changed)
            {
                collectionNode.Params.VideoCollectionSelectionInitialized = true;
            }

            return changed;
        }

        private static IEnumerable<string> EnumerateStoryboardVideoSourcePaths(WorkflowNode storyboardVideoNode)
        {
            foreach (var clip in storyboardVideoNode.Params?.StoryboardVideoGeneratedClips ?? Enumerable.Empty<StoryboardVideoGeneratedClip>())
            {
                if (!string.IsNullOrWhiteSpace(clip.ArtifactPath))
                {
                    yield return clip.ArtifactPath;
                }
            }

            if (string.Equals(storyboardVideoNode.ArtifactKind, "video", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(storyboardVideoNode.ArtifactPath))
            {
                yield return storyboardVideoNode.ArtifactPath;
            }
        }

        private static string NormalizeCollectionSourcePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path.Trim();
            }
        }

        private string ResolveStoryboardVideoOwnerId(WorkflowNode node)
        {
            if (string.Equals(node.Type, WorkflowNodeCatalog.StoryboardVideo, StringComparison.OrdinalIgnoreCase))
            {
                return node.Id;
            }

            if (string.Equals(node.Type, WorkflowNodeCatalog.VideoPreview, StringComparison.OrdinalIgnoreCase))
            {
                return node.Params?.AutoGeneratedSourceNodeId.OrDefault(string.Empty);
            }

            return string.Empty;
        }

        private bool IsVideoCollectionConnectedToStoryboardVideo(WorkflowNode collectionNode, string storyboardVideoId)
        {
            return WorkflowExecutor.CollectUpstreamNodes(_document, collectionNode)
                .Any(upstream => string.Equals(upstream.Id, storyboardVideoId, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildVideoCollectionSummary(WorkflowNode collectionNode, IReadOnlyList<VideoCollectionSourceItem> selectedSources)
        {
            collectionNode.Params ??= new WorkflowNodeParameters();
            collectionNode.Params.EnsureDefaults(collectionNode.Type);

            var lines = new List<string>
            {
                $"已收集视频片段：{selectedSources.Count}"
            };

            if (!string.IsNullOrWhiteSpace(collectionNode.ArtifactPath) && File.Exists(collectionNode.ArtifactPath))
            {
                lines.Add($"合集文件：{collectionNode.ArtifactPath}");
            }

            if (!string.IsNullOrWhiteSpace(collectionNode.Params.VideoCollectionAudioPath))
            {
                lines.Add($"音轨：{Path.GetFileName(collectionNode.Params.VideoCollectionAudioPath)}");
            }

            if (!string.IsNullOrWhiteSpace(collectionNode.Params.VideoCollectionSubtitleText))
            {
                lines.Add("字幕：已添加");
            }

            if (!string.Equals(collectionNode.Params.VideoCollectionTransitionType, "none", StringComparison.Ordinal))
            {
                lines.Add($"转场：{WorkflowNodeParameters.GetVideoCollectionTransitionDisplayName(collectionNode.Params.VideoCollectionTransitionType)}");
            }

            if (selectedSources.Count == 0)
            {
                lines.Add("等待分镜视频或视频预览节点提供可合成片段。");
            }
            else
            {
                foreach (var source in selectedSources.Select((value, index) => new { value, index }))
                {
                    lines.Add($"- #{source.index + 1} {source.value.DisplayName} | {source.value.DurationLabel}");
                    lines.Add($"  {source.value.ArtifactPath}");
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        private void SaveStoryboardVideoAssets(WorkflowNode node)
        {
            try
            {
                node.Params ??= new WorkflowNodeParameters();
                node.Params.EnsureDefaults(node.Type);

                var hasPrompt = !string.IsNullOrWhiteSpace(node.Params.StoryboardVideoPrompt);
                var hasFusedImage = !string.IsNullOrWhiteSpace(node.Params.StoryboardVideoFusedImagePath) &&
                                    File.Exists(node.Params.StoryboardVideoFusedImagePath);
                if (!hasPrompt || !hasFusedImage)
                {
                    MessageBox.Show(
                        this,
                        "请先生成融合参考图和视频提示词，再执行保存。",
                        "保存分镜融合资产",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                var suggestedName = $"{(_document.ProjectName ?? "新项目").Trim()}_{node.Id}";
                var saveName = PromptForFolderName("保存分镜融合资产", "请输入保存名称：", suggestedName);
                if (string.IsNullOrWhiteSpace(saveName))
                {
                    return;
                }

                var folderPath = ProjectLibraryExportService.ExportStoryboardVideoFusionAssets(
                    _document.ProjectName,
                    node,
                    saveName);

                UpdateStatus($"分镜融合资产已保存：{folderPath}", Color.FromArgb(90, 176, 255));
                MessageBox.Show(
                    this,
                    $"已保存到：{folderPath}",
                    "保存成功",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "保存分镜融合资产失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private string? PromptForFolderName(string title, string labelText, string initialValue)
        {
            using var dialog = new Form
            {
                Text = title,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                ClientSize = new Size(420, 138),
                BackColor = Color.FromArgb(24, 28, 40),
                ForeColor = Color.White,
            };

            var label = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 24,
                Text = labelText,
                ForeColor = Color.White,
                Margin = new Padding(0, 0, 0, 8),
            };

            var textBox = new TextBox
            {
                Dock = DockStyle.Top,
                Height = 30,
                Text = initialValue ?? string.Empty,
                Margin = new Padding(0, 0, 0, 12),
            };

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = Padding.Empty,
                Margin = Padding.Empty,
            };

            var okButton = new Button
            {
                Text = "确定",
                Width = 92,
                Height = 32,
                DialogResult = DialogResult.OK,
                BackColor = Color.FromArgb(255, 122, 0),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(8, 0, 0, 0),
            };
            okButton.FlatAppearance.BorderSize = 0;

            var cancelButton = new Button
            {
                Text = "取消",
                Width = 92,
                Height = 32,
                DialogResult = DialogResult.Cancel,
                BackColor = Color.FromArgb(64, 70, 84),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            cancelButton.FlatAppearance.BorderSize = 0;

            buttonPanel.Controls.Add(okButton);
            buttonPanel.Controls.Add(cancelButton);

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18, 18, 18, 14),
            };
            body.Controls.Add(buttonPanel);
            body.Controls.Add(textBox);
            body.Controls.Add(label);

            dialog.Controls.Add(body);
            dialog.AcceptButton = okButton;
            dialog.CancelButton = cancelButton;

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return null;
            }

            return textBox.Text.Trim();
        }

        private async Task RunCharacterActionAsync(WorkflowNode node, string characterName, CharacterDesignActionType action)
        {
            if (_running)
            {
                return;
            }

            _canvas.SetNodeBusy(node.Id, true);
            using var processingScope = BeginProcessingScope($"正在生成角色内容：{characterName}");
            try
            {
                var actionLabel = action == CharacterDesignActionType.GenerateExpression ? "九宫格" : "三视图";
                UpdateStatus($"正在生成角色{actionLabel}：{characterName}", Color.FromArgb(90, 176, 255));
                SetProcessingDetail($"正在生成角色{actionLabel}：{characterName}");
                await _runtimeService.ExecuteCharacterDesignActionAsync(_document, node, characterName, action, CancellationToken.None);
                _canvas.RefreshNode(node.Id);
                if (_selectedNode?.Id == node.Id)
                {
                    SelectNode(node);
                }

                SaveWorkingCopy();
                UpdateStatus($"角色{actionLabel}已完成：{characterName}", Color.FromArgb(90, 176, 255));
            }
            catch (Exception ex)
            {
                if (!await TryRetryCharacterActionWithCloudImageModelAsync(node, characterName, action, ex))
                {
                    var friendlyMessage = FormatCharacterActionError(ex);
                    _canvas.RefreshNode(node.Id);
                    if (_selectedNode?.Id == node.Id)
                    {
                        SelectNode(node);
                    }

                    UpdateStatus($"角色设计执行失败：{GetFirstLine(friendlyMessage)}", Color.DarkOrange);
                    MessageBox.Show(this, friendlyMessage, "执行失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            finally
            {
                _canvas.SetNodeBusy(node.Id, false);
            }
        }

        private async Task<bool> TryRetryNodeWithCloudImageModelAsync(WorkflowNode node, string input, Exception originalException)
        {
            if (ModelConfig.LocalOnlyMode)
            {
                return false;
            }

            if (!NodeUsesImageModel(node))
            {
                return false;
            }

            var settings = ModelConfig.Load();
            var currentImageModel = ResolveCurrentImageModelForNode(settings, node);
            if (currentImageModel == null || !ModelConfig.IsLocalImageModelUrl(currentImageModel.Url))
            {
                return false;
            }

            var cloudImageModel = ModelConfig.GetPreferredCloudImageModel(settings);
            if (cloudImageModel == null ||
                string.IsNullOrWhiteSpace(cloudImageModel.Url) ||
                string.Equals(ModelConfig.GetModelSelector(cloudImageModel), ModelConfig.GetModelSelector(currentImageModel), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var result = MessageBox.Show(
                this,
                $"本地图片接口调用失败：{originalException.Message}\n\n是否切换到云端 API（{cloudImageModel.Name}）重试？",
                "本地图片接口不可用",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            {
                return false;
            }

            node.Params ??= new WorkflowNodeParameters();
            node.Params.SetPreferredModelId(ModelCategory.Image, ModelConfig.GetModelSelector(cloudImageModel));
            if (IsCharacterDesignModelNode(node))
            {
                node.Params.CharacterTextToImageModelId = ModelConfig.GetModelSelector(cloudImageModel);
                node.Params.CharacterImageToImageModelId = ModelConfig.GetModelSelector(cloudImageModel);
            }
            else if (IsStoryboardImageModelNode(node))
            {
                node.Params.StoryboardTextToImageModelId = ModelConfig.GetModelSelector(cloudImageModel);
                node.Params.StoryboardImageToImageModelId = ModelConfig.GetModelSelector(cloudImageModel);
            }

            node.Params.PreferredModelId = string.Empty;
            UpdateStatus($"本地图片接口不可用，已切换到云端模型：{cloudImageModel.Name}", Color.DarkOrange);
            await _runtimeService.ExecuteNodeAsync(_document, node, input, CancellationToken.None);
            return true;
        }

        private async Task<bool> TryRetryCharacterActionWithCloudImageModelAsync(WorkflowNode node, string characterName, CharacterDesignActionType action, Exception originalException)
        {
            if (ModelConfig.LocalOnlyMode)
            {
                return false;
            }

            var settings = ModelConfig.Load();
            var currentImageModel = ResolveCurrentImageModelForNode(settings, node);
            if (currentImageModel == null || !ModelConfig.IsLocalImageModelUrl(currentImageModel.Url))
            {
                return false;
            }

            var cloudImageModel = ModelConfig.GetPreferredCloudImageModel(settings);
            if (cloudImageModel == null ||
                string.IsNullOrWhiteSpace(cloudImageModel.Url) ||
                string.Equals(ModelConfig.GetModelSelector(cloudImageModel), ModelConfig.GetModelSelector(currentImageModel), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var actionLabel = action == CharacterDesignActionType.GenerateExpression ? "九宫格" : "三视图";
            var friendlyMessage = FormatCharacterActionError(originalException);
            var result = MessageBox.Show(
                this,
                $"本地图片接口调用失败：{friendlyMessage}\n\n是否切换到云端 API（{cloudImageModel.Name}）重试角色{actionLabel}：{characterName}？",
                "本地图片接口不可用",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            {
                return false;
            }

            node.Params ??= new WorkflowNodeParameters();
            node.Params.SetPreferredModelId(ModelCategory.Image, ModelConfig.GetModelSelector(cloudImageModel));
            node.Params.CharacterTextToImageModelId = ModelConfig.GetModelSelector(cloudImageModel);
            node.Params.CharacterImageToImageModelId = ModelConfig.GetModelSelector(cloudImageModel);
            node.Params.PreferredModelId = string.Empty;
            UpdateStatus($"已切换到云端图片模型：{cloudImageModel.Name}，正在重试角色{actionLabel}。", Color.DarkOrange);
            await _runtimeService.ExecuteCharacterDesignActionAsync(_document, node, characterName, action, CancellationToken.None);
            _canvas.RefreshNode(node.Id);
            if (_selectedNode?.Id == node.Id)
            {
                SelectNode(node);
            }

            SaveWorkingCopy();
            UpdateStatus($"角色{actionLabel}已完成：{characterName}", Color.FromArgb(90, 176, 255));
            return true;
        }

        private static string FormatCharacterActionError(Exception exception)
        {
            var deepestMessage = GetDeepestExceptionMessage(exception);
            if (LooksLikeLocalImageTransportError(exception))
            {
                return
                    "本地图片模型请求发送失败。\n\n" +
                    "请检查：\n" +
                    "1. ComfyUI 或 Stable Diffusion 是否已经启动；\n" +
                    "2. 模型设置里的本地地址是否正确，ComfyUI 默认是 http://127.0.0.1:8000；\n" +
                    "3. Workflowsapi 工作流模板、模型文件和参考图节点是否可用。\n\n" +
                    $"原始错误：{deepestMessage}";
            }

            return string.IsNullOrWhiteSpace(exception.Message) ? deepestMessage : exception.Message;
        }

        private static bool LooksLikeLocalImageTransportError(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                var typeName = current.GetType().FullName ?? string.Empty;
                var message = current.Message ?? string.Empty;
                if (current is IOException ||
                    typeName.Contains("HttpRequestException", StringComparison.OrdinalIgnoreCase) ||
                    typeName.Contains("SocketException", StringComparison.OrdinalIgnoreCase) ||
                    typeName.Contains("HttpIOException", StringComparison.OrdinalIgnoreCase) ||
                    typeName.Contains("HttpProtocolException", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("An error occurred while sending the request", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("actively refused", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("connection refused", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("response ended prematurely", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("ComfyUI", StringComparison.OrdinalIgnoreCase) && message.Contains("连接", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("目标计算机积极拒绝", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetDeepestExceptionMessage(Exception exception)
        {
            var current = exception;
            while (current.InnerException != null)
            {
                current = current.InnerException;
            }

            return string.IsNullOrWhiteSpace(current.Message) ? exception.Message : current.Message;
        }

        private static string GetFirstLine(string text)
        {
            return (text ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? string.Empty;
        }

        private static bool NodeUsesImageModel(WorkflowNode node)
        {
            return WorkflowExecutor.GetRequiredModelCategories(node.Type).Contains(ModelCategory.Image);
        }

        private static ModelInfo? ResolveCurrentImageModelForNode(ModelSettings settings, WorkflowNode node)
        {
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);

            var preferredImageModelId = node.Params.GetPreferredModelId(ModelCategory.Image);
            if (IsCharacterDesignModelNode(node))
            {
                preferredImageModelId = node.Params.CharacterImageToImageModelId
                    .OrDefault(node.Params.CharacterTextToImageModelId)
                    .OrDefault(preferredImageModelId);
            }
            else if (IsStoryboardImageModelNode(node))
            {
                preferredImageModelId = node.Params.StoryboardImageToImageModelId
                    .OrDefault(node.Params.StoryboardTextToImageModelId)
                    .OrDefault(preferredImageModelId);
            }

            if (string.IsNullOrWhiteSpace(preferredImageModelId))
            {
                preferredImageModelId = node.Params.PreferredModelId ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(preferredImageModelId))
            {
                var preferredModel = settings.Models.FirstOrDefault(model =>
                    model.Category == ModelCategory.Image &&
                    ModelConfig.MatchesModelSelector(model, preferredImageModelId));
                if (preferredModel != null)
                {
                    return ModelConfig.ApplyRelayOverrides(settings, preferredModel);
                }
            }

            var nodeDefaultId = ModelConfig.GetDefaultModelForNodeType(settings, node.Type);
            if (!string.IsNullOrWhiteSpace(nodeDefaultId))
            {
                var nodeDefaultModel = settings.Models.FirstOrDefault(model =>
                    model.Category == ModelCategory.Image &&
                    ModelConfig.MatchesModelSelector(model, nodeDefaultId));
                if (nodeDefaultModel != null)
                {
                    return ModelConfig.ApplyRelayOverrides(settings, nodeDefaultModel);
                }
            }

            if (string.Equals(node.Type, WorkflowNodeCatalog.StoryboardImage, StringComparison.OrdinalIgnoreCase))
            {
                return ModelConfig.GetStoryboardImageWorkflowModel(settings);
            }

            if (string.IsNullOrWhiteSpace(settings.SelectedImageModel))
            {
                return null;
            }

            var selectedModel = settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Image &&
                ModelConfig.MatchesModelSelector(model, settings.SelectedImageModel));
            return selectedModel == null ? null : ModelConfig.ApplyRelayOverrides(settings, selectedModel);
        }
        private void DeleteSelectedNode()
        {
            if (_selectedNode == null)
            {
                return;
            }

            var result = MessageBox.Show(this, $"确认删除节点“{_selectedNode.Type}”吗？", "删除节点", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
            {
                return;
            }

            _document.Nodes.Remove(_selectedNode);
            _document.Edges.RemoveAll(edge => edge.From == _selectedNode.Id || edge.To == _selectedNode.Id);
            _canvas.SetDocument(_document);
            SelectNode(null);
            OnDocumentChanged();
        }

        private void ImportWorkflow()
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "Workflow JSON|*.json|全部文件|*.*",
                Title = "导入工作流",
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var document = WorkflowStore.LoadFromFile(dialog.FileName, throwOnFailure: false);
            if (document == null)
            {
                MessageBox.Show(this, "文件读取失败或格式无效。", "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var session = AddProjectSession(document, isDirty: true);
            ActivateProjectSession(session);
            SaveWorkingCopy();
            UpdateStatus("工作流已导入。", Color.FromArgb(90, 176, 255));
        }

        private void ExportWorkflow()
        {
            if (!EnsureProjectSaveAllowed())
            {
                return;
            }

            using var dialog = new SaveFileDialog
            {
                Filter = "Workflow JSON|*.json",
                FileName = $"{_document.ProjectName}.json",
                Title = "导出工作流",
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            WorkflowStore.SaveToFile(_document, dialog.FileName);
            UpdateStatus("工作流已导出。", Color.FromArgb(90, 176, 255));
        }

        private void SaveProjectPackage()
        {
            if (!EnsureProjectSaveAllowed())
            {
                return;
            }

            SaveWorkingCopy();

            using var dialog = new FolderBrowserDialog
            {
                Description = "閫夋嫨椤圭洰淇濆瓨浣嶇疆",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var targetDirectory = Path.Combine(
                dialog.SelectedPath,
                WorkflowStore.BuildProjectDirectoryName(_document.ProjectName));

            if (Directory.Exists(targetDirectory) && Directory.EnumerateFileSystemEntries(targetDirectory).Any())
            {
                var overwrite = MessageBox.Show(
                    this,
                    $"鐩綍宸插瓨鍦細\n{targetDirectory}\n\n缁х画淇濆瓨浼氳鐩栬椤圭洰鐩綍涓嬩箣鍓嶅鍑虹殑宸ヤ綔娴併€佽妭鐐规枃鏈拰浜х墿鍓湰銆傛槸鍚︾户缁紵",
                    "淇濆瓨椤圭洰",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (overwrite != DialogResult.Yes)
                {
                    return;
                }
            }

            try
            {
                var result = WorkflowStore.SaveProjectPackage(_document, targetDirectory);
                var libraryResult = ProjectLibraryExportService.Export(_document);
                UpdateStatus($"项目已保存到：{result.RootDirectory}", Color.FromArgb(90, 176, 255));
                MessageBox.Show(
                    this,
                    $"项目已保存完成。\n\n目录：{result.RootDirectory}\n工作流：{result.WorkflowPath}\n节点文本：{result.OutputFileCount} 个\n复制文件：{result.CopiedFileCount} 个\n\n素材库：{libraryResult.RootPath}\n角色目录：{libraryResult.CharacterFolderCount} 个\n分镜目录：{libraryResult.StoryboardFolderCount} 个",
                    "保存完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                UpdateStatus($"项目保存失败：{ex.Message}", Color.DarkOrange);
                MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SaveWorkingCopy()
        {
            SyncActiveSessionSnapshot();
            WorkflowStore.SaveLastWorkflow(_document);
            RefreshStats();
            RefreshAssets();
            RefreshConnections();
            RefreshDirectImageHistory();
        }

        private void OnDocumentChanged()
        {
            MarkActiveSessionDirty();
            SaveWorkingCopy();
        }

        private void RefreshStats()
        {
            var workspaceStats = _document.ProjectMode == ProjectWorkspaceMode.DirectStudio
                ? $"{_document.ProjectName} · {_document.Nodes.Count} 个模组节点"
                : $"{_document.ProjectName} · {_document.Nodes.Count} 个节点 · {_document.Edges.Count} 条连线 · {_document.Assets.Count} 个资产";
            var memberText = MembershipContext.CurrentDisplayText;
            _statsLabel.Text = string.IsNullOrWhiteSpace(memberText)
                ? workspaceStats
                : $"{workspaceStats} · {memberText}";
            _saveProjectButton.Enabled = !_running && MembershipContext.CurrentSession?.User?.CanSaveProjects == true;
            _saveProjectAsButton.Enabled = !_running && MembershipContext.CurrentSession?.User?.CanSaveProjects == true;
            RefreshMembershipInspector();
        }

        private void RefreshAssets()
        {
            _assetListView.BeginUpdate();
            _assetListView.Items.Clear();

            if (_document.ProjectMode == ProjectWorkspaceMode.AiAnimeProject)
            {
                var projectRoot = ProjectStoragePaths.EnsureProjectRootPath(ProjectWorkspaceMode.AiAnimeProject, _document.ProjectName);
                var videoFiles = new List<FileInfo>();
                var videoPatterns = new[] { "*.mp4", "*.mov", "*.mkv", "*.avi", "*.webm" };
                foreach (var pattern in videoPatterns)
                {
                    videoFiles.AddRange(Directory
                        .EnumerateFiles(projectRoot, pattern, SearchOption.AllDirectories)
                        .Select(path => new FileInfo(path))
                        .Where(info => info.Exists));
                }

                foreach (var video in videoFiles
                    .OrderByDescending(info => info.LastWriteTimeUtc)
                    .ThenBy(info => info.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var listItem = new GeneratedVideoListItem
                    {
                        Name = video.Name,
                        FullPath = video.FullName,
                        Size = video.Length,
                        LastWriteTime = video.LastWriteTime,
                    };

                    var item = new ListViewItem(new[]
                    {
                        listItem.Name,
                        "视频",
                        FormatSize(listItem.Size),
                    })
                    {
                        Tag = listItem,
                        ToolTipText = listItem.FullPath,
                    };
                    _assetListView.Items.Add(item);
                }

                _importAssetButton.Visible = false;
                UpdateAssetPanelMetadata("生成视频列表", "双击可直接预览已生成的视频，列表只读取当前 AI 漫剧项目目录下的视频文件。");
            }
            else
            {
                foreach (var asset in _document.Assets)
                {
                    var item = new ListViewItem(new[]
                    {
                        asset.Name,
                        asset.Kind,
                        FormatSize(asset.Size),
                    })
                    {
                        Tag = asset,
                    };
                    _assetListView.Items.Add(item);
                }

                _importAssetButton.Visible = _document.ProjectMode != ProjectWorkspaceMode.DirectStudio;
                UpdateAssetPanelMetadata("本地资产", "双击素材会创建一个“本地资产”节点，并自动绑定文件路径。");
            }

            _assetListView.EndUpdate();
        }

        private void UpdateAssetPanelMetadata(string title, string hint)
        {
            if (_assetCardTitleLabel != null)
            {
                _assetCardTitleLabel.Text = title;
            }

            if (_assetCardHintLabel != null)
            {
                _assetCardHintLabel.Text = hint;
            }
        }

        private void RefreshConnections()
        {
            _connectionListView.BeginUpdate();
            _connectionListView.Items.Clear();
            foreach (var edge in _document.Edges)
            {
                var item = new ListViewItem(new[]
                {
                    DescribeNode(edge.From),
                    DescribeNode(edge.To),
                })
                {
                    Tag = edge,
                };
                _connectionListView.Items.Add(item);
            }
            _connectionListView.EndUpdate();
        }

        private void OpenModelSettings()
        {
            try
            {
                using var form = new ModelSettingsForm();
                form.ShowDialog(this);
                if (_selectedNode != null)
                {
                    PopulateModelOptions(_selectedNode);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"打开模型设置失败：{ex.Message}", "模型设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ModelCategoryComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_syncingInspector || _selectedNode == null || sender is not ComboBox comboBox || comboBox.SelectedItem is not ModelPickerItem item)
            {
                return;
            }

            _selectedNode.Params ??= new WorkflowNodeParameters();
            if (comboBox.Tag is CharacterDesignModelPickerTag characterTag)
            {
                ApplyCharacterDesignModelSelection(characterTag, item);
                return;
            }

            if (comboBox.Tag is StoryboardImageModelPickerTag storyboardTag)
            {
                ApplyStoryboardImageModelSelection(storyboardTag, item);
                return;
            }

            if (comboBox.Tag is not ModelCategory category)
            {
                return;
            }

            _selectedNode.Params.SetPreferredModelId(category, item.ModelId ?? string.Empty);
            _selectedNode.Params.PreferredModelId = string.Empty;
            _canvas.RefreshNode(_selectedNode.Id);
            SelectNode(_selectedNode);
            SaveWorkingCopy();
            UpdateStatus($"已更新当前节点的{GetModelCategoryDisplayName(category)}模型路由。", Color.FromArgb(90, 176, 255));
        }

        private void ApplyCharacterDesignModelSelection(CharacterDesignModelPickerTag tag, ModelPickerItem item)
        {
            if (_selectedNode == null)
            {
                return;
            }

            var modelId = item.ModelId ?? string.Empty;
            _selectedNode.Params ??= new WorkflowNodeParameters();
            switch (tag.Slot)
            {
                case CharacterDesignModelSlot.Text:
                    _selectedNode.Params.SetPreferredModelId(ModelCategory.Text, modelId);
                    break;
                case CharacterDesignModelSlot.TextToImage:
                    _selectedNode.Params.CharacterTextToImageModelId = modelId;
                    _selectedNode.Params.SetPreferredModelId(ModelCategory.Image, string.Empty);
                    break;
                case CharacterDesignModelSlot.ImageToImage:
                    _selectedNode.Params.CharacterImageToImageModelId = modelId;
                    _selectedNode.Params.SetPreferredModelId(ModelCategory.Image, string.Empty);
                    break;
            }

            _selectedNode.Params.PreferredModelId = string.Empty;
            _canvas.RefreshNode(_selectedNode.Id);
            SelectNode(_selectedNode);
            SaveWorkingCopy();
            UpdateStatus($"已更新当前角色节点的{GetCharacterDesignModelSlotDisplayName(tag.Slot)}。", Color.FromArgb(90, 176, 255));
        }

        private void ApplyStoryboardImageModelSelection(StoryboardImageModelPickerTag tag, ModelPickerItem item)
        {
            if (_selectedNode == null)
            {
                return;
            }

            var modelId = item.ModelId ?? string.Empty;
            _selectedNode.Params ??= new WorkflowNodeParameters();
            switch (tag.Slot)
            {
                case StoryboardImageModelSlot.TextToImage:
                    _selectedNode.Params.StoryboardTextToImageModelId = modelId;
                    break;
                case StoryboardImageModelSlot.ImageToImage:
                    _selectedNode.Params.StoryboardImageToImageModelId = modelId;
                    break;
            }

            _selectedNode.Params.SetPreferredModelId(ModelCategory.Image, string.Empty);
            _selectedNode.Params.PreferredModelId = string.Empty;
            _canvas.RefreshNode(_selectedNode.Id);
            SelectNode(_selectedNode);
            SaveWorkingCopy();
            UpdateStatus($"已更新当前分镜图片节点的{GetStoryboardImageModelSlotDisplayName(tag.Slot)}。", Color.FromArgb(90, 176, 255));
        }

        private Control CreateInspectorModelRow(ModelCategory category, ComboBox comboBox)
        {
            return CreateInspectorModelRow(GetInspectorModelCategoryDisplayName(category), comboBox);
        }

        private Control CreateInspectorModelRow(string labelText, ComboBox comboBox)
        {
            var panel = new Panel
            {
                Width = Math.Max(220, _canvasSplit.Panel2.Width - 60),
                Height = 58,
                Margin = new Padding(0, 0, 0, 10),
                Padding = Padding.Empty,
                BackColor = Color.Transparent,
            };

            var label = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                ForeColor = Color.FromArgb(177, 190, 214),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = labelText,
            };

            comboBox.Dock = DockStyle.Bottom;
            panel.Controls.Add(comboBox);
            panel.Controls.Add(label);
            return panel;
        }

        private static string FormatInspectorModelOptionLabel(ModelInfo model)
        {
            var workflowJson = ModelConfig.ResolveComfyUiWorkflowJson(model);
            var baseText = $"[{ModelConfig.GetModelSourceDisplayName(model)}] {model.Name} ({model.Id})";
            return string.IsNullOrWhiteSpace(workflowJson)
                ? baseText
                : $"{baseText} / {workflowJson}";
        }

        private ComboBox CreateInspectorModelComboBox()
        {
            return new ComboBox
            {
                Height = 32,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(23, 28, 40),
                ForeColor = Color.White,
                Margin = Padding.Empty,
            };
        }

        private Task<bool> ConfirmStoryboardVideoContinueAsync(StoryboardVideoClipGeneratedEventArgs e, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(false);
            }

            var clip = e.Clip;
            var current = Math.Min(e.Index + 1, e.Total);
            var message =
                $"第 {current} / {e.Total} 段分镜视频已生成。{Environment.NewLine}" +
                $"第{Math.Max(1, clip.ShotNumber)}镜：{FormatStoryboardClipSceneForDisplay(clip.Scene)}{Environment.NewLine}{Environment.NewLine}" +
                "是否继续生成下一个分镜？";
            var result = MessageBox.Show(
                this,
                message,
                "继续生成分镜视频",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);
            return Task.FromResult(result == DialogResult.Yes);
        }

        private static string FormatStoryboardClipSceneForDisplay(string? scene)
        {
            if (string.IsNullOrWhiteSpace(scene))
            {
                return "未命名分镜";
            }

            var filtered = new string(scene
                .Where(ch =>
                    (ch >= '\u3400' && ch <= '\u4DBF') ||
                    (ch >= '\u4E00' && ch <= '\u9FFF') ||
                    "，。！？、：；“”‘’（）《》【】".Contains(ch))
                .ToArray())
                .Trim();
            return string.IsNullOrWhiteSpace(filtered) ? "未命名分镜" : filtered;
        }

        private static string GetModelCategoryDisplayName(ModelCategory category)
        {
            return category switch
            {
                ModelCategory.Text => "文本",
                ModelCategory.Image => "图片",
                ModelCategory.Video => "视频",
                _ => "模型",
            };
        }

        private static string GetInspectorModelCategoryDisplayName(ModelCategory category)
        {
            return category switch
            {
                ModelCategory.Text => "文本模型 / Text Model",
                ModelCategory.Image => "图片模型 / Image Model",
                ModelCategory.Video => "视频模型 / Video Model",
                _ => "模型 / Model",
            };
        }

        private static string GetCharacterDesignModelSlotDisplayName(CharacterDesignModelSlot slot)
        {
            return slot switch
            {
                CharacterDesignModelSlot.Text => "文本模型路由",
                CharacterDesignModelSlot.TextToImage => "文生图模型路由",
                CharacterDesignModelSlot.ImageToImage => "图生图模型路由",
                _ => "模型路由",
            };
        }

        private static string GetStoryboardImageModelSlotDisplayName(StoryboardImageModelSlot slot)
        {
            return slot switch
            {
                StoryboardImageModelSlot.TextToImage => "文生图选择模型",
                StoryboardImageModelSlot.ImageToImage => "图生图选择模型",
                _ => "模型路由",
            };
        }

        private void SetRunState(bool enabled)
        {
            _newProjectButton.Enabled = enabled;
            _saveProjectButton.Enabled = enabled && MembershipContext.CurrentSession?.User?.CanSaveProjects == true;
            _saveProjectAsButton.Enabled = enabled && MembershipContext.CurrentSession?.User?.CanSaveProjects == true;
            _importWorkflowButton.Enabled = enabled;
            _exportWorkflowButton.Enabled = enabled;
            _runWorkflowButton.Enabled = enabled;
            _importAssetButton.Enabled = enabled;
            _removeConnectionButton.Enabled = enabled;
            _runNodeButton.Enabled = enabled &&
                                     _selectedNode != null &&
                                     (_selectedNode.Type != WorkflowNodeCatalog.CreativeDescription ||
                                      !string.IsNullOrWhiteSpace(_selectedNode.Output));
            _deleteNodeButton.Enabled = enabled && _selectedNode != null;
        }

        private void UpdateStatus(string message, Color color)
        {
            _statusLabel.Text = message;
            _statusLabel.ForeColor = color;
        }

        private string GetDefaultModelIdForNodeType(string nodeType)
        {
            var settings = ModelConfig.Load();
            return ModelConfig.GetDefaultModelForNodeType(settings, nodeType);
        }

        private string DescribeNode(string nodeId)
        {
            var node = _document.Nodes.FirstOrDefault(item => item.Id == nodeId);
            return node == null ? nodeId : node.Type;
        }

        private static string GetAssetKind(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".png" or ".jpg" or ".jpeg" or ".webp" => "image",
                ".mp4" or ".mov" or ".mkv" => "video",
                ".mp3" or ".wav" => "audio",
                _ => "file",
            };
        }

        private static string GetAssetMime(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                ".mp4" => "video/mp4",
                ".mov" => "video/quicktime",
                ".mkv" => "video/x-matroska",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                _ => "application/octet-stream",
            };
        }

        private static string FormatSize(long size)
        {
            if (size <= 0)
            {
                return "0 B";
            }

            string[] units = { "B", "KB", "MB", "GB" };
            var value = (double)size;
            var unitIndex = 0;
            while (value >= 1024 && unitIndex < units.Length - 1)
            {
                value /= 1024;
                unitIndex++;
            }

            return $"{value:0.#} {units[unitIndex]}";
        }

        private static void ApplySplitDistance(SplitContainer split, int desired)
        {
            if (split.Width <= 0)
            {
                return;
            }

            var min = split.Panel1MinSize;
            var max = Math.Max(min, split.Width - split.Panel2MinSize - split.SplitterWidth);
            split.SplitterDistance = Math.Max(min, Math.Min(desired, max));
        }
    }
}

