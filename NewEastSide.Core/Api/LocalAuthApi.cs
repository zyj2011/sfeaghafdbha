using System;
using System.IO;
using System.Text.Json;
using Serilog;
using System.Text.Json.Serialization;

namespace NewEastSide.Core.Api;

public class LocalAuthApi : IDisposable
{
    private static readonly Lazy<LocalAuthApi> _instance = new(() => new LocalAuthApi());
    public static LocalAuthApi Instance => _instance.Value;

    private readonly string _usersFile;
    private readonly object _lock = new();
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private LocalAuthApi()
    {
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "NewEastSide", "data");
        Directory.CreateDirectory(dataDir);
        _usersFile = Path.Combine(dataDir, "users.json");
    }

    public LocalAuthResult SignUp(string email, string password)
    {
        lock (_lock)
        {
            var users = LoadUsers();
            if (users.Exists(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
                return new LocalAuthResult(false, "该邮箱已被注册");

            var nextId = users.Count + 1;
            var user = new LocalUser
            {
                Id = nextId.ToString(),
                Email = email,
                PasswordHash = HashPassword(password),
                CreatedAt = DateTime.UtcNow
            };
            users.Add(user);
            SaveUsers(users);
            return new LocalAuthResult(true, "注册成功", user);
        }
    }

    public LocalAuthResult SignIn(string email, string password)
    {
        lock (_lock)
        {
            var users = LoadUsers();
            var user = users.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
            if (user == null || !VerifyPassword(password, user.PasswordHash))
                return new LocalAuthResult(false, "邮箱或密码错误");

            user.LastLogin = DateTime.UtcNow;
            SaveUsers(users);
            return new LocalAuthResult(true, "登录成功", user);
        }
    }

    public LocalUser? GetUser(string id)
    {
        lock (_lock)
        {
            var users = LoadUsers();
            return users.FirstOrDefault(u => u.Id == id);
        }
    }

    public LocalUser? GetUserByEmail(string email)
    {
        lock (_lock)
        {
            var users = LoadUsers();
            return users.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
        }
    }

    public LocalAuthResult ResetPassword(string email, string newPassword)
    {
        lock (_lock)
        {
            var users = LoadUsers();
            var user = users.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
            if (user == null)
                return new LocalAuthResult(false, "该邮箱未注册", null);
            user.PasswordHash = HashPassword(newPassword);
            SaveUsers(users);
            return new LocalAuthResult(true, "密码重置成功", user);
        }
    }

    private List<LocalUser> LoadUsers()
    {
        if (!File.Exists(_usersFile)) return new List<LocalUser>();
        try
        {
            var json = File.ReadAllText(_usersFile);
            return JsonSerializer.Deserialize<List<LocalUser>>(json, JsonOptions) ?? new List<LocalUser>();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "读取用户文件失败");
            return new List<LocalUser>();
        }
    }

    private void SaveUsers(List<LocalUser> users)
    {
        try
        {
            var json = JsonSerializer.Serialize(users, JsonOptions);
            File.WriteAllText(_usersFile, json);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存用户文件失败");
        }
    }

    private static string HashPassword(string password)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(hash);
    }

    private static bool VerifyPassword(string password, string hash)
    {
        return HashPassword(password) == hash;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

public class LocalAuthResult
{
    public bool Success { get; }
    public string Message { get; }
    public LocalUser? User { get; }

    public LocalAuthResult(bool success, string message, LocalUser? user = null)
    {
        Success = success;
        Message = message;
        User = user;
    }
}

public class LocalUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("password_hash")]
    public string PasswordHash { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("last_login")]
    public DateTime? LastLogin { get; set; }

    [JsonPropertyName("user_metadata")]
    public LocalUserMetadata? UserMetadata { get; set; } = new();
}

public class LocalUserMetadata
{
    [JsonPropertyName("full_name")]
    public string? FullName { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}