using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Templates.Providers;

/// <summary>
/// 满意度评价变量提供者 — satisfaction_url, rating_stars, rating_label 等
/// 所有评分变量预初始化为空字符串
/// </summary>
public class RatingVariableProvider : ITemplateVariableProvider
{
    public int Order => 50;

    public Task<Dictionary<string, string>> ResolveAsync(VariableContext context)
    {
        var vars = new Dictionary<string, string>
        {
            ["satisfaction_url"] = string.Empty,
            ["rating_url"] = string.Empty,
            ["feedback_url"] = string.Empty,
            ["satisfaction_rating"] = string.Empty,
            ["satisfaction_rating_value"] = string.Empty,
            ["satisfaction_comment"] = string.Empty,
            ["rating_stars"] = string.Empty,
            ["rating_label"] = string.Empty,
            ["rating_comment"] = string.Empty,
        };

        if (context.Rating is SatisfactionRating rating)
        {
            vars["satisfaction_rating"] = rating.Rating.ToString();
            vars["satisfaction_rating_value"] = rating.Rating.ToString();
            vars["rating_stars"] = new string('★', Math.Clamp(rating.Rating, 0, 5));
            vars["rating_label"] = GetRatingLabel(rating.Rating);
            vars["satisfaction_comment"] = rating.Comment ?? string.Empty;
            vars["rating_comment"] = rating.Comment ?? string.Empty;
        }

        // 聊天会话的评分
        if (context.ChatSession is ChatSession session && session.Rating.HasValue)
        {
            vars["satisfaction_rating"] = session.Rating.Value.ToString();
            vars["satisfaction_rating_value"] = session.Rating.Value.ToString();
            vars["rating_stars"] = new string('★', Math.Clamp(session.Rating.Value, 0, 5));
            vars["rating_label"] = GetRatingLabel(session.Rating.Value);
            vars["rating_comment"] = session.RatingComment ?? string.Empty;
            vars["satisfaction_comment"] = session.RatingComment ?? string.Empty;
        }

        return Task.FromResult(vars);
    }

    private static string GetRatingLabel(int rating) => rating switch
    {
        5 => "远超期望",
        4 => "物有所值",
        3 => "中规中矩",
        2 => "略有失望",
        1 => "无法接受",
        _ => "未知"
    };
}
