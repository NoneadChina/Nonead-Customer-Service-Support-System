namespace NoneadCSS.Application.Templates;

/// <summary>
/// 变量注册表 — 全项目唯一的变量规范定义源
/// 包含所有模板变量的规范名、同义词别名、中文标签和分类
/// </summary>
public static class TemplateVariableRegistry
{
    public record VariableDef(
        string Canonical,
        string[] Aliases,
        string Label,
        string Category
    );

    /// <summary>
    /// 所有变量的规范定义。新增变量只需在此添加一行。
    /// </summary>
    public static readonly Dictionary<string, VariableDef> Definitions = new()
    {
        // —— 会话 ——
        ["subject"]          = new("subject",          [],                   "会话主题",       "会话"),
        ["conversation_id"]  = new("conversation_id",  [],                   "会话ID",         "会话"),
        ["conversation_url"] = new("conversation_url", [],                   "会话链接",       "会话"),
        ["priority"]         = new("priority",         [],                   "优先级",         "会话"),
        ["update_content"]   = new("update_content",   [],                   "更新内容",       "会话"),

        // —— 客户 ——
        ["customer_name"]    = new("customer_name",    [],                   "客户姓名",       "客户"),
        ["customer_email"]   = new("customer_email",   [],                   "客户邮箱",       "客户"),
        ["customer_phone"]   = new("customer_phone",   [],                   "客户电话",       "客户"),

        // —— 用户/客服 ——
        ["user_name"]        = new("user_name",        [],                   "客服姓名",       "用户"),
        ["user_email"]       = new("user_email",       [],                   "客服邮箱",       "用户"),
        ["user_role"]        = new("user_role",        [],                   "客服角色",       "用户"),

        // —— 邮箱 ——
        ["mailbox_name"]     = new("mailbox_name",     [],                   "邮箱名称",       "邮箱"),
        ["mailbox_email"]    = new("mailbox_email",    [],                   "邮箱地址",       "邮箱"),

        // —— 消息 ——
        ["message_body"]     = new("message_body",     [],                   "最新消息内容",   "消息"),
        ["first_message_body"] = new("first_message_body", [],               "首条消息内容",   "消息"),

        // —— 角色/人员 ——
        ["assignee_name"]    = new("assignee_name",    ["assigned_to"],      "受让人姓名",     "角色"),
        ["assigner_name"]    = new("assigner_name",    [],                   "分配人姓名",     "角色"),
        ["replier_name"]     = new("replier_name",     ["reply_from"],       "回复人姓名",     "角色"),
        ["closer_name"]      = new("closer_name",      ["closed_by"],        "关闭人姓名",     "角色"),
        ["updater_name"]     = new("updater_name",     [],                   "更新人姓名",     "角色"),
        ["mentioner_name"]   = new("mentioner_name",   [],                   "提及人姓名",     "角色"),
        ["escalated_by"]     = new("escalated_by",     [],                   "升级人",         "角色"),

        // —— SLA ——
        ["sla_name"]         = new("sla_name",         [],                   "SLA策略名称",    "SLA"),
        ["sla_level"]        = new("sla_level",        [],                   "SLA优先级",      "SLA"),
        ["sla_remaining"]    = new("sla_remaining",    [],                   "SLA剩余时间",    "SLA"),
        ["sla_deadline"]     = new("sla_deadline",     ["deadline"],         "SLA截止时间",    "SLA"),
        ["sla_response_time"]= new("sla_response_time",[],                   "SLA响应时长",    "SLA"),
        ["remaining_time"]   = new("remaining_time",   [],                   "剩余时间",       "SLA"),
        ["violation_type"]   = new("violation_type",   [],                   "违规类型",       "SLA"),

        // —— 满意度 ——
        ["satisfaction_url"]      = new("satisfaction_url",      [],         "满意度评价链接", "满意度"),
        ["satisfaction_rating"]   = new("satisfaction_rating",   [],         "满意度评级",     "满意度"),
        ["satisfaction_rating_value"] = new("satisfaction_rating_value", [], "满意度分值",     "满意度"),
        ["satisfaction_comment"]  = new("satisfaction_comment",  [],         "满意度评论",     "满意度"),
        ["rating_url"]            = new("rating_url",            [],         "评分链接",       "满意度"),
        ["feedback_url"]          = new("feedback_url",          [],         "反馈链接",       "满意度"),
        ["rating_stars"]          = new("rating_stars",          [],         "评分星星",       "满意度"),
        ["rating_label"]          = new("rating_label",          [],         "评分标签",       "满意度"),
        ["rating_comment"]        = new("rating_comment",        [],         "评分评论",       "满意度"),

        // —— 升级 ——
        ["escalation_reason"] = new("escalation_reason", [],                  "升级原因",       "升级"),
        ["escalated_at"]      = new("escalated_at",      [],                  "升级时间",       "升级"),

        // —— 聊天 ——
        ["chat_session_id"]  = new("chat_session_id",   [],                   "聊天会话ID",     "聊天"),
        ["chat_created_at"]  = new("chat_created_at",   [],                   "聊天创建时间",   "聊天"),
        ["agent_name"]       = new("agent_name",        [],                   "接待客服",       "聊天"),
        ["chat_subject"]     = new("chat_subject",      [],                   "聊天主题",       "聊天"),

        // —— 日历 ——
        ["event_title"]      = new("event_title",       [],                   "事件标题",       "日历"),
        ["event_type"]       = new("event_type",        [],                   "事件类型",       "日历"),
        ["event_start_time"] = new("event_start_time",  [],                   "开始时间",       "日历"),
        ["event_end_time"]   = new("event_end_time",    [],                   "结束时间",       "日历"),
        ["event_location"]   = new("event_location",    [],                   "事件地点",       "日历"),
        ["event_meeting_url"]= new("event_meeting_url", [],                   "会议链接",       "日历"),
        ["event_description"]= new("event_description", [],                   "事件描述",       "日历"),
        ["event_priority"]   = new("event_priority",    [],                   "事件优先级",     "日历"),
        ["event_url"]        = new("event_url",         [],                   "事件链接",       "日历"),

        // —— 时间 ——
        ["created_at"]       = new("created_at",        [],                   "创建时间",       "系统"),
        ["received_time"]    = new("received_time",     [],                   "接收时间",       "系统"),
        ["last_reply_time"]  = new("last_reply_time",   [],                   "最后回复时间",   "系统"),
        ["waiting_time"]     = new("waiting_time",      [],                   "等待时长",       "系统"),
        ["timeout_minutes"]  = new("timeout_minutes",   [],                   "超时分钟数",     "系统"),
        ["expire_time"]      = new("expire_time",       [],                   "过期时间",       "系统"),

        // —— 系统 ——
        ["company_name"]         = new("company_name",         [],            "公司名称",       "系统"),
        ["login_url"]            = new("login_url",            [],            "登录链接",       "系统"),
        ["reset_password_url"]   = new("reset_password_url",   ["reset_url"], "重置密码链接",   "系统"),
        ["activation_url"]       = new("activation_url",       [],            "激活链接",       "系统"),

        // —— 批量导入 ——
        ["total_count"]      = new("total_count",       [],                   "总记录数",       "导入"),
        ["success_count"]    = new("success_count",     [],                   "成功数",         "导入"),
        ["failed_count"]     = new("failed_count",      [],                   "失败数",         "导入"),
        ["error_report_url"] = new("error_report_url",  [],                   "错误报告链接",   "导入"),
    };

    /// <summary>
    /// 别名展开：将规范变量的值复制到所有同义词，
    /// 这样模板里写 assignee_name 或 assigned_to 都能拿到值
    /// </summary>
    public static Dictionary<string, string> Expand(Dictionary<string, string> variables)
    {
        var result = new Dictionary<string, string>(variables);
        foreach (var kvp in variables)
        {
            if (Definitions.TryGetValue(kvp.Key, out var def))
            {
                foreach (var alias in def.Aliases)
                {
                    if (!result.ContainsKey(alias))
                        result[alias] = kvp.Value;
                }
            }
        }
        return result;
    }

    /// <summary>
    /// 返回所有变量定义（供前端展示可用变量列表）
    /// </summary>
    public static List<VariableDef> GetAll() => Definitions.Values.ToList();
}
