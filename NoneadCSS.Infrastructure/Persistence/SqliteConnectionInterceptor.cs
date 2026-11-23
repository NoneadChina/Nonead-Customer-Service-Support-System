using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Data.Common;

namespace NoneadCSS.Infrastructure.Persistence;

public class SqliteConnectionInterceptor : DbConnectionInterceptor
{
    private readonly ILogger<SqliteConnectionInterceptor> _logger;

    public SqliteConnectionInterceptor(ILogger<SqliteConnectionInterceptor> logger)
    {
        _logger = logger;
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        try
        {
            using var walCmd = connection.CreateCommand();
            walCmd.CommandText = "PRAGMA journal_mode=WAL;";
            var result = await walCmd.ExecuteScalarAsync(cancellationToken);
            _logger.LogInformation("WAL mode set to: {Mode}", result);
        }
        catch (Exception ex)
        {
            // 仅在 WAL 设置失败时记录，避免与外键设置混淆
            _logger.LogError(ex, "设置 SQLite WAL 模式失败");
        }

        try
        {
            using var fkCmd = connection.CreateCommand();
            fkCmd.CommandText = "PRAGMA foreign_keys=ON;";
            await fkCmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启用 SQLite 外键约束失败");
        }

        try
        {
            using var busyCmd = connection.CreateCommand();
            busyCmd.CommandText = "PRAGMA busy_timeout=30000;";
            await busyCmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "设置 SQLite busy_timeout 失败");
        }

        // M-06：强制标准 SQL 引号语义。AuditSaveChangesInterceptor 的原始 SQL 使用
        //       "AuditLogs"/"EntityId"/"Id" 双引号包裹标识符；若 sql_standard_strings=OFF（某些连接默认），
        //       双引号会被当作字符串字面量解析，导致 ExecuteSqlRaw 生成语法错误。统一设为 ON 保证标识符解析正确。
        try
        {
            using var stdSqlCmd = connection.CreateCommand();
            stdSqlCmd.CommandText = "PRAGMA sql_standard_strings=ON;";
            await stdSqlCmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启用 SQLite sql_standard_strings 失败");
        }

        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        try
        {
            using var walCmd = connection.CreateCommand();
            walCmd.CommandText = "PRAGMA journal_mode=WAL;";
            var result = walCmd.ExecuteScalar();
            _logger.LogInformation("WAL mode set to: {Mode}", result);
        }
        catch (Exception ex)
        {
            // 仅在 WAL 设置失败时记录，避免与外键设置混淆
            _logger.LogError(ex, "设置 SQLite WAL 模式失败");
        }

        try
        {
            using var fkCmd = connection.CreateCommand();
            fkCmd.CommandText = "PRAGMA foreign_keys=ON;";
            fkCmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启用 SQLite 外键约束失败");
        }

        try
        {
            using var busyCmd = connection.CreateCommand();
            busyCmd.CommandText = "PRAGMA busy_timeout=30000;";
            busyCmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "设置 SQLite busy_timeout 失败");
        }

        // M-06：强制标准 SQL 引号语义（详见上方异步版本说明）。
        try
        {
            using var stdSqlCmd = connection.CreateCommand();
            stdSqlCmd.CommandText = "PRAGMA sql_standard_strings=ON;";
            stdSqlCmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启用 SQLite sql_standard_strings 失败");
        }

        base.ConnectionOpened(connection, eventData);
    }

    /// <summary>
    /// 连接失败时根据异常类型记录准确的错误信息，便于排查。
    /// </summary>
    public override Task ConnectionFailedAsync(DbConnection connection, ConnectionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        LogConnectionFailure(eventData.Exception, GetSafeConnectionString(connection));
        return base.ConnectionFailedAsync(connection, eventData, cancellationToken);
    }

    /// <summary>
    /// 连接失败时根据异常类型记录准确的错误信息，便于排查。
    /// </summary>
    public override void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData)
    {
        LogConnectionFailure(eventData.Exception, GetSafeConnectionString(connection));
        base.ConnectionFailed(connection, eventData);
    }

    /// <summary>
    /// 安全获取连接字符串，连接对象可能为 null 或访问 ConnectionString 时抛出异常。
    /// 会移除/掩码 Password、User ID、pwd、credential 等敏感键后再返回，避免日志泄露凭据。
    /// </summary>
    private static string GetSafeConnectionString(DbConnection? connection)
    {
        try
        {
            var raw = connection?.ConnectionString;
            if (string.IsNullOrEmpty(raw))
            {
                return "(unavailable)";
            }

            // 使用 DbConnectionStringBuilder 解析连接字符串，按提供程序无关方式掩码敏感键。
            // 即便解析失败（极少数非标准格式），也回退到原始字符串的脱敏占位符，绝不输出原始值。
            try
            {
                var builder = new DbConnectionStringBuilder
                {
                    ConnectionString = raw
                };

                // 敏感键列表（涵盖常见 SQLite/MSSQL/MySQL/Postgres 提供程序）：
                // Password / Pwd：密码
                // User ID / UID / Username：账号
                // Credential / Credentials：托管凭据对象
                // Persist Security Info：包含凭据时可能为 true
                var sensitiveKeys = new[]
                {
                    "Password", "Pwd", "PWD",
                    "User ID", "UID", "Username", "UserName",
                    "Credential", "Credentials",
                    "Persist Security Info"
                };

                foreach (var key in sensitiveKeys)
                {
                    // DbConnectionStringBuilder 索引器查找不区分大小写
                    if (builder.ContainsKey(key))
                    {
                        builder[key] = "***";
                    }
                }

                return builder.ConnectionString;
            }
            catch
            {
                // 解析失败：返回脱敏占位符，绝不返回可能含密码的原始字符串
                return "(unavailable: connection string parse failed)";
            }
        }
        catch
        {
            return "(unavailable)";
        }
    }

    private void LogConnectionFailure(Exception? exception, string connectionString)
    {
        if (exception == null)
        {
            _logger.LogError("数据库连接失败：未知错误（ConnectionString={ConnectionString}）", connectionString);
            return;
        }

        // 根据异常类型准确描述失败原因，避免误导排查
        if (IsTimeoutException(exception))
        {
            _logger.LogError(exception, "数据库连接超时：{Message}（ConnectionString={ConnectionString}）",
                exception.Message, connectionString);
        }
        else if (IsAuthException(exception))
        {
            _logger.LogError(exception, "数据库连接认证失败：{Message}（ConnectionString={ConnectionString}）",
                exception.Message, connectionString);
        }
        else
        {
            _logger.LogError(exception, "数据库连接失败：{Message}（ConnectionString={ConnectionString}）",
                exception.Message, connectionString);
        }
    }

    private static bool IsTimeoutException(Exception exception)
    {
        // 超时异常类型判断，兼容不同数据库提供程序
        return exception is TimeoutException
            || exception.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("连接超时", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("超时", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAuthException(Exception exception)
    {
        var message = exception.Message;
        return message.Contains("auth", StringComparison.OrdinalIgnoreCase)
            || message.Contains("password", StringComparison.OrdinalIgnoreCase)
            || message.Contains("credential", StringComparison.OrdinalIgnoreCase)
            || message.Contains("认证", StringComparison.OrdinalIgnoreCase)
            || message.Contains("授权", StringComparison.OrdinalIgnoreCase);
    }
}
