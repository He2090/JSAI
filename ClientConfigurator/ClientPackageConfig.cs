using System.Text.Json;

namespace JSAI.ClientConfigurator;

internal sealed class ClientPackageConfig
{
    public string ApiBaseUrl { get; set; } = "http://127.0.0.1:5157";
    public string ManifestUrl { get; set; } = "http://127.0.0.1:5157/api/update/manifest/stable";
    public string Channel { get; set; } = "stable";
    public int RequestTimeoutSeconds { get; set; } = 8;

    public static ClientPackageConfig Load(string targetDirectory)
    {
        var config = new ClientPackageConfig();
        var membershipPath = Path.Combine(targetDirectory, "membership-config.json");
        var updatePath = Path.Combine(targetDirectory, "update-config.json");

        if (File.Exists(membershipPath))
        {
            try
            {
                var membership = JsonSerializer.Deserialize<MembershipConfig>(
                    File.ReadAllText(membershipPath), JsonOptions);
                if (!string.IsNullOrWhiteSpace(membership?.ApiBaseUrl))
                {
                    config.ApiBaseUrl = membership.ApiBaseUrl.Trim().TrimEnd('/');
                }
            }
            catch
            {
            }
        }

        if (File.Exists(updatePath))
        {
            try
            {
                var update = JsonSerializer.Deserialize<UpdateConfig>(
                    File.ReadAllText(updatePath), JsonOptions);
                if (!string.IsNullOrWhiteSpace(update?.ManifestUrl))
                {
                    config.ManifestUrl = update.ManifestUrl.Trim();
                }

                if (!string.IsNullOrWhiteSpace(update?.Channel))
                {
                    config.Channel = update.Channel;
                }

                if (update?.RequestTimeoutSeconds > 0)
                {
                    config.RequestTimeoutSeconds = update.RequestTimeoutSeconds;
                }
            }
            catch
            {
            }
        }

        return config;
    }

    public void Save(string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        var membershipPath = Path.Combine(targetDirectory, "membership-config.json");
        var updatePath = Path.Combine(targetDirectory, "update-config.json");

        File.WriteAllText(membershipPath, JsonSerializer.Serialize(new MembershipConfig
        {
            ApiBaseUrl = ApiBaseUrl.Trim().TrimEnd('/'),
        }, JsonOptions));

        File.WriteAllText(updatePath, JsonSerializer.Serialize(new UpdateConfig
        {
            ManifestUrl = ManifestUrl.Trim(),
            Channel = string.IsNullOrWhiteSpace(Channel) ? "stable" : Channel.Trim(),
            RequestTimeoutSeconds = RequestTimeoutSeconds <= 0 ? 8 : RequestTimeoutSeconds,
        }, JsonOptions));
    }

    public static string BuildManifestUrl(string serverBaseUrl)
    {
        var normalized = NormalizeServerBaseUrl(serverBaseUrl);
        return $"{normalized}/api/update/manifest/stable";
    }

    public static string NormalizeServerBaseUrl(string serverBaseUrl)
    {
        var value = (serverBaseUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return "http://127.0.0.1:5157";
        }

        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            value = "http://" + value;
        }

        return value.TrimEnd('/');
    }

    private sealed class MembershipConfig
    {
        public string ApiBaseUrl { get; set; } = string.Empty;
    }

    private sealed class UpdateConfig
    {
        public string ManifestUrl { get; set; } = string.Empty;
        public string Channel { get; set; } = "stable";
        public int RequestTimeoutSeconds { get; set; } = 8;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}
