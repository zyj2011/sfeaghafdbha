# NewEastSide IRC 迁移到 Supabase Realtime 指南

## 第一步：配置 Supabase 数据库

1. 登录你的 Supabase 控制台：https://supabase.com/dashboard
2. 进入 SQL Editor
3. 运行 `supabase_migration.sql` 文件中的 SQL 脚本
   - 创建 `irc_messages` 表（聊天消息）
   - 创建 `online_users` 表（在线用户）
   - 启用 Realtime 功能
   - 设置行级安全策略（RLS）

## 第二步：更新代码配置

### 修改 `IrcManager.cs`
```csharp
// 确认 Supabase 配置正确
public static string SupabaseUrl { get; set; } = "https://hddbbytazxevekgghgfv.supabase.co";
public static string SupabaseAnonKey { get; set; } = "你的anon_key";

// 启用 Supabase IRC 模式
public static bool UseSupabaseIrc { get; set; } = true;
```

### 修改 `IrcClient.cs` 以使用 SupabaseIrcClient

在 `IrcClient.cs` 中添加条件逻辑：

```csharp
public class IrcClient : IDisposable
{
    // 添加 Supabase IRC 客户端
    SupabaseIrcClient? _supabaseIrc;
    
    // 在 Start 方法中
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
            Task.Run(Run); // 原有 TCP IRC 逻辑
        }
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
            
            _supabaseIrc.ChatReceived += (s, e) => ProcessChatBroadcast(e.Message);
            _supabaseIrc.OnlineCountUpdated += (s, count) => HandleOnlineCount(count);
            
            await _supabaseIrc.ConnectAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SupabaseIRC] 启动失败");
        }
    }
    
    public void SendChat(string playerName, string msg)
    {
        if (IrcManager.UseSupabaseIrc && _supabaseIrc != null)
        {
            _ = _supabaseIrc.SendChatAsync(msg);
        }
        else if (_tcp != null)
        {
            var cmd = IrcProtocol.Chat(_token, _roleId, msg);
            _tcp.Send(cmd);
        }
    }
    
    public void Dispose()
    {
        Stop();
        _supabaseIrc?.Dispose();
        _tcp?.Dispose();
    }
}
```

## 第三步：测试

1. 启动游戏客户端
2. 进入游戏服务器
3. 检查日志中是否有 `[SupabaseIRC] 连接成功` 消息
4. 使用 `/irc 测试消息` 命令发送聊天
5. 查看 Supabase 控制台的 `irc_messages` 表，确认消息已保存
6. 查看 `online_users` 表，确认在线状态正确

## 第四步：故障排除

### 如果连接失败：
- 检查 Supabase URL 和 Anon Key 是否正确
- 确认数据库表已创建
- 检查 RLS 策略是否允许匿名访问
- 查看日志中的详细错误信息

### 如果消息不显示：
- 确认 `UseSupabaseIrc` 设置为 `true`
- 检查 `irc_messages` 表是否有新记录
- 确认消息轮询间隔（默认2秒）

## 回滚方案

如果想恢复原有的 TCP IRC：
```csharp
public static bool UseSupabaseIrc { get; set; } = false;
```

## 优势

迁移到 Supabase Realtime 后：
- ✅ 完全自主控制数据和服务器
- ✅ 不再依赖外部 `api.fandmc.cn` 服务
- ✅ 数据持久化，可查询历史记录
- ✅ 更好的可扩展性和稳定性
- ✅ 利用 Supabase 的免费额度

## 注意事项

- Supabase Realtime 有连接数和消息频率限制，免费计划通常足够小规模使用
- 建议定期清理 `irc_messages` 表的旧数据（可设置定时任务）
- 如果需要更高性能，可以考虑使用 Supabase 的 Realtime 广播功能替代轮询
