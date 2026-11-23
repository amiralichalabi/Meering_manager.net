using System.Globalization;
using ClosedXML.Excel;
using MeetingManager.Data;

namespace MeetingManager.Core;

/// <summary>One parsed row of the Items sheet (keys match the golden tasks.json: row, project, subject, who, status, sprint,
/// carried, ado_id, done_date, type, parent, priority, notes).</summary>
public sealed class TaskRow
{
    public int Row { get; set; }
    public string Project { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Who { get; set; } = "";
    public string Status { get; set; } = "";
    public string Sprint { get; set; } = "";
    public string Carried { get; set; } = "";
    public string AdoId { get; set; } = "";
    public string DoneDate { get; set; } = "";
    public string Type { get; set; } = "";
    public string Parent { get; set; } = "";
    public int? Priority { get; set; }
    public string Notes { get; set; } = "";
}

public sealed class ImportResult
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public List<string> Problems { get; set; } = new();
}

/// <summary>Import/export of the Azure DevOps task sheet (Guide / Items / hidden Lists).
/// Only the Items sheet matters. One row = one action; the same layout is written on export so a file round-trips.</summary>
public static class TasksExcel
{
    public static readonly string[] Headers =
    {
        "Project *", "Description *", "Assigned To *", "Status *", "Sprint *", "Carried Over From",
        "Existing ADO ID", "Done Date", "Type", "Parent", "Priority", "Notes",
    };
    const int Required = 5;
    static readonly Dictionary<string, string> StatusIn = new() { ["new"] = "جدید", ["in progress"] = "در حال انجام", ["done"] = "انجام شده" };
    static readonly Dictionary<string, string> StatusOut = new() { ["جدید"] = "New", ["در حال انجام"] = "In Progress", ["انجام شده"] = "Done", ["عقب‌افتاده"] = "In Progress" };
    public static readonly string[] Types = { "Epic", "Feature", "User Story", "Bug" };
    static readonly Dictionary<int, string> PriorityIn = new() { [1] = "بحرانی", [2] = "زیاد", [3] = "متوسط", [4] = "کم" };
    static readonly Dictionary<string, int> PriorityOut = PriorityIn.ToDictionary(kv => kv.Value, kv => kv.Key);

    // ---- reading ---------------------------------------------------------------------------------------------
    static string NumText(double d)
    {
        if (double.IsFinite(d) && d == Math.Floor(d)) return d.ToString("F0", CultureInfo.InvariantCulture);
        return d.ToString("R", CultureInfo.InvariantCulture);
    }

    static string CellText(IXLCell cell)
    {
        var v = cell.Value;
        if (v.IsBlank) return "";
        if (v.IsDateTime) return Jalali.FromDateTime(v.GetDateTime());
        if (v.IsNumber)
        {
            if (cell.DataType == XLDataType.DateTime)
            {
                try { return Jalali.FromDateTime(DateTime.FromOADate(v.GetNumber())); } catch (Exception) { }
            }
            return NumText(v.GetNumber());
        }
        if (v.IsBoolean) return v.GetBoolean() ? "True" : "False";
        if (v.IsText) return v.GetText().Trim();
        return (cell.GetString() ?? "").Trim();
    }

    /// <summary>Rows plus Persian problem messages with row numbers.</summary>
    public static (List<TaskRow> Rows, List<string> Problems) Parse(byte[] data)
    {
        var rows = new List<TaskRow>();
        var problems = new List<string>();
        using var wb = new XLWorkbook(new MemoryStream(data));
        IXLWorksheet ws;
        if (!wb.Worksheets.TryGetWorksheet("Items", out ws)) ws = wb.Worksheet(1);
        var last = ws.LastCellUsed();
        if (last == null) return (rows, problems);
        int maxRow = last.Address.RowNumber, maxCol = last.Address.ColumnNumber;

        var head = new List<string>();
        for (int c = 1; c <= maxCol; c++) head.Add(CellText(ws.Cell(1, c)).ToLowerInvariant().Replace("*", "").Trim());
        var col = new Dictionary<int, int>();
        for (int i = 0; i < Headers.Length; i++)
        {
            var h = Headers[i].ToLowerInvariant().Replace("*", "").Trim();
            int ix = head.IndexOf(h);
            if (ix >= 0) col[i] = ix;
        }

        for (int n = 2; n <= maxRow; n++)
        {
            var vals = new string[Headers.Length];
            for (int i = 0; i < Headers.Length; i++)
                vals[i] = col.TryGetValue(i, out var ci) && ci < maxCol ? CellText(ws.Cell(n, ci + 1)) : "";
            if (vals.All(x => x == "")) continue;
            string project = vals[0], desc = vals[1], who = vals[2], status = vals[3], sprint = vals[4], carried = vals[5], ado = vals[6],
                   done = vals[7], typ = vals[8], parent = vals[9], prio = vals[10], notes = vals[11];
            if (desc == "")
            {
                problems.Add($"ردیف {n}: Description خالی است؛ رد شد.");
                continue;
            }
            StatusIn.TryGetValue(status.ToLowerInvariant(), out var st);
            if (status != "" && st == null) problems.Add($"ردیف {n}: Status «{status}» نامعتبر است؛ New در نظر گرفته شد.");
            if (status == "") problems.Add($"ردیف {n}: Status خالی است؛ New در نظر گرفته شد.");
            foreach (var (name, v) in new[] { ("Project", project), ("Assigned To", who), ("Sprint", sprint) })
                if (v == "") problems.Add($"ردیف {n}: {name} خالی است.");
            int? p = null;
            if (prio != "")
            {
                if (double.TryParse(Jalali.AsciiDigits(prio), NumberStyles.Float, CultureInfo.InvariantCulture, out var dv)
                    && double.IsFinite(dv) && Math.Abs(dv) < 1000 && PriorityIn.ContainsKey((int)Math.Truncate(dv)))
                    p = (int)Math.Truncate(dv);
                else
                    problems.Add($"ردیف {n}: Priority «{prio}» نامعتبر است؛ نادیده گرفته شد.");
            }
            if (typ != "" && !Types.Contains(typ)) problems.Add($"ردیف {n}: Type «{typ}» نامعتبر است.");
            rows.Add(new TaskRow
            {
                Row = n, Project = project, Subject = desc, Who = who, Status = st ?? "جدید", Sprint = sprint, Carried = carried,
                AdoId = ado, DoneDate = done, Type = typ, Parent = parent, Priority = p, Notes = notes,
            });
        }
        return (rows, problems);
    }

