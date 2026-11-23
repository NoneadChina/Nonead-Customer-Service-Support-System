using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Templates.Providers;

/// <summary>
/// SLA 变量提供者 — sla_name, sla_level, sla_remaining, deadline 等
/// 所有 SLA 变量预初始化为空字符串，避免条件注入导致占位符残留
/// </summary>
public class SlaVariableProvider : ITemplateVariableProvider
{
    public int Order => 40;

    public Task<Dictionary<string, string>> ResolveAsync(VariableContext context)
    {
        // 预初始化所有 SLA 变量为空字符串
        var vars = new Dictionary<string, string>
        {
            ["sla_name"] = string.Empty,
            ["sla_level"] = string.Empty,
            ["sla_remaining"] = string.Empty,
            ["sla_deadline"] = string.Empty,
            ["deadline"] = string.Empty,
            ["sla_response_time"] = string.Empty,
            ["remaining_time"] = string.Empty,
            ["violation_type"] = string.Empty,
        };

        if (context.SlaPolicy is SlaPolicy sla)
        {
            vars["sla_name"] = sla.Name ?? string.Empty;
            vars["sla_level"] = sla.Priority.ToString();
            vars["sla_response_time"] = $"{sla.FirstResponseHours} 小时";
        }

        if (context.SlaViolation is SlaViolation violation)
        {
            vars["violation_type"] = violation.ViolationType ?? string.Empty;
            var deadlineStr = violation.DueAt.ToString("yyyy-MM-dd HH:mm");
            vars["deadline"] = deadlineStr;
            vars["sla_deadline"] = deadlineStr;
        }

        return Task.FromResult(vars);
    }
}
