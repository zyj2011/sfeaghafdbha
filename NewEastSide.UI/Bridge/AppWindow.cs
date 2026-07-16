using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using PhotinoNET;

namespace NewEastSide.UI.Bridge;

public static class AppWindow
{
    private static PhotinoWindow? _window;

    public static void Run()
    {
        if (_window != null)
        {
            return;
        }

        _window = new PhotinoWindow();
        ConfigureWindow(_window);

        var indexPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"));
        if (File.Exists(indexPath))
        {
            _window.Load(new Uri(indexPath).AbsoluteUri);
        }
        else
        {
            _window.Load("data:text/html;charset=utf-8,<h1>NewEastSide</h1>");
        }

        _window.Show();
        InvokeIfAvailable(_window, "WaitForExit");
    }

    public static void PushNotification(string message, string level)
    {
        if (_window == null)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            type = "notification",
            message,
            level
        });

        _window.SendWebMessage(payload);
    }

    public static void PushEvent(string eventName, object payload)
    {
        if (_window == null)
        {
            return;
        }

        var payloadJson = JsonSerializer.Serialize(new
        {
            type = "event",
            eventName,
            payload
        });

        _window.SendWebMessage(payloadJson);
    }

    private static void ConfigureWindow(PhotinoWindow window)
    {
        window.Title = "NewEastSide - 高级启动器";
        InvokeIfAvailable(window, "SetUseOsDefaultSize", false);
        InvokeIfAvailable(window, "SetSize", 1360, 860);
        InvokeIfAvailable(window, "SetMinSize", 1080, 720);
        InvokeIfAvailable(window, "SetResizable", true);
        InvokeIfAvailable(window, "SetContextMenuEnabled", false);
        InvokeIfAvailable(window, "Center");
    }

    private static void InvokeIfAvailable(object target, string methodName, params object[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (method == null)
        {
            return;
        }

        var parameterTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();
        if (parameterTypes.Length != args.Length)
        {
            return;
        }

        var converted = new object?[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            converted[i] = Convert.ChangeType(args[i], parameterTypes[i]);
        }

        method.Invoke(target, converted);
    }
}
