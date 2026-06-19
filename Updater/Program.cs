using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace JSAI.Updater;

internal static class Program
{
    private const string FromUpdaterArgument = "--from-updater";

    [STAThread]
    private static async Task Main(string[] args)
    {
        var mainExePath = ResolveMainExePath(args);
        if (string.IsNullOrWhiteSpace(mainExePath) || !File.Exists(mainExePath))
        {
            return;
        }

        var applicationDirectory = Path.GetDirectoryName(mainExePath) ?? AppDomain.CurrentDomain.BaseDirectory;
        var logger = new UpdaterLogger(applicationDirectory);
        try
        {
            var config = UpdateConfig.Load(applicationDirectory);
            if (config == null || string.IsNullOrWhiteSpace(config.ManifestUrl))
            {
                LaunchMain(mainExePath, logger);
                return;
            }

            var localVersion = ReadLocalVersion(mainExePath);
            var manifest = await FetchManifestAsync(config, logger);
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version))
            {
                LaunchMain(mainExePath, logger);
                return;
            }

            if (!IsRemoteNewer(localVersion, manifest.Version))
            {
                logger.Info($"No update required. Local={localVersion}, Remote={manifest.Version}");
                LaunchMain(mainExePath, logger);
                return;
            }

            logger.Info($"Update found. Local={localVersion}, Remote={manifest.Version}");
            if (!string.IsNullOrWhiteSpace(manifest.PackageUrl))
            {
                await DownloadAndApplyAsync(applicationDirectory, manifest, logger);
            }

            LaunchMain(mainExePath, logger);
        }
        catch (Exception ex)
        {
            logger.Error("Updater failed, continue launching main application.", ex);
            LaunchMain(mainExePath, logger);
        }
    }

    private static string ResolveMainExePath(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], "--main", StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1].Trim('"');
            }
        }

        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WinApp.exe");
    }

    private static async Task<UpdateManifest?> FetchManifestAsync(UpdateConfig config, UpdaterLogger logger)
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(3, config.RequestTimeoutSeconds)),
        };

        try
        {
            var response = await client.GetAsync(config.ManifestUrl);
            if (!response.IsSuccessStatusCode)
            {
                logger.Info($"Manifest request returned {(int)response.StatusCode}.");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions.Default);
            if (manifest == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(manifest.PackageUrl) &&
                Uri.TryCreate(config.ManifestUrl, UriKind.Absolute, out var baseUri) &&
                Uri.TryCreate(baseUri, manifest.PackageUrl, out var packageUri))
            {
                manifest.PackageUrl = packageUri.ToString();
            }

            return manifest;
        }
        catch (Exception ex)
        {
            logger.Error("Failed to fetch manifest.", ex);
            return null;
        }
    }

    private static async Task DownloadAndApplyAsync(string applicationDirectory, UpdateManifest manifest, UpdaterLogger logger)
    {
        if (string.IsNullOrWhiteSpace(manifest.PackageUrl))
        {
            return;
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), $"JSAI_Update_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var packagePath = Path.Combine(tempRoot, "package.zip");
        var extractPath = Path.Combine(tempRoot, "extract");
        Directory.CreateDirectory(extractPath);

        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(3),
            };

            logger.Info($"Downloading update package from {manifest.PackageUrl}");
            await using (var packageStream = File.Create(packagePath))
            await using (var responseStream = await client.GetStreamAsync(manifest.PackageUrl))
            {
                await responseStream.CopyToAsync(packageStream);
            }

            ZipFile.ExtractToDirectory(packagePath, extractPath, overwriteFiles: true);
            CopyDirectory(extractPath, applicationDirectory, logger);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory, UpdaterLogger logger)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(targetDirectory, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            if (ShouldSkip(relative))
            {
                continue;
            }

            var destination = Path.Combine(targetDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            Retry(6, 500, () => File.Copy(file, destination, overwrite: true));
            logger.Info($"Updated file: {relative}");
        }
    }

    private static bool ShouldSkip(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);
        return fileName.Equals("Updater.exe", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("Updater.dll", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("Updater.deps.json", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("Updater.runtimeconfig.json", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadLocalVersion(string mainExePath)
    {
        var fileVersion = FileVersionInfo.GetVersionInfo(mainExePath).FileVersion;
        if (!string.IsNullOrWhiteSpace(fileVersion))
        {
            return fileVersion;
        }

        return "0.0.0";
    }

    private static bool IsRemoteNewer(string localVersionText, string remoteVersionText)
    {
        var localVersion = NormalizeVersion(localVersionText);
        var remoteVersion = NormalizeVersion(remoteVersionText);
        return remoteVersion > localVersion;
    }

    private static Version NormalizeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new Version(0, 0, 0);
        }

        var candidate = value.Trim();
        var dashIndex = candidate.IndexOf('-', StringComparison.Ordinal);
        if (dashIndex >= 0)
        {
            candidate = candidate[..dashIndex];
        }

        return Version.TryParse(candidate, out var version)
            ? version
            : new Version(0, 0, 0);
    }

    private static void LaunchMain(string mainExePath, UpdaterLogger logger)
    {
        logger.Info($"Launching main application: {mainExePath}");
        Process.Start(new ProcessStartInfo
        {
            FileName = mainExePath,
            Arguments = FromUpdaterArgument,
            WorkingDirectory = Path.GetDirectoryName(mainExePath) ?? AppDomain.CurrentDomain.BaseDirectory,
            UseShellExecute = true,
        });
    }

    private static void Retry(int attempts, int delayMs, Action action)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                Thread.Sleep(delayMs);
            }
        }

        throw last ?? new IOException("Retry failed.");
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
        }
    }
}

internal sealed class UpdateConfig
{
    public string ManifestUrl { get; set; } = string.Empty;
    public string Channel { get; set; } = "stable";
    public int RequestTimeoutSeconds { get; set; } = 8;

    public static UpdateConfig? Load(string baseDirectory)
    {
        var path = Path.Combine(baseDirectory, "update-config.json");
        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<UpdateConfig>(json, JsonOptions.Default);
    }
}

internal sealed class UpdateManifest
{
    public string Version { get; set; } = string.Empty;
    public string PackageUrl { get; set; } = string.Empty;
    public string Channel { get; set; } = "stable";
    public bool Mandatory { get; set; }
    public string Notes { get; set; } = string.Empty;
}

internal sealed class UpdaterLogger
{
    private readonly string _logPath;

    public UpdaterLogger(string baseDirectory)
    {
        _logPath = Path.Combine(baseDirectory, "updater.log");
    }

    public void Info(string message)
    {
        Write("INFO", message, null);
    }

    public void Error(string message, Exception? exception)
    {
        Write("ERROR", message, exception);
    }

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            var text =
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}" +
                $"{exception}{Environment.NewLine}";
            File.AppendAllText(_logPath, text);
        }
        catch
        {
        }
    }
}

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}
