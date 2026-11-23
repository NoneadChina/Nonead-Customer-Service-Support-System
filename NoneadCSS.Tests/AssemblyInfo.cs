using Xunit;

// 本测试套件共享可变的静态状态（如 SecretHasher._key，由 H-08 测试通过反射置空以验证未初始化守卫）。
// 默认的并行 collection 执行下，这些并发重置会与其他依赖该静态状态的测试（如 CalendarFeedControllerTests
// 的 GenerateToken）产生竞态，导致间歇性 "SecretHasher 未初始化" 失败。
// 因此对本程序集禁用测试并行化，保证测试串行执行、结果可靠稳定。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
