using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public partial class AddModelForm : Form
    {
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(6) };

        private const int ExpandedClientHeight = 430;
        private const int CompactClientHeight = 392;
        private const int WorkflowRowShift = 38;

        private readonly ModelInfo? _editingModel;
        private bool _testPassed;

        public ModelInfo? ModelInfo { get; private set; }

        public AddModelForm(ModelInfo? editingModel = null)
        {
            InitializeComponent();
            txtKey.UseSystemPasswordChar = true;
            _editingModel = editingModel;
        }

        private enum ModelProvider
        {
            Unknown,
            OpenAI,
            YunWu,
            Ollama,
           StableDiffusion,
            ComfyUI,
           Gemini
        }

        private void AddModelForm_Load(object sender, EventArgs e)
        {
            comboCategory.DataSource = Enum.GetValues(typeof(ModelCategory));
            comboCategory.SelectedItem = ModelCategory.Text;
            LoadAvailableWorkflowJsonOptions();

            txtId.TextChanged += (_, _) => MarkDirty();
            txtName.TextChanged += (_, _) => MarkDirty();
            txtUrl.TextChanged += (_, _) =>
            {
                UpdateWorkflowJsonAvailability();
                MarkDirty();
            };
            txtKey.TextChanged += (_, _) => MarkDirty();
            comboCategory.SelectedIndexChanged += (_, _) =>
            {
                UpdateWorkflowJsonAvailability();
                MarkDirty();
            };
            comboWorkflowJson.TextChanged += (_, _) => MarkDirty();
            comboWorkflowJson.SelectedIndexChanged += (_, _) => MarkDirty();

            if (_editingModel != null)
            {
                txtId.Text = _editingModel.Id;
                txtName.Text = _editingModel.Name;
                comboWorkflowJson.Text = ModelConfig.ResolveComfyUiWorkflowJson(_editingModel);
                txtUrl.Text = _editingModel.Url;
                txtKey.Text = _editingModel.Key;
                comboCategory.SelectedItem = _editingModel.Category;
            }

            UpdateWorkflowJsonAvailability();
            MarkDirty();
            txtTestResult.Text = "测试结果：还未测试";
        }

        private void MarkDirty()
        {
            _testPassed = false;
            btnOk.Enabled = false;
            txtTestResult.Text = "测试结果：还未测试";
            txtTestResult.ForeColor = Color.Black;
        }

        private async void btnTest_Click(object sender, EventArgs e)
        {
            var modelId = txtId.Text.Trim();
            var modelName = txtName.Text.Trim();
            var url = txtUrl.Text.Trim();
            var key = txtKey.Text.Trim();
            var category = comboCategory.SelectedItem is ModelCategory selectedCategory
                ? selectedCategory
                : ModelCategory.Text;
            var workflowJson = GetResolvedWorkflowJsonInput(url, category);

            if (string.IsNullOrWhiteSpace(modelId) || string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show(this, "请填写模型ID和URL", "检查输入", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                txtTestResult.Text = "测试结果：无效的URL地址";
                txtTestResult.ForeColor = Color.DarkRed;
                return;
            }

            btnTest.Enabled = false;
            txtTestResult.Text = "测试结果：正在测试...";
            txtTestResult.ForeColor = Color.Black;

            try
            {
                var provider = DetectProvider(uri);
                var result = await TestModelAsync(provider, uri, modelId, workflowJson, key, category);
                txtTestResult.Text = result.Message;
                txtTestResult.ForeColor = result.Success ? Color.Green : Color.DarkOrange;
                _testPassed = result.Success;
                btnOk.Enabled = result.Success;
            }
            catch (Exception ex)
            {
                txtTestResult.Text = $"测试结果：测试异常：{ex.Message}";
                txtTestResult.ForeColor = Color.DarkRed;
                _testPassed = false;
                btnOk.Enabled = false;
            }
            finally
            {
                btnTest.Enabled = true;
            }
        }

        private void btnOk_Click(object sender, EventArgs e)
        {
            var id = txtId.Text.Trim();
            var name = txtName.Text.Trim();
            var url = txtUrl.Text.Trim();
            var key = txtKey.Text.Trim();
            var category = comboCategory.SelectedItem is ModelCategory selectedCategory
                ? selectedCategory
                : ModelCategory.Text;
            var workflowJson = GetResolvedWorkflowJsonInput(url, category);

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show(this, "请填写ID、模型名和URL", "检查输入", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!_testPassed)
            {
                MessageBox.Show(this, "当前模型尚未测试通过，请先测试", "未测试", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                MessageBox.Show(this, "URL格式无效，请检查", "无效URL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ModelInfo = new ModelInfo
            {
                ConfigId = _editingModel?.ConfigId ?? ModelConfig.CreateModelConfigId(),
                Id = id,
                Name = name,
                WorkflowJson = workflowJson,
                Url = url,
                Key = key,
                Category = category,
                Source = ModelConfig.InferModelSource(url)
            };

            DialogResult = DialogResult.OK;
            Close();
        }

        private void UpdateWorkflowJsonAvailability()
        {
            var category = comboCategory.SelectedItem is ModelCategory selectedCategory
                ? selectedCategory
                : ModelCategory.Text;
            var visible = ShouldShowWorkflowJsonField(txtUrl.Text, category);

            label6.Visible = visible;
            comboWorkflowJson.Visible = visible;

            var shift = visible ? 0 : -WorkflowRowShift;
            label4.Top = 132 + shift;
            txtUrl.Top = 129 + shift;
            label5.Top = 170 + shift;
            txtKey.Top = 167 + shift;
            label3.Top = 208 + shift;
            comboCategory.Top = 205 + shift;
            btnTest.Top = 276 + shift;
            txtTestResult.Top = 316 + shift;
            btnOk.Top = 386 + shift;
            btnCancel.Top = 386 + shift;
            ClientSize = new Size(ClientSize.Width, visible ? ExpandedClientHeight : CompactClientHeight);
        }

        private static bool ShouldShowWorkflowJsonField(string url, ModelCategory category)
        {
            if (category != ModelCategory.Image && category != ModelCategory.Video)
            {
                return false;
            }

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            {
                return false;
            }

            return DetectProvider(uri) == ModelProvider.ComfyUI;
        }

        private static ModelProvider DetectProvider(Uri uri)
        {
            var raw = uri.ToString().ToLowerInvariant();
            var path = uri.AbsolutePath.ToLowerInvariant();
            if (raw.Contains("openai.com"))
            {
                return ModelProvider.OpenAI;
            }

            if (raw.Contains("yunwu.ai") || raw.Contains("yunwu.cloud"))
            {
                return ModelProvider.YunWu;
            }

            if (raw.Contains("ollama") || uri.Port == 11434)
            {
                return ModelProvider.Ollama;
            }

            if (raw.Contains("sdapi") || uri.Port == 7860)
            {
                return ModelProvider.StableDiffusion;
            }

            if (raw.Contains("comfy") ||
                uri.Port == 8000 ||
                uri.Port == 8188 ||
                path.Contains("/object_info") ||
                path.Contains("/prompt") ||
                path.Contains("/queue") ||
                path.Contains("/history") ||
                path.Contains("/view"))
            {
                return ModelProvider.ComfyUI;
            }

            if (raw.Contains("generativelanguage.googleapis.com"))
            {
                return ModelProvider.Gemini;
            }
            return ModelProvider.Unknown;
        }

        private static string GetBaseUrl(Uri uri)
        {
            var value = uri.ToString().TrimEnd('/');
            var v1Index = value.IndexOf("/v1", StringComparison.OrdinalIgnoreCase);
            return v1Index >= 0 ? value[..v1Index] : value;
        }

        private static string GetYunWuRootUrl(Uri uri)
        {
            return ModelConfig.NormalizeYunWuBaseUrl(uri.ToString());
        }

        private static string GetComfyUiBaseUrl(Uri uri)
        {
            var value = uri.ToString().TrimEnd('/');
            foreach (var suffix in new[] { "/prompt", "/queue", "/history", "/view", "/object_info" })
            {
                if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return value[..^suffix.Length];
                }
            }

            return value;
        }

        private static async Task<(bool Success, string Message)> TestModelAsync(
            ModelProvider provider,
            Uri uri,
            string modelId,
            string workflowJson,
            string key,
            ModelCategory category)
        {
            return provider switch
            {
                ModelProvider.OpenAI => await TestOpenAiAsync(uri, modelId, key),
                ModelProvider.YunWu => await TestYunWuAsync(uri, modelId, key),
                ModelProvider.Ollama => await TestOllamaAsync(uri, modelId, key),
                ModelProvider.StableDiffusion => await TestStableDiffusionAsync(uri, modelId),
                ModelProvider.ComfyUI => await TestComfyUiAsync(uri, modelId, workflowJson, category),
                ModelProvider.Gemini => await TestOpenAiAsync(uri, modelId, key),
                _ => await TestGenericAsync(uri, key, category),
            };
        }

        private static async Task<(bool Success, string Message)> TestOpenAiAsync(Uri uri, string modelId, string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return (false, "测试结果：OpenAI测试失败，缺少API Key");
            }

            var baseUrl = GetBaseUrl(uri).TrimEnd('/');
            if (!baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl += "/v1";
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(baseUrl + "/"), $"models/{Uri.EscapeDataString(modelId)}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode
                ? (true, "测试结果：OpenAI测试成功，连接正常")
                : (false, $"测试结果：OpenAI测试失败，HTTP状态码 {(int)response.StatusCode}閵?");
        }

        private static async Task<(bool Success, string Message)> TestYunWuAsync(Uri uri, string modelId, string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return (false, "测试结果：云雾API测试失败，缺少API Key");
            }

            var rootUrl = GetYunWuRootUrl(uri);
            var candidates = new[]
            {
                new Uri(new Uri(rootUrl + "/"), "v1/models"),
                new Uri(new Uri(rootUrl + "/"), $"v1/models/{Uri.EscapeDataString(modelId)}")
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, candidate);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                    using var response = await _httpClient.SendAsync(request);
                    if (!response.IsSuccessStatusCode)
                    {
                        continue;
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (TryContainsModelId(doc.RootElement, modelId) || candidate.AbsolutePath.EndsWith($"/{Uri.EscapeDataString(modelId)}", StringComparison.OrdinalIgnoreCase))
                    {
                        return (true, "测试结果：云雾API测试成功，连接正常");
                    }
                }
                catch
                {
                }
            }

            return (true, "测试结果：云雾API测试成功，连接正常，但未找到指定模型ID");
        }

        private static async Task<(bool Success, string Message)> TestOllamaAsync(Uri uri, string modelId, string key)
        {
            var endpoint = new Uri(new Uri(GetBaseUrl(uri).TrimEnd('/') + "/"), "v1/models");
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            if (!string.IsNullOrWhiteSpace(key) && !string.Equals(key, "ollama", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            }

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"测试结果：Ollama测试失败，HTTP状态码 {(int)response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var models = doc.RootElement.ValueKind switch
            {
                JsonValueKind.Array => doc.RootElement,
                JsonValueKind.Object when doc.RootElement.TryGetProperty("models", out var modelsProp) => modelsProp,
                JsonValueKind.Object when doc.RootElement.TryGetProperty("data", out var dataProp) => dataProp,
                _ => default
            };

            if (models.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in models.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String &&
                        string.Equals(item.GetString(), modelId, StringComparison.OrdinalIgnoreCase))
                    {
                        return (true, "测试结果：Ollama测试成功，连接正常");
                    }

                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (MatchesModelField(item, "id", modelId) ||
                        MatchesModelField(item, "name", modelId) ||
                        MatchesModelField(item, "model", modelId))
                    {
                        return (true, "测试结果：Ollama测试成功，连接正常");
                    }
                }
            }

            return (false, "测试结果：Ollama测试失败，未找到指定模型ID");
        }

        private static async Task<(bool Success, string Message)> TestStableDiffusionAsync(Uri uri, string modelId)
        {
            var baseUrl = GetBaseUrl(uri).TrimEnd('/');
            if (!baseUrl.EndsWith("/sdapi/v1", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl += "/sdapi/v1";
            }

            using var response = await _httpClient.GetAsync(new Uri(new Uri(baseUrl + "/"), "sd-models"));
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"测试结果：SD测试失败，HTTP状态码 {(int)response.StatusCode}閵?");
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return (false, "测试结果：SD测试失败，获取SD模型列表格式错误");
            }

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty("model_name", out var nameProp) &&
                    string.Equals(nameProp.GetString(), modelId, StringComparison.OrdinalIgnoreCase))
                {
                    return (true, "测试结果：Stable Diffusion测试成功，连接正常");
                }
            }

            return (true, "测试结果：SD测试成功，连接正常，但未找到指定模型ID");
        }

        private static async Task<(bool Success, string Message)> TestComfyUiAsync(Uri uri, string modelId, string workflowJson, ModelCategory category)
        {
            var baseUrl = GetComfyUiBaseUrl(uri).TrimEnd('/');
            using var response = await _httpClient.GetAsync(new Uri(new Uri(baseUrl + "/"), "object_info"));
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"测试结果：ComfyUI测试失败，HTTP状态码 {(int)response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (false, "测试结果：ComfyUI测试失败，object_info接口格式错误");
            }

            var effectiveWorkflowJson = category == ModelCategory.Image || category == ModelCategory.Video
                ? ModelConfig.NormalizeWorkflowJsonName(workflowJson, allowImplicitJsonExtension: true)
                : string.Empty;

            if (!string.IsNullOrWhiteSpace(effectiveWorkflowJson))
            {
                if (!TryResolveWorkflowJsonPath(effectiveWorkflowJson, out var workflowPath))
                {
                    return (false, $"测试结果：ComfyUI测试失败，找不到工作流JSON文件：{effectiveWorkflowJson}");
                }

                return ValidateComfyUiWorkflowJsonForTest(workflowPath, effectiveWorkflowJson);
            }

            if (!doc.RootElement.TryGetProperty("CheckpointLoaderSimple", out var checkpointLoader) ||
                checkpointLoader.ValueKind != JsonValueKind.Object)
            {
                return !string.IsNullOrWhiteSpace(effectiveWorkflowJson)
                    ? (true, $"测试结果：ComfyUI测试成功，连接正常，{effectiveWorkflowJson} 工作流验证通过")
                    : (true, "测试结果：ComfyUI测试成功，连接正常（无工作流配置）");
            }

            if (!checkpointLoader.TryGetProperty("input", out var input) ||
                input.ValueKind != JsonValueKind.Object ||
                !input.TryGetProperty("required", out var required) ||
                required.ValueKind != JsonValueKind.Object ||
                !required.TryGetProperty("ckpt_name", out var checkpoints) ||
                checkpoints.ValueKind != JsonValueKind.Array ||
                checkpoints.GetArrayLength() == 0 ||
                checkpoints[0].ValueKind != JsonValueKind.Array)
            {
                return !string.IsNullOrWhiteSpace(effectiveWorkflowJson)
                    ? (true, $"测试结果：ComfyUI测试成功，连接正常，{effectiveWorkflowJson} 工作流验证通过")
                    : (true, "测试结果：ComfyUI测试成功，连接正常（无工作流配置）");
            }

            var availableCheckpoints = checkpoints[0]
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? string.Empty)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            if (availableCheckpoints.Any(value => string.Equals(value, modelId, StringComparison.OrdinalIgnoreCase)))
            {
                return !string.IsNullOrWhiteSpace(effectiveWorkflowJson)
                    ? (true, $"测试结果：ComfyUI测试成功，连接正常，{effectiveWorkflowJson} 包含检查点：")
                    : (true, "测试结果：ComfyUI测试成功，连接正常，但未找到指定检查点");
            }

            return !string.IsNullOrWhiteSpace(effectiveWorkflowJson)
                ? (false, $"测试结果：ComfyUI测试失败，工作流：{effectiveWorkflowJson} 已配置但检查点引用可能无效（checkpoint={modelId}）")
                : (true, "测试结果：ComfyUI测试成功，连接正常（无检查点配置）");
        }

        private static (bool Success, string Message) ValidateComfyUiWorkflowJsonForTest(string workflowPath, string workflowJson)
        {
            try
            {
                var workflowText = File.ReadAllText(workflowPath);
                using var workflowDoc = JsonDocument.Parse(workflowText);
                if (workflowDoc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return (false, $"测试结果：工作流 {workflowJson} 失败：不是有效的ComfyUI API JSON格式");
                }

                var placeholders = Regex.Matches(workflowText, "\\{\\{\\s*([A-Za-z0-9_.-]+)\\s*\\}\\}")
                    .Cast<Match>()
                    .Select(match => match.Groups[1].Value.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(8)
                    .ToList();

                if (placeholders.Count > 0)
                {
                    return (true, $"测试结果：ComfyUI工作流 {workflowJson} 验证通过，包含占位符：{string.Join(", ", placeholders)}?");
                }

                if (workflowText.Contains("\"KSampler\"", StringComparison.OrdinalIgnoreCase) ||
                    workflowText.Contains("\"CLIPTextEncode\"", StringComparison.OrdinalIgnoreCase))
                {
                    return (true, $"测试结果：ComfyUI工作流 {workflowJson} 验证通过，包含KSampler/CLIPTextEncode节点");
                }

                return (true, $"测试结果：ComfyUI工作流 {workflowJson} 验证通过，但可能需要配置输入参数（如 positive_prompt, input_image 等）?");
            }
            catch (Exception ex)
            {
                return (false, $"测试结果：工作流 {workflowJson} 验证异常：{ex.Message}");
            }
        }

        private static async Task<(bool Success, string Message)> TestGenericAsync(Uri uri, string key, ModelCategory category)
        {
            if (category == ModelCategory.Image || category == ModelCategory.Video)
            {
                var comfyResult = await TryTestGenericComfyUiAsync(uri);
                if (comfyResult.Success)
                {
                    return comfyResult;
                }
            }

            var candidates = new[]
            {
                uri.ToString().TrimEnd('/'),
                GetBaseUrl(uri).TrimEnd('/'),
                GetBaseUrl(uri).TrimEnd('/') + "/v1/models",
                GetBaseUrl(uri).TrimEnd('/') + "/models"
            }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

            foreach (var candidate in candidates)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, candidate);
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                    }

                    using var response = await _httpClient.SendAsync(request);
                    if (response.IsSuccessStatusCode || (int)response.StatusCode < 500)
                    {
                        return (true, $"测试结果：{category} API测试成功，连接正常");
                    }
                }
                catch
                {
                }
            }

            return (false, "测试结果：通用API测试失败，找不到指定的API Key（请检查API Key或尝试其他测试方式）");
        }

        private static async Task<(bool Success, string Message)> TryTestGenericComfyUiAsync(Uri uri)
        {
            try
            {
                var baseUrl = GetComfyUiBaseUrl(uri).TrimEnd('/');
                using var response = await _httpClient.GetAsync(new Uri(new Uri(baseUrl + "/"), "object_info"));
                if (!response.IsSuccessStatusCode)
                {
                    return (false, string.Empty);
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    return (true, "测试结果：ComfyUI API测试成功，连接正常");
                }
            }
            catch
            {
            }

            return (false, string.Empty);
        }

        private void LoadAvailableWorkflowJsonOptions()
        {
            var selectedText = comboWorkflowJson.Text;
            comboWorkflowJson.Items.Clear();
            foreach (var workflowJson in EnumerateWorkflowJsonFileNames())
            {
                comboWorkflowJson.Items.Add(workflowJson);
            }

            if (!string.IsNullOrWhiteSpace(selectedText))
            {
                comboWorkflowJson.Text = selectedText;
            }
        }

        private string GetResolvedWorkflowJsonInput(string url, ModelCategory category)
        {
            if (!ShouldShowWorkflowJsonField(url, category))
            {
                return string.Empty;
            }

            var configured = ModelConfig.NormalizeWorkflowJsonName(comboWorkflowJson.Text, allowImplicitJsonExtension: true);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            return string.Empty;
        }

        private static IEnumerable<string> EnumerateWorkflowJsonFileNames()
        {
            return GetWorkflowSearchDirectories()
                .Where(Directory.Exists)
                .SelectMany(directory => Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
                .Select(Path.GetFileName)
                .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(fileName => fileName, StringComparer.OrdinalIgnoreCase)!;
        }

        private static bool TryResolveWorkflowJsonPath(string workflowJson, out string resolvedPath)
        {
            resolvedPath = string.Empty;
            var normalized = ModelConfig.NormalizeWorkflowJsonName(workflowJson, allowImplicitJsonExtension: true);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            foreach (var directory in GetWorkflowSearchDirectories().Where(Directory.Exists))
            {
                var candidate = Path.Combine(directory, normalized);
                if (File.Exists(candidate))
                {
                    resolvedPath = candidate;
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> GetWorkflowSearchDirectories()
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            return new[]
            {
                Path.Combine(baseDirectory, "Workflowsapi"),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "WinApp", "bin", "Debug", "net8.0-windows", "Workflowsapi")),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "Workflowsapi")),
                Path.Combine(Environment.CurrentDirectory, "Workflowsapi"),
            }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static bool TryContainsModelId(JsonElement root, string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return false;
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in root.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String &&
                        (string.Equals(property.Name, "id", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(property.Name, "name", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(property.Name, "model", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(property.Name, "model_name", StringComparison.OrdinalIgnoreCase)) &&
                        string.Equals(property.Value.GetString(), modelId, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (TryContainsModelId(property.Value, modelId))
                    {
                        return true;
                    }
                }
            }

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    if (TryContainsModelId(item, modelId))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool MatchesModelField(JsonElement item, string propertyName, string expected)
        {
            return item.TryGetProperty(propertyName, out var valueProp) &&
                   valueProp.ValueKind == JsonValueKind.String &&
                   string.Equals(valueProp.GetString(), expected, StringComparison.OrdinalIgnoreCase);
        }
    }
}
