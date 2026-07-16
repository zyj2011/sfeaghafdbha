using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Net.Http;
using System.Threading;
using System.Collections.Generic;
using Serilog;
using NewEastSide.Core.Api;

namespace BotRegister;

class Program
{
    static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateLogger();

        Log.Information("小蜜注册器启动中...");

        try
        {
            var dataDir = Path.Combine(AppContext.BaseDirectory, "accounts");
            Directory.CreateDirectory(dataDir);

            Console.WriteLine("==================================");
            Console.WriteLine("          小蜜注册器");
            Console.WriteLine("==================================");
            Console.WriteLine("1. 注册单个小蜜");
            Console.WriteLine("2. 批量注册小蜜");
            Console.WriteLine("3. 查看已注册账号");
            Console.WriteLine("4. 退出");
            Console.WriteLine("==================================");
            Console.Write("请选择: ");

            var choice = Console.ReadLine();
            switch (choice)
            {
                case "1":
                    await RegisterSingleAccount(dataDir);
                    break;
                case "2":
                    await RegisterBatchAccounts(dataDir);
                    break;
                case "3":
                    ViewAccounts(dataDir);
                    break;
                case "4":
                    Log.Information("退出程序...");
                    return;
                default:
                    Log.Warning("无效选择，请重新运行程序");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "程序运行出错");
        }
        finally
        {
            Log.CloseAndFlush();
            Console.WriteLine("程序已退出...");
            Console.ReadKey();
        }
    }

    static async Task RegisterSingleAccount(string dataDir)
    {
        Log.Information("开始注册单个小蜜...");

        var accountInfo = GenerateRandomAccount();
        Log.Information("账号名称: {Username}", accountInfo.Username);
        Log.Information("账号邮箱: {Email}", accountInfo.Email);
        Log.Information("账号密码: {Password}", accountInfo.Password);

        var result = await RegisterAccount(accountInfo);
        if (result.Success)
        {
            Log.Information("注册成功！");
            SaveAccount(dataDir, accountInfo);
        }
        else
        {
            Log.Error("注册失败: {Message}", result.Message);
        }
    }

    static async Task RegisterBatchAccounts(string dataDir)
    {
        Console.Write("请输入要注册的账号数量: ");
        if (!int.TryParse(Console.ReadLine(), out int count) || count <= 0)
        {
            Log.Warning("无效数量，请输入正整数");
            return;
        }

        Log.Information("开始批量注册 {Count} 个小蜜...", count);

        int successCount = 0;
        for (int i = 0; i < count; i++)
        {
            Log.Information("注册第 {Index} 个账号...", i + 1);

            var accountInfo = GenerateRandomAccount();
            Log.Information("账号名称: {Username}", accountInfo.Username);

            var result = await RegisterAccount(accountInfo);
            if (result.Success)
            {
                Log.Information("注册成功！");
                SaveAccount(dataDir, accountInfo);
                successCount++;
            }
            else
            {
                Log.Error("注册失败: {Message}", result.Message);
            }

            await Task.Delay(1000);
        }

        Log.Information("批量注册完成，成功 {Success} 个，失败 {Failed} 个", successCount, count - successCount);
    }

    static void ViewAccounts(string dataDir)
    {
        var files = Directory.GetFiles(dataDir, "*.json");
        if (files.Length == 0)
        {
            Log.Information("暂无已注册账号");
            return;
        }

        Log.Information("已注册账号 ({Count} 个):", files.Length);
        foreach (var file in files)
        {
            try
            {
                var content = File.ReadAllText(file, Encoding.UTF8);
                var account = JsonSerializer.Deserialize<AccountInfo>(content);
                if (account != null)
                {
                    Log.Information("账号: {Username}, 邮箱: {Email}, 密码: {Password}",
                        account.Username, account.Email, account.Password);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "读取账号文件失败: {File}", file);
            }
        }
    }

    static AccountInfo GenerateRandomAccount()
    {
        var random = new Random();
        var username = "bot_" + Guid.NewGuid().ToString().Substring(0, 8);
        var email = $"{username}@example.com";
        var password = GenerateRandomPassword(8);

        return new AccountInfo
        {
            Username = username,
            Email = email,
            Password = password
        };
    }

    static string GenerateRandomPassword(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        var password = new StringBuilder(length);
        for (int i = 0; i < length; i++)
        {
            password.Append(chars[random.Next(chars.Length)]);
        }
        return password.ToString();
    }

    static async Task<RegisterResult> RegisterAccount(AccountInfo accountInfo)
    {
        try
        {
            var result = LocalAuthApi.Instance.SignUp(accountInfo.Email, accountInfo.Password);
            if (!result.Success)
            {
                return new RegisterResult(false, result.Message ?? "注册失败");
            }

            return new RegisterResult(true, result.Message, result.User?.Id);
        }
        catch (Exception ex)
        {
            return new RegisterResult(false, "注册流程出错: " + ex.Message);
        }
    }

    static void SaveAccount(string dataDir, AccountInfo accountInfo)
    {
        try
        {
            var fileName = Path.Combine(dataDir, $"{accountInfo.Username}.json");
            var content = JsonSerializer.Serialize(accountInfo, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(fileName, content, Encoding.UTF8);
            Log.Information("账号信息已保存到: {File}", fileName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存账号信息失败");
        }
    }
}

class AccountInfo
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Token { get; set; }
}

class RegisterResult
{
    public bool Success { get; }
    public string Message { get; }
    public string? Token { get; }

    public RegisterResult(bool success, string message, string? token = null)
    {
        Success = success;
        Message = message;
        Token = token;
    }
}