    // ---- import ----------------------------------------------------------------------------------------------
    static Project? FindProject(AppDb db, string name)
    {
        var low = name.Trim().ToLowerInvariant();
        return db.Projects.Where(x => x.DeletedAt == null).OrderBy(x => x.Id).ToList().FirstOrDefault(x => (x.Name ?? "").Trim().ToLowerInvariant() == low);
    }

    static Person? FindPerson(AppDb db, string name)
    {
        var low = name.Trim().ToLowerInvariant();
        return db.People.Where(x => x.DeletedAt == null).OrderBy(x => x.Id).ToList().FirstOrDefault(x => (x.Name ?? "").Trim().ToLowerInvariant() == low);
    }

    public static ImportResult Import(AppDb db, byte[] data)
    {
        var (rows, problems) = Parse(data);
        int created = 0, updated = 0;
        using var tx = db.Database.BeginTransaction();
        foreach (var r in rows)
        {
            Project? proj = null;
            if (r.Project != "")
            {
                proj = FindProject(db, r.Project);
                if (proj == null) { proj = new Project { Name = r.Project }; db.Projects.Add(proj); db.SaveChanges(); }
            }
            Person? who = null;
            if (r.Who != "")
            {
                who = FindPerson(db, r.Who);
                if (who == null) { who = new Person { Name = r.Who }; db.People.Add(who); db.SaveChanges(); }
            }
            int? projId = proj?.Id;
            var q = db.Actions.Where(x => x.DeletedAt == null);
            ActionItem? a = null;
            if (r.AdoId != "")
            {
                var ado = r.AdoId;
                a = q.Where(x => x.AdoId == ado).OrderBy(x => x.Id).FirstOrDefault();
            }
            if (a == null)
            {
                var subject = r.Subject;
                var sprint = r.Sprint;
                a = q.Where(x => x.Subject == subject && x.ProjectId == projId && x.Sprint == sprint).OrderBy(x => x.Id).FirstOrDefault();
            }
            bool isNew = a == null;
            if (a == null) { a = new ActionItem { Subject = r.Subject }; db.Actions.Add(a); }
            a.Subject = r.Subject;
            a.ProjectId = projId;
            if (who != null) { a.OwnerId = who.Id; a.AssigneeId = who.Id; }
            a.Status = r.Status;
            a.Progress = r.Status == "انجام شده" ? 100 : (!isNew && a.Progress > 0 && a.Progress < 100 ? a.Progress : 0);
            a.Sprint = r.Sprint;
            a.CarriedOverFrom = r.Carried;
            a.AdoId = r.AdoId;
            a.DoneDate = r.DoneDate;
            a.WorkItemType = r.Type;
            a.ParentRef = r.Parent;
            a.AdoNotes = r.Notes;
            a.AdoPriority = r.Priority;
            a.Priority = r.Priority != null && PriorityIn.TryGetValue(r.Priority.Value, out var pn) ? pn : (string.IsNullOrEmpty(a.Priority) ? "متوسط" : a.Priority);
            db.SaveChanges();
            if (isNew) created++; else updated++;
        }
        tx.Commit();
        return new ImportResult { Created = created, Updated = updated, Skipped = problems.Count(p => p.Contains("رد شد")), Problems = problems };
    }

