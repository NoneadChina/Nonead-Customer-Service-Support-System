using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Api.Controllers;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Infrastructure.Services;
using Xunit;

namespace NoneadCSS.Tests.Controllers;

/// <summary>
/// 附件控制器**按错误码分支**的回归测试（2026-09-27 批次 12 切片④a）。
///
/// 改造前：控制器用 `ex.Message.Contains("无权访问")` / `Contains("仅图片类型")` 决定回 403 / 400 / 404
/// —— 即把**中文异常文案当控制流判据**，一旦把文案本地化（en 界面下变英文）分支就会静默失效。
/// 现在改为按 <see cref="AttachmentException.Code"/> 判定，本文件锁三件事：
/// ① 三个错误码各自映射到正确的状态（403 / 400 / 404），且**与异常文案无关**（故意传英文文案）；
/// ② 未编码的 `InvalidOperationException` 维持改造前的兜底行为（不再靠文案猜 403）；
/// ③ `PublicPreview` 的既有行为（"仅图片"今天落到 404）**不被顺手改掉**。
/// </summary>
public class AttachmentsControllerCodeBranchTests
{
    private static Mock<ILocalizationService> Localizer()
    {
        var mock = new Mock<ILocalizationService>();
        mock.Setup(l => l[It.IsAny<string>()]).Returns<string>(k => $"L:{k}");
        return mock;
    }

    private static AttachmentsController Build(Mock<IAttachmentService> service)
    {
        var controller = new AttachmentsController(
            service.Object,
            Localizer().Object,
            Options.Create(new AttachmentSettings()));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    // 匿名类型的属性是 internal ⇒ 用 dynamic 会抛 RuntimeBinderException，改用反射（同 ApiResponseWrapperFilter 的取法）
    private static string? MessageOf(IActionResult result)
    {
        if (result is not ObjectResult obj || obj.Value is null) return null;
        var type = obj.Value.GetType();
        var prop = type.GetProperty("message") ?? type.GetProperty("Message");
        return prop?.GetValue(obj.Value) as string;
    }

    // ── Download（改造前：Contains("无权访问") ? 403 : 404）──

    [Fact]
    public async Task Download_AccessDeniedCode_ReturnsForbid_RegardlessOfMessageLanguage()
    {
        var svc = new Mock<IAttachmentService>();
        svc.Setup(s => s.DownloadAsync(1, It.IsAny<int?>(), It.IsAny<bool>()))
           .ThrowsAsync(new AttachmentException(AttachmentErrorCode.AccessDenied, "Access denied"));

        var result = await Build(svc).Download(1);

        result.Should().BeOfType<ForbidResult>("错误码才是判据，异常文案是英文也一样回 403");
    }

    [Fact]
    public async Task Download_NotFoundCode_ReturnsLocalizedNotFoundMessage()
    {
        var svc = new Mock<IAttachmentService>();
        svc.Setup(s => s.DownloadAsync(1, It.IsAny<int?>(), It.IsAny<bool>()))
           .ThrowsAsync(new AttachmentException(AttachmentErrorCode.NotFound, "Attachment not found"));

        var result = await Build(svc).Download(1);

        result.Should().BeOfType<NotFoundObjectResult>();
        MessageOf(result).Should().Be("L:AttachmentNotFound");
    }

    [Fact]
    public async Task Download_UncodedChineseMessage_DoesNotBecomeForbid()
    {
        // 文案里带"无权访问"但**不是**编码异常 ⇒ 不能被文案骗成 403（探针式回归）
        var svc = new Mock<IAttachmentService>();
        svc.Setup(s => s.DownloadAsync(1, It.IsAny<int?>(), It.IsAny<bool>()))
           .ThrowsAsync(new InvalidOperationException("无权访问此附件"));

        var result = await Build(svc).Download(1);

        result.Should().BeOfType<NotFoundObjectResult>("未编码异常走兜底分支（按附件不存在处理），与改造前一致");
    }

    // ── Preview（改造前：无权 → 403；仅图片 → 400；其它 → 404）──

    [Fact]
    public async Task Preview_NotImageCode_ReturnsBadRequest_WithLocalizedMessage()
    {
        var svc = new Mock<IAttachmentService>();
        svc.Setup(s => s.GetPreviewAsync(1, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<bool>()))
           .ThrowsAsync(new AttachmentException(AttachmentErrorCode.NotImage, "Only images can be previewed"));

        var result = await Build(svc).Preview(1);

        result.Should().BeOfType<BadRequestObjectResult>();
        MessageOf(result).Should().Be("L:AttachmentPreviewImageOnly");
    }

    [Fact]
    public async Task Preview_AccessDeniedCode_ReturnsForbid()
    {
        var svc = new Mock<IAttachmentService>();
        svc.Setup(s => s.GetPreviewAsync(1, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<bool>()))
           .ThrowsAsync(new AttachmentException(AttachmentErrorCode.AccessDenied));

        var result = await Build(svc).Preview(1);

        result.Should().BeOfType<ForbidResult>();
    }

    // ── PublicPreview：只对"无权"做 403，其余（含 NotImage）维持裸 404 ──

    [Fact]
    public async Task PublicPreview_AccessDeniedCode_ReturnsForbid_AndNotImageKeepsNotFound()
    {
        var denied = new Mock<IAttachmentService>();
        denied.Setup(s => s.GetPreviewAsync(1, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<bool>()))
              .ThrowsAsync(new AttachmentException(AttachmentErrorCode.AccessDenied));

        var noImage = new Mock<IAttachmentService>();
        noImage.Setup(s => s.GetPreviewAsync(1, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<bool>()))
               .ThrowsAsync(new AttachmentException(AttachmentErrorCode.NotImage));

        (await Build(denied).PublicPreview(1)).Should().BeOfType<ForbidResult>();
        (await Build(noImage).PublicPreview(1)).Should().BeOfType<NotFoundResult>(
            "既有行为就是裸 404 —— 本切片只换判据，不顺手改状态码");
    }

    // ── 列表/删除类端点的 `when` 过滤（改造前 catch .. when (Message.Contains)）──

    [Fact]
    public async Task GetAttachment_AccessDeniedCode_ReturnsForbid()
    {
        var svc = new Mock<IAttachmentService>();
        svc.Setup(s => s.GetAttachmentAsync(1, It.IsAny<int?>(), It.IsAny<bool>()))
           .ThrowsAsync(new AttachmentException(AttachmentErrorCode.AccessDenied));

        var result = await Build(svc).GetAttachment(1);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Delete_AccessDeniedCode_ReturnsForbid()
    {
        var svc = new Mock<IAttachmentService>();
        svc.Setup(s => s.DeleteAsync(1, It.IsAny<int?>(), It.IsAny<bool>()))
           .ThrowsAsync(new AttachmentException(AttachmentErrorCode.AccessDenied));

        var result = await Build(svc).Delete(1);

        result.Should().BeOfType<ForbidResult>();
    }
}
