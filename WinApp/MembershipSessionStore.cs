using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JSAI.WinApp;

internal static class MembershipSessionStore
{
    private const int DefaultOfflineGraceHours = 24 * 7;
    private const string IntegritySecret = "JSAI.MemberCache.2026.TerryHe20900";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static string SessionPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "member-session.dat");

    public static MembershipSessionCache? Load()
    {
        if (!File.Exists(SessionPath))
        {
            return null;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(SessionPath);
            var jsonBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(jsonBytes);
            var cache = JsonSerializer.Deserialize<MembershipSessionCache>(json, JsonOptions);
            if (cache == null)
            {
                return null;
            }

            if (!IsCacheValid(cache))
            {
                return null;
            }

            cache.OfflineGraceHours = cache.OfflineGraceHours <= 0 ? DefaultOfflineGraceHours : cache.OfflineGraceHours;
            return cache;
        }
        catch
        {
            return null;
        }
    }

    public static void Save(UserSessionResponse session)
    {
        var machineBinding = ComputeMachineBinding();
        var cache = new MembershipSessionCache
        {
            Token = session.Token,
            User = session.User,
            Orders = session.Orders ?? new List<MembershipOrderResponse>(),
            LastValidatedAtUtc = session.LastValidatedAtUtc == default ? DateTime.UtcNow : session.LastValidatedAtUtc,
            OfflineGraceHours = session.OfflineGraceHours <= 0 ? DefaultOfflineGraceHours : session.OfflineGraceHours,
            MachineBinding = machineBinding,
        };
        cache.IntegritySignature = ComputeIntegritySignature(cache);

        var json = JsonSerializer.Serialize(cache, JsonOptions);
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(jsonBytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(SessionPath, protectedBytes);
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(SessionPath))
            {
                File.Delete(SessionPath);
            }
        }
        catch
        {
        }
    }

    public static bool CanUseOffline(MembershipSessionCache? cache, out string message)
    {
        if (cache?.User == null || string.IsNullOrWhiteSpace(cache.Token))
        {
            message = "本地没有可用的会员缓存，请连接服务器重新登录。";
            return false;
        }

        if (!IsCacheValid(cache))
        {
            message = "本地会员缓存校验失败，请重新连接服务器验证身份。";
            return false;
        }

        if (!cache.User.HasActiveMembership || !cache.User.MembershipExpiresAt.HasValue)
        {
            message = "本地缓存中的会员状态已失效。";
            return false;
        }

        if (cache.User.MembershipExpiresAt.Value <= DateTime.Now)
        {
            message = "会员已到期，请连接服务器续费。";
            return false;
        }

        var offlineGraceHours = cache.OfflineGraceHours <= 0 ? DefaultOfflineGraceHours : cache.OfflineGraceHours;
        if (cache.LastValidatedAtUtc.AddHours(offlineGraceHours) < DateTime.UtcNow)
        {
            message = "已超过 7 天未连接服务器验证身份，软件已自动锁定，请重新联网登录。";
            return false;
        }

        message = "服务器暂时不可用，已使用本地加密会员缓存进入离线模式。";
        return true;
    }

    private static bool IsCacheValid(MembershipSessionCache cache)
    {
        if (string.IsNullOrWhiteSpace(cache.MachineBinding) ||
            string.IsNullOrWhiteSpace(cache.IntegritySignature))
        {
            return false;
        }

        var currentBinding = ComputeMachineBinding();
        if (!string.Equals(cache.MachineBinding, currentBinding, StringComparison.Ordinal))
        {
            return false;
        }

        var expected = ComputeIntegritySignature(cache);
        return string.Equals(cache.IntegritySignature, expected, StringComparison.Ordinal);
    }

    private static string ComputeMachineBinding()
    {
        var raw = $"{Environment.MachineName}|{Environment.UserName}|{AppDomain.CurrentDomain.BaseDirectory}";
        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(raw)));
    }

    private static string ComputeIntegritySignature(MembershipSessionCache cache)
    {
        var source = string.Join("|",
            cache.Token,
            cache.User?.UserId ?? string.Empty,
            cache.User?.Email ?? string.Empty,
            cache.User?.MembershipPlan ?? string.Empty,
            cache.User?.MembershipExpiresAt?.ToUniversalTime().ToString("O") ?? string.Empty,
            cache.User?.TrialExpiresAt?.ToUniversalTime().ToString("O") ?? string.Empty,
            cache.User?.HasActiveMembership == true ? "1" : "0",
            cache.User?.IsTrial == true ? "1" : "0",
            cache.User?.CanSaveProjects == true ? "1" : "0",
            cache.LastValidatedAtUtc.ToUniversalTime().ToString("O"),
            cache.OfflineGraceHours.ToString(),
            cache.MachineBinding,
            IntegritySecret);

        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(source)));
    }
}
