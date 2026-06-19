using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public partial class ModelSettingsForm : Form
    {
        private static readonly HttpClient RelayHttpClient = new() { Timeout = TimeSpan.FromSeconds(8) };
        private readonly Dictionary<string, ComboBox> _nodeDefaultModelBoxes = new(StringComparer.OrdinalIgnoreCase);
        private ModelSettings _settings;
        private GroupBox? _relayApiGroupBox;
        private TextBox? _relayProviderCodeTextBox;
        private TextBox? _relayProviderNameTextBox;
        private TextBox? _yunWuBaseUrlTextBox;
        private TextBox? _yunWuKeyTextBox;
        private Button? _testRelayApiButton;
        private Button? _saveRelayApiButton;
        private Label? _relayApiHintLabel;
        private GroupBox? _nodeDefaultsGroupBox;
        private TableLayoutPanel? _nodeDefaultsLayout;
        private bool _syncingNodeDefaults;
        private readonly ListViewGroup _localModelsGroup = new("本地模型");
        private readonly ListViewGroup _cloudModelsGroup = new("云端模型");
        private readonly ListViewGroup _unknownModelsGroup = new("未识别来源");

        public ModelSettingsForm()
        {
            InitializeComponent();
            _settings = ModelConfig.Load();
        }

        private void ModelSettingsForm_Load(object sender, EventArgs e)
        {
            ConfigureExpandedLayout();
            EnsureRelayApiSection();
            NormalizeRelayApiSection();
            EnsureNodeDefaultsSection();
            LayoutExtendedSections();
            if (ModelConfig.LocalOnlyMode && _relayApiGroupBox != null)
            {
                _relayApiGroupBox.Visible = false;
            }
            ReloadSettingsAndRefreshViews();

            listModels.DoubleClick += ListModels_DoubleClick;
        }

        private void ConfigureExpandedLayout()
        {
            ClientSize = new Size(1040, 860);
            MinimumSize = new Size(1040, 860);

            listModels.Location = new Point(12, 52);
            listModels.Size = new Size(ClientSize.Width - 24, 250);
            listModels.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            var buttonY = 12;
            var buttonGap = 8;
            btnAddModel.Location = new Point(12, buttonY);
            btnAddModel.Size = new Size(96, 30);
            btnDeleteModel.Location = new Point(btnAddModel.Right + buttonGap, buttonY);
            btnDeleteModel.Size = new Size(96, 30);
            btnSetTextModel.Location = new Point(btnDeleteModel.Right + 22, buttonY);
            btnSetTextModel.Size = new Size(118, 30);
            btnSetImagePromptTextModel.Location = new Point(btnSetTextModel.Right + buttonGap, buttonY);
            btnSetImagePromptTextModel.Size = new Size(146, 30);
            btnSetImageModel.Location = new Point(btnSetImagePromptTextModel.Right + buttonGap, buttonY);
            btnSetImageModel.Size = new Size(118, 30);
            btnSetVideoModel.Location = new Point(btnSetImageModel.Right + buttonGap, buttonY);
            btnSetVideoModel.Size = new Size(118, 30);

            groupBox1.Text = "当前选择";
            groupBox1.Location = new Point(12, 314);
            groupBox1.Size = new Size(ClientSize.Width - 24, 74);
            groupBox1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            lblTextModel.AutoSize = false;
            lblTextModel.Location = new Point(12, 24);
            lblTextModel.Size = new Size(groupBox1.Width - 24, 20);
            lblTextModel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblTextModel.AutoEllipsis = true;

            lblImageModel.AutoSize = false;
            lblImageModel.Location = new Point(12, 46);
            lblImageModel.Size = new Size(groupBox1.Width - 24, 20);
            lblImageModel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblImageModel.AutoEllipsis = true;

            lblImagePromptTextModel.Visible = false;
            lblVideoModel.Visible = false;

            colName.Width = 180;
            colSource.Text = "来源";
            colSource.Width = 90;
            colCategory.Width = 80;
            colUrl.Width = 260;
            colKey.Width = 120;
            colId.Width = 180;
            listModels.ShowGroups = true;
        }

        private void EnsureRelayApiSection()
        {
            if (_relayApiGroupBox != null)
            {
                return;
            }

            _relayApiGroupBox = new GroupBox
            {
                Text = "中转API设置",
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 4,
                Padding = new Padding(12, 8, 12, 8),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));

            _relayProviderCodeTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
            };

            _relayProviderNameTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
            };

            _saveRelayApiButton = new Button
            {
                Dock = DockStyle.Right,
                Width = 118,
                Text = "保存中转API",
                UseVisualStyleBackColor = true,
            };
            _saveRelayApiButton.Click += (_, _) => SaveRelayApiSettings();

            _testRelayApiButton = new Button
            {
                Dock = DockStyle.Right,
                Width = 96,
                Text = "测试连接",
                UseVisualStyleBackColor = true,
            };
            _testRelayApiButton.Click += async (_, _) => await TestRelayApiSettingsAsync();

            _yunWuBaseUrlTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
            };

            _yunWuKeyTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                UseSystemPasswordChar = true,
            };

            _relayApiHintLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(96, 96, 96),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "中转API会优先覆盖同类视频模型的地址和密钥，适合云雾这类统一转发接口。",
            };

            var actionPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            actionPanel.Controls.Add(_testRelayApiButton);
            actionPanel.Controls.Add(_saveRelayApiButton);

            layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Provider Code", TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            layout.Controls.Add(_relayProviderCodeTextBox, 1, 0);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "显示名称", TextAlign = ContentAlignment.MiddleLeft }, 2, 0);
            layout.Controls.Add(_relayProviderNameTextBox, 3, 0);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "基础地址", TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            layout.Controls.Add(_yunWuBaseUrlTextBox, 1, 1);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "API Key", TextAlign = ContentAlignment.MiddleLeft }, 2, 1);
            layout.Controls.Add(_yunWuKeyTextBox, 3, 1);
            layout.Controls.Add(_relayApiHintLabel, 0, 2);
            layout.SetColumnSpan(_relayApiHintLabel, 3);
            layout.Controls.Add(actionPanel, 3, 2);
            var tailHintLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(96, 96, 96),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "视频节点中可以自由填写主模型与子模型，这里只维护中转平台的地址和密钥。",
            };
            layout.Controls.Add(tailHintLabel, 0, 3);
            layout.SetColumnSpan(tailHintLabel, 4);

            _relayApiGroupBox.Controls.Add(layout);
            Controls.Add(_relayApiGroupBox);
            _relayApiGroupBox.BringToFront();
        }

        private void LayoutExtendedSections()
        {
            if (_relayApiGroupBox != null)
            {
                _relayApiGroupBox.Location = new Point(12, 396);
                _relayApiGroupBox.Size = new Size(ClientSize.Width - 24, 154);
            }

            if (_nodeDefaultsGroupBox != null)
            {
                var nodeDefaultsTop = ModelConfig.LocalOnlyMode ? 396 : 558;
                _nodeDefaultsGroupBox.Location = new Point(12, nodeDefaultsTop);
                _nodeDefaultsGroupBox.Size = new Size(ClientSize.Width - 24, ClientSize.Height - nodeDefaultsTop - 12);
            }
        }

        private void NormalizeRelayApiSection()
        {
            if (_relayApiGroupBox?.Controls.OfType<TableLayoutPanel>().FirstOrDefault() is not TableLayoutPanel layout)
            {
                return;
            }

            _relayApiGroupBox.Text = "视频中转 API 设置";
            layout.RowCount = 3;
            layout.RowStyles.Clear();
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));

            if (_relayApiHintLabel != null)
            {
                _relayApiHintLabel.Text = "中转 API 会优先覆盖同类视频模型的地址和密钥，适合云雾这类统一转发接口；视频节点中可以自由填写主模型与子模型，这里只维护中转平台的地址和密钥。";
            }

            if (layout.GetControlFromPosition(0, 3) is Control tailHint)
            {
                layout.Controls.Remove(tailHint);
                tailHint.Dispose();
            }

            if (layout.GetControlFromPosition(2, 0) is Label nameLabel)
            {
                nameLabel.Text = "显示名称";
            }

            if (layout.GetControlFromPosition(0, 1) is Label urlLabel)
            {
                urlLabel.Text = "基础地址";
            }

            if (_testRelayApiButton != null)
            {
                _testRelayApiButton.Text = "测试连接";
            }

            if (_saveRelayApiButton != null)
            {
                _saveRelayApiButton.Text = "保存中转 API";
            }
        }

        private void EnsureNodeDefaultsSection()
        {
            if (_nodeDefaultsGroupBox != null)
            {
                return;
            }

            _nodeDefaultsGroupBox = new GroupBox
            {
                Text = "节点默认模型",
                Location = new Point(12, 526),
                Size = new Size(ClientSize.Width - 24, ClientSize.Height - 538),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            };

            _nodeDefaultsLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = WorkflowNodeCatalog.ConfigurableNodeTypes.Count,
                AutoScroll = false,
                Padding = new Padding(12, 8, 12, 8),
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
            };
            _nodeDefaultsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160F));
            _nodeDefaultsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var rowIndex = 0;
            foreach (var nodeType in WorkflowNodeCatalog.ConfigurableNodeTypes)
            {
                _nodeDefaultsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));

                var label = new Label
                {
                    Dock = DockStyle.Fill,
                    Text = nodeType,
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = Color.FromArgb(48, 48, 48),
                };

                var comboBox = new ComboBox
                {
                    Dock = DockStyle.Fill,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Margin = new Padding(0, 0, 0, 4),
                };
                comboBox.SelectedIndexChanged += (_, _) => NodeDefaultModelChanged(nodeType, comboBox);
                _nodeDefaultModelBoxes[nodeType] = comboBox;

                _nodeDefaultsLayout.Controls.Add(label, 0, rowIndex);
                _nodeDefaultsLayout.Controls.Add(comboBox, 1, rowIndex);
                rowIndex++;
            }

            _nodeDefaultsGroupBox.Controls.Add(_nodeDefaultsLayout);
            _nodeDefaultsGroupBox.Text = "节点默认模型";
            Controls.Add(_nodeDefaultsGroupBox);
            _nodeDefaultsGroupBox.BringToFront();
        }

        private void ListModels_DoubleClick(object? sender, EventArgs e)
        {
            if (listModels.SelectedItems.Count == 0)
            {
                return;
            }

            if (listModels.SelectedItems[0].Tag is not ModelInfo model)
            {
                return;
            }

            ShowModelEditor(model);
        }

        private void ReloadSettingsAndRefreshViews(string? preferredModelId = null)
        {
            preferredModelId ??= GetSelectedModelId();
            _settings = ModelConfig.Load();
            RefreshModelList(preferredModelId);
            RefreshCurrentSelections();
            ApplyCompactCurrentSelectionSummary();
            RefreshRelayApiSelections();
            RefreshNodeDefaultSelections();
        }

        private void RefreshRelayApiSelections()
        {
            if (_relayProviderCodeTextBox == null || _relayProviderNameTextBox == null || _yunWuBaseUrlTextBox == null || _yunWuKeyTextBox == null)
            {
                return;
            }

            var relay = ModelConfig.GetRelayApis(_settings).FirstOrDefault()
                        ?? ModelConfig.GetRelayApi(_settings, "yunwuapi")
                        ?? new RelayApiInfo
                        {
                            ProviderCode = "yunwuapi",
                            Name = "云雾API",
                            BaseUrl = ModelConfig.DefaultYunWuBaseUrl,
                            Enabled = true
                        };

            _relayProviderCodeTextBox.Text = string.IsNullOrWhiteSpace(relay.ProviderCode) ? "yunwuapi" : relay.ProviderCode;
            _relayProviderNameTextBox.Text = string.IsNullOrWhiteSpace(relay.Name) ? _relayProviderCodeTextBox.Text : relay.Name;
            _yunWuBaseUrlTextBox.Text = relay.BaseUrl ?? string.Empty;
            _yunWuKeyTextBox.Text = relay.Key ?? string.Empty;
        }

        private string? GetSelectedModelId()
        {
            if (listModels.SelectedItems.Count == 0)
            {
                return null;
            }

            return listModels.SelectedItems[0].Tag is ModelInfo model ? ModelConfig.GetModelSelector(model) : null;
        }

        private void RefreshModelList(string? preferredModelId = null)
        {
            listModels.BeginUpdate();
            listModels.Items.Clear();
            listModels.Groups.Clear();
            listModels.Groups.AddRange(new[] { _localModelsGroup, _cloudModelsGroup, _unknownModelsGroup });

            ListViewItem? preferredItem = null;
            foreach (var model in _settings.Models
                .OrderBy(item => ModelConfig.GetModelSource(item))
                .ThenBy(item => item.Category)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var keyDisplay = string.IsNullOrWhiteSpace(model.Key)
                    ? string.Empty
                    : (model.Key.Length <= 4 ? new string('*', model.Key.Length) : $"****{model.Key[^4..]}");
                var item = new ListViewItem(new[]
                {
                    model.Name,
                    ModelConfig.GetModelSourceDisplayName(model),
                    model.Category.ToString(),
                    model.Url,
                    keyDisplay,
                    model.Id
                })
                {
                    Tag = model,
                    Group = GetModelGroup(model),
                };
                listModels.Items.Add(item);

                if (!string.IsNullOrWhiteSpace(preferredModelId) &&
                    ModelConfig.MatchesModelSelector(model, preferredModelId))
                {
                    preferredItem = item;
                }
            }

            if (preferredItem != null)
            {
                preferredItem.Selected = true;
                preferredItem.Focused = true;
                preferredItem.EnsureVisible();
            }

            listModels.EndUpdate();
        }

        private void RefreshCurrentSelections()
        {
            var textModel = ModelConfig.FindModel(_settings, ModelCategory.Text, _settings.SelectedTextModel);
            var imagePromptTextModel = ModelConfig.FindModel(_settings, ModelCategory.Text, ModelConfig.GetImagePromptTextModelId(_settings));
            var imageModel = ModelConfig.FindModel(_settings, ModelCategory.Image, _settings.SelectedImageModel);
            var videoModel = ModelConfig.FindModel(_settings, ModelCategory.Video, _settings.SelectedVideoModel);

            lblTextModel.Text = $"文本模型：{FormatModelSelectionSummary(textModel, _settings.SelectedTextModel)}";
            lblImagePromptTextModel.Text = $"图片提示词模型：{FormatModelSelectionSummary(imagePromptTextModel, ModelConfig.GetImagePromptTextModelId(_settings))}";
            lblImageModel.Text = $"图片模型：{FormatModelSelectionSummary(imageModel, _settings.SelectedImageModel)}";
            lblVideoModel.Text = $"视频模型：{FormatModelSelectionSummary(videoModel, _settings.SelectedVideoModel)}";
        }

        private void ApplyCompactCurrentSelectionSummary()
        {
            var textModel = ModelConfig.FindModel(_settings, ModelCategory.Text, _settings.SelectedTextModel);
            var imagePromptTextModel = ModelConfig.FindModel(_settings, ModelCategory.Text, ModelConfig.GetImagePromptTextModelId(_settings));
            var imageModel = ModelConfig.FindModel(_settings, ModelCategory.Image, _settings.SelectedImageModel);
            var videoModel = ModelConfig.FindModel(_settings, ModelCategory.Video, _settings.SelectedVideoModel);

            lblTextModel.Text = $"文本模型：{FormatModelSelectionSummary(textModel, _settings.SelectedTextModel)}    图片提示词模型：{FormatModelSelectionSummary(imagePromptTextModel, ModelConfig.GetImagePromptTextModelId(_settings))}";
            lblImageModel.Text = $"图片模型：{FormatModelSelectionSummary(imageModel, _settings.SelectedImageModel)}    视频模型：{FormatModelSelectionSummary(videoModel, _settings.SelectedVideoModel)}";
            lblImagePromptTextModel.Visible = false;
            lblVideoModel.Visible = false;
        }

        private void SaveRelayApiSettings()
        {
            if (_relayProviderCodeTextBox == null || _relayProviderNameTextBox == null || _yunWuBaseUrlTextBox == null || _yunWuKeyTextBox == null)
            {
                return;
            }

            var providerCode = _relayProviderCodeTextBox.Text.Trim();
            var providerName = _relayProviderNameTextBox.Text.Trim();
            var baseUrl = _yunWuBaseUrlTextBox.Text.Trim();
            var key = _yunWuKeyTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(providerCode))
            {
                MessageBox.Show(this, "请先填写 Provider Code。", "输入不完整", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                MessageBox.Show(this, "请先填写视频中转 API 的基础地址。", "输入不完整", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
            {
                MessageBox.Show(this, "视频中转 API 地址格式不正确。", "地址错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ModelConfig.UpsertRelayApi(
                _settings,
                providerCode,
                string.IsNullOrWhiteSpace(providerName) ? providerCode : providerName,
                baseUrl,
                key,
                true);
            ModelConfig.Save(_settings);
            ReloadSettingsAndRefreshViews();
            MessageBox.Show(this, "视频中转 API 设置已保存。", "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async System.Threading.Tasks.Task TestRelayApiSettingsAsync()
        {
            if (_relayProviderCodeTextBox == null || _yunWuBaseUrlTextBox == null || _yunWuKeyTextBox == null || _testRelayApiButton == null)
            {
                return;
            }

            var providerCode = _relayProviderCodeTextBox.Text.Trim();
            var baseUrl = _yunWuBaseUrlTextBox.Text.Trim().TrimEnd('/');
            var key = _yunWuKeyTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(providerCode) || string.IsNullOrWhiteSpace(baseUrl))
            {
                MessageBox.Show(this, "请先填写 Provider Code 和基础地址。", "输入不完整", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var rootUri))
            {
                MessageBox.Show(this, "基础地址格式不正确。", "地址错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var candidates = new[]
            {
                new Uri(rootUri, "v1/models"),
                new Uri(rootUri, "models"),
                rootUri
            }.Distinct().ToList();

            _testRelayApiButton.Enabled = false;
            try
            {
                Exception? lastError = null;
                foreach (var candidate in candidates)
                {
                    try
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, candidate);
                        if (!string.IsNullOrWhiteSpace(key))
                        {
                            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                        }

                        using var response = await RelayHttpClient.SendAsync(request);
                        var body = await response.Content.ReadAsStringAsync();
                        if (!response.IsSuccessStatusCode)
                        {
                            lastError = new InvalidOperationException($"{(int)response.StatusCode} {response.ReasonPhrase} {body}".Trim());
                            continue;
                        }

                        var successText = "连接测试成功。";
                        try
                        {
                            using var document = JsonDocument.Parse(body);
                            JsonElement listElement;
                            if (document.RootElement.TryGetProperty("data", out listElement) && listElement.ValueKind == JsonValueKind.Array)
                            {
                                successText = $"连接测试成功，返回 {listElement.GetArrayLength()} 个模型。";
                            }
                            else if (document.RootElement.TryGetProperty("models", out listElement) && listElement.ValueKind == JsonValueKind.Array)
                            {
                                successText = $"连接测试成功，返回 {listElement.GetArrayLength()} 个模型。";
                            }
                        }
                        catch
                        {
                        }

                        MessageBox.Show(this, $"{providerCode}：{successText}", "测试成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                    }
                }

                MessageBox.Show(this, $"连接测试失败：{lastError?.Message ?? "未知错误"}", "测试失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _testRelayApiButton.Enabled = true;
            }
        }

        private void RefreshNodeDefaultSelections()
        {
            if (_nodeDefaultModelBoxes.Count == 0)
            {
                return;
            }

            _syncingNodeDefaults = true;

            foreach (var pair in _nodeDefaultModelBoxes)
            {
                var nodeType = pair.Key;
                var comboBox = pair.Value;
                var entries = new[]
                {
                    new ModelPickerItem(string.Empty, "跟随分类默认模型"),
                }
                .Concat(ModelConfig.GetModelsForNodeType(_settings, nodeType)
                    .Select(model => new ModelPickerItem(ModelConfig.GetModelSelector(model), $"[{ModelConfig.GetModelSourceDisplayName(model)}] {model.Name} ({model.Id})")))
                .ToList();

                comboBox.DisplayMember = nameof(ModelPickerItem.DisplayName);
                comboBox.ValueMember = nameof(ModelPickerItem.ModelId);
                comboBox.DataSource = entries;

                var nodeDefault = ModelConfig.GetDefaultModelForNodeType(_settings, nodeType);
                var categoryDefault = ModelConfig.GetCategoryDefaultModel(_settings, nodeType);
                comboBox.SelectedValue = string.Equals(nodeDefault, categoryDefault, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : nodeDefault;
            }

            _syncingNodeDefaults = false;
        }

        private void NodeDefaultModelChanged(string nodeType, ComboBox comboBox)
        {
            if (_syncingNodeDefaults || comboBox.SelectedItem is not ModelPickerItem item)
            {
                return;
            }

            var effectiveModelId = string.IsNullOrWhiteSpace(item.ModelId)
                ? ModelConfig.GetCategoryDefaultModel(_settings, nodeType)
                : item.ModelId;
            ModelConfig.SetDefaultModelForNodeType(_settings, nodeType, effectiveModelId);
            ModelConfig.Save(_settings);
        }

        private void btnAddModel_Click(object sender, EventArgs e)
        {
            ShowModelEditor();
        }

        private void btnDeleteModel_Click(object sender, EventArgs e)
        {
            if (listModels.SelectedItems.Count == 0)
            {
                return;
            }

            if (listModels.SelectedItems[0].Tag is not ModelInfo model)
            {
                return;
            }

            var result = MessageBox.Show(this, $"确认删除模型 \"{model.Name}\" 吗？", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
            {
                return;
            }

            var modelSelector = ModelConfig.GetModelSelector(model);
            _settings.Models.RemoveAll(item => ModelConfig.MatchesModelSelector(item, modelSelector));
            if (string.Equals(_settings.SelectedTextModel, modelSelector, StringComparison.OrdinalIgnoreCase))
            {
                _settings.SelectedTextModel = string.Empty;
            }

            if (string.Equals(_settings.SelectedImagePromptTextModel, modelSelector, StringComparison.OrdinalIgnoreCase))
            {
                _settings.SelectedImagePromptTextModel = string.Empty;
            }

            if (string.Equals(_settings.SelectedImageModel, modelSelector, StringComparison.OrdinalIgnoreCase))
            {
                _settings.SelectedImageModel = string.Empty;
            }

            if (string.Equals(_settings.SelectedVideoModel, modelSelector, StringComparison.OrdinalIgnoreCase))
            {
                _settings.SelectedVideoModel = string.Empty;
            }

            foreach (var nodeType in _settings.DefaultNodeModels.Keys.ToList())
            {
                if (string.Equals(_settings.DefaultNodeModels[nodeType], modelSelector, StringComparison.OrdinalIgnoreCase))
                {
                    _settings.DefaultNodeModels[nodeType] = string.Empty;
                }
            }

            ModelConfig.Save(_settings);
            ReloadSettingsAndRefreshViews();
        }

        private void ShowModelEditor(ModelInfo? existingModel = null)
        {
            using var dialog = new AddModelForm(existingModel);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var model = dialog.ModelInfo;
            if (model == null)
            {
                return;
            }

            if (ModelConfig.LocalOnlyMode)
            {
                if (!ModelConfig.IsLocalEndpointUrl(model.Url))
                {
                    MessageBox.Show(this, "本地版只允许添加本机或局域网模型地址。", "本地模型地址无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                model.Source = ModelEndpointSource.Local;
            }

            if (existingModel == null)
            {
                if (ModelConfig.HasConflictingModelDefinition(_settings.Models, model))
                {
                    MessageBox.Show(this, "模型 ID 已存在，请使用不同的 ID。", "重复 ID", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _settings.Models.Add(model);
            }
            else
            {
                if (ModelConfig.HasConflictingModelDefinition(_settings.Models, model, existingModel))
                {
                    MessageBox.Show(this, "模型 ID 已存在，请使用不同的 ID。", "重复 ID", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                existingModel.ConfigId = model.ConfigId;
                existingModel.Id = model.Id;
                existingModel.Name = model.Name;
                existingModel.WorkflowJson = model.WorkflowJson;
                existingModel.Url = model.Url;
                existingModel.Key = model.Key;
                existingModel.Category = model.Category;
                existingModel.Source = model.Source;
            }

            ModelConfig.Save(_settings);
            ReloadSettingsAndRefreshViews(ModelConfig.GetModelSelector(model));
        }

        private ListViewGroup GetModelGroup(ModelInfo model)
        {
            return ModelConfig.GetModelSource(model) switch
            {
                ModelEndpointSource.Local => _localModelsGroup,
                ModelEndpointSource.Cloud => _cloudModelsGroup,
                _ => _unknownModelsGroup,
            };
        }

        private static string FormatModelSelectionSummary(ModelInfo? model, string fallbackId)
        {
            if (model == null)
            {
                return string.IsNullOrWhiteSpace(fallbackId) ? "未选择" : fallbackId;
            }

            return $"[{ModelConfig.GetModelSourceDisplayName(model)}] {model.Name}";
        }

        private void btnSetTextModel_Click(object sender, EventArgs e)
        {
            if (listModels.SelectedItems.Count == 0)
            {
                return;
            }

            if (listModels.SelectedItems[0].Tag is not ModelInfo model)
            {
                return;
            }

            if (model.Category != ModelCategory.Text)
            {
                MessageBox.Show(this, "请选择文本模型。", "类别不匹配", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _settings.SelectedTextModel = ModelConfig.GetModelSelector(model);
            ModelConfig.Save(_settings);
            ReloadSettingsAndRefreshViews(ModelConfig.GetModelSelector(model));
        }

        private void btnSetImageModel_Click(object sender, EventArgs e)
        {
            if (listModels.SelectedItems.Count == 0)
            {
                return;
            }

            if (listModels.SelectedItems[0].Tag is not ModelInfo model)
            {
                return;
            }

            if (model.Category != ModelCategory.Image)
            {
                MessageBox.Show(this, "请选择图片模型。", "类别不匹配", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _settings.SelectedImageModel = ModelConfig.GetModelSelector(model);
            ModelConfig.Save(_settings);
            ReloadSettingsAndRefreshViews(ModelConfig.GetModelSelector(model));
        }

        private void btnSetImagePromptTextModel_Click(object sender, EventArgs e)
        {
            if (listModels.SelectedItems.Count == 0)
            {
                return;
            }

            if (listModels.SelectedItems[0].Tag is not ModelInfo model)
            {
                return;
            }

            if (model.Category != ModelCategory.Text)
            {
                MessageBox.Show(this, "请选择文本模型。", "类别不匹配", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _settings.SelectedImagePromptTextModel = ModelConfig.GetModelSelector(model);
            ModelConfig.Save(_settings);
            ReloadSettingsAndRefreshViews(ModelConfig.GetModelSelector(model));
        }

        private void btnSetVideoModel_Click(object sender, EventArgs e)
        {
            if (listModels.SelectedItems.Count == 0)
            {
                return;
            }

            if (listModels.SelectedItems[0].Tag is not ModelInfo model)
            {
                return;
            }

            if (model.Category != ModelCategory.Video)
            {
                MessageBox.Show(this, "请选择视频模型。", "类别不匹配", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _settings.SelectedVideoModel = ModelConfig.GetModelSelector(model);
            ModelConfig.Save(_settings);
            ReloadSettingsAndRefreshViews(ModelConfig.GetModelSelector(model));
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
        }
    }
}
