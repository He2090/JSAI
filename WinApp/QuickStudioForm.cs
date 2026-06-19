using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public sealed class QuickStudioForm : Form
    {
        private readonly QuickStudioMode _mode;
        private readonly WorkflowRuntimeService _runtimeService = new();

        private readonly Label _headlineLabel = new();
        private readonly TextBox _promptTextBox = new();
        private readonly Panel _referencePanel = new();
        private readonly PictureBox _referencePreview = new();
        private readonly Label _referencePathLabel = new();
        private readonly Button _pickReferenceButton = new();
        private readonly Button _generateButton = new();
        private readonly Label _statusLabel = new();

        private readonly PictureBox _resultPreview = new();
        private readonly TextBox _artifactPathTextBox = new();
        private readonly RichTextBox _positivePromptBox = new();
        private readonly RichTextBox _negativePromptBox = new();
        private readonly Button _openArtifactButton = new();
        private readonly Button _openFolderButton = new();

        private string _referenceImagePath = string.Empty;
        private string _generatedArtifactPath = string.Empty;

        public QuickStudioForm(QuickStudioMode mode)
        {
            _mode = mode;

            Text = GetModeTitle(mode);
            StartPosition = FormStartPosition.CenterParent;
            WindowState = FormWindowState.Normal;
            Size = new Size(1180, 820);
            MinimumSize = new Size(1080, 760);
            BackColor = Color.FromArgb(15, 18, 28);
            ForeColor = Color.White;

            BuildLayout();
            RefreshHeader();
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(18),
                BackColor = BackColor,
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54F));

            root.Controls.Add(BuildEditorPanel(), 0, 0);
            root.Controls.Add(BuildResultPanel(), 1, 0);
            Controls.Add(root);
        }

        private Control BuildEditorPanel()
        {
            var panel = CreateCardPanel();

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 58,
                BackColor = Color.Transparent,
                Padding = new Padding(14, 0, 14, 0),
            };
            _headlineLabel.Dock = DockStyle.Fill;
            _headlineLabel.Font = new Font("Microsoft YaHei", 14F, FontStyle.Bold, GraphicsUnit.Point);
            _headlineLabel.ForeColor = Color.White;
            _headlineLabel.TextAlign = ContentAlignment.MiddleLeft;
            header.Controls.Add(_headlineLabel);

            var promptLabel = CreateSectionLabel("提示词");
            _promptTextBox.Multiline = true;
            _promptTextBox.Dock = DockStyle.Top;
            _promptTextBox.Height = 220;
            _promptTextBox.BorderStyle = BorderStyle.FixedSingle;
            _promptTextBox.Font = new Font("Microsoft YaHei", 11F, FontStyle.Regular, GraphicsUnit.Point);
            _promptTextBox.BackColor = Color.FromArgb(23, 28, 40);
            _promptTextBox.ForeColor = Color.White;
            _promptTextBox.ScrollBars = ScrollBars.Vertical;

            _referencePanel.Dock = DockStyle.Top;
            _referencePanel.Height = _mode == QuickStudioMode.TextImageToVideo ? 210 : 0;
            _referencePanel.Visible = _mode == QuickStudioMode.TextImageToVideo;
            _referencePanel.BackColor = Color.Transparent;

            if (_mode == QuickStudioMode.TextImageToVideo)
            {
                _pickReferenceButton.Text = "选择参考图";
                _pickReferenceButton.Width = 126;
                _pickReferenceButton.Height = 34;
                _pickReferenceButton.FlatStyle = FlatStyle.Flat;
                _pickReferenceButton.BackColor = Color.FromArgb(56, 74, 112);
                _pickReferenceButton.ForeColor = Color.White;
                _pickReferenceButton.FlatAppearance.BorderSize = 0;
                _pickReferenceButton.Margin = new Padding(0, 0, 0, 10);
                _pickReferenceButton.Click += (_, _) => PickReferenceImage();

                _referencePreview.SizeMode = PictureBoxSizeMode.Zoom;
                _referencePreview.BorderStyle = BorderStyle.FixedSingle;
                _referencePreview.BackColor = Color.FromArgb(23, 28, 40);
                _referencePreview.Width = 160;
                _referencePreview.Height = 120;

                _referencePathLabel.AutoSize = false;
                _referencePathLabel.Width = 280;
                _referencePathLabel.Height = 120;
                _referencePathLabel.ForeColor = Color.FromArgb(180, 191, 210);
                _referencePathLabel.TextAlign = ContentAlignment.MiddleLeft;
                _referencePathLabel.Text = "尚未选择参考图。";

                var referenceRow = new FlowLayoutPanel
                {
                    Dock = DockStyle.Top,
                    Height = 132,
                    WrapContents = false,
                    FlowDirection = FlowDirection.LeftToRight,
                    BackColor = Color.Transparent,
                };
                referenceRow.Controls.Add(_referencePreview);
                referenceRow.Controls.Add(_referencePathLabel);

                _referencePanel.Controls.Add(referenceRow);
                _referencePanel.Controls.Add(_pickReferenceButton);
                _referencePanel.Controls.Add(CreateSectionLabel("参考图片"));
            }

            _generateButton.Dock = DockStyle.Bottom;
            _generateButton.Height = 50;
            _generateButton.FlatStyle = FlatStyle.Flat;
            _generateButton.FlatAppearance.BorderSize = 0;
            _generateButton.BackColor = Color.FromArgb(255, 122, 0);
            _generateButton.ForeColor = Color.Black;
            _generateButton.Font = new Font("Microsoft YaHei", 11F, FontStyle.Bold, GraphicsUnit.Point);
            _generateButton.Text = GetGenerateButtonText(_mode);
            _generateButton.Click += async (_, _) => await GenerateAsync();

            _statusLabel.Dock = DockStyle.Bottom;
            _statusLabel.Height = 34;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.ForeColor = Color.FromArgb(175, 186, 206);
            _statusLabel.Text = "准备就绪。";

            panel.Controls.Add(_generateButton);
            panel.Controls.Add(_statusLabel);
            panel.Controls.Add(_referencePanel);
            panel.Controls.Add(_promptTextBox);
            panel.Controls.Add(promptLabel);
            panel.Controls.Add(header);
            return panel;
        }

        private Control BuildResultPanel()
        {
            var panel = CreateCardPanel();

            var header = CreateSectionLabel("生成结果");
            header.Dock = DockStyle.Top;
            header.Height = 52;
            header.Font = new Font("Microsoft YaHei", 13F, FontStyle.Bold, GraphicsUnit.Point);

            _resultPreview.Dock = DockStyle.Top;
            _resultPreview.Height = 300;
            _resultPreview.SizeMode = PictureBoxSizeMode.Zoom;
            _resultPreview.BorderStyle = BorderStyle.FixedSingle;
            _resultPreview.BackColor = Color.FromArgb(23, 28, 40);

            var actionBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 42,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 8, 0, 0),
            };

            ConfigureMiniButton(_openArtifactButton, "打开文件", (_, _) => OpenArtifact());
            ConfigureMiniButton(_openFolderButton, "打开目录", (_, _) => OpenArtifactFolder());
            actionBar.Controls.Add(_openArtifactButton);
            actionBar.Controls.Add(_openFolderButton);

            _artifactPathTextBox.Dock = DockStyle.Top;
            _artifactPathTextBox.Height = 28;
            _artifactPathTextBox.ReadOnly = true;
            _artifactPathTextBox.BorderStyle = BorderStyle.FixedSingle;
            _artifactPathTextBox.BackColor = Color.FromArgb(23, 28, 40);
            _artifactPathTextBox.ForeColor = Color.FromArgb(215, 223, 240);

            var promptLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.Transparent,
            };
            promptLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            promptLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            promptLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            promptLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            _positivePromptBox.Dock = DockStyle.Fill;
            _positivePromptBox.ReadOnly = true;
            _positivePromptBox.BackColor = Color.FromArgb(23, 28, 40);
            _positivePromptBox.ForeColor = Color.White;
            _positivePromptBox.BorderStyle = BorderStyle.FixedSingle;
            _positivePromptBox.Font = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point);

            _negativePromptBox.Dock = DockStyle.Fill;
            _negativePromptBox.ReadOnly = true;
            _negativePromptBox.BackColor = Color.FromArgb(23, 28, 40);
            _negativePromptBox.ForeColor = Color.White;
            _negativePromptBox.BorderStyle = BorderStyle.FixedSingle;
            _negativePromptBox.Font = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point);

            promptLayout.Controls.Add(CreateSectionLabel("正向提示词"), 0, 0);
            promptLayout.Controls.Add(_positivePromptBox, 0, 1);
            promptLayout.Controls.Add(CreateSectionLabel("反向提示词"), 0, 2);
            promptLayout.Controls.Add(_negativePromptBox, 0, 3);

            panel.Controls.Add(promptLayout);
            panel.Controls.Add(_artifactPathTextBox);
            panel.Controls.Add(actionBar);
            panel.Controls.Add(_resultPreview);
            panel.Controls.Add(header);
            return panel;
        }

        private async System.Threading.Tasks.Task GenerateAsync()
        {
            var userPrompt = _promptTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(userPrompt))
            {
                MessageBox.Show(this, "请输入提示词。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_mode == QuickStudioMode.TextImageToVideo && string.IsNullOrWhiteSpace(_referenceImagePath))
            {
                MessageBox.Show(this, "请先选择参考图。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var settings = ModelConfig.Load();
            _generateButton.Enabled = false;
            _statusLabel.Text = "正在生成，请稍候...";

            try
            {
                DirectGenerationResult result = _mode switch
                {
                    QuickStudioMode.TextToImage => await GenerateTextToImageAsync(settings, userPrompt),
                    QuickStudioMode.TextToVideo => await GenerateTextToVideoAsync(settings, userPrompt, null),
                    QuickStudioMode.TextImageToVideo => await GenerateTextToVideoAsync(settings, userPrompt, _referenceImagePath),
                    _ => throw new InvalidOperationException("不支持的模式。"),
                };

                BindResult(result);
                _statusLabel.Text = result.Description;
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"生成失败：{ex.Message}";
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _generateButton.Enabled = true;
            }
        }

        private async System.Threading.Tasks.Task<DirectGenerationResult> GenerateTextToImageAsync(ModelSettings settings, string userPrompt)
        {
            var localModel = ResolvePreferredLocalMediaModel(settings, ModelCategory.Image);
            var cloudModel = ResolvePreferredCloudImageModel(settings);
            var executionModel = localModel ?? cloudModel ?? throw new InvalidOperationException("未配置可用的图片模型。");
            var promptModel = ResolvePromptModel(settings, isImageWorkflow: true, preferLocalText: ModelConfig.IsLocalEndpointUrl(executionModel.Url));

            try
            {
                return await _runtimeService.GenerateDirectImageAsync(
                    userPrompt,
                    "single",
                    promptModel,
                    executionModel,
                    optimizeForCloud: !ModelConfig.IsLocalEndpointUrl(executionModel.Url),
                    width: 2048,
                    height: 2048,
                    cancellationToken: default);
            }
            catch (Exception ex) when (localModel != null &&
                                       cloudModel != null &&
                                       !string.Equals(localModel.Id, cloudModel.Id, StringComparison.OrdinalIgnoreCase))
            {
                var retry = MessageBox.Show(
                    this,
                    $"本地图片接口调用失败：{ex.Message}\n\n是否改用云端图片模型“{cloudModel.Name}”继续生成？",
                    "本地图片不可用",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (retry != DialogResult.Yes)
                {
                    throw;
                }

                var cloudPromptModel = ResolvePromptModel(settings, isImageWorkflow: true, preferLocalText: false);
                return await _runtimeService.GenerateDirectImageAsync(
                    userPrompt,
                    "single",
                    cloudPromptModel,
                    cloudModel,
                    optimizeForCloud: true,
                    width: 2048,
                    height: 2048,
                    cancellationToken: default);
            }
        }

        private async System.Threading.Tasks.Task<DirectGenerationResult> GenerateTextToVideoAsync(ModelSettings settings, string userPrompt, string? referenceImagePath)
        {
            var localModel = ResolvePreferredLocalMediaModel(settings, ModelCategory.Video);
            var cloudModel = ResolvePreferredCloudVideoModel(settings);
            var executionModel = localModel ?? cloudModel ?? throw new InvalidOperationException("未配置可用的视频模型或视频中转 API。");
            var promptModel = ResolvePromptModel(settings, isImageWorkflow: false, preferLocalText: ModelConfig.IsLocalEndpointUrl(executionModel.Url));

            try
            {
                return await _runtimeService.GenerateDirectVideoAsync(
                    userPrompt,
                    referenceImagePath,
                    promptModel,
                    executionModel,
                    optimizeForCloud: !ModelConfig.IsLocalEndpointUrl(executionModel.Url),
                    aspectRatio: "16:9",
                    durationSeconds: 5,
                    quality: "高清",
                    cancellationToken: default);
            }
            catch (Exception ex) when (localModel != null &&
                                       cloudModel != null &&
                                       !string.Equals(localModel.Id, cloudModel.Id, StringComparison.OrdinalIgnoreCase))
            {
                var retry = MessageBox.Show(
                    this,
                    $"本地视频接口调用失败：{ex.Message}\n\n是否改用云端视频模型“{cloudModel.Name}”继续生成？",
                    "本地视频不可用",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (retry != DialogResult.Yes)
                {
                    throw;
                }

                var cloudPromptModel = ResolvePromptModel(settings, isImageWorkflow: false, preferLocalText: false);
                return await _runtimeService.GenerateDirectVideoAsync(
                    userPrompt,
                    referenceImagePath,
                    cloudPromptModel,
                    cloudModel,
                    optimizeForCloud: true,
                    aspectRatio: "16:9",
                    durationSeconds: 5,
                    quality: "高清",
                    cancellationToken: default);
            }
        }

        private void BindResult(DirectGenerationResult result)
        {
            _generatedArtifactPath = result.ArtifactPath ?? string.Empty;
            _artifactPathTextBox.Text = _generatedArtifactPath;
            _positivePromptBox.Text = result.PositivePrompt ?? string.Empty;
            _negativePromptBox.Text = result.NegativePrompt ?? string.Empty;
            _openArtifactButton.Enabled = File.Exists(_generatedArtifactPath);
            _openFolderButton.Enabled = File.Exists(_generatedArtifactPath);

            if (_mode == QuickStudioMode.TextToImage && File.Exists(_generatedArtifactPath))
            {
                using var bitmap = new Bitmap(_generatedArtifactPath);
                _resultPreview.Image?.Dispose();
                _resultPreview.Image = new Bitmap(bitmap);
            }
            else
            {
                _resultPreview.Image?.Dispose();
                _resultPreview.Image = null;
            }
        }

        private void RefreshHeader()
        {
            var settings = ModelConfig.Load();
            var displayModel = _mode switch
            {
                QuickStudioMode.TextToImage => ResolvePreferredLocalMediaModel(settings, ModelCategory.Image) ??
                                               ResolvePreferredCloudImageModel(settings),
                QuickStudioMode.TextToVideo => ResolvePreferredLocalMediaModel(settings, ModelCategory.Video) ??
                                               ResolvePreferredCloudVideoModel(settings),
                QuickStudioMode.TextImageToVideo => ResolvePreferredLocalMediaModel(settings, ModelCategory.Video) ??
                                                    ResolvePreferredCloudVideoModel(settings),
                _ => null,
            };

            _headlineLabel.Text = $"{displayModel?.Name ?? "未配置模型"} 创作模式";
        }

        private void PickReferenceImage()
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.webp|全部文件|*.*",
                Title = "选择参考图",
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            _referenceImagePath = dialog.FileName;
            _referencePathLabel.Text = _referenceImagePath;
            using var bitmap = new Bitmap(_referenceImagePath);
            _referencePreview.Image?.Dispose();
            _referencePreview.Image = new Bitmap(bitmap);
        }

        private static ModelInfo ResolvePromptModel(ModelSettings settings, bool isImageWorkflow, bool preferLocalText)
        {
            var preferredId = isImageWorkflow
                ? ModelConfig.GetImagePromptTextModelId(settings)
                : settings.SelectedTextModel;

            var preferred = settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Text &&
                ModelConfig.MatchesModelSelector(model, preferredId));

            if (preferLocalText)
            {
                var local = settings.Models.FirstOrDefault(model =>
                    model.Category == ModelCategory.Text &&
                    ModelConfig.IsLocalEndpointUrl(model.Url));
                if (local != null)
                {
                    return local;
                }
            }

            return preferred ?? settings.Models.First(model => model.Category == ModelCategory.Text);
        }

        private static ModelInfo? ResolvePreferredLocalMediaModel(ModelSettings settings, ModelCategory category)
        {
            if (category == ModelCategory.Image)
            {
                return ModelConfig.GetPreferredLocalImageModel(settings);
            }

            return settings.Models.FirstOrDefault(model =>
                model.Category == category &&
                ModelConfig.IsLocalEndpointUrl(model.Url));
        }

        private static ModelInfo? ResolvePreferredCloudImageModel(ModelSettings settings)
        {
            return ModelConfig.GetPreferredCloudImageModel(settings);
        }

        private static ModelInfo? ResolvePreferredCloudVideoModel(ModelSettings settings)
        {
            var model = settings.Models.FirstOrDefault(item =>
                item.Category == ModelCategory.Video &&
                !ModelConfig.IsLocalEndpointUrl(item.Url));
            if (model != null)
            {
                return ModelConfig.ApplyRelayOverrides(settings, model);
            }

            var relay = ModelConfig.GetRelayApi(settings, "yunwuapi");
            if (relay == null || !relay.Enabled || string.IsNullOrWhiteSpace(relay.BaseUrl))
            {
                return null;
            }

            return new ModelInfo
            {
                Id = "ray-v2",
                Name = relay.Name,
                Url = relay.BaseUrl,
                Key = relay.Key,
                Category = ModelCategory.Video,
            };
        }

        private void OpenArtifact()
        {
            if (!File.Exists(_generatedArtifactPath))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = _generatedArtifactPath,
                UseShellExecute = true,
            });
        }

        private void OpenArtifactFolder()
        {
            if (!File.Exists(_generatedArtifactPath))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{_generatedArtifactPath}\"",
                UseShellExecute = true,
            });
        }

        private static string GetModeTitle(QuickStudioMode mode)
        {
            return mode switch
            {
                QuickStudioMode.TextToImage => "文生图",
                QuickStudioMode.TextToVideo => "文生视频",
                QuickStudioMode.TextImageToVideo => "文图生视频",
                _ => "快捷创作",
            };
        }

        private static string GetGenerateButtonText(QuickStudioMode mode)
        {
            return mode switch
            {
                QuickStudioMode.TextToImage => "生成图片",
                QuickStudioMode.TextToVideo => "生成视频",
                QuickStudioMode.TextImageToVideo => "生成图生视频",
                _ => "开始生成",
            };
        }

        private static Panel CreateCardPanel()
        {
            return new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14),
                Margin = new Padding(0, 0, 12, 0),
                BackColor = Color.FromArgb(24, 28, 40),
            };
        }

        private static Label CreateSectionLabel(string text)
        {
            return new Label
            {
                Dock = DockStyle.Top,
                Height = 32,
                Text = text,
                Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleLeft,
            };
        }

        private static void ConfigureMiniButton(Button button, string text, EventHandler onClick)
        {
            button.Text = text;
            button.Width = 96;
            button.Height = 30;
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Color.FromArgb(44, 52, 70);
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderSize = 0;
            button.Margin = new Padding(0, 0, 10, 0);
            button.Enabled = false;
            button.Click += onClick;
        }
    }
}
