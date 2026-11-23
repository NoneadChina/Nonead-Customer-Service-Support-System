using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Infrastructure;
using NoneadCSS.Infrastructure.Persistence;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// 验证渠道接入时，新客户（Id=0）场景下 Conversation 实体通过导航属性 Customer
/// 而非 CustomerId=0 来避免 FK 约束失败的测试。
/// 
/// 核心问题：
///   老代码在创建新客户时直接设置 conversation.CustomerId = customer.Id，
///   但此时 customer.Id 还是默认值 0（EF 在 SaveChanges 后才回填 Id），
///   导致 Conversation.CustomerId 被写入 0，而非正确的客户 Id。
///   正确做法是设置 conversation.Customer = customer（导航属性），
///   让 EF Core 识别关系并先插入客户，再自动回填 CustomerId。
/// </summary>
public class IngestionServiceCustomerFkTests : IAsyncLifetime
{
    private NoneadCSSDbContext _context = null!;
    private UnitOfWork _unitOfWork = null!;

    public Task InitializeAsync()
    {
        // 使用独立命名的 InMemory 数据库，避免测试之间相互干扰
        var options = new DbContextOptionsBuilder<NoneadCSSDbContext>()
            .UseInMemoryDatabase(databaseName: $"CustomerFkTestDb_{Guid.NewGuid()}")
            .Options;

        _context = new NoneadCSSDbContext(options);
        // 使用真实的 Infrastructure.UnitOfWork，并通过 TestServiceProviderBuilder 注入仓储
        _unitOfWork = new UnitOfWork(_context, Shared.TestServiceProviderBuilder.Build(_context));

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        // 确保删除数据库（InMemory 提供程序中主要是清理状态）
        await _context.Database.EnsureDeletedAsync();
        await _context.DisposeAsync();
    }

    /// <summary>
    /// 验证正确模式：使用导航属性 Customer = customer，EF Core 自动处理插入顺序并回填 CustomerId。
    /// 这是修复后的推荐写法。
    /// </summary>
    [Fact]
    public async Task Conversation_WithNewCustomer_UsesNavigationProperty_EfFixesUpCustomerId()
    {
        // 1. 新建客户（Id 初始为默认值 0）
        var customer = new Customer
        {
            Channel = "wechat",
            ChannelId = "oAbc123",
            FirstName = "新",
            LastName = "客户"
        };

        // 2. 创建会话，使用【导航属性】关联客户，而不是直接设置 CustomerId
        var conversation = new Conversation
        {
            MailboxId = 1,
            Subject = "测试会话 - 新客户导航属性模式",
            Status = ConversationStatus.Active,
            State = ConversationState.Published,
            SourceType = SourceType.WeChat,
            CustomerEmail = "test@example.com",
            // 关键：使用导航属性关联，EF Core 会自动先插入客户并回填 Id
            Customer = customer
            // 注意：这里故意不设置 CustomerId = customer.Id（老代码 bug 写法）
        };

        // 3. 加入上下文并保存
        await _unitOfWork.Customers.AddAsync(customer);
        await _unitOfWork.Conversations.AddAsync(conversation);
        await _unitOfWork.SaveChangesAsync();

        // 4. 断言：客户 Id 已生成（> 0）
        customer.Id.Should().BeGreaterThan(0, "SaveChanges 后 EF 应回填客户 Id");

        // 5. 断言：会话的 CustomerId 已被 EF Core 正确回填，且等于客户 Id
        conversation.CustomerId.Should().BeGreaterThan(0,
            "使用导航属性时，EF 应在 SaveChanges 后自动回填 Conversation.CustomerId");
        conversation.CustomerId.Should().Be(customer.Id,
            "Conversation.CustomerId 应与 Customer.Id 一致");
    }

