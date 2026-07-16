using System;
using System.IO;
using NewEastSide.Manager;
using NewEastSide.UI.Bridge;
using NewEastSide;
using NewEastSide.Utils;
using Serilog;
using Serilog.Events;

namespace NewEastSide.UI;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var baseDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        Directory.SetCurrentDirectory(baseDir);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            string info = ex?.ToString() ?? e.ExceptionObject?.ToString() ?? "Unknown error";
            File.AppendAllText("crash.log", $"[Unhandled] {info}\n");
        };

        ConfigureLogger();
        Log.Information("NewEastSide 启动中...");

        // 连接 Java 下载进度到 UI
        JavaEnvironmentHelper.OnDownloadProgress += (filename, downloaded, total, percent) =>
        {
            var speed = "";
            if (total > 0 && percent > 0)
            {
                var mbDown = downloaded / 1024.0 / 1024.0;
                var mbTotal = total / 1024.0 / 1024.0;
                var pct = Math.Max(0, Math.Min(100, percent));
                speed = $"{mbDown:F1}/{mbTotal:F1}MB ({pct}%)";
            }
            else
            {
                var mbDown = downloaded / 1024.0 / 1024.0;
                speed = $"{mbDown:F1}MB";
            }
            var msg = $"下载: {filename} - {speed}";
            Log.Information("[Progress] {Msg}", msg);
            NotificationHelper.ShowInfo(msg);
        };

        JavaEnvironmentHelper.OnStatusMessage += message =>
        {
            Log.Information("[Status] {Message}", message);
        };

        NotificationHelper.OnNotify += (message, level) =>
        {
            var levelStr = level switch
            {
                NotifyLevel.Success => "success",
                NotifyLevel.Warning => "warning",
                NotifyLevel.Error => "error",
                _ => "info"
            };
            AppWindow.PushNotification(message, levelStr);
        };

        BanRecordManager.OnBanAdded += entry =>
        {
            AppWindow.PushEvent("ban:updated", new
            {
                serverId = entry.ServerId,
                roleName = entry.RoleName,
                permanent = entry.IsPermanent,
                unbanTime = entry.UnbanTime?.ToString("o")
            });
        };

        Backend.Initialize();

        AppWindow.Run();
    }

    private static void ConfigureLogger()
    {
        var baseDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var logDir = Path.Combine(baseDir, "logs");
        Directory.CreateDirectory(logDir);
        var fileName = DateTime.Now.ToString("yyyy-MM-dd-HHmm-ss") + ".log";
        var filePath = Path.Combine(logDir, fileName);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Information)
            .WriteTo.Console()
            .WriteTo.File(filePath)
            .CreateLogger();
    }
}
