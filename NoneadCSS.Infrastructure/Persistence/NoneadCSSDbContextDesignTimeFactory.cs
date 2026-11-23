using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NoneadCSS.Infrastructure.Persistence;

public class NoneadCSSDbContextDesignTimeFactory : IDesignTimeDbContextFactory<NoneadCSSDbContext>
{
    public NoneadCSSDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<NoneadCSSDbContext>();

        // 允许通过设计时参数切换 Provider，便于生成不同数据库的迁移脚本：
        //   dotnet ef migrations script -- --Provider SqlServer --ConnectionString "Server=.;Database=xxx;..."
        //   dotnet ef migrations script -- --Provider Sqlite  [--ConnectionString "..."]
        var provider = GetArg(args, "Provider") ?? "Sqlite";
        var connectionString = GetArg(args, "ConnectionString");

        if (string.IsNullOrEmpty(connectionString))
        {
            connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        }

        if (string.IsNullOrEmpty(connectionString))
        {
            // 向上查找 NoneadCSS.Api/data 目录，避免硬编码开发机绝对路径
            var basePath = AppContext.BaseDirectory;
            string? projectRoot = null;
            var dir = new DirectoryInfo(basePath);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "NoneadCSS.sln")))
                {
                    projectRoot = dir.FullName;
                    break;
                }
                dir = dir.Parent;
            }

            if (string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var dbPath = Path.Combine(projectRoot ?? basePath, "NoneadCSS.Api", "data", "noneadcss.db");
                connectionString = $"Data Source={dbPath};Cache=Shared";
            }
            else
            {
                // SQL Server 默认用 LocalDB 占位，仅用于生成迁移脚本（不会真的建立连接）
                var mdfDir = Path.Combine(projectRoot ?? basePath, "NoneadCSS.Api", "data");
                Directory.CreateDirectory(mdfDir);
                var mdf = Path.Combine(mdfDir, "noneadcss_design.mdf");
                connectionString = $@"Data Source=(LocalDB)\MSSQLLocalDB;AttachDbFilename={mdf};Initial Catalog=noneadcss_design;Integrated Security=True;MultipleActiveResultSets=True";
            }
        }

        if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            optionsBuilder.UseSqlServer(connectionString);
        }
        else
        {
            optionsBuilder.UseSqlite(connectionString);
        }

        return new NoneadCSSDbContext(optionsBuilder.Options);
    }

    private static string? GetArg(string[] args, string key)
    {
        if (args == null || args.Length == 0) return null;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == null) continue;

            // --Key Value
            if (a.Equals($"--{key}", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }

            // --Key=Value
            if (a.StartsWith($"--{key}=", StringComparison.OrdinalIgnoreCase))
            {
                return a.Substring(a.IndexOf('=') + 1);
            }

            // /Provider Sqlite  (old style)
            if (a.Equals($"/{key}", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        return null;
    }
}