    /// <summary>
    /// 回归测试：演示老代码"直接设置 CustomerId = customer.Id"在客户 Id 尚未生成时的问题。
    /// 此测试故意构造 BUG 场景：显式设置 CustomerId = 0 且不设置 Customer 导航属性。
    /// 真实场景：接入代码在 new Customer() 后立刻读 customer.Id（此时是默认 0）赋给 conversation.CustomerId，
    ///          之后再 Add/SaveChanges，导致 CustomerId 被写入 0 而非新生成的客户 Id。
    /// </summary>
    [Fact]
    public async Task Conversation_OldCodeSettingCustomerIdDirectly_ZeroId_RegressionTest()
    {
        // 1. 新建客户
        var customer = new Customer
        {
            Channel = "wechat",
            ChannelId = "oBugDemo123",
            FirstName = "Bug演示",
            LastName = "客户"
        };
        await _unitOfWork.Customers.AddAsync(customer);

        // 2. 模拟老代码 Bug：显式把 CustomerId = 0（模拟在 customer.Id 还未生成时就已赋值），
        //    并且故意不设置 Customer 导航属性，这样 EF 无法自动修复关系。
        var conversation = new Conversation
        {
            MailboxId = 1,
            Subject = "老代码 Bug 演示 - 直接 CustomerId = 0",
            Status = ConversationStatus.Active,
            State = ConversationState.Published,
            SourceType = SourceType.WeChat,
            CustomerEmail = "bug@example.com",
            // BUG：直接写入 0（模拟老代码在客户 Id 生成前执行了 CustomerId = customer.Id）
            CustomerId = 0
            // 故意不设置 Customer 导航属性，模拟老代码
        };

        // 3. 保存
        await _unitOfWork.Conversations.AddAsync(conversation);
        await _unitOfWork.SaveChangesAsync();

        // 4. 断言 - Bug 现象：
        //    客户被保存后获得了正 Id，但 Conversation.CustomerId 仍然是之前写入的 0。
        //    因为显式设置了 CustomerId=0，EF 不会用导航属性修正它（何况这里没设置导航属性）。
        customer.Id.Should().BeGreaterThan(0, "客户被保存后 Id 应为正数");
        // BUG 确认：CustomerId 仍然为 0。这就是 Bug —— 真实 FK 数据库中若存在 FK 约束会报错，
        // 或者即使入库（如未启用 FK 的 SQLite）也会形成脏数据，导致会话查不到客户。
        conversation.CustomerId.Should().Be(0,
            "BUG：显式 CustomerId = 0 且不设置导航属性时，SaveChanges 后 CustomerId 仍然是 0，" +
            "无法关联到已保存的客户。修复方案：设置 conversation.Customer = customer（导航属性），" +
            "让 EF Core 自动处理关系并回填正确的 CustomerId。");
    }

    /// <summary>
    /// 验证已有客户场景（Id > 0）下，老方式 CustomerId = customer.Id 仍可正常工作。
    /// 这说明 Bug 只发生在"新客户 Id 尚未生成"的场景。
    /// </summary>
    [Fact]
    public async Task Conversation_ExistingCustomer_CustomerIdWorksFine()
    {
        // 1. 先创建客户并保存，确保 Id > 0
        var customer = new Customer
        {
            Channel = "email",
            ChannelId = "existing@example.com",
            FirstName = "已有",
            LastName = "客户"
        };
        await _unitOfWork.Customers.AddAsync(customer);
        await _unitOfWork.SaveChangesAsync();
        customer.Id.Should().BeGreaterThan(0, "先保存客户使其有正 Id");

        // 2. 使用老方式：直接设置 CustomerId = customer.Id（此时已有正 Id）
        var conversation = new Conversation
        {
            MailboxId = 1,
            Subject = "已有客户 - 直接赋值 CustomerId 正常",
            Status = ConversationStatus.Active,
            State = ConversationState.Published,
            SourceType = SourceType.Email,
            CustomerEmail = "existing@example.com",
            // 老方式在已有客户（Id>0）时没问题
            CustomerId = customer.Id
        };

        await _unitOfWork.Conversations.AddAsync(conversation);
        await _unitOfWork.SaveChangesAsync();

        // 3. 断言：Id 匹配正确
        conversation.CustomerId.Should().Be(customer.Id,
            "已有客户（Id>0）场景下，直接设置 CustomerId = customer.Id 可正常工作");
    }

