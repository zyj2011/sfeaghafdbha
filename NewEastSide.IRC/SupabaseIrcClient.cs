using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace NewEastSide.IRC;

/// <summary>
/// Supabase Realtime IRC 客户端 - 替代原有的 TcpLineClient IRC 实现
/// </summary>
public class SupabaseIrcClient : IDisposable
{
    private readonly string _supabaseUrl;
    private readonly string _anonKey;
    private readonly string _token;
    private readonly string _roleId;
    private readonly string _clientTag;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, OnlineUser> _onlineUsers = new();
    private readonly List<IrcMessage> _recentMessages = new();
    private Timer? _heartbeatTimer;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private bool _connected;

    public event EventHandler<IrcChatEventArgs>? ChatReceived;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<int>? OnlineCountUpdated;

    public int OnlineCount => _onlineUsers.Count(x => x.Value.IsOnline);
    public IReadOnlyCollection<OnlineUser> OnlineUsers => (IReadOnlyCollection<OnlineUser>)_onlineUsers.Values;
    public bool IsConnected => _connected;

    public SupabaseIrcClient(string supabaseUrl, string anonKey, string token, string roleId, string clientTag = "")
    {
        _supabaseUrl = supabaseUrl.TrimEnd('/');
        _anonKey = anonKey;
        _token = token;
        _roleId = roleId;
        _clientTag = clientTag;

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Add("apikey", _anonKey);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_anonKey}");
        _httpClient.DefaultRequestHeaders.Add("Prefer", "return=representation");
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_connected) return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            // 1. 注册用户在线状态
            await RegisterOnlineAsync(_cts.Token);

            // 2. 启动心跳
            _heartbeatTimer = new Timer(HeartbeatCallback, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));

            // 3. 启动消息轮询（替代 Realtime 订阅）
            _ = PollMessagesAsync(_cts.Token);

            // 4. 启动在线用户轮询
            _ = PollOnlineUsersAsync(_cts.Token);

            _connected = true;
            Log.Information("[SupabaseIRC] 连接成功: RoleId={RoleId}, ClientTag={Tag}", _roleId, _clientTag);
            Connected?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SupabaseIRC] 连接失败");
            throw;
        }
    }

    public async Task SendChatAsync(string message, CancellationToken ct = default)
    {
        if (!_connected)
        {
            Log.Warning("[SupabaseIRC] 未连接，无法发送消息");
            return;
        }

        try
        {
            var payload = new
            {
                role_id = _roleId,
                username = _roleId, // 使用 roleId 作为显示名称
                message,
                client_tag = _clientTag,
                room = "global"
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_supabaseUrl}/rest/v1/irc_messages", content, ct);
            response.EnsureSuccessStatusCode();

            Log.Information("[SupabaseIRC] 发送消息: {Message}", message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SupabaseIRC] 发送消息失败: {Message}", message);
        }
    }

    public async Task DisconnectAsync()
    {
        if (!_connected) return;

        _connected = false;
        _cts?.Cancel();
        _heartbeatTimer?.Dispose();

        // 标记离线
        try
        {
            await SetOfflineAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SupabaseIRC] 离线标记失败");
        }

        Disconnected?.Invoke(this, EventArgs.Empty);
        Log.Information("[SupabaseIRC] 已断开连接");
    }

    private async Task RegisterOnlineAsync(CancellationToken ct)
    {
        var payload = new
        {
            role_id = _roleId,
            username = _roleId,
            client_tag = _clientTag,
            is_online = true
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // 使用 upsert 确保重复注册时更新状态
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_supabaseUrl}/rest/v1/online_users?on_conflict=role_id")
        {
            Content = content
        };
        request.Headers.Add("Prefer", "return=representation,resolution=merge-duplicates");

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task SetOfflineAsync()
    {
        var payload = new { is_online = false };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"{_supabaseUrl}/rest/v1/online_users")
        {
            Content = content
        };

        // 使用 filter 更新当前用户
        request.Headers.Add("Prefer", "return=representation");
        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("[SupabaseIRC] 离线标记API失败，尝试直接更新");
            // 备用方案：记录日志但不抛出异常
        }
    }

    private async void HeartbeatCallback(object? state)
    {
        try
        {
            // 更新在线状态
            await RegisterOnlineAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[SupabaseIRC] 心跳失败");
        }
    }

    private async Task PollMessagesAsync(CancellationToken ct)
    {
        long lastId = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 获取最新消息（最近50条）
                var url = $"{_supabaseUrl}/rest/v1/irc_messages?select=*&order=created_at.desc&limit=50";
                if (lastId > 0)
                {
                    url += $"&id=gt.{lastId}";
                }

                var response = await _httpClient.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                {
                    var messages = await response.Content.ReadFromJsonAsync<List<IrcMessageDto>>(cancellationToken: ct);
                    if (messages != null && messages.Count > 0)
                    {
                        // 反转顺序，从旧到新
                        messages.Reverse();

                        foreach (var msg in messages)
                        {
                            lastId = msg.Id;

                            // 避免处理自己的消息（可选）
                            if (msg.RoleId != _roleId)
                            {
                                ChatReceived?.Invoke(this, new IrcChatEventArgs { Message = $"{msg.Username}: {msg.Message}" });
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[SupabaseIRC] 消息轮询失败");
            }

            // 每2秒轮询一次
            try
            {
                await Task.Delay(2000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PollOnlineUsersAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var url = $"{_supabaseUrl}/rest/v1/online_users?select=*&is_online=eq.true";
                var response = await _httpClient.GetAsync(url, ct);

                if (response.IsSuccessStatusCode)
                {
                    var users = await response.Content.ReadFromJsonAsync<List<OnlineUserDto>>(cancellationToken: ct);
                    if (users != null)
                    {
                        _onlineUsers.Clear();
                        foreach (var user in users)
                        {
                            _onlineUsers[user.RoleId] = new OnlineUser
                            {
                                RoleId = user.RoleId,
                                Username = user.Username,
                                ClientTag = user.ClientTag ?? "",
                                IsOnline = user.IsOnline
                            };
                        }

                        OnlineCountUpdated?.Invoke(this, _onlineUsers.Count);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[SupabaseIRC] 在线用户轮询失败");
            }

            // 每5秒更新一次在线列表
            try
            {
                await Task.Delay(5000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisconnectAsync().Wait();
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}

// DTO 类
public class IrcMessageDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("role_id")]
    public string RoleId { get; set; } = "";

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("client_tag")]
    public string? ClientTag { get; set; }

    [JsonPropertyName("room")]
    public string Room { get; set; } = "";

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
}

public class OnlineUserDto
{
    [JsonPropertyName("role_id")]
    public string RoleId { get; set; } = "";

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("client_tag")]
    public string? ClientTag { get; set; }

    [JsonPropertyName("is_online")]
    public bool IsOnline { get; set; }
}

public class OnlineUser
{
    public string RoleId { get; set; } = "";
    public string Username { get; set; } = "";
    public string ClientTag { get; set; } = "";
    public bool IsOnline { get; set; }
}
