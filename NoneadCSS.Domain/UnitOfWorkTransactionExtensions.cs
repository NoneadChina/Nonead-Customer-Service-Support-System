namespace NoneadCSS.Domain;

/// <summary>
/// IUnitOfWorkTransaction 扩展方法。
/// </summary>
public static class UnitOfWorkTransactionExtensions
{
    /// <summary>
    /// 安全回滚事务：当回滚本身失败（如事务已被提交、连接断开、超时）时不抛异常。
    /// 这避免了回滚异常掩盖原始的业务异常，使全局异常处理器能拿到真实的失败原因。
    /// 失败时调用方可在 catch 中自行记录日志。
    /// </summary>
    /// <param name="transaction">事务对象</param>
    public static async Task SafeRollbackAsync(this IUnitOfWorkTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync();
        }
        catch
        {
            // 事务可能已被 CommitAsync 部分提交、连接已断开或事务已超时，
            // 此处吞掉回滚异常，避免掩盖原始异常；调用方应在 catch 中处理原始异常
        }
    }
}
