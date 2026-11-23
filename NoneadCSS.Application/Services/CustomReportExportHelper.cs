using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using NoneadCSS.Application.DTOs.Responses;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace NoneadCSS.Application.Services;

public class CustomReportExportHelper
{
    static CustomReportExportHelper()
    {
        try
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }
        catch
        {
            // QuestPDF 许可证只能设置一次，多次设置会抛异常。
            // 静态构造函数中无法注入 logger，此处静默忽略是安全的：
            // 许可证要么已在前一次调用中成功设置，要么后续导出时会再次抛出。
        }
    }

    public byte[] ExportToCsv(CustomReportExecutionResultDto result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"报表名称,{CsvEscape(result.ReportMeta.Name)}");
        sb.AppendLine($"开始日期,{result.ReportMeta.StartDate:yyyy-MM-dd}");
        sb.AppendLine($"结束日期,{result.ReportMeta.EndDate:yyyy-MM-dd}");
        sb.AppendLine();

        foreach (var section in result.Sections)
        {
            sb.AppendLine($"【{SectionDisplayName(section.Key)}】");
            if (section.Data == null)
            {
                sb.AppendLine("(无数据)");
                sb.AppendLine();
                continue;
            }

            if (section.Key == "summary")
            {
                var summary = section.Data as ReportSummaryDto ?? JsonSerializer.Deserialize<ReportSummaryDto>(JsonSerializer.Serialize(section.Data));
                if (summary != null)
                {
                    sb.AppendLine("指标,数值");
                    sb.AppendLine($"新建会话数,{summary.NewConversations}");
                    sb.AppendLine($"关闭会话数,{summary.ClosedConversations}");
                    sb.AppendLine($"客服回复数,{summary.TotalReplies}");
                    sb.AppendLine($"新增客户数,{summary.NewCustomers}");
                    sb.AppendLine($"平均首次响应时长(小时),{summary.AverageFirstResponseHours:F2}");
                    sb.AppendLine($"平均解决时长(小时),{summary.AverageResolutionHours:F2}");
                    sb.AppendLine($"SLA达成率(%),{summary.SlaAchievementRate:F2}");
                }
            }
            else if (section.Key == "trend")
            {
                var trend = section.Data as List<ReportTrendPointDto> ?? JsonSerializer.Deserialize<List<ReportTrendPointDto>>(JsonSerializer.Serialize(section.Data));
                if (trend != null && trend.Count > 0)
                {
                    sb.AppendLine("日期,新建会话数,关闭会话数,回复数");
                    foreach (var p in trend)
                    {
                        sb.AppendLine($"{CsvEscape(p.Date)},{p.NewCount},{p.ClosedCount},{p.ReplyCount}");
                    }
                }
            }
            else if (section.Key == "statusDistribution")
            {
                var list = section.Data as List<ReportStatusDistributionDto> ?? JsonSerializer.Deserialize<List<ReportStatusDistributionDto>>(JsonSerializer.Serialize(section.Data));
                if (list != null && list.Count > 0)
                {
                    sb.AppendLine("状态,状态名称,数量,百分比(%)");
                    foreach (var x in list)
                    {
                        sb.AppendLine($"{x.Status},{CsvEscape(x.StatusName)},{x.Count},{x.Percentage:F2}");
                    }
                }
            }
            else if (section.Key == "userPerformance")
            {
                var list = section.Data as List<ReportUserPerformanceDto> ?? JsonSerializer.Deserialize<List<ReportUserPerformanceDto>>(JsonSerializer.Serialize(section.Data));
                if (list != null && list.Count > 0)
                {
                    sb.AppendLine("用户ID,用户名,邮箱,分配会话数,回复数,关闭会话数,平均首次响应(小时)");
                    foreach (var x in list)
                    {
                        sb.AppendLine($"{x.UserId},{CsvEscape(x.UserName)},{CsvEscape(x.Email)},{x.AssignedCount},{x.ReplyCount},{x.ClosedCount},{x.AverageFirstResponseHours:F2}");
                    }
                }
            }
            else if (section.Key == "mailboxLoad")
            {
                var list = section.Data as List<ReportMailboxLoadDto> ?? JsonSerializer.Deserialize<List<ReportMailboxLoadDto>>(JsonSerializer.Serialize(section.Data));
                if (list != null && list.Count > 0)
                {
                    sb.AppendLine("邮箱ID,邮箱名称,邮箱,新建会话数,关闭会话数,当前活跃数,百分比(%)");
                    foreach (var x in list)
                    {
                        sb.AppendLine($"{x.MailboxId},{CsvEscape(x.MailboxName)},{CsvEscape(x.Email)},{x.NewCount},{x.ClosedCount},{x.ActiveCount},{x.Percentage:F2}");
                    }
                }
            }
            else if (section.Key == "customerDomains")
            {
                var list = section.Data as List<ReportCustomerDomainDto> ?? JsonSerializer.Deserialize<List<ReportCustomerDomainDto>>(JsonSerializer.Serialize(section.Data));
                if (list != null && list.Count > 0)
                {
                    sb.AppendLine("域名,会话数,独立客户数,百分比(%)");
                    foreach (var x in list)
                    {
                        sb.AppendLine($"{CsvEscape(x.Domain)},{x.ConversationCount},{x.UniqueCustomerCount},{x.Percentage:F2}");
                    }
                }
            }
            else
            {
                sb.AppendLine(JsonSerializer.Serialize(section.Data));
            }
            sb.AppendLine();
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    public byte[] ExportToExcel(CustomReportExecutionResultDto result)
    {
        using var wb = new XLWorkbook();
        var metaWs = wb.AddWorksheet("报表概览");
        metaWs.Cell(1, 1).Value = "报表名称";
        metaWs.Cell(1, 2).Value = result.ReportMeta.Name;
        metaWs.Cell(2, 1).Value = "开始日期";
        metaWs.Cell(2, 2).Value = result.ReportMeta.StartDate;
        metaWs.Cell(2, 2).Style.DateFormat.Format = "yyyy-MM-dd";
        metaWs.Cell(3, 1).Value = "结束日期";
        metaWs.Cell(3, 2).Value = result.ReportMeta.EndDate;
        metaWs.Cell(3, 2).Style.DateFormat.Format = "yyyy-MM-dd";
        metaWs.Cell(1, 1).Style.Font.Bold = true;
        metaWs.Cell(2, 1).Style.Font.Bold = true;
        metaWs.Cell(3, 1).Style.Font.Bold = true;
        metaWs.Columns().AdjustToContents();

        foreach (var section in result.Sections)
        {
            var wsName = SectionDisplayName(section.Key);
            if (wsName.Length > 31) wsName = wsName[..31];
            var ws = wb.AddWorksheet(wsName);

            if (section.Data == null)
            {
                ws.Cell(1, 1).Value = "(无数据)";
                continue;
            }

            if (section.Key == "summary")
            {
                var summary = section.Data as ReportSummaryDto ?? JsonSerializer.Deserialize<ReportSummaryDto>(JsonSerializer.Serialize(section.Data));
                if (summary != null)
                {
                    ws.Cell(1, 1).Value = "指标";
                    ws.Cell(1, 2).Value = "数值";
                    ws.Range(1, 1, 1, 2).Style.Font.Bold = true;
                    ws.Cell(2, 1).Value = "新建会话数"; ws.Cell(2, 2).Value = summary.NewConversations;
                    ws.Cell(3, 1).Value = "关闭会话数"; ws.Cell(3, 2).Value = summary.ClosedConversations;
                    ws.Cell(4, 1).Value = "客服回复数"; ws.Cell(4, 2).Value = summary.TotalReplies;
                    ws.Cell(5, 1).Value = "新增客户数"; ws.Cell(5, 2).Value = summary.NewCustomers;
                    ws.Cell(6, 1).Value = "平均首次响应时长(小时)"; ws.Cell(6, 2).Value = Math.Round(summary.AverageFirstResponseHours, 2);
                    ws.Cell(7, 1).Value = "平均解决时长(小时)"; ws.Cell(7, 2).Value = Math.Round(summary.AverageResolutionHours, 2);
                    ws.Cell(8, 1).Value = "SLA达成率(%)"; ws.Cell(8, 2).Value = Math.Round(summary.SlaAchievementRate, 2);
                }
            }
            else if (section.Key == "trend")
            {
                var trend = section.Data as List<ReportTrendPointDto> ?? JsonSerializer.Deserialize<List<ReportTrendPointDto>>(JsonSerializer.Serialize(section.Data));
                if (trend != null && trend.Count > 0)
                {
                    ws.Cell(1, 1).Value = "日期";
                    ws.Cell(1, 2).Value = "新建会话数";
                    ws.Cell(1, 3).Value = "关闭会话数";
                    ws.Cell(1, 4).Value = "回复数";
                    ws.Range(1, 1, 1, 4).Style.Font.Bold = true;
                    for (int i = 0; i < trend.Count; i++)
                    {
                        var p = trend[i];
                        ws.Cell(i + 2, 1).Value = p.Date;
                        ws.Cell(i + 2, 2).Value = p.NewCount;
                        ws.Cell(i + 2, 3).Value = p.ClosedCount;
                        ws.Cell(i + 2, 4).Value = p.ReplyCount;
                    }
                }
            }
            else if (section.Key == "statusDistribution")
            {
                var list = section.Data as List<ReportStatusDistributionDto> ?? JsonSerializer.Deserialize<List<ReportStatusDistributionDto>>(JsonSerializer.Serialize(section.Data));
                if (list != null && list.Count > 0)
                {
                    ws.Cell(1, 1).Value = "状态"; ws.Cell(1, 2).Value = "状态名称"; ws.Cell(1, 3).Value = "数量"; ws.Cell(1, 4).Value = "百分比(%)";
                    ws.Range(1, 1, 1, 4).Style.Font.Bold = true;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var x = list[i];
                        ws.Cell(i + 2, 1).Value = x.Status;
                        ws.Cell(i + 2, 2).Value = x.StatusName;
                        ws.Cell(i + 2, 3).Value = x.Count;
                        ws.Cell(i + 2, 4).Value = Math.Round(x.Percentage, 2);
                    }
                }
            }
            else if (section.Key == "userPerformance")
            {
                var list = section.Data as List<ReportUserPerformanceDto> ?? JsonSerializer.Deserialize<List<ReportUserPerformanceDto>>(JsonSerializer.Serialize(section.Data));
                if (list != null && list.Count > 0)
                {
                    ws.Cell(1, 1).Value = "用户ID"; ws.Cell(1, 2).Value = "用户名"; ws.Cell(1, 3).Value = "邮箱";
                    ws.Cell(1, 4).Value = "分配会话数"; ws.Cell(1, 5).Value = "回复数"; ws.Cell(1, 6).Value = "关闭会话数"; ws.Cell(1, 7).Value = "平均首次响应(小时)";
                    ws.Range(1, 1, 1, 7).Style.Font.Bold = true;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var x = list[i];
                        ws.Cell(i + 2, 1).Value = x.UserId;
                        ws.Cell(i + 2, 2).Value = x.UserName;
                        ws.Cell(i + 2, 3).Value = x.Email;
                        ws.Cell(i + 2, 4).Value = x.AssignedCount;
                        ws.Cell(i + 2, 5).Value = x.ReplyCount;
                        ws.Cell(i + 2, 6).Value = x.ClosedCount;
                        ws.Cell(i + 2, 7).Value = Math.Round(x.AverageFirstResponseHours, 2);
                    }
                }
            }
            else if (section.Key == "mailboxLoad")
            {
                var list = section.Data as List<ReportMailboxLoadDto> ?? JsonSerializer.Deserialize<List<ReportMailboxLoadDto>>(JsonSerializer.Serialize(section.Data));
                if (list != null && list.Count > 0)
                {
                    ws.Cell(1, 1).Value = "邮箱ID"; ws.Cell(1, 2).Value = "邮箱名称"; ws.Cell(1, 3).Value = "邮箱";
                    ws.Cell(1, 4).Value = "新建会话数"; ws.Cell(1, 5).Value = "关闭会话数"; ws.Cell(1, 6).Value = "当前活跃数"; ws.Cell(1, 7).Value = "百分比(%)";
                    ws.Range(1, 1, 1, 7).Style.Font.Bold = true;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var x = list[i];
                        ws.Cell(i + 2, 1).Value = x.MailboxId;
                        ws.Cell(i + 2, 2).Value = x.MailboxName;
                        ws.Cell(i + 2, 3).Value = x.Email;
                        ws.Cell(i + 2, 4).Value = x.NewCount;
                        ws.Cell(i + 2, 5).Value = x.ClosedCount;
                        ws.Cell(i + 2, 6).Value = x.ActiveCount;
                        ws.Cell(i + 2, 7).Value = Math.Round(x.Percentage, 2);
                    }
                }
            }
            else if (section.Key == "customerDomains")
            {
                var list = section.Data as List<ReportCustomerDomainDto> ?? JsonSerializer.Deserialize<List<ReportCustomerDomainDto>>(JsonSerializer.Serialize(section.Data));
                if (list != null && list.Count > 0)
                {
                    ws.Cell(1, 1).Value = "域名"; ws.Cell(1, 2).Value = "会话数"; ws.Cell(1, 3).Value = "独立客户数"; ws.Cell(1, 4).Value = "百分比(%)";
                    ws.Range(1, 1, 1, 4).Style.Font.Bold = true;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var x = list[i];
                        ws.Cell(i + 2, 1).Value = x.Domain;
                        ws.Cell(i + 2, 2).Value = x.ConversationCount;
                        ws.Cell(i + 2, 3).Value = x.UniqueCustomerCount;
                        ws.Cell(i + 2, 4).Value = Math.Round(x.Percentage, 2);
                    }
                }
            }
            ws.Columns().AdjustToContents();
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public byte[] ExportToPdf(CustomReportExecutionResultDto result)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Calibri));

                page.Header().Element(c =>
                {
                    c.Column(col =>
                    {
                        col.Item().Text(result.ReportMeta.Name).FontSize(18).Bold();
                        col.Item().Text($"统计周期: {result.ReportMeta.StartDate:yyyy-MM-dd} ~ {result.ReportMeta.EndDate:yyyy-MM-dd}").FontSize(10).AlignRight();
                    });
                });

                page.Content().Element(c =>
                {
                    c.Column(col =>
                    {
                        col.Spacing(15);

                        foreach (var section in result.Sections)
                        {
                            col.Item().Element(ce => BuildSectionPdf(ce, section));
                        }
                    });
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("第 ");
                    x.CurrentPageNumber();
                    x.Span(" / ");
                    x.TotalPages();
                });
            });
        }).GeneratePdf();
    }

    private static void BuildSectionPdf(IContainer container, SectionResultDto section)
    {
        container.Column(col =>
        {
            col.Item().Text(SectionDisplayName(section.Key)).FontSize(14).Bold().Underline();

            if (section.Data == null)
            {
                col.Item().Text("(无数据)").Italic();
                return;
            }

            if (section.Key == "summary")
            {
                var summary = section.Data as ReportSummaryDto ?? JsonSerializer.Deserialize<ReportSummaryDto>(JsonSerializer.Serialize(section.Data));
                if (summary != null)
                {
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(cd => { cd.RelativeColumn(); cd.RelativeColumn(); });
                        t.Cell().Border(1).Padding(3).Text("指标").Bold();
                        t.Cell().Border(1).Padding(3).Text("数值").Bold();
                        t.Cell().Border(1).Padding(3).Text("新建会话数");
                        t.Cell().Border(1).Padding(3).Text(summary.NewConversations.ToString());
                        t.Cell().Border(1).Padding(3).Text("关闭会话数");
                        t.Cell().Border(1).Padding(3).Text(summary.ClosedConversations.ToString());
                        t.Cell().Border(1).Padding(3).Text("客服回复数");
                        t.Cell().Border(1).Padding(3).Text(summary.TotalReplies.ToString());
                        t.Cell().Border(1).Padding(3).Text("新增客户数");
                        t.Cell().Border(1).Padding(3).Text(summary.NewCustomers.ToString());
                        t.Cell().Border(1).Padding(3).Text("平均首次响应(小时)");
                        t.Cell().Border(1).Padding(3).Text(summary.AverageFirstResponseHours.ToString("F2"));
                        t.Cell().Border(1).Padding(3).Text("平均解决(小时)");
                        t.Cell().Border(1).Padding(3).Text(summary.AverageResolutionHours.ToString("F2"));
                        t.Cell().Border(1).Padding(3).Text("SLA达成率(%)");
                        t.Cell().Border(1).Padding(3).Text(summary.SlaAchievementRate.ToString("F2"));
                    });
                }
            }
            else if (section.Key == "trend")
            {
                var trend = section.Data as List<ReportTrendPointDto> ?? JsonSerializer.Deserialize<List<ReportTrendPointDto>>(JsonSerializer.Serialize(section.Data));
                if (trend != null && trend.Count > 0)
                {
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(cd => { cd.RelativeColumn(); cd.RelativeColumn(); cd.RelativeColumn(); cd.RelativeColumn(); });
                        t.Cell().Border(1).Padding(3).Text("日期").Bold();
                        t.Cell().Border(1).Padding(3).Text("新建").Bold();
                        t.Cell().Border(1).Padding(3).Text("关闭").Bold();
                        t.Cell().Border(1).Padding(3).Text("回复").Bold();
                        foreach (var p in trend)
                        {
                            t.Cell().Border(1).Padding(3).Text(p.Date);
                            t.Cell().Border(1).Padding(3).Text(p.NewCount.ToString());
                            t.Cell().Border(1).Padding(3).Text(p.ClosedCount.ToString());
                            t.Cell().Border(1).Padding(3).Text(p.ReplyCount.ToString());
                        }
                    });
                }
            }
            else if (section.Key == "statusDistribution")
            {
                var list = section.Data as List<ReportStatusDistributionDto> ?? JsonSerializer.Deserialize<List<ReportStatusDistributionDto>>(JsonSerializer.Serialize(section.Data));
                if (list != null && list.Count > 0)
                {
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(cd => { cd.RelativeColumn(); cd.RelativeColumn(); cd.RelativeColumn(); cd.RelativeColumn(); });
                        t.Cell().Border(1).Padding(3).Text("状态").Bold();
                        t.Cell().Border(1).Padding(3).Text("名称").Bold();
                        t.Cell().Border(1).Padding(3).Text("数量").Bold();
                        t.Cell().Border(1).Padding(3).Text("%").Bold();
                        foreach (var x in list)
                        {
                            t.Cell().Border(1).Padding(3).Text(x.Status.ToString());
                            t.Cell().Border(1).Padding(3).Text(x.StatusName);
                            t.Cell().Border(1).Padding(3).Text(x.Count.ToString());
                            t.Cell().Border(1).Padding(3).Text(x.Percentage.ToString("F2"));
                        }
                    });
                }
            }
            else if (section.Key == "userPerformance")
            {
                var list = section.Data as List<ReportUserPerformanceDto> ?? JsonSerializer.Deserialize<List<ReportUserPerformanceDto>>(JsonSerializer.Serialize(section.Data));
                if (list != null && list.Count > 0)
                {
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(cd => { cd.RelativeColumn(1); cd.RelativeColumn(1.5f); cd.RelativeColumn(2); cd.RelativeColumn(1); cd.RelativeColumn(1); cd.RelativeColumn(1); cd.RelativeColumn(1.5f); });
                        t.Cell().Border(1).Padding(2).Text("ID").Bold();
                        t.Cell().Border(1).Padding(2).Text("用户名").Bold();
                        t.Cell().Border(1).Padding(2).Text("邮箱").Bold();
                        t.Cell().Border(1).Padding(2).Text("分配").Bold();
                        t.Cell().Border(1).Padding(2).Text("回复").Bold();
                        t.Cell().Border(1).Padding(2).Text("关闭").Bold();
                        t.Cell().Border(1).Padding(2).Text("首次响应(H)").Bold();
                        foreach (var x in list)
                        {
                            t.Cell().Border(1).Padding(2).Text(x.UserId.ToString());
                            t.Cell().Border(1).Padding(2).Text(x.UserName);
                            t.Cell().Border(1).Padding(2).Text(x.Email);
                            t.Cell().Border(1).Padding(2).Text(x.AssignedCount.ToString());
                            t.Cell().Border(1).Padding(2).Text(x.ReplyCount.ToString());
                            t.Cell().Border(1).Padding(2).Text(x.ClosedCount.ToString());
                            t.Cell().Border(1).Padding(2).Text(x.AverageFirstResponseHours.ToString("F2"));
                        }
                    });
                }
            }
            else if (section.Key == "mailboxLoad")
            {
                var list = section.Data as List<ReportMailboxLoadDto> ?? JsonSerializer.Deserialize<List<ReportMailboxLoadDto>>(JsonSerializer.Serialize(section.Data));
                if (list != null && list.Count > 0)
                {
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(cd => { cd.RelativeColumn(1); cd.RelativeColumn(1.5f); cd.RelativeColumn(2); cd.RelativeColumn(1); cd.RelativeColumn(1); cd.RelativeColumn(1); cd.RelativeColumn(1); });
                        t.Cell().Border(1).Padding(2).Text("ID").Bold();
                        t.Cell().Border(1).Padding(2).Text("名称").Bold();
                        t.Cell().Border(1).Padding(2).Text("邮箱").Bold();
                        t.Cell().Border(1).Padding(2).Text("新建").Bold();
                        t.Cell().Border(1).Padding(2).Text("关闭").Bold();
                        t.Cell().Border(1).Padding(2).Text("活跃").Bold();
                        t.Cell().Border(1).Padding(2).Text("%").Bold();
                        foreach (var x in list)
                        {
                            t.Cell().Border(1).Padding(2).Text(x.MailboxId.ToString());
                            t.Cell().Border(1).Padding(2).Text(x.MailboxName);
                            t.Cell().Border(1).Padding(2).Text(x.Email);
                            t.Cell().Border(1).Padding(2).Text(x.NewCount.ToString());
                            t.Cell().Border(1).Padding(2).Text(x.ClosedCount.ToString());
                            t.Cell().Border(1).Padding(2).Text(x.ActiveCount.ToString());
                            t.Cell().Border(1).Padding(2).Text(x.Percentage.ToString("F2"));
                        }
                    });
                }
            }
            else if (section.Key == "customerDomains")
            {
                var list = section.Data as List<ReportCustomerDomainDto> ?? JsonSerializer.Deserialize<List<ReportCustomerDomainDto>>(JsonSerializer.Serialize(section.Data));
                if (list != null && list.Count > 0)
                {
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.RelativeColumn(1); cd.RelativeColumn(1); cd.RelativeColumn(1); });
                        t.Cell().Border(1).Padding(3).Text("域名").Bold();
                        t.Cell().Border(1).Padding(3).Text("会话数").Bold();
                        t.Cell().Border(1).Padding(3).Text("独立客户").Bold();
                        t.Cell().Border(1).Padding(3).Text("%").Bold();
                        foreach (var x in list)
                        {
                            t.Cell().Border(1).Padding(3).Text(x.Domain);
                            t.Cell().Border(1).Padding(3).Text(x.ConversationCount.ToString());
                            t.Cell().Border(1).Padding(3).Text(x.UniqueCustomerCount.ToString());
                            t.Cell().Border(1).Padding(3).Text(x.Percentage.ToString("F2"));
                        }
                    });
                }
            }
            else
            {
                col.Item().Text(JsonSerializer.Serialize(section.Data)).FontFamily(Fonts.Consolas).FontSize(9);
            }
        });
    }

    private static string SectionDisplayName(string key)
    {
        return key switch
        {
            "summary" => "核心指标",
            "trend" => "趋势数据",
            "statusDistribution" => "会话状态分布",
            "userPerformance" => "客服绩效",
            "mailboxLoad" => "邮箱负载",
            "customerDomains" => "客户域名Top",
            _ => key
        };
    }

    private static string CsvEscape(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;

        // BUG-03 修复：CSV 公式注入防护 — 以 = + - @ \t \r 开头的单元格前缀单引号，
        // 防止 Excel/Sheets 将恶意值（如 =HYPERLINK(...)、+cmd|...）当作公式执行
        var firstChar = s[0];
        if (firstChar == '=' || firstChar == '+' || firstChar == '-'
            || firstChar == '@' || firstChar == '\t' || firstChar == '\r')
        {
            s = "'" + s;
        }

        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
        {
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        return s;
    }

    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "report";
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "report" : sanitized;
    }
}
