/*
<OxygenNEL>
Copyright (C) <2025>  <OxygenNEL>

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.
*/

using System;
using System.Threading;
using System.Threading.Tasks;
using NewEastSide.IRC.Packet;
using Codexus.Development.SDK.Connection;
using Serilog;

namespace NewEastSide.IRC;

public class IrcClient : IDisposable
{
    readonly GameConnection _conn;
    readonly string _token;
    string _roleId = string.Empty;
    string _clientTag = string.Empty;

    TcpLineClient? _tcp;
    SupabaseIrcClient? _supabaseIrc;
    bool _welcomed;
    bool _listShown;
    Timer? _pingTimer;
    Timer? _tabTickTimer;
    volatile bool _running;
    readonly IrcTabList _tabList = new();

    public string RoleId => _roleId;
    public string ClientTag => _clientTag;
    public GameConnection Connection => _conn;
    public IrcTabList TabList => _tabList;
    public event EventHandler<IrcChatEventArgs>? ChatReceived;

    public IrcClient(GameConnection conn, Func<string>? tokenProvider)
    {
        _conn = conn;
        _token = tokenProvider?.Invoke() ?? "";
    }

    public void Start(string nickName, string clientTag = "")
    {
        if (_running) return;
        _running = true;
        _roleId = nickName;
        _clientTag = clientTag;
        Log.Information("[IRC] 启动: NickName={NickName}, ClientTag={Tag}", nickName, clientTag);

        if (IrcManager.UseSupabaseIrc)
        {
            Task.Run(async () => await StartSupabaseAsync());
        }
        else
        {
            Task.Run(Run);
        }
    }

    public void Stop()
    {
        _running = false;
        _pingTimer?.Dispose();
        _tabTickTimer?.Dispose();
        _tabList.Clear(_conn);
        _tcp?.Close();
        _supabaseIrc?.Dispose();
    }

    public void SendChat(string playerName, string msg)
    {
        if (IrcManager.UseSupabaseIrc && _supabaseIrc != null)
        {
            _ = _supabaseIrc.SendChatAsync(msg);
            return;
        }

        if (_tcp == null)
        {
            Log.Warning("[IRC] SendChat: TCP 未连接");
            return;
        }
        var cmd = IrcProtocol.Chat(_token, _roleId, msg);
        Log.Information("[IRC] 发送聊天 {Cmd}", cmd);
        _tcp.Send(cmd);
    }

    public void Dispose()
    {
        Stop();
        _tcp?.Dispose();
        _supabaseIrc?.Dispose();
    }

    private async Task StartSupabaseAsync()
    {
        try
        {
            _supabaseIrc = new SupabaseIrcClient(
                IrcManager.SupabaseUrl,
                IrcManager.SupabaseAnonKey,
                _token,
                _roleId,
                _clientTag
            );

            _supabaseIrc.ChatReceived += (s, e) =>
            {
                ChatReceived?.Invoke(this, new IrcChatEventArgs { Message = e.Message });
            };

            _supabaseIrc.OnlineCountUpdated += (s, count) =>
            {
                if (_running && _conn.State == Codexus.Development.SDK.Enums.EnumConnectionState.Play)
                {
                    if (!_listShown)
                    {
                        _listShown = true;
                        Msg("§a[§bIRC§a] IRC 连接成功 Ciallo～∠・☆)ノ");
                    }
                    var hintEnabled = IrcManager.IrcHintEnabledProvider?.Invoke() ?? true;
                    if (hintEnabled && count > 0)
                        Msg($"§e[§bIRC§e] 当前在线 {count} 人，使用 §a/irc 想说的话§e 聊天");
                }
            };

            await _supabaseIrc.ConnectAsync();

            Log.Information("[SupabaseIRC] 连接成功");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SupabaseIRC] 启动失败");
            if (_running) Thread.Sleep(3000);
        }
    }

    void Run()
    {
        while (_running)
        {
            try
            {
                _tcp = new TcpLineClient(IrcProtocol.Host, IrcProtocol.Port);
                _tcp.Connect();

                _tcp.Send(IrcProtocol.Register(_token, _roleId, _clientTag));

                var interval = (IrcManager.IrcHintIntervalProvider?.Invoke() ?? 30) * 1000;
                if (interval < 10000) interval = 10000;

                _pingTimer = new Timer(_ =>
                {
                    _tcp?.Send(IrcProtocol.Ping());
                    _tcp?.Send(IrcProtocol.List());
                }, null, interval, interval);

                _tabTickTimer = new Timer(_ =>
                {
                    if (_running) _tabList.Tick(_conn);
                }, null, 3000, 3000);

                while (_running)
                {
                    var line = _tcp.Read();
                    if (line == null) break;
                    Process(line);
                }
            }
            catch (Exception ex)
            {
                if (_running) Log.Warning(ex, "[IRC] 异常");
            }

            _pingTimer?.Dispose();
            _tabTickTimer?.Dispose();
            _tcp?.Close();
            if (_running) Thread.Sleep(3000);
        }
    }

    void Process(string line)
    {
        Log.Debug("[IRC] 收到: {Line}", line);
        var msg = IrcProtocol.Parse(line);
        if (msg == null) return;

        if (msg.IsOk && !_welcomed)
        {
            _welcomed = true;
            _tcp?.Send(IrcProtocol.List());
        }
        else if (msg.IsError && msg.Data.Contains("已注册"))
        {
            _welcomed = true;
            _tcp?.Send(IrcProtocol.List());
        }
        else if (msg.IsList)
        {
            Task.Run(async () =>
            {
                for (int i = 0; i < 10 && _running; i++)
                {
                    if (_conn.State == Codexus.Development.SDK.Enums.EnumConnectionState.Play) break;
                    await Task.Delay(500);
                }
                if (_running && _conn.State == Codexus.Development.SDK.Enums.EnumConnectionState.Play)
                {
                    if (!_listShown)
                    {
                        _listShown = true;
                        Msg("§a[§bIRC§a] IRC 连接成功 Ciallo～∠・☆)ノ");
                    }
                    var hintEnabled = IrcManager.IrcHintEnabledProvider?.Invoke() ?? true;
                    if (hintEnabled && msg.PlayerCount > 0)
                        Msg($"§e[§bIRC§e] 当前在线 {msg.PlayerCount} 人，使用 §a/irc 想说的话§e 聊天");

                    _tabList.UpdateIrcList(msg.PlayerEntries);
                }
            });
        }
        else if (msg.IsError)
        {
            Log.Warning("[IRC] 服务器错误: {Error}", msg.Data);
        }
        else if (msg.IsChatBroadcast)
        {
            ChatReceived?.Invoke(this, new IrcChatEventArgs { Message = msg.Data });
        }
    }

    void Msg(string msg)
    {
        Log.Information("[IRC] 显示消息: {Msg}", msg);
        CChatCommandIrc.SendLocalMessage(_conn, msg);
    }
}
