using ClosedXML.Excel;
using MeetingManager.Api;
using MeetingManager.Data;

namespace MeetingManager.Core;

/// <summary>Excel exports of actions and meetings.</summary>
public static class XlsxExport
{
    static string FontName => Loc.En ? "Calibri" : "B Nazanin";
    static readonly XLColor HeaderFill = XLColor.FromHtml("#D9EAF7");

    static XLAlignmentHorizontalValues Align => Loc.En ? XLAlignmentHorizontalValues.Left : XLAlignmentHorizontalValues.Right;

    static void Put(IXLCell cell, object? v)
    {
        switch (v)
        {
            case null: return;
            case string s:
                if (s.Length > 0) cell.Value = OfficeText.Clean(s, 32767);
                return;
            case int i: cell.Value = i; return;
            default: cell.Value = OfficeText.Clean(v.ToString(), 32767); return;
        }
    }

    static void Title(IXLWorksheet ws, string text, int cols)
    {
        ws.Range(1, 1, 1, cols).Merge();
        var a1 = ws.Cell(1, 1);
        a1.Value = text;
        a1.Style.Font.FontName = FontName;
        a1.Style.Font.FontSize = 20;
        a1.Style.Font.Bold = true;
        a1.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }

    static void Headers(IXLWorksheet ws, string[] heads, bool verticalCenter)
    {
        for (int c = 0; c < heads.Length; c++)
        {
            var z = ws.Cell(3, c + 1);
            z.Value = heads[c];
            z.Style.Font.FontName = FontName;
            z.Style.Font.Bold = true;
            z.Style.Fill.BackgroundColor = HeaderFill;
            z.Style.Alignment.Horizontal = Align;
            if (verticalCenter) z.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            z.Style.Alignment.WrapText = true;
        }
    }

    static void DataRow(IXLWorksheet ws, int r, object?[] vals)
    {
        for (int c = 0; c < vals.Length; c++)
        {
            var cell = ws.Cell(r, c + 1);
            Put(cell, vals[c]);
            cell.Style.Font.FontName = FontName;
            cell.Style.Font.FontSize = 12;
            cell.Style.Alignment.Horizontal = Align;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            cell.Style.Alignment.WrapText = true;
        }
    }

    static void Finish(IXLWorksheet ws, double[] widths, int lastRow, int cols)
    {
        for (int i = 0; i < widths.Length; i++) ws.Column(i + 1).Width = widths[i];
        ws.SheetView.FreezeRows(3);
        ws.Range(3, 1, Math.Max(3, lastRow), cols).SetAutoFilter();
    }

    static byte[] Save(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static byte[] Actions(IList<ActionItem> acts)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(Loc.T("اقدامات"));
        ws.RightToLeft = !Loc.En;
        var heads = new[] { "ردیف", "موضوع اقدام", "تصمیم", "پروژه", "جلسه", "مسئول اصلی", "همکاران", "انجام‌دهنده", "مهلت", "اولویت", "پیشرفت", "وضعیت", "اسپرینت" };
        Title(ws, Loc.T("گزارش اقدامات"), heads.Length);
        Headers(ws, heads.Select(Loc.T).ToArray(), true);
        int r = 4;
        foreach (var a in acts)
        {
            DataRow(ws, r, new object?[]
            {
                r - 3, a.Subject, a.Decision, a.Project?.Name ?? "", a.Meeting?.Title ?? "", Views.PName(a.OwnerPerson),
                string.Join(Loc.Sep, a.Collaborators.Select(p => p.Name)), Views.PName(a.AssigneePerson), a.Due, Loc.T(a.Priority),
                $"{a.Progress}%", Loc.T(Status.Effective(a)), a.Sprint ?? "",
            });
            r++;
        }
        Finish(ws, new double[] { 7, 34, 34, 22, 24, 18, 26, 18, 15, 12, 12, 18, 16 }, r - 1, heads.Length);
        return Save(wb);
    }

    public static byte[] Meetings(IList<Meeting> meetings)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(Loc.T("جلسات"));
        ws.RightToLeft = !Loc.En;
        var heads = new[] { "ردیف", "جلسه", "تاریخ", "ساعت", "محل", "پروژه", "خلاصه جلسه", "تعداد اقدامات", "اسپرینت‌ها" };
        Title(ws, Loc.T("گزارش جلسات"), heads.Length);
        Headers(ws, heads.Select(Loc.T).ToArray(), false);
        int r = 4;
        foreach (var m in meetings)
        {
            var ma = Views.MeetingActions(m);
            var sprints = string.Join(Loc.Sep, ma.Where(a => !string.IsNullOrEmpty(a.Sprint)).GroupBy(a => a.Sprint)
                .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} ({g.Count()})"));
            DataRow(ws, r, new object?[]
            {
                r - 3, m.Title, m.MeetingDate, m.Time, m.Place, m.Project?.Name ?? "",
                string.IsNullOrEmpty(m.Summary) ? m.Description : m.Summary, ma.Count, sprints,
            });
            r++;
        }
        Finish(ws, new double[] { 7, 30, 15, 12, 25, 22, 55, 15, 30 }, r - 1, heads.Length);
        return Save(wb);
    }
}
