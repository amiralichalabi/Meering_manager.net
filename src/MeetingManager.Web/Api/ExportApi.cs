using Microsoft.EntityFrameworkCore;
using MeetingManager.Core;
using MeetingManager.Data;

namespace MeetingManager.Api;

public static class ExportApi
{
    const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    static readonly string[] DateBy = { "due", "start", "created", "done" };

    sealed record ActionFilter(string From, string To, string By, List<string> Sprints);

    static ActionFilter? ParseFilter(HttpRequest r)
    {
        var by = r.Query["date_by"].ToString(); if (by == "") by = "due";
        if (!DateBy.Contains(by)) throw new ApiException(400, "نوع تاریخ فیلتر معتبر نیست.");
        var (f, t) = Range(r);
        var names = r.Query["sprints"].ToString().Split(',').Select(x => x.Trim()).Where(x => x != "").ToList();
        return f != "" || t != "" || names.Count > 0 ? new ActionFilter(f, t, by, names) : null;
    }

    static (string, string) Range(HttpRequest r)
    {
        string f, t;
        try
        {
            f = r.Query["date_from"].ToString().Trim() != "" ? Jalali.Normalize(r.Query["date_from"]) : "";
            t = r.Query["date_to"].ToString().Trim() != "" ? Jalali.Normalize(r.Query["date_to"]) : "";
        }
        catch (FormatException e) { throw new ApiException(400, e.Message); }
        if (f != "" && t != "" && string.CompareOrdinal(t, f) < 0) throw new ApiException(400, "پایان بازه نمی‌تواند قبل از شروع آن باشد.");
        return (f, t);
    }

    static string ActionDate(ActionItem a, string by) => by switch
    {
        "created" => Jalali.FromDateTime(a.CreatedAt),
        "start" => a.StartDate,
        "done" => a.DoneDate,
        _ => a.Due,
    };

    static List<ActionItem> Actions(AppDb db, User u, List<int>? ids, ActionFilter? f)
    {
        var q = Views.ActionQuery(db).OrderByDescending(a => a.Id).AsQueryable();
        q = ids != null ? q.Where(a => ids.Contains(a.Id)) : q.Where(a => a.ArchivedAt == null);
        var list = q.ToList().Where(a => Security.ActionAllowed(db, u, a)).ToList();
        if (f == null) return list;
        var outList = new List<ActionItem>();
        foreach (var a in list)
        {
            if (f.Sprints.Count > 0 && !f.Sprints.Contains(a.Sprint)) continue;
            if (f.From != "" || f.To != "")
            {
                var d = ActionDate(a, f.By);
                if (d == "" || (f.From != "" && string.CompareOrdinal(d, f.From) < 0) || (f.To != "" && string.CompareOrdinal(d, f.To) > 0)) continue;
            }
            outList.Add(a);
        }
        return outList;
    }

    static List<Meeting> Meetings(AppDb db, List<int>? ids, (string, string) rng)
    {
        var q = db.Meetings.OrderByDescending(m => m.Id).AsQueryable();
        if (ids != null) q = q.Where(m => ids.Contains(m.Id));
        var ms = Views.LoadMeetings(db, q);
        var (f, t) = rng;
        return ms.Where(m => (f == "" && t == "") || (m.MeetingDate != "" && (f == "" || string.CompareOrdinal(m.MeetingDate, f) >= 0) && (t == "" || string.CompareOrdinal(m.MeetingDate, t) <= 0))).ToList();
    }

    static IResult Pdf(HttpContext c, byte[] data, string name)
    {
        if (data.Length == 0) throw new ApiException(500, "ساخت PDF ناموفق بود: خروجی خالی است.");
        // inline=1: shown in the browser (print preview) instead of saved as a file
        return c.Request.Query["inline"] == "1" ? Results.File(data, "application/pdf") : Results.File(data, "application/pdf", name);
    }

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/export/actions.xlsx", (HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "actions.export");
            var f = ParseFilter(c.Request);
            return Results.File(XlsxExport.Actions(Actions(db, u, Http.IdList(c.Request.Query["ids"]), f)), Xlsx, "actions.xlsx");
        });
        app.MapGet("/api/export/tasks.xlsx", (HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "actions.export");
            var f = ParseFilter(c.Request);
            var acts = Actions(db, u, Http.IdList(c.Request.Query["ids"]), f); acts.Reverse();
            var names = db.Projects.Where(p => p.DeletedAt == null).Select(p => p.Name).ToList();
            return Results.File(TasksExcel.Export(acts, names), Xlsx, "Full_Tasks.xlsx");
        });
        app.MapPost("/api/import/tasks", async (HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "actions.manage");
            using var ms = new MemoryStream();
            await c.Request.Body.CopyToAsync(ms);
            var data = ms.ToArray();
            if (data.Length == 0) throw new ApiException(400, "فایلی ارسال نشده است.");
            ImportResult res;
            try { res = TasksExcel.Import(db, data); }
            catch (ApiException) { throw; }
            catch (Exception)
            {
                db.ChangeTracker.Clear();
                throw new ApiException(400, "فایل اکسل قابل خواندن نیست؛ قالب شیت Items را بررسی کنید.");
            }
            db.SaveChanges();
            Http.Audit(db, u, "action", null, "import_tasks", $"{res.Created} جدید، {res.Updated} به‌روزرسانی");
            return Http.Ok(new { created = res.Created, updated = res.Updated, skipped = res.Skipped, problems = res.Problems });
        });
        app.MapGet("/api/export/actions.pdf", (HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "actions.export");
            var f = ParseFilter(c.Request);
            var acts = Actions(db, u, Http.IdList(c.Request.Query["ids"]), f);
            return Pdf(c, PdfExport.Actions(ReportModel.Actions(acts, u.FullName)), "actions.pdf");
        });
        app.MapGet("/api/export/actions.docx", (HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "actions.export");
            var f = ParseFilter(c.Request);
            return Results.File(DocxExport.Actions(Actions(db, u, Http.IdList(c.Request.Query["ids"]), f)), Docx, "actions.docx");
        });
        app.MapGet("/api/export/meetings.docx", (HttpContext c, AppDb db) =>
        {
            Security.Require(c, db, "meetings.export");
            var rng = Range(c.Request);
            return Results.File(DocxExport.Meetings(Meetings(db, Http.IdList(c.Request.Query["ids"]), rng)), Docx, "meetings.docx");
        });
        app.MapGet("/api/export/meetings.pdf", (HttpContext c, AppDb db) =>
        {
            Security.Require(c, db, "meetings.export");
            var rng = Range(c.Request);
            return Pdf(c, PdfExport.Meetings(ReportModel.Meetings(Meetings(db, Http.IdList(c.Request.Query["ids"]), rng))), "meetings.pdf");
        });
        app.MapGet("/api/export/meetings.xlsx", (HttpContext c, AppDb db) =>
        {
            Security.Require(c, db, "meetings.export");
            var rng = Range(c.Request);
            return Results.File(XlsxExport.Meetings(Meetings(db, Http.IdList(c.Request.Query["ids"]), rng)), Xlsx, "meetings.xlsx");
        });
    }
}
