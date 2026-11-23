using MeetingManager.Api;
using MeetingManager.Data;

namespace MeetingManager.Core;

/// <summary>Format-neutral content of the actions and meetings reports (port of backend/report_model.py).
/// Plain data: strings and numbers, ASCII digits, empty string for "missing". Property names map to the golden JSON keys
/// through the snake_case serializer options (PreparedDate -> prepared_date, ...).</summary>
public sealed class ActionRowData
{
    public int Index { get; set; }
    public string Subject { get; set; } = "";
    public string Decision { get; set; } = "";
    public string Project { get; set; } = "";
    public string Meeting { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Assignee { get; set; } = "";
    public string Collaborators { get; set; } = "";
    public string Due { get; set; } = "";
    public string Priority { get; set; } = "";
    public int Progress { get; set; }
    public string Status { get; set; } = "";
}

public sealed class KpisData
{
    public int Total { get; set; }
    public int Done { get; set; }
    public int InProgress { get; set; }
    public int Late { get; set; }
    public int AvgProgress { get; set; }
}

public sealed class ActionsReportData
{
    public string Title { get; set; } = "";
    public string PreparedDate { get; set; } = "";
    public string PreparedBy { get; set; } = "";
    public KpisData Kpis { get; set; } = new();
    public List<ActionRowData> Rows { get; set; } = new();
}

public sealed class MeetingSectionData
{
    public string Title { get; set; } = "";
    public string Date { get; set; } = "";
    public string Time { get; set; } = "";
    public string Place { get; set; } = "";
    public string Project { get; set; } = "";
    public List<string> Attendees { get; set; } = new();
    public string Summary { get; set; } = "";
    public int ActionCount { get; set; }
    public List<ActionRowData> Actions { get; set; } = new();
}

public sealed class MeetingsReportData
{
    public string Title { get; set; } = "";
    public string PreparedDate { get; set; } = "";
    public int MeetingCount { get; set; }
    public List<MeetingSectionData> Meetings { get; set; } = new();
}

public static class ReportModel
{
    public const string InProgress = "در حال انجام";
    public const string ActionsTitle = "گزارش اقدامات و پیگیری";
    public const string MeetingsTitle = "گزارش جلسات و مصوبات";

    static string Name(Person? p) => p?.Name ?? "";

    /// <summary>One action as report data. Status is the effective status (an overdue action is reported as late).</summary>
    public static ActionRowData ActionRow(ActionItem a, int index, string today) => new()
    {
        Index = index,
        Subject = a.Subject ?? "",
        Decision = a.Decision ?? "",
        Project = a.Project?.Name ?? "",
        Meeting = a.Meeting?.Title ?? "",
        Owner = Name(a.OwnerPerson),
        Assignee = Name(a.AssigneePerson),
        Collaborators = string.Join("، ", a.Collaborators.Select(p => p.Name)),
        Due = a.Due ?? "",
        Priority = a.Priority ?? "",
        Progress = a.Progress,
        Status = Status.Effective(a, today),
    };

    public static ActionsReportData Actions(IEnumerable<ActionItem> actions, string? userName = null, string? today = null)
    {
        today ??= Jalali.Today();
        var rows = new List<ActionRowData>();
        int i = 1;
        foreach (var a in actions) rows.Add(ActionRow(a, i++, today));
        // Python's round() is "half to even": an average of 2.5 is reported as 2.
        int avg = rows.Count == 0 ? 0 : (int)Math.Round(rows.Sum(r => (double)r.Progress) / rows.Count, MidpointRounding.ToEven);
        return new ActionsReportData
        {
            Title = ActionsTitle,
            PreparedDate = today,
            PreparedBy = userName ?? "",
            Kpis = new KpisData
            {
                Total = rows.Count,
                Done = rows.Count(r => r.Status == Status.Done),
                InProgress = rows.Count(r => r.Status == InProgress),
                Late = rows.Count(r => r.Status == Status.Late),
                AvgProgress = avg,
            },
            Rows = rows,
        };
    }

    public static MeetingsReportData Meetings(IEnumerable<Meeting> meetings, string? today = null)
    {
        today ??= Jalali.Today();
        var sections = new List<MeetingSectionData>();
        foreach (var m in meetings)
        {
            var acts = Views.MeetingActions(m);
            var rows = new List<ActionRowData>();
            int i = 1;
            foreach (var a in acts) rows.Add(ActionRow(a, i++, today));
            var summary = string.IsNullOrEmpty(m.Summary) ? (m.Description ?? "") : m.Summary;
            sections.Add(new MeetingSectionData
            {
                Title = m.Title ?? "",
                Date = m.MeetingDate ?? "",
                Time = m.Time ?? "",
                Place = m.Place ?? "",
                Project = m.Project?.Name ?? "",
                Attendees = m.Attendees.Select(p => p.Name).ToList(),
                Summary = summary.Trim(),
                ActionCount = acts.Count,
                Actions = rows,
            });
        }
        return new MeetingsReportData { Title = MeetingsTitle, PreparedDate = today, MeetingCount = sections.Count, Meetings = sections };
    }
}

/// <summary>Text cleaning shared by the Office writers: XML-illegal control characters are dropped.</summary>
internal static class OfficeText
{
    public static string Clean(string? s, int maxLen = int.MaxValue)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            if (ch == '\t' || ch == '\n' || ch == '\r') { sb.Append(ch); continue; }
            if (ch < ' ') continue;
            if (ch == '￾' || ch == '￿') continue;
            if (char.IsHighSurrogate(ch))
            {
                if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(ch).Append(s[i + 1]); i++; }
                continue;
            }
            if (char.IsLowSurrogate(ch)) continue;
            sb.Append(ch);
        }
        var r = sb.ToString();
        return r.Length > maxLen ? r.Substring(0, maxLen) : r;
    }
}
