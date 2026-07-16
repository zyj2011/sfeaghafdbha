using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using NewEastSide.Core.Api.Entities;
using NewEastSide.Core.Api.Entities.System;
using Serilog;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NewEastSide.Core.Api;

public class SupabaseApi : IDisposable
{
    private static readonly Lazy<SupabaseApi> _instance = new(() => new SupabaseApi());
    public static SupabaseApi Instance => _instance.Value;

    private const string ProjectRef = "hddbbytazxevekgghgfv";
    private const string AnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImhkZGJieXRhenhldmVrZ2doZ2Z2Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODI1MzA3ODksImV4cCI6MjA5ODEwNjc4OX0.xt8UkhYyD5WvBx3Gl-3XiP2X_lSetmTIpRLpibKpDmU";

    public string BaseUrl { get; }
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private SupabaseApi()
    {
        BaseUrl = $"https://{ProjectRef}.supabase.co/auth/v1";

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(BaseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.Add("apikey", AnonKey);
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {AnonKey}");
        _http.DefaultRequestHeaders.Add("X-Client-Info", "NewEastSide");

        Log.Information("Supabase API 初始化完成: BaseUrl={BaseUrl}", BaseUrl);
    }

    public async Task<SupabaseAuthResult> SignUpAsync(string email, string password, CancellationToken ct = default)
    {
        // 添加密码长度检查（Supabase 要求至少6位）
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
        {
            return new SupabaseAuthResult
            {
                Error = "invalid_password",
                ErrorDescription = "密码长度至少为6位"
            };
        }
        // email 格式检查
        if (!email.Contains("@") || !email.Contains("."))
        {
            return new SupabaseAuthResult
            {
                Error = "invalid_email",
                ErrorDescription = "请输入有效的邮箱地址"
            };
        }

        Log.Information("Supabase 注册请求: email={Email}, passwordLength={PasswordLength}", email, password.Length);

        // 手动构建请求以调试路径问题
        var effectiveCt = ct == default ? CancellationToken.None : ct;
        await _gate.WaitAsync(effectiveCt);
        try
        {
            var jsonBody = JsonSerializer.Serialize(new { email, password }, JsonOptions);
            Log.Debug("请求体: {Body}", jsonBody);

            var fullUrl = BaseUrl + "/signup";
            Log.Debug("完整请求URL: {FullUrl}", fullUrl);

            var request = new HttpRequestMessage(HttpMethod.Post, fullUrl)
            {
                Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json")
            };

            using var resp = await _http.SendAsync(request, effectiveCt);
            var responseJson = await resp.Content.ReadAsStringAsync(effectiveCt);

            Log.Debug("Supabase 响应: StatusCode={StatusCode}, Response={Response}", resp.StatusCode, responseJson);

            try
            {
                var result = JsonSerializer.Deserialize<SupabaseAuthResult>(responseJson, JsonOptions);
                if (result != null) return result;
            }
            catch (JsonException ex2)
            {
                Log.Error(ex2, "JSON解析失败: {ResponseJson}", responseJson);
            }

            if (!resp.IsSuccessStatusCode)
            {
                return new SupabaseAuthResult
                {
                    Error = $"http_{(int)resp.StatusCode}",
                    ErrorDescription = $"请求失败 ({resp.StatusCode})"
                };
            }

            return new SupabaseAuthResult
            {
                Error = "parse_error",
                ErrorDescription = "响应解析失败"
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "注册请求异常");
            return new SupabaseAuthResult
            {
                Error = "unknown_error",
                ErrorDescription = ex.Message
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 获取下一个用户编号（从1开始递增）
    /// </summary>
    public async Task<int> GetNextUserNumberAsync(CancellationToken ct = default)
    {
        var effectiveCt = ct == default ? CancellationToken.None : ct;
        await _gate.WaitAsync(effectiveCt);
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "/user")
            {
                Headers = { { "apikey", AnonKey }, { "Authorization", $"Bearer {AnonKey}" } }
            };

            // 先获取当前用户数（通过管理员接口或计数）
            // 由于 anon key 权限限制，这里简化为返回当前时间戳作为编号
            // 实际应该查询数据库中的用户总数
            var responseJson = await request.Content?.ReadAsStringAsync(effectiveCt) ?? "";

            // 简化实现：使用当前时间戳的后几位作为编号
            // TODO: 实际应该查询 Supabase 数据库中的用户总数
            return (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 100000);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SupabaseAuthResult> SignInWithEmailAsync(string email, string password, CancellationToken ct = default)
    {
        return await PostAsync<SupabaseAuthResult>("/token?grant_type=password", new { email, password }, ct);
    }

    public async Task<SupabaseUser?> GetUserAsync(string accessToken, CancellationToken ct = default)
    {
        try
        {
            var effectiveCt = ct == default ? CancellationToken.None : ct;
            await _gate.WaitAsync(effectiveCt);
            using var resp = await _http.GetAsync("/user", effectiveCt);
            resp.Headers.Remove("Authorization");
            resp.Headers.Add("Authorization", $"Bearer {accessToken}");
            var json = await resp.Content.ReadAsStringAsync(effectiveCt);
            if (!resp.IsSuccessStatusCode)
            {
                Log.Error("获取用户信息失败: StatusCode={StatusCode}, Response={Response}", resp.StatusCode, json);
                return null;
            }
            return JsonSerializer.Deserialize<SupabaseUser>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "获取用户信息失败");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SupabaseAuthResult> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        return await PostAsync<SupabaseAuthResult>("/token?grant_type=refresh_token", new { refresh_token = refreshToken }, ct);
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct) where T : class, new()
    {
        var effectiveCt = ct == default ? CancellationToken.None : ct;
        await _gate.WaitAsync(effectiveCt);
        try
        {
            var jsonBody = JsonSerializer.Serialize(body, JsonOptions);
            Log.Debug("Supabase API 请求: Path={Path}, FullUrl={FullUrl}, Body: {Body}", path, BaseUrl + path, jsonBody);

            // 使用 BaseAddress + path 构建完整 URL，避免路径解析问题
            var fullUrl = BaseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
            Log.Information("Supabase 完整请求 URL: {FullUrl}", fullUrl);

            var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json")
            };

            using var resp = await _http.SendAsync(request, effectiveCt);
            var responseJson = await resp.Content.ReadAsStringAsync(effectiveCt);

            Log.Information("Supabase 响应: {Path} -> StatusCode={StatusCode}, Response: {Response}",
                path, resp.StatusCode, responseJson);

            try
            {
                var result = JsonSerializer.Deserialize<T>(responseJson, JsonOptions);
                if (result != null) return result;
            }
            catch (JsonException)
            {
            }

            if (!resp.IsSuccessStatusCode)
            {
                Log.Error("请求失败: {Path}, StatusCode: {StatusCode}, Response: {Response}", path, resp.StatusCode, responseJson);
                var error = new T();
                if (error is SupabaseAuthResult apiResp)
                {
                    apiResp.Error = $"http_{(int)resp.StatusCode}";
                    apiResp.ErrorDescription = $"请求失败: {path} ({resp.StatusCode})";
                }
                return error;
            }

            Log.Error("解析响应失败: {Path}, JSON: {ResponseJson}", path, responseJson);
            var parseError = new T();
            if (parseError is SupabaseAuthResult apiResp2)
            {
                apiResp2.Error = "parse_error";
                apiResp2.ErrorDescription = $"响应解析失败: {path}";
            }
            return parseError;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "未知错误: {Path}", path);
            var error = new T();
            if (error is SupabaseAuthResult apiResp)
            {
                apiResp.Error = "unknown_error";
                apiResp.ErrorDescription = "请求失败: " + ex.Message;
            }
            return error;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 获取公告列表（从 Supabase 数据库）
    /// </summary>
    public async Task<AnnouncementResponse> GetAnnouncementAsync(CancellationToken ct = default)
    {
        var effectiveCt = ct == default ? CancellationToken.None : ct;
        await _gate.WaitAsync(effectiveCt);
        try
        {
            var fullUrl = BaseUrl.Replace("/auth/v1", "/rest/v1/") + "announcements?select=*";
            Log.Information("请求公告: URL={Url}", fullUrl);

            var request = new HttpRequestMessage(HttpMethod.Get, fullUrl)
            {
                Headers = { { "apikey", AnonKey }, { "Authorization", $"Bearer {AnonKey}" }, { "Prefer", "count=exact" } }
            };

            using var resp = await _http.SendAsync(request, effectiveCt);
            var json = await resp.Content.ReadAsStringAsync(effectiveCt);

            Log.Information("公告响应: StatusCode={StatusCode}, Response={Response}", resp.StatusCode, json);

            if (!resp.IsSuccessStatusCode)
            {
                Log.Warning("获取公告失败: StatusCode={StatusCode}", resp.StatusCode);
                // 如果表不存在或无数据，返回空结果
                return new AnnouncementResponse { Success = true, Title = "", Content = "", Level = "info" };
            }

            var announcements = System.Text.Json.JsonSerializer.Deserialize<List<AnnouncementDto>>(json, JsonOptions);
            if (announcements == null || announcements.Count == 0)
            {
                return new AnnouncementResponse { Success = true, Title = "", Content = "", Level = "info" };
            }

            var latest = announcements[0];
            Log.Information("获取到公告: Title={Title}, Content={Content}", latest.Title, latest.Content);
            return new AnnouncementResponse
            {
                Success = true,
                Title = latest.Title,
                Content = latest.Content,
                Level = latest.Level ?? "info",
                Updated = latest.Updated
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "获取公告异常");
            return new AnnouncementResponse { Success = true, Title = "", Content = "", Level = "info" };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 设置公告（管理员功能，需要 service role key）
    /// </summary>
    public async Task<bool> SetAnnouncementAsync(string title, string content, string level = "info", CancellationToken ct = default)
    {
        var effectiveCt = ct == default ? CancellationToken.None : ct;
        await _gate.WaitAsync(effectiveCt);
        try
        {
            var announcement = new
            {
                title,
                content,
                level,
                updated = DateTime.UtcNow.ToString("o")
            };

            var jsonBody = System.Text.Json.JsonSerializer.Serialize(announcement, JsonOptions);
            var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl.Replace("/auth/v1", "/rest/v1/") + "announcements")
            {
                Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json"),
                Headers = { { "apikey", AnonKey }, { "Authorization", $"Bearer {AnonKey}" }, { "Prefer", "return=representation" } }
            };

            using var resp = await _http.SendAsync(request, effectiveCt);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "设置公告失败");
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }
}

public class AnnouncementDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("level")]
    public string? Level { get; set; }

    [JsonPropertyName("updated")]
    public string? Updated { get; set; }
}

public class SupabaseAuthResult
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("expires_at")]
    public long ExpiresAt { get; set; }

    [JsonPropertyName("user")]
    public SupabaseUser? User { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    public bool Success => !string.IsNullOrWhiteSpace(AccessToken);
}

public class SupabaseUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("confirmed_at")]
    public string? ConfirmedAt { get; set; }

    [JsonPropertyName("app_metadata")]
    public SupabaseAppMetadata? AppMetadata { get; set; }

    [JsonPropertyName("user_metadata")]
    public SupabaseUserMetadata? UserMetadata { get; set; }
}

public class SupabaseAppMetadata
{
    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("providers")]
    public string[]? Providers { get; set; }
}

public class SupabaseUserMetadata
{
    [JsonPropertyName("full_name")]
    public string? FullName { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("user_number")]
    public int? UserNumber { get; set; }
}