    // ---- export ----------------------------------------------------------------------------------------------
    static string Clean(string? s) => OfficeText.Clean(s, 32767);

    public static byte[] Export(IList<ActionItem> actions, IEnumerable<string> projectNames)
    {
        using var wb = new XLWorkbook();
        var guide = wb.Worksheets.Add("Guide");
        guide.RightToLeft = true;
        guide.Cell(1, 1).Value = "راهنمای پر کردن";
        guide.Cell(1, 1).Style.Font.Bold = true;
        guide.Cell(1, 1).Style.Font.FontSize = 14;
        var help = new[]
        {
            ("کجا پر کنم؟", "فقط شیت Items. هر ردیف = یک کار. ستون‌های قرمز (*) الزامی هستند، ستون‌های آبی اختیاری."),
            ("Status", "New / In Progress / Done"),
            ("Existing ADO ID", "اگر کار در DevOps هست شماره‌اش را بنویسید."),
            ("Priority", "۱ (بحرانی) تا ۴ (کم)"),
        };
        for (int i = 0; i < help.Length; i++)
        {
            var k = guide.Cell(i + 3, 1);
            k.Value = help[i].Item1;
            k.Style.Font.Bold = true;
            guide.Cell(i + 3, 2).Value = help[i].Item2;
        }
        guide.Column(1).Width = 26;
        guide.Column(2).Width = 95;

        var ws = wb.Worksheets.Add("Items");
        ws.RightToLeft = false;
        for (int c = 1; c <= Headers.Length; c++)
        {
            var z = ws.Cell(1, c);
            z.Value = Headers[c - 1];
            z.Style.Font.Bold = true;
            z.Style.Font.FontColor = XLColor.White;
            z.Style.Fill.BackgroundColor = XLColor.FromHtml(c <= Required ? "#C00000" : "#2F5597");
            z.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            z.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            z.Style.Alignment.WrapText = true;
        }
        var names = new HashSet<string>(projectNames.Where(x => x != null));
        int n = 1;
        foreach (var a in actions)
        {
            n++;
            var pname = a.Project?.Name ?? "";
            names.Add(pname);
            var who = a.AssigneePerson ?? a.OwnerPerson;
            int? prio = a.AdoPriority != null && PriorityIn.ContainsKey(a.AdoPriority.Value) ? a.AdoPriority
                      : (PriorityOut.TryGetValue(a.Priority ?? "", out var po) ? po : (int?)null);
            var vals = new object?[]
            {
                pname, a.Subject, who?.Name ?? "", StatusOut.TryGetValue(a.Status ?? "", out var so) ? so : "New", a.Sprint, a.CarriedOverFrom,
                a.AdoId, a.DoneDate, a.WorkItemType, a.ParentRef, prio, a.AdoNotes,
            };
            for (int c = 1; c <= vals.Length; c++)
            {
                var z = ws.Cell(n, c);
                if (c == 7 || c == 8) z.Style.NumberFormat.Format = "@";
                var v = vals[c - 1];
                if (v is string s) { if (s != "") z.Value = Clean(s); }
                else if (v is int iv) z.Value = iv;
                z.Style.Alignment.WrapText = true;
                z.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            }
        }
        double[] widths = { 24, 60, 22, 14, 16, 18, 14, 14, 12, 28, 10, 40 };
        for (int i = 0; i < widths.Length; i++) ws.Column(i + 1).Width = widths[i];
        ws.SheetView.FreezeRows(1);

        var lists = wb.Worksheets.Add("Lists");
        lists.Cell(1, 1).Value = "Projects";
        lists.Cell(1, 2).Value = "Status";
        lists.Cell(1, 3).Value = "Type";
        lists.Cell(1, 4).Value = "Priority";
        var plist = names.Where(x => x != "").OrderBy(x => x, StringComparer.Ordinal).ToList();
        for (int i = 0; i < plist.Count; i++) lists.Cell(i + 2, 1).Value = Clean(plist[i]);
        var sts = new[] { "New", "In Progress", "Done" };
        for (int i = 0; i < sts.Length; i++) lists.Cell(i + 2, 2).Value = sts[i];
        for (int i = 0; i < Types.Length; i++)
        {
            lists.Cell(i + 2, 3).Value = Types[i];
            lists.Cell(i + 2, 4).Value = i + 1;
        }
        lists.Visibility = XLWorksheetVisibility.Hidden;

        int lastRow = Math.Max(n, 200);
        void Validate(string col, IXLRange source)
        {
            var dv = ws.Range($"{col}2:{col}{lastRow}").CreateDataValidation();
            dv.IgnoreBlanks = true;
            dv.List(source, true);
        }
        Validate("D", lists.Range("B2:B4"));
        Validate("I", lists.Range("C2:C5"));
        Validate("K", lists.Range("D2:D5"));
        if (plist.Count > 0) Validate("A", lists.Range(2, 1, plist.Count + 1, 1));

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
