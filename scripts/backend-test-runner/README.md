# 后端测试执行器（进程内 xunit）

本机 `dotnet test` / `dotnet vstest` **恒失败**时用来跑 `NoneadCSS.Tests` 全量测试的替代执行器：直接在本进程内驱动 xunit 执行引擎，**不进 VSTest、不启 testhost**。

## 为什么需要它

本机运行 `dotnet test`（或 `dotnet vstest <测试DLL>`）会在启动瞬间中止：

```
源“…\NoneadCSS.Tests.dll”的 Testhost 进程已退出，但出现错误:
Unhandled exception. System.ComponentModel.Win32Exception (5): 拒绝访问。
   at System.Diagnostics.Process.EnableRaisingEvents(...)
   at Microsoft.VisualStudio.TestPlatform.TestHost.DefaultEngineInvoker.SetParentProcessExitCallback(...)
测试运行已中止。
```

即 testhost 无法打开 `vstest.console` 父进程的句柄。已验证下列手段**均无效**（不要重复尝试）：`--arch x64`、runsettings `<TargetPlatform>x64</TargetPlatform>`、WMI 脱离进程树启动、关闭 MSBuild/VBCSCompiler 构建节点。该问题与进程树、完整性级别（Medium）、架构（testhost 仅有 x64）均无关。

## 用法

```powershell
# 1) 构建执行器（输出必须落到测试输出目录，否则依赖解析不到）
dotnet build scripts/backend-test-runner/BackendTestRunner.csproj -c Release `
  -o NoneadCSS.Tests/bin/Release/net10.0

# 2) 确保测试程序集已构建
dotnet build NoneadCSS.Tests/NoneadCSS.Tests.csproj -c Release

# 3) 运行
$t = "NoneadCSS.Tests/bin/Release/net10.0"
dotnet "$t/BackendTestRunner.dll" "$t/NoneadCSS.Tests.dll" --one-by-one
```

### 模式

| 参数 | 作用 |
|---|---|
| 无 | 一次性执行全部用例（最快，但可能被个别类「提前返回」拖住） |
| `--list` | 只做发现，输出 `CLASS_COUNT=` / `CASE_COUNT=` 与全部 `CLASS=` 行 |
| `--class <子串>` | 只跑类名含该子串的用例（分片用） |
| `--one-by-one` | 逐个用例执行（绕开提前返回，最慢但最完整） |

### 推荐流程（零缺口）

```powershell
pwsh -File scripts/backend-test-runner/run-all-sharded.ps1
```

该脚本先**一类一进程**分片跑完全部类，再对「提前返回」的类用 `--one-by-one` 补齐。

### 结果判读

```
MISSING_COUNT=0
SUMMARY pass=2228 fail=0 skip=5 reported=2233 selected=2233 discovered=2233
```

- `reported` 必须等于 `selected`，且 `MISSING_COUNT=0`，才算零缺口。
- 判定用例是否都有结论，要用**「选中 displayName 集合 vs 已报告集合」**比对；**不要**用执行前后计数器差——异步结果消息未落地时会误报缺失。
- 退出码：`0` = 无失败；`1` = 有失败；`2` = 目标程序集不存在/执行期异常。

## 四个必须踩对的坑（改这个工程时别踩回去）

1. **必须 `FrameworkReference Microsoft.AspNetCore.App`**。测试程序集依赖 ASP.NET Core 共享框架，普通控制台应用只加载 `Microsoft.NETCore.App`，否则 `Microsoft.Extensions.*` / `Microsoft.AspNetCore.*` 全部 `FileNotFoundException`（表现为上千个假失败）。
2. **必须自己挂依赖解析钩子**：`AssemblyLoadContext.Default.Resolving` 按 `AppContext.BaseDirectory` 补托管依赖；`ResolvingUnmanagedDll` 还要探 `runtimes/win-x64/native/`（`e_sqlite3.dll` 在那里，否则 `SqliteConnection` 静态构造抛 `DllNotFoundException`）。
3. **xunit 2.9 没有公开的 `TestFrameworkOptions`**：自行实现 `ITestFrameworkExecutionOptions` / `ITestFrameworkDiscoveryOptions`（只有 `GetValue<T>`/`SetValue<T>`），**且不要预置任何选项**——预置 `maxParallelThreads=-1` 等值会让执行退化为串行并提前返回；默认值由引擎的 `*OrDefault` 扩展方法兜底。
4. **`ITestFrameworkDiscoverer.Find` 是异步的**：调用后必须等 `IDiscoveryCompleteMessage` 再取结果，否则拿到的是进行中的快照（实测 0 → 46 → 1470）。

另：`ITestFrameworkExecutor.RunAll` 返回 `void`；要分片得用 `RunTests(IEnumerable<ITestCase>, IMessageSink, ITestFrameworkExecutionOptions)`。

## 已验证结果

- **2026-11-23（v2.3.0 工作树，当前基线）**：`--list` 输出 `CLASS_COUNT=124` / `CASE_COUNT=2233`；全量结果为 **2233 用例全部有结论 —— 2228 通过 / 0 失败 / 5 跳过**，`MISSING_COUNT=0`。
- 2026-10-10 提交 `fbe4f4f`（v2.2.17）时为 1470 用例 / 1465 通过 / 0 失败 / 5 跳过。
- 5 个跳过均为源码里显式 `[Fact(Skip = …)]`（InMemory 提供程序不支持 `ExecuteDeleteAsync` / `ExecuteUpdateAsync`）。

## 说明

- 本工程**不进 `NoneadCSS.slnx`**，不参与常规 `dotnet build` 与 CI。
- 本工程内的 `bin/`、`obj/` 由 `.gitignore` 排除。
