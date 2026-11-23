using System;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace NoneadCSS.Tests;

public class DbQueryTest
{
    private readonly ITestOutputHelper _output;

    public DbQueryTest(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string GetDbPath()
    {
        var dbPath = System.IO.Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "NoneadCSS.Api", "Data", "noneadcss.db");
        return dbPath;
    }

    [Fact]
    public void QueryWeChatSettings()
    {
        var dbPath = GetDbPath();

        if (!System.IO.File.Exists(dbPath))
        {
            _output.WriteLine($"DB not found at: {dbPath}");
            return;
        }

        _output.WriteLine($"DB path: {dbPath}");

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        // 先查表结构
        cmd.CommandText = "PRAGMA table_info(SystemOptions)";
        using (var schemaReader = cmd.ExecuteReader())
        {
            _output.WriteLine("SystemOptions columns:");
            while (schemaReader.Read())
            {
                _output.WriteLine($"  col={schemaReader.GetString(1)} type={schemaReader.GetString(2)}");
            }
        }

        cmd.CommandText = "SELECT * FROM SystemOptions WHERE Category='wechat' ORDER BY Id";

        _output.WriteLine(string.Empty);
        _output.WriteLine("=== wechat 配置完整值（用于核对微信公众平台后台）===");
        _output.WriteLine(string.Empty);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(1);  // Name 列
            var val = reader.IsDBNull(2) ? "(NULL)" : reader.GetString(2);  // Value 列
            var len = val.Length;
            // Token / AppSecret / EncodingAESKey 显示完整值用于核对；其他字段也完整显示
            _output.WriteLine($"  {name}  (len={len})");
            _output.WriteLine($"    => {val}");
            _output.WriteLine(string.Empty);
        }
    }

    [Fact]
    public void QueryConversation439LatestThreads()
    {
        var dbPath = GetDbPath();
        if (!System.IO.File.Exists(dbPath))
        {
            _output.WriteLine($"DB not found at: {dbPath}");
            return;
        }

        _output.WriteLine($"DB path: {dbPath}");
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, ConversationId, Type, Body, CreatedAt FROM Threads WHERE ConversationId=439 ORDER BY CreatedAt DESC LIMIT 5";
        using var reader = cmd.ExecuteReader();
        _output.WriteLine("=== Conversation 439 最新线程 ===");
        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            var type = reader.GetString(2);
            var body = reader.IsDBNull(3) ? "(NULL)" : reader.GetString(3);
            var createdAt = reader.GetString(4);
            _output.WriteLine($"Id={id} Type={type} CreatedAt={createdAt}");
            _output.WriteLine($"Body={body}");
            _output.WriteLine("---");
        }
    }

    /// <summary>
    /// 临时修复工具：将数据库中的 EncodingAESKey 从加密密文更新为明文值。
    /// 原因：历史加密存储 + 开发环境 Encryption:Key 变化导致解密出错误值，
    /// 进而在安全模式下微信消息解密失败。改为明文存储与 Token 一致。
    /// </summary>
    [Fact]
    public void FixEncodingAesKeyToPlain()
    {
        var dbPath = GetDbPath();
        if (!System.IO.File.Exists(dbPath))
        {
            _output.WriteLine($"DB not found at: {dbPath}");
            return;
        }

        var plainAesKey = "yvsfMkSQpT54VGWKxCmPSFfB6m5RtFas6WbMzhPzbXt";
        _output.WriteLine($"Plain AESKey length: {plainAesKey.Length}");

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        // 更新前查询
        using (var beforeCmd = conn.CreateCommand())
        {
            beforeCmd.CommandText = "SELECT * FROM SystemOptions WHERE Category='wechat' AND Name='wechat.encoding_aes_key'";
            using var r = beforeCmd.ExecuteReader();
            if (r.Read())
            {
                _output.WriteLine($"Columns: {r.FieldCount}");
                for (int i = 0; i < r.FieldCount; i++)
                {
                    var v = r.IsDBNull(i) ? "(null)" : r.GetValue(i)?.ToString();
                    _output.WriteLine($"  col[{i}] {r.GetName(i)} = {v}");
                }
            }
            else
            {
                _output.WriteLine("No row found before update");
            }
        }

        // 更新为明文
        using (var updateCmd = conn.CreateCommand())
        {
            updateCmd.CommandText = "UPDATE SystemOptions SET Value = @val WHERE Category='wechat' AND Name='wechat.encoding_aes_key'";
            updateCmd.Parameters.AddWithValue("@val", plainAesKey);
            var rows = updateCmd.ExecuteNonQuery();
            _output.WriteLine($"Updated rows: {rows}");
        }

        // 更新后验证
        using (var afterCmd = conn.CreateCommand())
        {
            afterCmd.CommandText = "SELECT * FROM SystemOptions WHERE Category='wechat' AND Name='wechat.encoding_aes_key'";
            using var r = afterCmd.ExecuteReader();
            if (r.Read())
            {
                for (int i = 0; i < r.FieldCount; i++)
                {
                    var v = r.IsDBNull(i) ? "(null)" : r.GetValue(i)?.ToString();
                    _output.WriteLine($"  col[{i}] {r.GetName(i)} = {v}");
                }
            }
        }
    }

    /// <summary>
    /// 专门查询 Token 完整值，便于与微信公众平台后台服务器配置中的 Token 核对。
    /// </summary>
    [Fact]
    public void QueryWeChatToken()
    {
        var dbPath = GetDbPath();

        if (!System.IO.File.Exists(dbPath))
        {
            _output.WriteLine($"DB not found at: {dbPath}");
            return;
        }

        _output.WriteLine($"DB path: {dbPath}");
        _output.WriteLine(string.Empty);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Name, Value, Length(Value) as ValLen, UpdatedAt FROM SystemOptions WHERE Category='wechat' AND Name='wechat.token'";

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            _output.WriteLine("数据库中未找到 wechat.token 记录！");
            return;
        }

        var name = reader.GetString(0);
        var val = reader.IsDBNull(1) ? "(NULL)" : reader.GetString(1);
        var len = reader.GetInt32(2);
        var updatedAt = reader.IsDBNull(3) ? "(unknown)" : reader.GetString(3);

        _output.WriteLine("========== wechat.token 完整信息 ==========");
        _output.WriteLine($"  Name:       {name}");
        _output.WriteLine($"  Length:     {len} 字符");
        _output.WriteLine($"  UpdatedAt:  {updatedAt}");
        _output.WriteLine($"  Value:      {val}");
        _output.WriteLine(string.Empty);

        // 关闭首个 reader，避免修改 CommandText 时 SQLite 报 "An open reader is associated with this command"
        reader.Close();

        // 同时显示其他相关字段，便于完整核对
        _output.WriteLine("========== 其他相关配置（用于对照）==========");
        cmd.CommandText = "SELECT Name, Value, Length(Value) as ValLen FROM SystemOptions WHERE Category='wechat' AND Name IN ('wechat.app_id','wechat.encrypt_mode','wechat.enabled') ORDER BY Name";
        using var reader2 = cmd.ExecuteReader();
        while (reader2.Read())
        {
            _output.WriteLine($"  {reader2.GetString(0)} (len={reader2.GetInt32(2)}) => {reader2.GetString(1)}");
        }
        _output.WriteLine(string.Empty);
        _output.WriteLine("请将上述 Token 值与微信公众平台 → 设置与开发 → 基本配置 → 服务器配置 → Token 进行核对。");
    }

    /// <summary>
    /// 一次性迁移：清除数据库中加密存储的 Token 密文，使其回退到 appsettings.json 基线值。
    /// 修复后 Token 改为明文存储（与 AppSecret/EncodingAESKey 不同）。
    /// 执行后需重启后端，并在前端重新保存 Token。
    /// </summary>
    [Fact]
    public void ClearEncryptedToken()
    {
        var dbPath = GetDbPath();

        if (!System.IO.File.Exists(dbPath))
        {
            _output.WriteLine($"DB not found at: {dbPath}");
            return;
        }

        _output.WriteLine($"DB path: {dbPath}");

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        // 先查看当前 Token 值
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT Value, Length(Value) FROM SystemOptions WHERE Category='wechat' AND Name='wechat.token'";
        using (var reader = checkCmd.ExecuteReader())
        {
            if (!reader.Read())
            {
                _output.WriteLine("数据库中未找到 wechat.token 记录，无需迁移");
                return;
            }

            var currentVal = reader.GetString(0);
            var currentLen = reader.GetInt32(1);
            _output.WriteLine($"迁移前 Token (len={currentLen}): {currentVal}");

            // 启发式判断：长度>40 且含 Base64 字符 = 加密密文
            var looksLikeCipher = currentLen > 40 && System.Text.RegularExpressions.Regex.IsMatch(currentVal, @"^[A-Za-z0-9+/=]+$");
            _output.WriteLine($"  看起来是加密密文？{looksLikeCipher}");
        }

        // 删除加密 Token 记录
        using var deleteCmd = conn.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM SystemOptions WHERE Category='wechat' AND Name='wechat.token'";
        var affected = deleteCmd.ExecuteNonQuery();
        _output.WriteLine($"已删除 wechat.token 记录 (affected rows: {affected})");

        // 验证删除
        using var verifyCmd = conn.CreateCommand();
        verifyCmd.CommandText = "SELECT COUNT(*) FROM SystemOptions WHERE Category='wechat' AND Name='wechat.token'";
        var remaining = (long)verifyCmd.ExecuteScalar()!;
        _output.WriteLine($"验证：剩余 wechat.token 记录数 = {remaining}");

        _output.WriteLine(string.Empty);
        _output.WriteLine("迁移完成。请：");
        _output.WriteLine("  1. 重启后端服务");
        _output.WriteLine("  2. 在前端 /wechat 页面解锁 Token 字段");
        _output.WriteLine("  3. 输入与微信公众平台后台一致的 Token（明文）");
        _output.WriteLine("  4. 保存配置");
        _output.WriteLine("  5. 在微信服务号发测试消息验证");
    }

    /// <summary>
    /// 插入测试用的 AppSecret 和 EncodingAESKey 明文值，用于验证前端"已配置"显示效果。
    /// 说明：开发环境 Encryption:Key 为空，AesEncryptionService 每次启动生成随机密钥，
    /// 因此无法生成可被当前运行后端解密的密文。WeChatSettingsProvider 有明文回退逻辑
    /// （TryDecrypt 失败时直接使用原值），所以插入明文即可让 HasAppSecret/HasEncodingAESKey 返回 true。
    /// 插入后需重启后端以清除 IMemoryCache 缓存（CacheKey: WeChat:EffectiveSettings）。
    /// </summary>
    [Fact]
    public void InsertTestWeChatSecrets()
    {
        var dbPath = GetDbPath();

        if (!System.IO.File.Exists(dbPath))
        {
            _output.WriteLine($"DB not found at: {dbPath}");
            return;
        }

        _output.WriteLine($"DB path: {dbPath}");

        // 测试用明文值（仅用于验证前端显示，不是真实密钥）
        const string testAppSecret = "TEST_APP_SECRET_FOR_DISPLAY_VERIFICATION_2026";
        const string testEncodingAESKey = "TEST_ENCODING_AES_KEY_FOR_DISPLAY_VERIFICATION_2026";

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        // 使用 INSERT OR REPLACE 保证幂等（重复运行不会报错，会覆盖旧值）
        // SystemOptions 表结构：Id (自增), Name, Value, Category, CreatedAt, UpdatedAt
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO SystemOptions (Name, Value, Category, CreatedAt, UpdatedAt)
            VALUES (@name, @value, @category, @created, @updated);
        ";

        // 插入 app_secret
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@name", "wechat.app_secret");
        cmd.Parameters.AddWithValue("@value", testAppSecret);
        cmd.Parameters.AddWithValue("@category", "wechat");
        cmd.Parameters.AddWithValue("@created", now);
        cmd.Parameters.AddWithValue("@updated", now);
        var rows1 = cmd.ExecuteNonQuery();
        _output.WriteLine($"Inserted wechat.app_secret (affected rows: {rows1})");

        // 插入 encoding_aes_key
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@name", "wechat.encoding_aes_key");
        cmd.Parameters.AddWithValue("@value", testEncodingAESKey);
        cmd.Parameters.AddWithValue("@category", "wechat");
        cmd.Parameters.AddWithValue("@created", now);
        cmd.Parameters.AddWithValue("@updated", now);
        var rows2 = cmd.ExecuteNonQuery();
        _output.WriteLine($"Inserted wechat.encoding_aes_key (affected rows: {rows2})");

        // 验证插入结果
        _output.WriteLine(string.Empty);
        _output.WriteLine("=== 验证插入后的 wechat 配置 ===");
        cmd.Parameters.Clear();
        cmd.CommandText = "SELECT Name, Length(Value) as ValLen, Category FROM SystemOptions WHERE Category='wechat' ORDER BY Name";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            _output.WriteLine($"  {reader.GetString(0)} (len={reader.GetInt32(1)}) category={reader.GetString(2)}");
        }

        _output.WriteLine(string.Empty);
        _output.WriteLine("插入完成。请重启后端服务以清除配置缓存，然后刷新前端页面验证显示效果。");
    }
}
