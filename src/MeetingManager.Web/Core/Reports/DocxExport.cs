using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MeetingManager.Data;

namespace MeetingManager.Core;

/// <summary>Word reports (actions / meetings): right-to-left tables, B Nazanin font, same columns as the Excel report.</summary>
public static class DocxExport
{
    const string Font = "B Nazanin";

    static RunProperties Props(int halfPoints, bool bold)
    {
        var rp = new RunProperties(new RunFonts { Ascii = Font, HighAnsi = Font, ComplexScript = Font }, new FontSize { Val = halfPoints.ToString() }, new FontSizeComplexScript { Val = halfPoints.ToString() });
        if (bold) { rp.Append(new Bold()); rp.Append(new BoldComplexScript()); }
        rp.Append(new RightToLeftText());
        return rp;
    }

    static Paragraph Para(string text, int halfPoints, bool bold, JustificationValues just)
    {
        var p = new Paragraph(new ParagraphProperties(new BiDi(), new Justification { Val = just }));
        p.Append(new Run(Props(halfPoints, bold), new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        return p;
    }

    static Table MakeTable(string[] heads, IEnumerable<string[]> rows)
    {
        var t = new Table();
        var borders = new TableBorders();
        foreach (var b in new BorderType[] { new TopBorder(), new BottomBorder(), new LeftBorder(), new RightBorder(), new InsideHorizontalBorder(), new InsideVerticalBorder() })
        { b.Val = BorderValues.Single; b.Size = 4; borders.Append(b); }
        t.Append(new TableProperties(borders, new BiDiVisual(), new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct }));
        void AddRow(string[] cells, bool bold)
        {
            var tr = new TableRow();
            foreach (var c in cells) tr.Append(new TableCell(Para(c, 20, bold, JustificationValues.Right)));
            t.Append(tr);
        }
        AddRow(heads, true);
        foreach (var r in rows) AddRow(r, false);
        return t;
    }

    static byte[] Build(Action<Body> fill)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());
            fill(main.Document.Body!);
            main.Document.Save();
        }
        return ms.ToArray();
    }

    static string Join(ActionItem a) => string.Join("، ", a.Collaborators.Select(p => p.Name));

    public static byte[] Actions(IList<ActionItem> acts)
    {
        return Build(body =>
        {
            body.Append(Para("گزارش اقدامات", 40, true, JustificationValues.Center));
            var heads = new[] { "ردیف", "موضوع", "پروژه", "مسئول اصلی", "همکاران", "مهلت", "پیشرفت", "وضعیت" };
            var rows = acts.Select((a, i) => new[] { (i + 1).ToString(), a.Subject, a.Project?.Name ?? "", a.OwnerPerson?.Name ?? "", Join(a), a.Due, $"{a.Progress}%", Status.Effective(a) });
            body.Append(MakeTable(heads, rows));
        });
    }

    public static byte[] Meetings(IList<Meeting> meetings)
    {
        return Build(body =>
        {
            body.Append(Para("گزارش جلسات و اقدامات", 40, true, JustificationValues.Center));
            foreach (var m in meetings)
            {
                body.Append(Para(m.Title, 32, true, JustificationValues.Right));
                foreach (var (label, val) in new[] { ("تاریخ", m.MeetingDate), ("ساعت", m.Time), ("محل", m.Place), ("پروژه", m.Project?.Name ?? ""), ("خلاصه جلسه", m.Summary) })
                    body.Append(Para($"{label}: {val}", 22, false, JustificationValues.Right));
                var acts = Api.Views.MeetingActions(m).OrderBy(a => a.Sprint == "" ? "~" : a.Sprint, StringComparer.Ordinal).ThenBy(a => a.Id).ToList();
                var heads = new[] { "ردیف", "موضوع", "تصمیم", "اسپرینت", "مسئول اصلی", "همکاران", "مهلت", "وضعیت / پیشرفت" };
                var rows = acts.Select((a, i) => new[] { (i + 1).ToString(), a.Subject, a.Decision, a.Sprint == "" ? "—" : a.Sprint, a.OwnerPerson?.Name ?? "", Join(a), a.Due, $"{Status.Effective(a)} / {a.Progress}%" });
                body.Append(MakeTable(heads, rows));
                body.Append(new Paragraph());
            }
        });
    }
}
