/*
<OxygenNEL>
Copyright (C) <2025>  <OxygenNEL>

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.
*/

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Codexus.Development.SDK.Connection;
using Serilog;

namespace NewEastSide.IRC;

public class IrcChatEventArgs : EventArgs
{
    public string Message { get; set; } = string.Empty;
}

public static class IrcManager
{
    static readonly ConcurrentDictionary<GameConnection, IrcClient> _clients = new();

    public static Func<string>? TokenProvider { get; set; }
    public static Action<GameConnection>? OnClientRemoved { get; set; }
    public static Func<bool>? IrcHintEnabledProvider { get; set; }
    public static Func<int>? IrcHintIntervalProvider { get; set; }
    public static Func<string, string, Task<(string SkinId, string SkinUrl, int SkinMode)?>>? SkinLookupProvider { get; set; }

    // 默认使用你提供的公网地址；如需内网访问可改为 172.20.74.78
    public static string ServerHost { get; set; } = "121.199.60.93";
    public static int ServerPort { get; set; } = 9527;

    // Supabase 配置（可通过环境变量覆盖）
    public static string SupabaseUrl { get; set; } = "https://hddbbytazxevekgghgfv.supabase.co";
    public static string SupabaseAnonKey { get; set; } = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImhkZGJieXRhenhldmVrZ2doZ2Z2Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODI1MzA3ODksImV4cCI6MjA5ODEwNjc4OX0.xt8UkhYyD5WvBx3Gl-3XiP2X_lSetmTIpRLpibKpDmU";

    // 是否使用 Supabase Realtime IRC（替代传统 IRC）
    public static bool UseSupabaseIrc { get; set; } = true;

    public static void LoadFromEnvironment()
    {
        ServerHost = GetEnvironmentValue("EASTSIDE_IRC_HOST", ServerHost);
        ServerPort = GetEnvironmentInt("EASTSIDE_IRC_PORT", ServerPort);
        SupabaseUrl = GetEnvironmentValue("EASTSIDE_SUPABASE_URL", SupabaseUrl);
        SupabaseAnonKey = GetEnvironmentValue("EASTSIDE_SUPABASE_ANON_KEY", SupabaseAnonKey);
        UseSupabaseIrc = GetEnvironmentBool("EASTSIDE_USE_SUPABASE_IRC", UseSupabaseIrc);

        Log.Information("[IRC] 配置已加载: Host={Host}:{Port}, UseSupabase={UseSupabase}", ServerHost, ServerPort, UseSupabaseIrc);
    }

    private static string GetEnvironmentValue(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static int GetEnvironmentInt(string name, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out var result) ? result : fallback;
    }

    private static bool GetEnvironmentBool(string name, bool fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return bool.TryParse(value, out var result) ? result : fallback;
    }

    public static IrcClient GetOrCreate(GameConnection conn)
    {
        return _clients.GetOrAdd(conn, c => new IrcClient(c, TokenProvider));
    }

    public static IrcClient? Get(GameConnection conn)
    {
        return _clients.TryGetValue(conn, out var client) ? client : null;
    }

    public static void Remove(GameConnection conn)
    {
        if (_clients.TryRemove(conn, out var client))
        {
            client.Dispose();
            OnClientRemoved?.Invoke(conn);
            Log.Information("[IRC] 已移除 {NickName}", conn.NickName);
        }
    }

    public static void Clear()
    {
        foreach (var kv in _clients)
        {
            kv.Value.Dispose();
        }
        _clients.Clear();
    }
}
