using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JSAI.WinApp;

internal sealed class MachineActivationStatus
{
    public bool IsActivated { get; init; }
    public string MachineCode { get; init; } = string.Empty;
    public string RegistrationCode { get; init; } = string.Empty;
    public DateTime? ActivatedAtUtc { get; init; }
    public bool HasCpuId { get; init; }
    public bool HasDiskSerial { get; init; }
    public string Message { get; init; } = string.Empty;
}

internal static class MachineActivationService
{
    private const string IntegritySecret = "JSAI.MachineActivation.Cache.2026.TerryHe20900";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static string ActivationPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "machine-license.dat");

    public static MachineActivationStatus LoadStatus()
    {
        var snapshot = MachineFingerprintService.GetSnapshot();
        var cache = LoadCache();
        if (cache == null)
        {
            return CreateStatus(snapshot, false, string.Empty, null, "当前机器尚未激活。");
        }

        if (!string.Equals(cache.MachineCode, snapshot.MachineCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(cache.FingerprintHash, MachineFingerprintService.ComputeFingerprintHash(snapshot.RawFingerprint), StringComparison.Ordinal) ||
            !IsCacheValid(cache) ||
            !ActivationCodeGenerator.IsRegistrationCodeValid(snapshot.MachineCode, cache.RegistrationCode))
        {
            return CreateStatus(snapshot, false, string.Empty, null, "本机授权缓存与当前硬件不匹配，请重新生成注册码。");
        }

        return CreateStatus(snapshot, true, cache.RegistrationCode, cache.ActivatedAtUtc, "本机已通过注册码激活。");
    }

    public static bool TryActivate(string registrationCode, out MachineActivationStatus status, out string message)
    {
        var snapshot = MachineFingerprintService.GetSnapshot();
        if (!ActivationCodeGenerator.IsRegistrationCodeValid(snapshot.MachineCode, registrationCode))
        {
            status = CreateStatus(snapshot, false, string.Empty, null, "注册码无效。");
            message = "注册码与当前机器码不匹配，请确认后台生成时使用的是本窗口显示的机器码。";
            return false;
        }

        var cache = new MachineActivationCache
        {
            MachineCode = snapshot.MachineCode,
            RegistrationCode = ActivationCodeGenerator.NormalizeRegistrationCode(registrationCode),
            FingerprintHash = MachineFingerprintService.ComputeFingerprintHash(snapshot.RawFingerprint),
            ActivatedAtUtc = DateTime.UtcNow,
        };
        cache.IntegritySignature = ComputeIntegrity(cache);
        SaveCache(cache);
        status = CreateStatus(snapshot, true, cache.RegistrationCode, cache.ActivatedAtUtc, "本机已通过注册码激活。");
        message = "激活成功，保存工程权限已解锁。";
        return true;
    }

    public static UserSessionResponse CreateSession(MachineActivationStatus status)
    {
        var activated = status.IsActivated;
        return new UserSessionResponse
        {
            Token = string.Empty,
            LastValidatedAtUtc = DateTime.UtcNow,
            OfflineGraceHours = int.MaxValue,
            User = new UserProfileResponse
            {
                UserId = status.MachineCode,
                Email = status.MachineCode,
                DisplayName = activated ? "本机已授权" : "本机未激活",
                MembershipPlan = activated ? "activation" : "unlicensed",
                HasActiveMembership = activated,
                IsTrial = !activated,
                CanSaveProjects = activated,
            },
        };
    }

    public static UserSessionResponse CreateUnlicensedSession(string machineCode)
    {
        return CreateSession(new MachineActivationStatus
        {
            IsActivated = false,
            MachineCode = machineCode,
            Message = "当前机器尚未激活。",
        });
    }

    private static MachineActivationStatus CreateStatus(
        MachineFingerprintSnapshot snapshot,
        bool activated,
        string registrationCode,
        DateTime? activatedAtUtc,
        string message)
    {
        return new MachineActivationStatus
        {
            IsActivated = activated,
            MachineCode = snapshot.MachineCode,
            RegistrationCode = registrationCode,
            ActivatedAtUtc = activatedAtUtc,
            HasCpuId = snapshot.HasCpuId,
            HasDiskSerial = snapshot.HasDiskSerial,
            Message = message,
        };
    }

    private static MachineActivationCache? LoadCache()
    {
        if (!File.Exists(ActivationPath))
        {
            return null;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(ActivationPath);
            var jsonBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<MachineActivationCache>(Encoding.UTF8.GetString(jsonBytes), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void SaveCache(MachineActivationCache cache)
    {
        var json = JsonSerializer.Serialize(cache, JsonOptions);
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(ActivationPath, protectedBytes);
    }

    private static bool IsCacheValid(MachineActivationCache cache)
    {
        if (string.IsNullOrWhiteSpace(cache.MachineCode) ||
            string.IsNullOrWhiteSpace(cache.RegistrationCode) ||
            string.IsNullOrWhiteSpace(cache.FingerprintHash) ||
            string.IsNullOrWhiteSpace(cache.IntegritySignature))
        {
            return false;
        }

        return string.Equals(cache.IntegritySignature, ComputeIntegrity(cache), StringComparison.Ordinal);
    }

    private static string ComputeIntegrity(MachineActivationCache cache)
    {
        var source = string.Join("|",
            cache.MachineCode,
            cache.RegistrationCode,
            cache.FingerprintHash,
            cache.ActivatedAtUtc.ToUniversalTime().ToString("O"),
            IntegritySecret);
        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(source)));
    }

    private sealed class MachineActivationCache
    {
        public string MachineCode { get; set; } = string.Empty;
        public string RegistrationCode { get; set; } = string.Empty;
        public string FingerprintHash { get; set; } = string.Empty;
        public DateTime ActivatedAtUtc { get; set; }
        public string IntegritySignature { get; set; } = string.Empty;
    }
}