    /// <summary>
    /// 模拟生产代码中"SaveChanges 后回填 CreatedByCustomerId"的模式。
    /// CreatedByCustomerId 不是 EF 的导航 FK，无法自动修复，需要手动回填。
    /// </summary>
    [Fact]
    public async Task CreatedByCustomerId_BackfilledAfterSaveChanges_WhenNewCustomer()
    {
        // 1. 新客户
        var customer = new Customer
        {
            Channel = "wechat",
            ChannelId = "oBackfill123",
            FirstName = "回填",
            LastName = "测试"
        };
        await _unitOfWork.Customers.AddAsync(customer);

        // 2. 会话使用导航属性关联客户，但 CreatedByCustomerId 显式初始化为 0，
        //    模拟老代码在客户 Id 生成前执行了 CreatedByCustomerId = customer.Id（此时=0）
        var conversation = new Conversation
        {
            MailboxId = 1,
            Subject = "回填 CreatedByCustomerId 测试",
            Status = ConversationStatus.Active,
            State = ConversationState.Published,
            SourceType = SourceType.WeChat,
            CustomerEmail = "backfill@example.com",
            Customer = customer,
            // CreatedByCustomerId 不是导航 FK，会被写入 0（模拟客户 Id 生成前就赋值了）
            CreatedByCustomerId = 0
        };

        await _unitOfWork.Conversations.AddAsync(conversation);
        await _unitOfWork.SaveChangesAsync();

        // 3. 此时 CustomerId 由 EF 自动修复好了（因为设置了导航属性），
        //    但 CreatedByCustomerId 仍然是 0（它不是导航 FK，EF 不会自动处理）
        conversation.CustomerId.Should().Be(customer.Id);
        customer.Id.Should().BeGreaterThan(0);
        conversation.CreatedByCustomerId.Should().Be(0,
            "SaveChanges 后 CreatedByCustomerId 仍然是 0，因为它不是导航 FK，需要手动回填");

        // 4. 生产代码中的回填逻辑：如果 CreatedByCustomerId == 0 且客户有了 Id，则回填
        if (conversation.CreatedByCustomerId == 0 && customer.Id > 0)
        {
            conversation.CreatedByCustomerId = customer.Id;
            await _unitOfWork.SaveChangesAsync();
        }

        // 5. 断言：回填成功
        conversation.CreatedByCustomerId.Should().Be(customer.Id,
            "手动回填后 CreatedByCustomerId 应等于 customer.Id");
    }

    /// <summary>
    /// 参数化测试：验证 9 个渠道（WeChat / DingTalk / Feishu(Lark) / WhatsApp / Email /
    /// Messenger / Telegram / LINE / iMessage）场景下，"设置导航属性 Customer = customer" 模式都能正确工作。
    /// </summary>
    /// <param name="sourceType">来源渠道类型</param>
    [Theory]
    [InlineData(SourceType.WeChat)]
    [InlineData(SourceType.DingTalk)]
    [InlineData(SourceType.Feishu)]   // 即 Lark
    [InlineData(SourceType.WhatsApp)]
    [InlineData(SourceType.Email)]
    [InlineData(SourceType.Messenger)]
    [InlineData(SourceType.Telegram)]
    [InlineData(SourceType.Line)]
    [InlineData(SourceType.IMessage)]
    public async Task NineChannels_AllUseNavigationProperty_CheckPattern(SourceType sourceType)
    {
        // 将 SourceType 转为对应渠道字符串标识（须与 ConversationRepository 的 channel 字面量一致）
        var channel = sourceType switch
        {
            SourceType.WeChat => "wechat",
            SourceType.DingTalk => "dingtalk",
            SourceType.Feishu => "feishu",
            SourceType.WhatsApp => "whatsapp",
            SourceType.Email => "email",
            SourceType.Messenger => "messenger",
            SourceType.Telegram => "telegram",
            SourceType.Line => "line",
            SourceType.IMessage => "imessage",
            _ => sourceType.ToString().ToLowerInvariant()
        };

        // 1. 新客户（Id=0），使用对应渠道
        var customer = new Customer
        {
            Channel = channel,
            ChannelId = $"{channel}_user_{Guid.NewGuid():N}",
            FirstName = $"渠道{sourceType}",
            LastName = "新客户"
        };
        customer.Id.Should().Be(0);
        await _unitOfWork.Customers.AddAsync(customer);

        // 2. 会话使用导航属性关联
        var conversation = new Conversation
        {
            MailboxId = 1,
            Subject = $"渠道 {sourceType} 导航属性测试",
            Status = ConversationStatus.Active,
            State = ConversationState.Published,
            SourceType = sourceType,
            CustomerEmail = $"{channel}@example.com",
            // 关键：使用导航属性
            Customer = customer
        };

        await _unitOfWork.Conversations.AddAsync(conversation);
        await _unitOfWork.SaveChangesAsync();

        // 3. 断言：任意渠道下 CustomerId 都被正确回填为正 Id 并与客户 Id 一致
        customer.Id.Should().BeGreaterThan(0, $"渠道 {sourceType}：客户 Id 应已生成");
        conversation.CustomerId.Should().BeGreaterThan(0,
            $"渠道 {sourceType}：使用导航属性时 Conversation.CustomerId 应 > 0");
        conversation.CustomerId.Should().Be(customer.Id,
            $"渠道 {sourceType}：Conversation.CustomerId 应与 Customer.Id 相等");
    }
}
