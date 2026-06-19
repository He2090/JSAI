using System.Text.Json;
using System.Net.Http;

namespace JSAI.WinApp;

internal sealed class AppServerConfig
{
    private const string DefaultApiBaseUrl = "http://127.0.0.1:5157";

    public string ApiBaseUrl { get; set; } = DefaultApiBaseUrl;

    public bool IsConfigured { get; set; }

    public string ConfigPath { get; set; } = string.Empty;

    public static AppServerConfig Load()
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var configPath = GetConfigPath(baseDirectory);
        if (File.Exists(configPath))
        {
            try
            {
                var text = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<AppServerConfig>(text, JsonOptions);
                if (config != null && !string.IsNullOrWhiteSpace(config.ApiBaseUrl))
                {
                    config.ConfigPath = configPath;
                    config.IsConfigured = true;
                    return NormalizeAndRepair(config);
                }
            }
            catch
            {
            }
        }

        return NormalizeAndRepair(new AppServerConfig
        {
            ApiBaseUrl = DefaultApiBaseUrl,
            ConfigPath = configPath,
            IsConfigured = false
        });
    }

    public void Save()
    {
        var resolvedPath = string.IsNullOrWhiteSpace(ConfigPath)
            ? GetConfigPath(AppDomain.CurrentDomain.BaseDirectory)
            : ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath)!);
        var payload = new AppServerConfig
        {
            ApiBaseUrl = NormalizeBaseUrl(ApiBaseUrl),
            IsConfigured = true
        };
        File.WriteAllText(resolvedPath, JsonSerializer.Serialize(payload, JsonOptions));
        ConfigPath = resolvedPath;
        IsConfigured = true;
    }

    public static string BuildManifestUrl(string apiBaseUrl)
    {
        return $"{NormalizeBaseUrl(apiBaseUrl)}/api/update/manifest/stable";
    }

    public static string NormalizeBaseUrl(string? apiBaseUrl)
    {
        var value = (apiBaseUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultApiBaseUrl;
        }

        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            value = "http://" + value;
        }

        return value.Trim().TrimEnd('/');
    }

    private static AppServerConfig NormalizeAndRepair(AppServerConfig config)
    {
        config.ApiBaseUrl = NormalizeBaseUrl(config.ApiBaseUrl);
        if (string.IsNullOrWhiteSpace(config.ApiBaseUrl))
        {
            config.ApiBaseUrl = DefaultApiBaseUrl;
        }

        var repairedBaseUrl = TryResolveReachableBaseUrl(config.ApiBaseUrl);
        if (!string.IsNullOrWhiteSpace(repairedBaseUrl) &&
            !string.Equals(repairedBaseUrl, config.ApiBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            config.ApiBaseUrl = repairedBaseUrl;
            if (!string.IsNullOrWhiteSpace(config.ConfigPath))
            {
                try
                {
                    config.Save();
                }
                catch
                {
                }
            }
        }

        return config;
    }

    private static string TryResolveReachableBaseUrl(string normalizedBaseUrl)
    {
        var candidates = BuildCandidateBaseUrls(normalizedBaseUrl);
        foreach (var candidate in candidates)
        {
            if (ProbeBaseUrl(candidate))
            {
                return candidate;
            }
        }

        return normalizedBaseUrl;
    }

    private static IReadOnlyList<string> BuildCandidateBaseUrls(string normalizedBaseUrl)
    {
        var results = new List<string>();
        AddCandidate(results, normalizedBaseUrl);

        if (Uri.TryCreate(normalizedBaseUrl, UriKind.Absolute, out var uri))
        {
            var host = uri.Host;
            var originalPort = uri.Port;
            var scheme = uri.Scheme.ToLowerInvariant();

            if (scheme == "https" && originalPort == 5157)
            {
                AddCandidate(results, $"http://{host}:5157");
                AddCandidate(results, $"https://{host}:7157");
            }
            else if (scheme == "http" && originalPort == 7157)
            {
                AddCandidate(results, $"https://{host}:7157");
                AddCandidate(results, $"http://{host}:5157");
            }
            else
            {
                AddCandidate(results, $"https://{host}:7157");
                AddCandidate(results, $"http://{host}:5157");
            }
        }

        return results;
    }

    private static void AddCandidate(ICollection<string> results, string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return;
        }

        var normalized = NormalizeBaseUrl(candidate);
        if (!results.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            results.Add(normalized);
        }
    }

    private static bool ProbeBaseUrl(string baseUrl)
    {
        try
        {
            var handler = new HttpClientHandler();
            if (baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
            }

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(2.5)
            };
            using var response = client.GetAsync(baseUrl).GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static string GetConfigPath(string baseDirectory)
    {
        return Path.Combine(baseDirectory, "membership-config.json");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}
