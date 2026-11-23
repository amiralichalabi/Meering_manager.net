using System.Reflection;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MeetingManager.Core;

/// <summary>PDF reports with right-to-left Persian layout (QuestPDF shapes Arabic script itself). Font: Vazirmatn, embedded.</summary>
public static class PdfExport
{
    const string Family = "Vazirmatn";
    static bool _ready;
    static readonly object Lock = new();

    static void Init()
    {
        lock (Lock)
        {
            if (_ready) return;
            QuestPDF.Settings.License = LicenseType.Community;
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in new[] { "font.Vazirmatn-Regular.ttf", "font.Vazirmatn-Bold.ttf" })
            {
                using var s = asm.GetManifestResourceStream(name) ?? throw new InvalidOperationException("فونت وزیرمتن پیدا نشد: " + name);
                FontManager.RegisterFont(s);
            }
            _ready = true;
        }
    }

    static string Fa(string? s) => Jalali.FaDigits(s ?? "");
    static string StatusColor(string st) => st == Status.Done ? "#15803D" : st == ReportModel.InProgress ? "#1D4ED8" : st == Status.Late ? "#B91C1C" : "#475569";

    static void Head(IContainer c, string text) => c.Background("#1E3A5F").Padding(4).AlignRight().Text(text).FontColor(Colors.White).Bold().FontSize(9);
    static IContainer Cell(IContainer c, bool zebra) => c.Background(zebra ? "#F5F7FA" : "#FFFFFF").BorderBottom(0.5f).BorderColor("#D5DCE6").Padding(4);

    static void ActionsTable(ColumnDescriptor col, List<ActionRowData> rows)
    {
        col.Item().Table(t =>
        {
            t.ColumnsDefinition(d =>
            {
                d.ConstantColumn(28); d.RelativeColumn(4); d.RelativeColumn(2); d.RelativeColumn(2); d.RelativeColumn(2); d.ConstantColumn(58); d.ConstantColumn(38); d.ConstantColumn(58);
            });
            t.Header(h =>
            {
                foreach (var x in new[] { "ردیف", "موضوع", "پروژه", "مسئول اصلی", "همکاران", "مهلت", "پیشرفت", "وضعیت" }) h.Cell().Element(c => Head(c, x));
            });
            foreach (var r in rows)
            {
                var z = r.Index % 2 == 0;
                t.Cell().Element(c => Cell(c, z)).AlignRight().Text(Fa(r.Index.ToString())).FontSize(9);
                t.Cell().Element(c => Cell(c, z)).AlignRight().Text(r.Subject).FontSize(9);
                t.Cell().Element(c => Cell(c, z)).AlignRight().Text(r.Project).FontSize(9);
                t.Cell().Element(c => Cell(c, z)).AlignRight().Text(r.Owner).FontSize(9);
                t.Cell().Element(c => Cell(c, z)).AlignRight().Text(r.Collaborators).FontSize(9);
                t.Cell().Element(c => Cell(c, z)).AlignRight().Text(Fa(r.Due)).FontSize(9);
                t.Cell().Element(c => Cell(c, z)).AlignRight().Text(Fa(r.Progress + "%")).FontSize(9);
                t.Cell().Element(c => Cell(c, z)).AlignRight().Text(r.Status).FontSize(9).FontColor(StatusColor(r.Status)).Bold();
            }
        });
    }

    static void Kpi(RowDescriptor row, string label, string value) =>
        row.RelativeItem().Border(0.5f).BorderColor("#D5DCE6").Padding(6).Column(c =>
        {
            c.Item().AlignRight().Text(value).Bold().FontSize(14).FontColor("#1E3A5F");
            c.Item().AlignRight().Text(label).FontSize(8).FontColor("#64748B");
        });

    static byte[] Doc(Action<ColumnDescriptor> body, string footer)
    {
        Init();
        return Document.Create(d => d.Page(p =>
        {
            p.Size(PageSizes.A4.Landscape());
            p.Margin(24);
            p.ContentFromRightToLeft();
            p.DefaultTextStyle(x => x.FontFamily(Family).FontSize(10).FontColor("#1F2937"));
            p.Content().Column(body);
            p.Footer().AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(8).FontColor("#64748B"));
                t.Span(footer + "  ·  ");
                t.CurrentPageNumber();
            });
        })).GeneratePdf();
    }

    public static byte[] Actions(ActionsReportData m) => Doc(col =>
    {
        col.Spacing(8);
        col.Item().AlignRight().Text(m.Title).Bold().FontSize(18).FontColor("#1E3A5F");
        col.Item().AlignRight().Text(Fa($"تاریخ تهیه: {m.PreparedDate}" + (m.PreparedBy != "" ? $"  ·  تهیه‌کننده: {m.PreparedBy}" : ""))).FontSize(9).FontColor("#64748B");
        col.Item().Row(r =>
        {
            r.Spacing(6);
            Kpi(r, "کل اقدامات", Fa(m.Kpis.Total.ToString()));
            Kpi(r, "انجام شده", Fa(m.Kpis.Done.ToString()));
            Kpi(r, "در حال انجام", Fa(m.Kpis.InProgress.ToString()));
            Kpi(r, "عقب‌افتاده", Fa(m.Kpis.Late.ToString()));
            Kpi(r, "میانگین پیشرفت", Fa(m.Kpis.AvgProgress + "%"));
        });
        ActionsTable(col, m.Rows);
    }, "Meeting Manager Pro");

    public static byte[] Meetings(MeetingsReportData m) => Doc(col =>
    {
        col.Spacing(10);
        col.Item().AlignRight().Text(m.Title).Bold().FontSize(18).FontColor("#1E3A5F");
        col.Item().AlignRight().Text(Fa($"تاریخ تهیه: {m.PreparedDate}  ·  تعداد جلسات: {m.MeetingCount}")).FontSize(9).FontColor("#64748B");
        foreach (var s in m.Meetings)
        {
            col.Item().Column(c =>
            {
                c.Spacing(3);
                c.Item().AlignRight().Text(s.Title).Bold().FontSize(13);
                var meta = new List<string> { $"تاریخ: {s.Date}" };
                if (s.Time != "") meta.Add($"ساعت: {s.Time}");
                if (s.Place != "") meta.Add($"محل: {s.Place}");
                if (s.Project != "") meta.Add($"پروژه: {s.Project}");
                c.Item().AlignRight().Text(Fa(string.Join("  ·  ", meta))).FontSize(9).FontColor("#64748B");
                if (s.Attendees.Count > 0) c.Item().AlignRight().Text("حاضران: " + string.Join("، ", s.Attendees)).FontSize(9);
                if (s.Summary != "") c.Item().AlignRight().Text(s.Summary).FontSize(9);
                if (s.Actions.Count > 0) ActionsTable(c, s.Actions);
            });
        }
    }, "Meeting Manager Pro");
}
