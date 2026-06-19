using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace JSAI.WinApp
{
    public enum ModelCategory
    {
        Text,
        Image,
        Video
    }

    public enum ModelEndpointSource
    {
        Unknown,
        Local,
        Cloud
    }

    public class ModelInfo
    {
        public string ConfigId { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string WorkflowJson { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public ModelCategory Category { get; set; }
        public ModelEndpointSource Source { get; set; }
    }

    public class RelayApiInfo
    {
        public string ProviderCode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
    }

    public class ModelSettings
    {
        public List<ModelInfo> Models { get; set; } = new();
        public List<RelayApiInfo> RelayApis { get; set; } = new();
        public string SelectedTextModel { get; set; } = string.Empty;
        public string SelectedImagePromptTextModel { get; set; } = string.Empty;
        public string SelectedImageModel { get; set; } = string.Empty;
        public string SelectedVideoModel { get; set; } = string.Empty;
        public Dictionary<string, string> DefaultNodeModels { get; set; } = new();
    }

    public static class ModelConfig
    {
        public const bool LocalOnlyMode = true;
        private const int CryptProtectUiForbidden = 0x1;
        private const string EncryptedKeyPrefix = "dpapi:";
        public const string RelayVideoModeModelId = "__relay_video_mode__";
        public const string RelayVideoModeModelName = "云端视频模式 / Cloud Relay Video";
        public const string DefaultComfyUiImageModelId = "dreamshaper_8.safetensors";
        public const string DefaultComfyUiImageModelName = "ComfyUI 本地生图";
        public const string DefaultComfyUiVideoModelId = "comfyui-video-local";
        public const string DefaultComfyUiVideoModelName = "ComfyUI Local Video";
        public const string DefaultComfyUiVideoWorkflowJson = "ImgandTextTovideo1.0.json";
        public const string DefaultComfyUiBaseUrl = "http://127.0.0.1:8188";
        public const string DefaultYunWuStoryboardImageModelId = "gemini-3-pro-image-preview";
        public const string DefaultYunWuStoryboardImageModelName = "云雾AI 分镜图";
        public const string DefaultYunWuBaseUrl = "https://api.yunwu.ai";
        public const string DefaultGeminiTextModelId = "gemini-2.5-flash";
        public const string DefaultGeminiTextModelName = "Google Gemini 文本";
        public const string DefaultGeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai";
 
        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "model-settings.json");
        private static readonly byte[] KeyEntropy = Encoding.UTF8.GetBytes("JSAI.WinApp.ModelKey.v1");

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(
            ref DataBlob pDataIn,
            string? szDataDescr,
            ref DataBlob pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            int dwFlags,
            out DataBlob pDataOut);

        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(
            ref DataBlob pDataIn,
            StringBuilder? ppszDataDescr,
            ref DataBlob pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            int dwFlags,
            out DataBlob pDataOut);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr hMem);

        public static ModelSettings Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    var defaultSettings = CreateDefault();
                    Save(defaultSettings);
                    return defaultSettings;
                }

                var json = File.ReadAllText(ConfigPath);
                var settings = JsonSerializer.Deserialize<ModelSettings>(json);
                if (settings == null)
                {
                    return CreateDefault();
                }

                var needsResave = DecryptStoredKeys(settings);
                var beforeDefaults = JsonSerializer.Serialize(settings);
                EnsureDefaults(settings);
                if (needsResave || !string.Equals(beforeDefaults, JsonSerializer.Serialize(settings), StringComparison.Ordinal))
                {
                    Save(settings);
                }

                return settings;
            }
            catch
            {
                return CreateDefault();
            }
        }

        public static void Save(ModelSettings settings)
        {
            try
            {
                EnsureDefaults(settings);
                var persistedSettings = CloneForPersistence(settings);
                EncryptStoredKeys(persistedSettings);
                var json = JsonSerializer.Serialize(persistedSettings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch
            {
                // ignore
            }
        }

        private static ModelSettings CreateDefault()
        {
            var settings = new ModelSettings
            {
                Models = new List<ModelInfo>
                {
                    new() { Id = "qwen-local", Name = "Ollama Local Text", Url = "http://127.0.0.1:11434/v1", Key = "", Category = ModelCategory.Text, Source = ModelEndpointSource.Local },
                    new() { Id = DefaultComfyUiImageModelId, Name = DefaultComfyUiImageModelName, Url = DefaultComfyUiBaseUrl, Key = "", Category = ModelCategory.Image, Source = ModelEndpointSource.Local },
                    new() { Id = "sd-1.5", Name = "Stable Diffusion 1.5", Url = "http://localhost:7860", Key = "", Category = ModelCategory.Image, Source = ModelEndpointSource.Local },
                    new() { Id = "sd-2.1", Name = "Stable Diffusion 2.1", Url = "http://localhost:7860", Key = "", Category = ModelCategory.Image, Source = ModelEndpointSource.Local },
                    new() { Id = DefaultComfyUiVideoModelId, Name = DefaultComfyUiVideoModelName, WorkflowJson = DefaultComfyUiVideoWorkflowJson, Url = DefaultComfyUiBaseUrl, Key = "", Category = ModelCategory.Video, Source = ModelEndpointSource.Local },
                },
                SelectedTextModel = "qwen-local",
                SelectedImagePromptTextModel = "qwen-local",
                SelectedImageModel = DefaultComfyUiImageModelId,
                SelectedVideoModel = DefaultComfyUiVideoModelId,
            };

            EnsureDefaults(settings);
            return settings;
        }

        private static void EnsureDefaults(ModelSettings settings)
        {
            if (settings.Models == null || settings.Models.Count == 0)
            {
                settings.Models = CreateDefault().Models;
            }

            foreach (var model in settings.Models)
            {
                if (model.Source == ModelEndpointSource.Unknown)
                {
                    model.Source = InferModelSource(model.Url);
                }

                TryMigrateLegacyComfyUiWorkflowJson(model);
                model.WorkflowJson = NormalizeWorkflowJsonName(model.WorkflowJson, allowImplicitJsonExtension: true);
            }

            if (LocalOnlyMode)
            {
                settings.Models = settings.Models
                    .Where(IsLocalOnlyAllowedModel)
                    .ToList();
                EnsureLocalOnlyCategoryDefaults(settings);
                settings.RelayApis = new List<RelayApiInfo>();
            }

            EnsureModelConfigIds(settings);
            NormalizeStoredModelSelectors(settings);

            settings.RelayApis ??= new List<RelayApiInfo>();

            EnsureLocalComfyUiImageModel(settings);
            EnsureLocalComfyUiVideoModel(settings);
            if (!LocalOnlyMode)
            {
                EnsureYunWuStoryboardImageModel(settings);
                EnsureRelayApis(settings);
            }

            if (string.IsNullOrWhiteSpace(settings.SelectedTextModel))
            {
                settings.SelectedTextModel = GetModelSelector(settings.Models.FirstOrDefault(model => model.Category == ModelCategory.Text));
            }
            else if (settings.Models.All(model => model.Category != ModelCategory.Text || !MatchesModelSelector(model, settings.SelectedTextModel)))
            {
                settings.SelectedTextModel = GetModelSelector(settings.Models.FirstOrDefault(model => model.Category == ModelCategory.Text));
            }

            if (string.IsNullOrWhiteSpace(settings.SelectedImagePromptTextModel))
            {
                settings.SelectedImagePromptTextModel = settings.SelectedTextModel;
            }
            else if (settings.Models.All(model => model.Category != ModelCategory.Text || !MatchesModelSelector(model, settings.SelectedImagePromptTextModel)))
            {
                settings.SelectedImagePromptTextModel = settings.SelectedTextModel;
            }

            var selectedImageModel = FindModel(settings, ModelCategory.Image, settings.SelectedImageModel);
            if (string.IsNullOrWhiteSpace(settings.SelectedImageModel))
            {
                settings.SelectedImageModel = GetModelSelector(settings.Models.FirstOrDefault(model => model.Category == ModelCategory.Image));
            }
            else if (selectedImageModel == null)
            {
                settings.SelectedImageModel = GetModelSelector(settings.Models.FirstOrDefault(model => model.Category == ModelCategory.Image));
                selectedImageModel = FindModel(settings, ModelCategory.Image, settings.SelectedImageModel);
            }

            var preferredLocalImageModel = ResolvePreferredLocalImageModel(settings);
            if (preferredLocalImageModel != null &&
                (string.IsNullOrWhiteSpace(settings.SelectedImageModel) ||
                 string.Equals(selectedImageModel?.Id, DefaultYunWuStoryboardImageModelId, StringComparison.OrdinalIgnoreCase)))
            {
                settings.SelectedImageModel = GetModelSelector(preferredLocalImageModel);
            }

            if (string.IsNullOrWhiteSpace(settings.SelectedVideoModel))
            {
                settings.SelectedVideoModel = GetModelSelector(settings.Models.FirstOrDefault(model => model.Category == ModelCategory.Video));
            }
            else if (settings.Models.All(model => model.Category != ModelCategory.Video || !MatchesModelSelector(model, settings.SelectedVideoModel)))
            {
                settings.SelectedVideoModel = GetModelSelector(settings.Models.FirstOrDefault(model => model.Category == ModelCategory.Video));
            }

            settings.DefaultNodeModels ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            MigrateLegacyNodeModelDefaults(settings);

            foreach (var nodeType in WorkflowNodeCatalog.ConfigurableNodeTypes)
            {
                if (settings.DefaultNodeModels.TryGetValue(nodeType, out var storedModelId) &&
                    !string.IsNullOrWhiteSpace(storedModelId) &&
                    settings.Models.Any(model => MatchesModelSelector(model, storedModelId)))
                {
                    continue;
                }

                settings.DefaultNodeModels[nodeType] = GetCategoryDefaultModel(settings, nodeType);
            }

            settings.DefaultNodeModels[WorkflowNodeCatalog.StoryboardImage] = GetModelSelector(ResolveStoryboardImageWorkflowModel(settings))
                                                                              ?? settings.DefaultNodeModels[WorkflowNodeCatalog.StoryboardImage];
            NormalizeLocalFirstImageDefaults(settings, GetModelSelector(preferredLocalImageModel));
        }

        private static bool IsLocalOnlyAllowedModel(ModelInfo model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.Url))
            {
                return false;
            }

            if (!Uri.TryCreate(model.Url, UriKind.Absolute, out _))
            {
                return false;
            }
 
            model.Source = InferModelSource(model.Url);
            return true;
        }

        private static void EnsureLocalOnlyCategoryDefaults(ModelSettings settings)
        {
            settings.Models ??= new List<ModelInfo>();

            if (!settings.Models.Any(model => model.Category == ModelCategory.Text && IsLocalEndpointUrl(model.Url)))
            {
                settings.Models.Add(new ModelInfo
                {
                    Id = "qwen-local",
                    Name = "Ollama Local Text",
                    Url = "http://127.0.0.1:11434/v1",
                    Key = "",
                    Category = ModelCategory.Text,
                    Source = ModelEndpointSource.Local,
                });
           }
 
            if (!settings.Models.Any(model => model.Category == ModelCategory.Text && !IsLocalEndpointUrl(model.Url) && IsGeminiModelUrl(model.Url)))
            {
                settings.Models.Add(new ModelInfo
                {
                    Id = DefaultGeminiTextModelId,
                    Name = DefaultGeminiTextModelName,
                    Url = DefaultGeminiBaseUrl,
                    Key = "",
                    Category = ModelCategory.Text,
                    Source = ModelEndpointSource.Cloud,
                });
            }
 
           if (!settings.Models.Any(model => model.Category == ModelCategory.Image && IsLocalEndpointUrl(model.Url)))
            {
                settings.Models.Add(new ModelInfo
                {
                    Id = DefaultComfyUiImageModelId,
                    Name = DefaultComfyUiImageModelName,
                    Url = DefaultComfyUiBaseUrl,
                    Key = "",
                    Category = ModelCategory.Image,
                    Source = ModelEndpointSource.Local,
                });
            }

            if (!settings.Models.Any(model => model.Category == ModelCategory.Video && IsLocalEndpointUrl(model.Url)))
            {
                settings.Models.Add(new ModelInfo
                {
                    Id = DefaultComfyUiVideoModelId,
                    Name = DefaultComfyUiVideoModelName,
                    WorkflowJson = DefaultComfyUiVideoWorkflowJson,
                    Url = DefaultComfyUiBaseUrl,
                    Key = "",
                    Category = ModelCategory.Video,
                    Source = ModelEndpointSource.Local,
                });
            }
        }

        public static string CreateModelConfigId()
        {
            return "model_" + Guid.NewGuid().ToString("N");
        }

        public static string GetModelSelector(ModelInfo? model)
        {
            if (model == null)
            {
                return string.Empty;
            }

            return !string.IsNullOrWhiteSpace(model.ConfigId)
                ? model.ConfigId
                : model.Id;
        }

        public static bool MatchesModelSelector(ModelInfo? model, string? selector)
        {
            if (model == null || string.IsNullOrWhiteSpace(selector))
            {
                return false;
            }

            return string.Equals(model.ConfigId, selector, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(model.Id, selector, StringComparison.OrdinalIgnoreCase);
        }

        public static ModelInfo? FindModel(ModelSettings settings, ModelCategory category, string? selector)
        {
            return (settings.Models ?? new List<ModelInfo>()).FirstOrDefault(model =>
                model.Category == category &&
                MatchesModelSelector(model, selector));
        }

        public static bool HasConflictingModelDefinition(IEnumerable<ModelInfo> models, ModelInfo candidate, ModelInfo? excludingModel = null)
        {
            var normalizedWorkflow = ResolveComfyUiWorkflowJson(candidate);
            var normalizedUrl = NormalizeComparableUrl(candidate.Url);

            return models.Any(model =>
                !ReferenceEquals(model, excludingModel) &&
                !ReferenceEquals(model, candidate) &&
                model.Category == candidate.Category &&
                string.Equals(model.Id, candidate.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeComparableUrl(model.Url), normalizedUrl, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ResolveComfyUiWorkflowJson(model), normalizedWorkflow, StringComparison.OrdinalIgnoreCase));
        }

        private static void EnsureModelConfigIds(ModelSettings settings)
        {
            var usedConfigIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var model in settings.Models)
            {
                if (string.IsNullOrWhiteSpace(model.ConfigId) || !usedConfigIds.Add(model.ConfigId))
                {
                    model.ConfigId = CreateModelConfigId();
                    usedConfigIds.Add(model.ConfigId);
                }
            }
        }

        private static void NormalizeStoredModelSelectors(ModelSettings settings)
        {
            settings.SelectedTextModel = NormalizeStoredModelSelector(settings, ModelCategory.Text, settings.SelectedTextModel);
            settings.SelectedImagePromptTextModel = NormalizeStoredModelSelector(settings, ModelCategory.Text, settings.SelectedImagePromptTextModel);
            settings.SelectedImageModel = NormalizeStoredModelSelector(settings, ModelCategory.Image, settings.SelectedImageModel);
            settings.SelectedVideoModel = NormalizeStoredModelSelector(settings, ModelCategory.Video, settings.SelectedVideoModel);

            settings.DefaultNodeModels ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var nodeType in settings.DefaultNodeModels.Keys.ToList())
            {
                var category = WorkflowExecutor.GetModelCategory(nodeType);
                if (category == null)
                {
                    continue;
                }

                settings.DefaultNodeModels[nodeType] = NormalizeStoredModelSelector(settings, category.Value, settings.DefaultNodeModels[nodeType]);
            }
        }

        private static string NormalizeStoredModelSelector(ModelSettings settings, ModelCategory category, string selector)
        {
            if (string.IsNullOrWhiteSpace(selector))
            {
                return string.Empty;
            }

            var model = FindModel(settings, category, selector);
            return GetModelSelector(model);
        }

        private static string NormalizeComparableUrl(string? rawUrl)
        {
            return (rawUrl ?? string.Empty).Trim().TrimEnd('/');
        }

        private static void EnsureLocalComfyUiImageModel(ModelSettings settings)
        {
            var comfyImageModel = settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Image &&
                string.Equals(model.Url?.Trim().TrimEnd('/'), DefaultComfyUiBaseUrl, StringComparison.OrdinalIgnoreCase));

            if (comfyImageModel == null)
            {
                return;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(comfyImageModel.Name))
                {
                    comfyImageModel.Name = DefaultComfyUiImageModelName;
                }

                if (string.IsNullOrWhiteSpace(comfyImageModel.Id))
                {
                    comfyImageModel.Id = DefaultComfyUiImageModelId;
                }

                comfyImageModel.Url = DefaultComfyUiBaseUrl;
            }

            var hasUsableImageSelection = settings.Models.Any(model =>
                model.Category == ModelCategory.Image &&
                !string.IsNullOrWhiteSpace(model.Url) &&
                MatchesModelSelector(model, settings.SelectedImageModel));

            if (!hasUsableImageSelection)
            {
                settings.SelectedImageModel = GetModelSelector(settings.Models
                    .FirstOrDefault(model => model.Category == ModelCategory.Image && !string.IsNullOrWhiteSpace(model.Url)));
            }
        }

        private static void EnsureLocalComfyUiVideoModel(ModelSettings settings)
        {
            var comfyVideoModel = settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Video &&
                string.Equals(model.Url?.Trim().TrimEnd('/'), DefaultComfyUiBaseUrl, StringComparison.OrdinalIgnoreCase));

            if (comfyVideoModel == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(comfyVideoModel.Name))
            {
                comfyVideoModel.Name = DefaultComfyUiVideoModelName;
            }

            if (string.IsNullOrWhiteSpace(comfyVideoModel.Id))
            {
                comfyVideoModel.Id = DefaultComfyUiVideoModelId;
            }

            if (string.IsNullOrWhiteSpace(comfyVideoModel.WorkflowJson))
            {
                comfyVideoModel.WorkflowJson = DefaultComfyUiVideoWorkflowJson;
            }

            comfyVideoModel.Url = DefaultComfyUiBaseUrl;

            var hasUsableVideoSelection = settings.Models.Any(model =>
                model.Category == ModelCategory.Video &&
                !string.IsNullOrWhiteSpace(model.Url) &&
                MatchesModelSelector(model, settings.SelectedVideoModel));

            if (!hasUsableVideoSelection)
            {
                settings.SelectedVideoModel = GetModelSelector(settings.Models
                    .FirstOrDefault(model => model.Category == ModelCategory.Video && !string.IsNullOrWhiteSpace(model.Url)));
            }
        }

        private static void EnsureYunWuStoryboardImageModel(ModelSettings settings)
        {
            var fallbackKey = settings.Models
                .FirstOrDefault(model =>
                    !string.IsNullOrWhiteSpace(model.Key) &&
                    IsYunWuModelUrl(model.Url))
                ?.Key ?? string.Empty;

            var storyboardModel = settings.Models.FirstOrDefault(model =>
                    model.Category == ModelCategory.Image &&
                    string.Equals(model.Id, DefaultYunWuStoryboardImageModelId, StringComparison.OrdinalIgnoreCase))
                ?? settings.Models.FirstOrDefault(model =>
                    model.Category == ModelCategory.Image &&
                    IsYunWuModelUrl(model.Url) &&
                    string.Equals(model.Name, DefaultYunWuStoryboardImageModelName, StringComparison.OrdinalIgnoreCase));

            if (storyboardModel == null)
            {
                return;
            }

            storyboardModel.Category = ModelCategory.Image;
            if (string.IsNullOrWhiteSpace(storyboardModel.Id))
            {
                storyboardModel.Id = DefaultYunWuStoryboardImageModelId;
            }

            if (string.IsNullOrWhiteSpace(storyboardModel.Name))
            {
                storyboardModel.Name = DefaultYunWuStoryboardImageModelName;
            }

            storyboardModel.Url = NormalizeYunWuBaseUrl(storyboardModel.Url);

            if (string.IsNullOrWhiteSpace(storyboardModel.Key) && !string.IsNullOrWhiteSpace(fallbackKey))
            {
                storyboardModel.Key = fallbackKey;
            }
        }

        private static void EnsureRelayApis(ModelSettings settings)
        {
            settings.RelayApis ??= new List<RelayApiInfo>();

            var fallbackKey = settings.Models
                .FirstOrDefault(model =>
                    !string.IsNullOrWhiteSpace(model.Key) &&
                    IsYunWuModelUrl(model.Url))
                ?.Key ?? string.Empty;

            var yunWuRelay = settings.RelayApis.FirstOrDefault(relay =>
                string.Equals(relay.ProviderCode, "yunwuapi", StringComparison.OrdinalIgnoreCase));

            if (yunWuRelay == null)
            {
                yunWuRelay = new RelayApiInfo
                {
                    ProviderCode = "yunwuapi",
                    Name = "浜戦浘API",
                    BaseUrl = DefaultYunWuBaseUrl,
                    Key = fallbackKey,
                    Enabled = true,
                };
                settings.RelayApis.Add(yunWuRelay);
                return;
            }

            if (string.IsNullOrWhiteSpace(yunWuRelay.Name))
            {
                yunWuRelay.Name = "浜戦浘API";
            }

            if (string.IsNullOrWhiteSpace(yunWuRelay.BaseUrl) || IsYunWuModelUrl(yunWuRelay.BaseUrl))
            {
                yunWuRelay.BaseUrl = NormalizeYunWuBaseUrl(yunWuRelay.BaseUrl);
            }

            if (string.IsNullOrWhiteSpace(yunWuRelay.Key) && !string.IsNullOrWhiteSpace(fallbackKey))
            {
                yunWuRelay.Key = fallbackKey;
            }
        }

        private static void MigrateLegacyNodeModelDefaults(ModelSettings settings)
        {
            if (settings.DefaultNodeModels.TryGetValue(WorkflowNodeCatalog.LegacySceneDescription, out var legacyBreakdownModelId) &&
                !settings.DefaultNodeModels.ContainsKey(WorkflowNodeCatalog.StoryboardBreakdown))
            {
                settings.DefaultNodeModels[WorkflowNodeCatalog.StoryboardBreakdown] = legacyBreakdownModelId;
            }

            if (settings.DefaultNodeModels.TryGetValue(WorkflowNodeCatalog.LegacySceneView, out var legacyImageModelId) &&
                !settings.DefaultNodeModels.ContainsKey(WorkflowNodeCatalog.StoryboardImage))
            {
                settings.DefaultNodeModels[WorkflowNodeCatalog.StoryboardImage] = legacyImageModelId;
            }
        }

        public static string GetDefaultModelForNodeType(ModelSettings settings, string nodeType)
        {
            EnsureDefaults(settings);
            if (settings.DefaultNodeModels.TryGetValue(nodeType, out var modelId) &&
                !string.IsNullOrWhiteSpace(modelId))
            {
                return modelId;
            }

            return GetCategoryDefaultModel(settings, nodeType);
        }

        public static void SetDefaultModelForNodeType(ModelSettings settings, string nodeType, string modelId)
        {
            EnsureDefaults(settings);
            settings.DefaultNodeModels[nodeType] = string.IsNullOrWhiteSpace(modelId)
                ? GetCategoryDefaultModel(settings, nodeType)
                : modelId.Trim();
        }

        public static string GetCategoryDefaultModel(ModelSettings settings, string nodeType)
        {
            return WorkflowExecutor.GetModelCategory(nodeType) switch
            {
                ModelCategory.Text => settings.SelectedTextModel,
                ModelCategory.Image => settings.SelectedImageModel,
                ModelCategory.Video => settings.SelectedVideoModel,
                _ => string.Empty,
            };
        }

        public static List<ModelInfo> GetModelsForNodeType(ModelSettings settings, string nodeType)
        {
            EnsureDefaults(settings);
            var category = WorkflowExecutor.GetModelCategory(nodeType);
            if (category == null)
            {
                return new List<ModelInfo>();
            }

            return settings.Models
                .Where(model => model.Category == category.Value)
                .ToList();
        }

        public static string GetImagePromptTextModelId(ModelSettings settings)
        {
            EnsureDefaults(settings);
            return string.IsNullOrWhiteSpace(settings.SelectedImagePromptTextModel)
                ? settings.SelectedTextModel
                : settings.SelectedImagePromptTextModel;
        }

        public static ModelInfo? GetStoryboardImageWorkflowModel(ModelSettings settings)
        {
            EnsureDefaults(settings);
            var model = ResolveStoryboardImageWorkflowModel(settings);
            return model == null ? null : ApplyRelayOverrides(settings, model);
        }

        public static ModelInfo? GetPreferredLocalImageModel(ModelSettings settings)
        {
            EnsureDefaults(settings);
            var model = ResolvePreferredLocalImageModel(settings);
            return model == null ? null : ApplyRelayOverrides(settings, model);
        }

        public static ModelInfo? GetPreferredCloudImageModel(ModelSettings settings)
        {
            if (LocalOnlyMode)
            {
                return null;
            }

            EnsureDefaults(settings);
            var model = ResolvePreferredCloudImageModel(settings);
            return model == null ? null : ApplyRelayOverrides(settings, model);
        }

        public static ModelInfo? GetPreferredLocalVideoModel(ModelSettings settings)
        {
            EnsureDefaults(settings);
            var model = ResolvePreferredLocalVideoModel(settings);
            return model == null ? null : ApplyRelayOverrides(settings, model);
        }

        public static ModelInfo? GetPreferredCloudVideoModel(ModelSettings settings)
        {
            if (LocalOnlyMode)
            {
                return null;
            }

            EnsureDefaults(settings);
            var model = ResolvePreferredCloudVideoModel(settings);
            return model == null ? null : ApplyRelayOverrides(settings, model);
        }

        public static ModelInfo? GetPreferredLocalTextModel(ModelSettings settings)
        {
            EnsureDefaults(settings);
            var model = ResolvePreferredLocalTextModel(settings);
            return model == null ? null : ApplyRelayOverrides(settings, model);
        }

        public static ModelInfo? GetPreferredCloudTextModel(ModelSettings settings)
        {
            if (LocalOnlyMode)
            {
                return null;
            }

            EnsureDefaults(settings);
            var model = ResolvePreferredCloudTextModel(settings);
            return model == null ? null : ApplyRelayOverrides(settings, model);
        }

        public static bool IsLocalImageModelUrl(string? url)
        {
            return IsLocalEndpointUrl(url);
        }

        public static ModelEndpointSource GetModelSource(ModelInfo? model)
        {
            if (model == null)
            {
                return ModelEndpointSource.Unknown;
            }

            return model.Source == ModelEndpointSource.Unknown
                ? InferModelSource(model.Url)
                : model.Source;
        }

        public static ModelEndpointSource InferModelSource(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return ModelEndpointSource.Unknown;
            }

            return IsLocalEndpointUrl(url)
                ? ModelEndpointSource.Local
                : ModelEndpointSource.Cloud;
        }

        public static string GetModelSourceDisplayName(ModelInfo? model)
        {
            return GetModelSourceDisplayName(GetModelSource(model));
        }

        public static string GetModelSourceDisplayName(ModelEndpointSource source)
        {
            return source switch
            {
                ModelEndpointSource.Local => "本地",
                ModelEndpointSource.Cloud => "云端",
                _ => "未识别",
            };
        }

        public static bool IsComfyUiEndpointUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var text = url.Trim();
            if (text.Contains("comfy", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (uri.Port == 8188 || uri.Port == 8000)
            {
                return true;
            }

            var path = uri.AbsolutePath ?? string.Empty;
            return path.Contains("/object_info", StringComparison.OrdinalIgnoreCase) ||
                   path.Contains("/prompt", StringComparison.OrdinalIgnoreCase) ||
                   path.Contains("/queue", StringComparison.OrdinalIgnoreCase) ||
                   path.Contains("/history", StringComparison.OrdinalIgnoreCase) ||
                   path.Contains("/view", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsLocalEndpointUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (uri.IsLoopback)
            {
                return true;
            }

            var host = uri.Host;
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!IPAddress.TryParse(host, out var ipAddress))
            {
                return false;
            }

            if (IPAddress.IsLoopback(ipAddress))
            {
                return true;
            }

            var bytes = ipAddress.GetAddressBytes();
            return ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                   (
                       bytes[0] == 10 ||
                       (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                       (bytes[0] == 192 && bytes[1] == 168)
                   );
        }

        public static RelayApiInfo? GetRelayApi(ModelSettings settings, string providerCode)
        {
            if (LocalOnlyMode)
            {
                return null;
            }

            EnsureDefaults(settings);
            return settings.RelayApis.FirstOrDefault(relay =>
                string.Equals(relay.ProviderCode, providerCode, StringComparison.OrdinalIgnoreCase));
        }

        public static List<RelayApiInfo> GetRelayApis(ModelSettings settings)
        {
            if (LocalOnlyMode)
            {
                return new List<RelayApiInfo>();
            }

            EnsureDefaults(settings);
            return settings.RelayApis
                .Where(relay => !string.IsNullOrWhiteSpace(relay.ProviderCode))
                .OrderBy(relay => relay.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static void UpsertRelayApi(ModelSettings settings, string providerCode, string name, string baseUrl, string key, bool enabled = true)
        {
            if (LocalOnlyMode)
            {
                settings.RelayApis = new List<RelayApiInfo>();
                return;
            }

            EnsureDefaults(settings);
            var relay = settings.RelayApis.FirstOrDefault(item =>
                string.Equals(item.ProviderCode, providerCode, StringComparison.OrdinalIgnoreCase));
            if (relay == null)
            {
                relay = new RelayApiInfo();
                settings.RelayApis.Add(relay);
            }

            relay.ProviderCode = providerCode?.Trim() ?? string.Empty;
            relay.Name = string.IsNullOrWhiteSpace(name) ? providerCode?.Trim() ?? string.Empty : name.Trim();
            relay.BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? string.Empty : baseUrl.Trim();
            relay.Key = key?.Trim() ?? string.Empty;
            relay.Enabled = enabled;
        }

        public static ModelInfo ApplyRelayOverrides(ModelSettings settings, ModelInfo model, string? providerCode = null)
        {
            if (LocalOnlyMode)
            {
                return model;
            }

            EnsureDefaults(settings);

            providerCode = string.IsNullOrWhiteSpace(providerCode)
                ? InferRelayProviderCode(model)
                : providerCode.Trim();

            if (string.IsNullOrWhiteSpace(providerCode))
            {
                return model;
            }

            var relay = GetRelayApi(settings, providerCode);
            if (relay == null || !relay.Enabled)
            {
                return model;
            }

            return new ModelInfo
            {
                ConfigId = model.ConfigId,
                Id = model.Id,
                Name = model.Name,
                WorkflowJson = model.WorkflowJson,
                Category = model.Category,
                Url = string.IsNullOrWhiteSpace(relay.BaseUrl) ? model.Url : relay.BaseUrl,
                Key = string.IsNullOrWhiteSpace(relay.Key) ? model.Key : relay.Key,
                Source = model.Source,
            };
        }

        public static string ResolveComfyUiWorkflowJson(ModelInfo? model)
        {
            if (model == null)
            {
                return string.Empty;
            }

            return NormalizeWorkflowJsonName(model.WorkflowJson, allowImplicitJsonExtension: true);
        }

        public static string NormalizeWorkflowJsonName(string? rawValue, bool allowImplicitJsonExtension = false)
        {
            var candidate = (rawValue ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return string.Empty;
            }

            candidate = Path.GetFileName(candidate);
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return string.Empty;
            }

            if (!candidate.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                if (!allowImplicitJsonExtension)
                {
                    return string.Empty;
                }

                candidate += ".json";
            }

            return candidate;
        }

        private static void TryMigrateLegacyComfyUiWorkflowJson(ModelInfo model)
        {
            if (model == null ||
                (model.Category != ModelCategory.Image && model.Category != ModelCategory.Video) ||
                !string.IsNullOrWhiteSpace(model.WorkflowJson) ||
                !LooksLikeComfyUiUrl(model.Url))
            {
                return;
            }

            var legacyWorkflow = NormalizeWorkflowJsonName(model.Name, allowImplicitJsonExtension: false);
            if (!string.IsNullOrWhiteSpace(legacyWorkflow))
            {
                model.WorkflowJson = legacyWorkflow;
            }
        }

        private static bool LooksLikeComfyUiUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var raw = uri.ToString().ToLowerInvariant();
            var path = uri.AbsolutePath.ToLowerInvariant();
            return raw.Contains("comfy") ||
                   uri.Port == 8000 ||
                   uri.Port == 8188 ||
                   path.Contains("/object_info") ||
                   path.Contains("/prompt") ||
                   path.Contains("/queue") ||
                   path.Contains("/history") ||
                   path.Contains("/view");
        }

        private static ModelInfo? ResolveStoryboardImageWorkflowModel(ModelSettings settings)
        {
            var selectedLocal = settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Image &&
                MatchesModelSelector(model, settings.SelectedImageModel) &&
                IsLocalImageModelUrl(model.Url));
            if (selectedLocal != null)
            {
                return selectedLocal;
            }

            var local = ResolvePreferredLocalImageModel(settings);
            if (local != null)
            {
                return local;
            }

            var selected = settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Image &&
                MatchesModelSelector(model, settings.SelectedImageModel));
            if (selected != null)
            {
                return selected;
            }

            var exact = settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Image &&
                string.Equals(model.Id, DefaultYunWuStoryboardImageModelId, StringComparison.OrdinalIgnoreCase) &&
                IsYunWuModelUrl(model.Url));
            if (exact != null)
            {
                return exact;
            }

            exact = settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Image &&
                string.Equals(model.Id, DefaultYunWuStoryboardImageModelId, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                return exact;
            }

            exact = settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Image &&
                IsYunWuModelUrl(model.Url));
            if (exact != null)
            {
                return exact;
            }

            return settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Image);
        }

        private static ModelInfo? ResolvePreferredLocalImageModel(ModelSettings settings)
        {
            var selectedLocal = settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Image &&
                MatchesModelSelector(model, settings.SelectedImageModel) &&
                IsLocalImageModelUrl(model.Url));
            if (selectedLocal != null)
            {
                return selectedLocal;
            }

            var comfyLocal = settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Image &&
                IsLocalImageModelUrl(model.Url) &&
                model.Url.Contains(":8188", StringComparison.OrdinalIgnoreCase));
            if (comfyLocal != null)
            {
                return comfyLocal;
            }

            comfyLocal = settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Image &&
                IsLocalImageModelUrl(model.Url) &&
                model.Url.Contains(":8000", StringComparison.OrdinalIgnoreCase));
            if (comfyLocal != null)
            {
                return comfyLocal;
            }

            return settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Image &&
                IsLocalImageModelUrl(model.Url));
        }

        private static ModelInfo? ResolvePreferredLocalTextModel(ModelSettings settings)
        {
            var selectedLocal = settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Text &&
                MatchesModelSelector(model, settings.SelectedTextModel) &&
                IsLocalEndpointUrl(model.Url));
            if (selectedLocal != null)
            {
                return selectedLocal;
            }

            return settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Text &&
                IsLocalEndpointUrl(model.Url));
        }

        private static ModelInfo? ResolvePreferredCloudTextModel(ModelSettings settings)
        {
            var selectedCloud = settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Text &&
                MatchesModelSelector(model, settings.SelectedTextModel) &&
                !IsLocalEndpointUrl(model.Url));
            if (selectedCloud != null)
            {
                return selectedCloud;
            }

            return settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Text &&
                !IsLocalEndpointUrl(model.Url));
        }

        private static ModelInfo? ResolvePreferredCloudImageModel(ModelSettings settings)
        {
            var selectedCloud = settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Image &&
                MatchesModelSelector(model, settings.SelectedImageModel) &&
                !IsLocalImageModelUrl(model.Url));
            if (selectedCloud != null)
            {
                return selectedCloud;
            }

            var exact = settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Image &&
                string.Equals(model.Id, DefaultYunWuStoryboardImageModelId, StringComparison.OrdinalIgnoreCase) &&
                IsYunWuModelUrl(model.Url));
            if (exact != null)
            {
                return exact;
            }

            return settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Image &&
                !IsLocalImageModelUrl(model.Url));
        }

        private static ModelInfo? ResolvePreferredLocalVideoModel(ModelSettings settings)
        {
            var selectedLocal = settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Video &&
                MatchesModelSelector(model, settings.SelectedVideoModel) &&
                IsLocalEndpointUrl(model.Url));
            if (selectedLocal != null)
            {
                return selectedLocal;
            }

            return settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Video &&
                IsLocalEndpointUrl(model.Url));
        }

        private static ModelInfo? ResolvePreferredCloudVideoModel(ModelSettings settings)
        {
            var selectedCloud = settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Video &&
                MatchesModelSelector(model, settings.SelectedVideoModel) &&
                !IsLocalEndpointUrl(model.Url));
            if (selectedCloud != null)
            {
                return selectedCloud;
            }

            return settings.Models.FirstOrDefault(model =>
                model.Category == ModelCategory.Video &&
                !IsLocalEndpointUrl(model.Url));
        }

        private static void NormalizeLocalFirstImageDefaults(ModelSettings settings, string localImageModelId)
        {
            if (string.IsNullOrWhiteSpace(localImageModelId))
            {
                return;
            }

            foreach (var nodeType in WorkflowNodeCatalog.ConfigurableNodeTypes)
            {
                if (!WorkflowExecutor.GetRequiredModelCategories(nodeType).Contains(ModelCategory.Image))
                {
                    continue;
                }

                if (!settings.DefaultNodeModels.TryGetValue(nodeType, out var modelId))
                {
                    continue;
                }

                if (string.Equals(modelId, DefaultYunWuStoryboardImageModelId, StringComparison.OrdinalIgnoreCase))
                {
                    settings.DefaultNodeModels[nodeType] = localImageModelId;
                }
            }
        }

        public static string NormalizeYunWuBaseUrl(string? rawUrl)
        {
            var value = (rawUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                return DefaultYunWuBaseUrl;
            }

            return IsYunWuHost(uri.Host)
                ? DefaultYunWuBaseUrl
                : uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        }

        private static bool IsYunWuModelUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            return IsYunWuHost(uri.Host);
        }

        private static bool IsYunWuHost(string? host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

           return host.EndsWith("yunwu.ai", StringComparison.OrdinalIgnoreCase) ||
                  host.EndsWith("yunwu.cloud", StringComparison.OrdinalIgnoreCase);
       }
 
        private static bool IsGeminiHost(string? host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }
 
            return host.EndsWith("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase);
        }
 
        public static bool IsGeminiModelUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }
 
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }
 
            return IsGeminiHost(uri.Host);
        }
 
       private static string InferRelayProviderCode(ModelInfo model)
        {
            if (model == null)
            {
                return string.Empty;
            }

            if (IsYunWuModelUrl(model.Url))
            {
                return "yunwuapi";
            }

            if (string.Equals(model.Id, DefaultYunWuStoryboardImageModelId, StringComparison.OrdinalIgnoreCase))
            {
                return "yunwuapi";
            }

            return string.Empty;
        }

        private static ModelSettings CloneForPersistence(ModelSettings settings)
        {
            return new ModelSettings
            {
                Models = settings.Models.Select(model => new ModelInfo
                {
                    ConfigId = model.ConfigId,
                    Id = model.Id,
                    Name = model.Name,
                    WorkflowJson = model.WorkflowJson,
                    Url = model.Url,
                    Key = model.Key,
                    Category = model.Category,
                    Source = model.Source,
                }).ToList(),
                RelayApis = settings.RelayApis.Select(relay => new RelayApiInfo
                {
                    ProviderCode = relay.ProviderCode,
                    Name = relay.Name,
                    BaseUrl = relay.BaseUrl,
                    Key = relay.Key,
                    Enabled = relay.Enabled,
                }).ToList(),
                SelectedTextModel = settings.SelectedTextModel,
                SelectedImagePromptTextModel = settings.SelectedImagePromptTextModel,
                SelectedImageModel = settings.SelectedImageModel,
                SelectedVideoModel = settings.SelectedVideoModel,
                DefaultNodeModels = new Dictionary<string, string>(settings.DefaultNodeModels, StringComparer.OrdinalIgnoreCase),
            };
        }

        private static bool DecryptStoredKeys(ModelSettings settings)
        {
            if (settings.Models == null || settings.Models.Count == 0)
            {
                return false;
            }

            var needsResave = false;
            foreach (var model in settings.Models)
            {
                if (string.IsNullOrWhiteSpace(model.Key))
                {
                    continue;
                }

                if (TryDecryptKey(model.Key, out var decryptedKey))
                {
                    model.Key = decryptedKey;
                    continue;
                }

                if (!model.Key.StartsWith(EncryptedKeyPrefix, StringComparison.Ordinal))
                {
                    needsResave = true;
                }
            }

            foreach (var relay in settings.RelayApis ?? Enumerable.Empty<RelayApiInfo>())
            {
                if (string.IsNullOrWhiteSpace(relay.Key))
                {
                    continue;
                }

                if (TryDecryptKey(relay.Key, out var decryptedKey))
                {
                    relay.Key = decryptedKey;
                    continue;
                }

                if (!relay.Key.StartsWith(EncryptedKeyPrefix, StringComparison.Ordinal))
                {
                    needsResave = true;
                }
            }

            return needsResave;
        }

        private static void EncryptStoredKeys(ModelSettings settings)
        {
            if (settings.Models == null || settings.Models.Count == 0)
            {
                return;
            }

            foreach (var model in settings.Models)
            {
                model.Key = EncryptKey(model.Key);
            }

            foreach (var relay in settings.RelayApis ?? Enumerable.Empty<RelayApiInfo>())
            {
                relay.Key = EncryptKey(relay.Key);
            }
        }

        private static string EncryptKey(string? plainTextKey)
        {
            if (string.IsNullOrWhiteSpace(plainTextKey))
            {
                return string.Empty;
            }

            if (plainTextKey.StartsWith(EncryptedKeyPrefix, StringComparison.Ordinal))
            {
                return plainTextKey;
            }

            try
            {
                var rawBytes = Encoding.UTF8.GetBytes(plainTextKey);
                var cipherBytes = ProtectBytes(rawBytes);
                return EncryptedKeyPrefix + Convert.ToBase64String(cipherBytes);
            }
            catch
            {
                return plainTextKey;
            }
        }

        private static bool TryDecryptKey(string storedValue, out string plainTextKey)
        {
            plainTextKey = storedValue;
            if (!storedValue.StartsWith(EncryptedKeyPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                var payload = storedValue[EncryptedKeyPrefix.Length..];
                var cipherBytes = Convert.FromBase64String(payload);
                var rawBytes = UnprotectBytes(cipherBytes);
                plainTextKey = Encoding.UTF8.GetString(rawBytes);
                return true;
            }
            catch
            {
                plainTextKey = string.Empty;
                return false;
            }
        }

        private static byte[] ProtectBytes(byte[] plainBytes)
        {
            var inputBlob = CreateBlob(plainBytes);
            var entropyBlob = CreateBlob(KeyEntropy);
            DataBlob outputBlob = default;

            try
            {
                if (!CryptProtectData(
                        ref inputBlob,
                        null,
                        ref entropyBlob,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        CryptProtectUiForbidden,
                        out outputBlob))
                {
                    throw new InvalidOperationException($"DPAPI protect failed: {Marshal.GetLastWin32Error()}");
                }

                return ReadBlob(outputBlob);
            }
            finally
            {
                FreeBlob(inputBlob);
                FreeBlob(entropyBlob);
                FreeBlob(outputBlob, true);
            }
        }

        private static byte[] UnprotectBytes(byte[] cipherBytes)
        {
            var inputBlob = CreateBlob(cipherBytes);
            var entropyBlob = CreateBlob(KeyEntropy);
            DataBlob outputBlob = default;

            try
            {
                if (!CryptUnprotectData(
                        ref inputBlob,
                        null,
                        ref entropyBlob,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        CryptProtectUiForbidden,
                        out outputBlob))
                {
                    throw new InvalidOperationException($"DPAPI unprotect failed: {Marshal.GetLastWin32Error()}");
                }

                return ReadBlob(outputBlob);
            }
            finally
            {
                FreeBlob(inputBlob);
                FreeBlob(entropyBlob);
                FreeBlob(outputBlob, true);
            }
        }

        private static DataBlob CreateBlob(byte[] data)
        {
            if (data.Length == 0)
            {
                return default;
            }

            var pointer = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, pointer, data.Length);
            return new DataBlob
            {
                cbData = data.Length,
                pbData = pointer,
            };
        }

        private static byte[] ReadBlob(DataBlob blob)
        {
            if (blob.cbData <= 0 || blob.pbData == IntPtr.Zero)
            {
                return Array.Empty<byte>();
            }

            var data = new byte[blob.cbData];
            Marshal.Copy(blob.pbData, data, 0, blob.cbData);
            return data;
        }

        private static void FreeBlob(DataBlob blob, bool useLocalFree = false)
        {
            if (blob.pbData == IntPtr.Zero)
            {
                return;
            }

            if (useLocalFree)
            {
                _ = LocalFree(blob.pbData);
                return;
            }

            Marshal.FreeHGlobal(blob.pbData);
        }
    }
}
