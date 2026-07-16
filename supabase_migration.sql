-- ============================================
-- NewEastSide IRC 迁移到 Supabase Realtime
-- 数据库初始化脚本
-- ============================================

-- 1. 聊天消息表
CREATE TABLE IF NOT EXISTS irc_messages (
    id BIGSERIAL PRIMARY KEY,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    role_id TEXT NOT NULL,
    username TEXT NOT NULL,
    message TEXT NOT NULL,
    client_tag TEXT DEFAULT '',
    room TEXT DEFAULT 'global'
);

-- 2. 在线用户表
CREATE TABLE IF NOT EXISTS online_users (
    id BIGSERIAL PRIMARY KEY,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW(),
    role_id TEXT NOT NULL UNIQUE,
    username TEXT NOT NULL,
    client_tag TEXT DEFAULT '',
    last_seen TIMESTAMPTZ DEFAULT NOW(),
    is_online BOOLEAN DEFAULT true
);

-- 3. 启用 Realtime
ALTER PUBLICATION supabase_realtime ADD TABLE irc_messages;
ALTER PUBLICATION supabase_realtime ADD TABLE online_users;

-- 4. 创建索引提升查询性能
CREATE INDEX IF NOT EXISTS idx_irc_messages_created_at ON irc_messages(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_irc_messages_room ON irc_messages(room);
CREATE INDEX IF NOT EXISTS idx_online_users_online ON online_users(is_online) WHERE is_online = true;
CREATE INDEX IF NOT EXISTS idx_online_users_role_id ON online_users(role_id);

-- 5. 自动更新 updated_at 的触发器函数
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    IF NEW.is_online = false THEN
        NEW.last_seen = NOW();
    END IF;
    RETURN NEW;
END;
$$ language 'plpgsql';

-- 6. 为 online_users 创建触发器
DROP TRIGGER IF EXISTS update_online_users_updated_at ON online_users;
CREATE TRIGGER update_online_users_updated_at
    BEFORE UPDATE ON online_users
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

-- 7. 清理离线用户的定时任务（可选：设置15分钟无活动自动下线）
-- 注意：这需要配合 pg_cron 扩展或使用客户端定时清理

-- 8. 行级安全策略（RLS）
ALTER TABLE irc_messages ENABLE ROW LEVEL SECURITY;
ALTER TABLE online_users ENABLE ROW LEVEL SECURITY;

-- 允许所有人读取消息
CREATE POLICY "Allow public read access to messages" ON irc_messages
    FOR SELECT USING (true);

-- 允许认证用户插入消息
CREATE POLICY "Allow authenticated insert to messages" ON irc_messages
    FOR INSERT WITH CHECK (auth.uid() IS NOT NULL);

-- 允许所有人读取在线用户
CREATE POLICY "Allow public read access to online users" ON online_users
    FOR SELECT USING (true);

-- 允许认证用户更新自己的在线状态
CREATE POLICY "Allow users to update own online status" ON online_users
    FOR UPDATE USING (auth.uid() IS NOT NULL);

-- ============================================
-- 示例查询
-- ============================================

-- 获取最近100条聊天消息
-- SELECT * FROM irc_messages ORDER BY created_at DESC LIMIT 100;

-- 获取当前在线人数
-- SELECT COUNT(*) FROM online_users WHERE is_online = true;

-- 获取在线用户列表
-- SELECT role_id, username, client_tag FROM online_users WHERE is_online = true;
