using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace NoneadCSS.BackendTestRunner
{
    internal sealed class NullSink : IMessageSink
    {
        public bool OnMessage(IMessageSinkMessage message) => true;
    }

    internal sealed class CollectingSink : IMessageSink
    {
        public readonly ConcurrentBag<ITestCase> Cases = new ConcurrentBag<ITestCase>();
        private readonly ManualResetEventSlim _done = new ManualResetEventSlim(false);

        public bool OnMessage(IMessageSinkMessage message)
        {
            if (message is ITestCaseDiscoveryMessage d && d.TestCase != null)
            {
                Cases.Add(d.TestCase);
            }
            else if (message is IDiscoveryCompleteMessage)
            {
                _done.Set();
            }
            return true;
        }

        /// <summary>等待发现阶段真正结束：Discoverer.Find 是异步的，返回时往往还没发现完。</summary>
        public bool WaitForCompletion(int millisecondsTimeout) => _done.Wait(millisecondsTimeout);
    }

    /// <summary>
    /// xunit 2.9 已不再公开 Xunit.TestFrameworkOptions，但执行引擎读取选项时走
    /// TestFrameworkOptionsReadExtensions 的 *OrDefault 包装，默认值由引擎兜底。
    /// 注意：这里**刻意不预置任何选项**——预置 maxParallelThreads=-1 等值会让执行
    /// 退化为串行并提前返回（实测）。
    /// </summary>
    internal class FrameworkOptionsBase : ITestFrameworkOptions
    {
        private readonly ConcurrentDictionary<string, object> _values = new ConcurrentDictionary<string, object>();

        public TValue GetValue<TValue>(string name)
        {
            if (_values.TryGetValue(name, out var v) && v != null)
            {
                if (v is TValue typed) return typed;
                try
                {
                    var target = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);
                    if (target.IsEnum) return (TValue)Enum.Parse(target, v.ToString(), true);
                    return (TValue)Convert.ChangeType(v, target);
                }
                catch { }
            }
            return default;
        }

        public void SetValue<TValue>(string name, TValue value) => _values[name] = value;
    }

    internal sealed class FrameworkExecutionOptions : FrameworkOptionsBase, ITestFrameworkExecutionOptions { }
    internal sealed class FrameworkDiscoveryOptions : FrameworkOptionsBase, ITestFrameworkDiscoveryOptions { }

    internal sealed class CountingSink : IMessageSink
    {
        public int Passed, Failed, Skipped;
        public readonly ConcurrentDictionary<string, byte> ReportedNames = new ConcurrentDictionary<string, byte>();
        public readonly ConcurrentBag<string> Failures = new ConcurrentBag<string>();
        public readonly ConcurrentBag<string> SkippedNames = new ConcurrentBag<string>();
        public readonly ConcurrentBag<string> Errors = new ConcurrentBag<string>();

        public bool OnMessage(IMessageSinkMessage message)
        {
            switch (message)
            {
                case ITestPassed p:
                    Interlocked.Increment(ref Passed);
                    if (p.TestCase != null) ReportedNames[p.TestCase.DisplayName] = 1;
                    break;

                case ITestFailed f:
                    Interlocked.Increment(ref Failed);
                    if (f.TestCase != null) ReportedNames[f.TestCase.DisplayName] = 1;
                    Failures.Add(
                        (f.TestCase != null ? f.TestCase.DisplayName : "?") +
                        "  ::  " + string.Join(", ", f.ExceptionTypes ?? Array.Empty<string>()) +
                        "  ::  " + Truncate(string.Join(" | ", f.Messages ?? Array.Empty<string>())));
                    break;

                case ITestSkipped s:
                    Interlocked.Increment(ref Skipped);
                    if (s.TestCase != null) ReportedNames[s.TestCase.DisplayName] = 1;
                    SkippedNames.Add(s.Test != null ? s.Test.DisplayName : "?");
                    break;

                case IErrorMessage e:
                    Errors.Add(Truncate(e.Messages == null ? "(无消息)" : string.Join(" | ", e.Messages)));
                    break;
            }
            return true;
        }

        private static string Truncate(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 400 ? s.Substring(0, 400) + " …" : s;
        }
    }

    public static class Program
    {
        public static int Main(string[] args)
        {
            // 用法: BackendTestRunner.dll <测试程序集> [--list] [--class <名称子串>] [--one-by-one]
            string dll = null;
            string classFilter = null;
            var listOnly = false;
            var oneByOne = false;

            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--list") { listOnly = true; }
                else if (args[i] == "--one-by-one") { oneByOne = true; }
                else if (args[i] == "--class" && i + 1 < args.Length) { classFilter = args[++i]; }
                else if (dll == null) { dll = args[i]; }
            }

            dll = Path.GetFullPath(dll ?? "NoneadCSS.Tests.dll");

            // ---- 依赖解析钩子 ----
            // 本工程的 deps.json 只声明自身依赖；测试项目那一百多个依赖（Microsoft.Extensions.*、
            // Moq、FluentAssertions、EF Core 等）不在其中，默认加载器不会去应用目录探测，故自行补上。
            var managedResolved = 0;
            var nativeResolved = 0;

            AssemblyLoadContext.Default.Resolving += (ctx, name) =>
            {
                var candidate = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
                if (File.Exists(candidate))
                {
                    Interlocked.Increment(ref managedResolved);
                    return ctx.LoadFromAssemblyPath(candidate);
                }
                return null;
            };

            AssemblyLoadContext.Default.ResolvingUnmanagedDll += (asm, name) =>
            {
                // 原生库既可能在输出根目录，也可能在 runtimes\<rid>\native\ 下
                // （e_sqlite3.dll 就在 runtimes\win-x64\native\，否则 SqliteConnection 静态构造会 DllNotFoundException）
                var probes = new[]
                {
                    Path.Combine(AppContext.BaseDirectory, name),
                    Path.Combine(AppContext.BaseDirectory, name + ".dll"),
                    Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", name + ".dll"),
                    Path.Combine(AppContext.BaseDirectory, "win-x64", name + ".dll"),
                };
                foreach (var p in probes)
                {
                    if (File.Exists(p))
                    {
                        Interlocked.Increment(ref nativeResolved);
                        return NativeLibrary.Load(p);
                    }
                }
                return IntPtr.Zero;
            };

            if (!File.Exists(dll))
            {
                Console.WriteLine("[runner] 目标程序集不存在: " + dll);
                return 2;
            }

            var framework = new XunitTestFramework(new NullSink());
            var fw = (ITestFramework)framework;
            var assembly = Assembly.LoadFrom(dll);
            var discoveryOptions = new FrameworkDiscoveryOptions();
            var executionOptions = new FrameworkExecutionOptions();

            // ---- 发现阶段 ----
            var collector = new CollectingSink();
            var discoverer = fw.GetDiscoverer(new ReflectionAssemblyInfo(assembly));
            discoverer.Find(false, collector, discoveryOptions);
            var discoveryCompleted = collector.WaitForCompletion(180000);
            Console.WriteLine("DISCOVERY_COMPLETED=" + discoveryCompleted);

            if (listOnly)
            {
                var classes = collector.Cases
                    .Select(c => c.TestMethod?.TestClass?.Class?.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Distinct()
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToList();
                Console.WriteLine("CLASS_COUNT=" + classes.Count);
                Console.WriteLine("CASE_COUNT=" + collector.Cases.Count);
                foreach (var c in classes) Console.WriteLine("CLASS=" + c);
                return 0;
            }

            IEnumerable<ITestCase> toRun = collector.Cases;
            if (!string.IsNullOrEmpty(classFilter))
            {
                toRun = collector.Cases.Where(c =>
                    (c.TestMethod?.TestClass?.Class?.Name ?? "").IndexOf(classFilter, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            var runList = toRun.ToList();

            var sink = new CountingSink();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var aborted = false;
            try
            {
                var executor = fw.GetExecutor(assembly.GetName());
                if (oneByOne)
                {
                    // 逐用例执行：xunit 在个别类上会提前返回，逐个跑可绕开并暴露具体是哪个用例
                    foreach (var cse in runList)
                    {
                        try
                        {
                            executor.RunTests(new[] { cse }, sink, executionOptions);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("CASE_EXCEPTION " + cse.DisplayName + " :: " + ex.GetType().Name + ": " + ex.Message);
                        }
                    }
                }
                else
                {
                    executor.RunTests(runList, sink, executionOptions);
                }
            }
            catch (Exception ex)
            {
                aborted = true;
                Console.WriteLine("RUNNER_EXCEPTION=" + ex.GetType().Name + ": " + ex.Message);
            }
            sw.Stop();

            // 等待可能仍在途的异步结果消息，再精确比对本轮选中用例是否都有结论。
            // 注意：不要用「执行前后的计数器差」判定缺口——异步结果消息未落地时会误报。
            Thread.Sleep(1500);
            var missing = runList
                .Select(c => c.DisplayName)
                .Distinct()
                .Where(n => !sink.ReportedNames.ContainsKey(n))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
            Console.WriteLine("MISSING_COUNT=" + missing.Count);
            foreach (var m in missing) Console.WriteLine("MISSING " + m);

            Console.WriteLine("RESULT_SUMMARY");
            Console.WriteLine("SUMMARY pass=" + sink.Passed + " fail=" + sink.Failed + " skip=" + sink.Skipped +
                              " reported=" + (sink.Passed + sink.Failed + sink.Skipped) + " selected=" + runList.Count +
                              " discovered=" + collector.Cases.Count);
            Console.WriteLine("  通过 = " + sink.Passed);
            Console.WriteLine("  失败 = " + sink.Failed);
            Console.WriteLine("  跳过 = " + sink.Skipped);
            Console.WriteLine("  耗时 = " + sw.Elapsed.TotalSeconds.ToString("F1") + " 秒");
            Console.WriteLine("  解析: managed=" + managedResolved + " native=" + nativeResolved);
            if (aborted) Console.WriteLine("  ⚠ 执行被中断（可能提前返回）");

            foreach (var e in sink.Errors.Take(10)) Console.WriteLine("  [ENGINE ERROR] " + e);
            foreach (var f in sink.Failures) Console.WriteLine("FAILCASE " + f);

            return sink.Failed == 0 ? 0 : 1;
        }
    }
}
