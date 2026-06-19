using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace JSAI.WinApp;

internal sealed class MembershipApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _baseUrl;
    private readonly HttpClient _client;

    public MembershipApiClient(string baseUrl)
    {
        _baseUrl = (baseUrl ?? string.Empty).Trim().TrimEnd('/');

        var handler = new HttpClientHandler();
        if (_baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }

        _client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(12),
        };
    }

    public async Task<(bool success, string message, VerificationCodeResponse? response, bool serverUnavailable)> SendRegisterCodeAsync(string email)
    {
        return await SendAsync<SendVerificationCodeRequest, VerificationCodeResponse>(
            "/api/auth/send-register-code",
            new SendVerificationCodeRequest { Email = email, Purpose = "register" });
    }

    public async Task<(bool success, string message, VerificationCodeResponse? response, bool serverUnavailable)> SendLoginCodeAsync(string email)
    {
        return await SendAsync<SendVerificationCodeRequest, VerificationCodeResponse>(
            "/api/auth/send-login-code",
            new SendVerificationCodeRequest { Email = email, Purpose = "login" });
    }

    public async Task<(bool success, string message, VerificationCodeResponse? response, bool serverUnavailable)> SendResetPasswordCodeAsync(string email)
    {
        return await SendAsync<SendVerificationCodeRequest, VerificationCodeResponse>(
            "/api/auth/send-reset-password-code",
            new SendVerificationCodeRequest { Email = email, Purpose = "reset-password" });
    }

    public async Task<(bool success, string message, LoginCaptchaResponse? response, bool serverUnavailable)> GetLoginCaptchaAsync()
    {
        try
        {
            using var response = await _client.GetAsync($"{_baseUrl}/api/auth/login-captcha");
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                return (false, ExtractMessage(content, "获取登录验证码失败。"), null, false);
            }

            var captcha = JsonSerializer.Deserialize<LoginCaptchaResponse>(content, JsonOptions);
            return captcha != null
                ? (true, string.Empty, captcha, false)
                : (false, "服务器返回了空验证码。", null, false);
        }
        catch (Exception ex)
        {
            return (false, $"无法连接会员服务器：{ex.Message}", null, true);
        }
    }

    public async Task<(bool success, string message, UserSessionResponse? session, bool serverUnavailable)> RegisterAsync(RegisterRequest request)
    {
        return await SendAsync<RegisterRequest, UserSessionResponse>("/api/auth/register", request);
    }

    public async Task<(bool success, string message, UserSessionResponse? session, bool serverUnavailable)> LoginAsync(LoginRequest request)
    {
        return await SendAsync<LoginRequest, UserSessionResponse>("/api/auth/login", request);
    }

    public async Task<(bool success, string message, UserSessionResponse? session, bool serverUnavailable)> AutoLoginAsync(AutoLoginRequest request)
    {
        return await SendAsync<AutoLoginRequest, UserSessionResponse>("/api/auth/auto-login", request);
    }

    public async Task<(bool success, string message, UserSessionResponse? session, bool serverUnavailable)> ResetPasswordAsync(ResetPasswordRequest request)
    {
        return await SendAsync<ResetPasswordRequest, UserSessionResponse>("/api/auth/reset-password", request);
    }

    public async Task<(bool success, string message, UserSessionResponse? session, bool serverUnavailable)> ValidateSessionAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, "缺少登录令牌。", null, false);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/auth/session");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _client.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                return (false, ExtractMessage(content, "登录状态已失效。"), null, false);
            }

            var session = JsonSerializer.Deserialize<UserSessionResponse>(content, JsonOptions);
            return session != null
                ? (true, "登录状态有效。", session, false)
                : (false, "服务器返回了空的会话结果。", null, false);
        }
        catch (Exception ex)
        {
            return (false, $"无法连接会员服务器：{ex.Message}", null, true);
        }
    }

    public async Task<(bool success, string message, IReadOnlyList<MembershipPlanResponse> plans, bool serverUnavailable)> GetPlansAsync()
    {
        try
        {
            using var response = await _client.GetAsync($"{_baseUrl}/api/membership/plans");
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                return (false, ExtractMessage(content, "获取会员套餐失败。"), Array.Empty<MembershipPlanResponse>(), false);
            }

            var plans = JsonSerializer.Deserialize<List<MembershipPlanResponse>>(content, JsonOptions) ?? new List<MembershipPlanResponse>();
            return (true, string.Empty, plans, false);
        }
        catch (Exception ex)
        {
            return (false, $"无法获取会员套餐：{ex.Message}", Array.Empty<MembershipPlanResponse>(), true);
        }
    }

    public async Task<(bool success, string message, IReadOnlyList<MembershipOrderResponse> orders, bool serverUnavailable)> GetOrdersAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, "缺少登录令牌。", Array.Empty<MembershipOrderResponse>(), false);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/membership/orders");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _client.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                return (false, ExtractMessage(content, "获取会员订单失败。"), Array.Empty<MembershipOrderResponse>(), false);
            }

            var orders = JsonSerializer.Deserialize<List<MembershipOrderResponse>>(content, JsonOptions) ?? new List<MembershipOrderResponse>();
            return (true, string.Empty, orders, false);
        }
        catch (Exception ex)
        {
            return (false, $"无法获取会员订单：{ex.Message}", Array.Empty<MembershipOrderResponse>(), true);
        }
    }

    public async Task<(bool success, string message, UserSessionResponse? session, bool serverUnavailable)> SubscribeAsync(string token, string planCode)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, "缺少登录令牌。", null, false);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/membership/subscribe");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = CreateJsonContent(new SubscribeRequest { PlanCode = planCode });
            using var response = await _client.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                return (false, ExtractMessage(content, "开通会员失败。"), null, false);
            }

            var session = JsonSerializer.Deserialize<UserSessionResponse>(content, JsonOptions);
            return session != null
                ? (true, "会员开通成功。", session, false)
                : (false, "服务器未返回会员结果。", null, false);
        }
        catch (Exception ex)
        {
            return (false, $"无法开通会员：{ex.Message}", null, true);
        }
    }

    public async Task<(bool success, string message, UserSessionResponse? session, bool serverUnavailable)> ChangePasswordAsync(string token, ChangePasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, "缺少登录令牌。", null, false);
        }

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/auth/change-password");
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            httpRequest.Content = CreateJsonContent(request);
            using var response = await _client.SendAsync(httpRequest);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                return (false, ExtractMessage(content, "修改密码失败。"), null, false);
            }

            var session = JsonSerializer.Deserialize<UserSessionResponse>(content, JsonOptions);
            return session != null
                ? (true, "密码修改成功。", session, false)
                : (false, "服务器未返回有效会话。", null, false);
        }
        catch (Exception ex)
        {
            return (false, $"无法修改密码：{ex.Message}", null, true);
        }
    }

    public async Task LogoutAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/auth/logout");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            await _client.SendAsync(request);
        }
        catch
        {
        }
    }

    private async Task<(bool success, string message, TResponse? response, bool serverUnavailable)> SendAsync<TRequest, TResponse>(string path, TRequest payload)
        where TRequest : class
        where TResponse : class
    {
        try
        {
            using var response = await _client.PostAsync($"{_baseUrl}{path}", CreateJsonContent(payload));
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                return (false, ExtractMessage(content, "请求失败。"), null, false);
            }

            var data = JsonSerializer.Deserialize<TResponse>(content, JsonOptions);
            return data != null
                ? (true, string.Empty, data, false)
                : (false, "服务器返回了空结果。", null, false);
        }
        catch (Exception ex)
        {
            return (false, $"无法连接会员服务器：{ex.Message}", null, true);
        }
    }

    private static StringContent CreateJsonContent<T>(T payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static string ExtractMessage(string json, string fallback)
    {
        try
        {
            var error = JsonSerializer.Deserialize<ApiErrorResponse>(json, JsonOptions);
            return string.IsNullOrWhiteSpace(error?.Message) ? fallback : error.Message;
        }
        catch
        {
            return fallback;
        }
    }
}
