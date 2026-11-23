using Microsoft.EntityFrameworkCore;
using MeetingManager.Core;
using MeetingManager.Data;

namespace MeetingManager.Api;

public static class ReportsApi
{
    public const string Version = "4.2.0";

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/health", () => Http.Ok(new { ok = true, version = Version, db = Paths.DbPath, pdf = new { ok = true, engine = "QuestPDF" } }));

        app.MapGet("/api/bootstrap", (HttpContext c, AppDb db) =>
        {
            var u = Security.Current(c, db);
            var people = db.People.Where(p => p.IsActive).OrderBy(p => p.Name).ToList();
            var projects = Views.ProjectQuery(db).Where(p => p.ArchivedAt == null).OrderBy(p => p.Name).ToList();
            var meetings = Views.LoadMeetings(db, db.Meetings.OrderByDescending(m => m.Id));
            var perms = u.Role == "admin" ? Security.Perms.Keys.ToList()
                : db.RolePermissions.Where(r => r.Role == u.Role && r.Enabled).Select(r => r.Permission!.Code).ToList();
            return Http.Ok(new { user = Views.UserMe(u), permissions = perms, people = people.Select(Views.PersonView).ToList(), projects = projects.Select(Views.ProjectView).ToList(), meetings = meetings.Select(Views.MeetingDict).ToList() });
        });

        app.MapGet("/api/dashboard", (HttpContext c, AppDb db) =>
        {
            var u = Security.Current(c, db);
            var acts = db.Actions.Include(a => a.OwnerPerson).Include(a => a.AssigneePerson).Include(a => a.Collaborators).ToList().Where(a => a.ArchivedAt == null && Security.ActionAllowed(db, u, a)).ToList();
            var today = Jalali.Today();
            var eff = acts.ToDictionary(a => a.Id, a => Status.Effective(a, today));
            var byStatus = new Dictionary<string, int>();
            foreach (var k in Status.All) byStatus[k] = acts.Count(a => eff[a.Id] == k);
            var projects = Views.ProjectQuery(db).Where(p => p.ArchivedAt == null).ToList();
            var pstats = new List<object>();
            foreach (var p in projects)
            {
                var aa = acts.Where(a => a.ProjectId == p.Id).ToList();
                var prog = aa.Count > 0 ? (int)Math.Round(aa.Sum(a => a.Progress) / (double)aa.Count, MidpointRounding.ToEven) : p.Progress;
                pstats.Add(new { id = p.Id, name = p.Name, progress = prog, actions = aa.Count, done = aa.Count(a => a.Progress >= 100), manager = Views.PName(p.Manager), end_date = p.EndDate });
            }
            var avg = acts.Count > 0 ? Math.Round(acts.Sum(a => a.Progress) / (double)acts.Count, 1, MidpointRounding.ToEven) : 0;
            var upcoming = db.Meetings.OrderByDescending(m => m.Id).Take(8).ToList().Select(m => new { id = m.Id, title = m.Title, date = m.MeetingDate, time = m.Time, place = m.Place }).ToList();
            return Http.Ok(new { today, meetings = db.Meetings.Count(), actions = acts.Count, avg_progress = avg, overdue = byStatus[Status.Late], by_status = byStatus, projects = projects.Count, project_stats = pstats, upcoming_meetings = upcoming });
        });

        app.MapGet("/api/calendar", (HttpContext c, AppDb db) =>
        {
            var u = Security.Current(c, db);
            var month = Jalali.AsciiDigits(c.Request.Query["month"].ToString()).Replace('-', '/');
            var events = new List<object>();
            foreach (var m in db.Meetings.Include(m => m.Project).ToList())
                if (month == "" || m.MeetingDate.StartsWith(month, StringComparison.Ordinal))
                    events.Add(new { type = "meeting", id = m.Id, date = m.MeetingDate, title = m.Title, time = m.Time, project = m.Project?.Name ?? "" });
            foreach (var a in db.Actions.Include(a => a.Collaborators).Where(a => a.ArchivedAt == null).ToList())
                if (Security.ActionAllowed(db, u, a) && a.Due != "" && (month == "" || a.Due.StartsWith(month, StringComparison.Ordinal)))
                    events.Add(new { type = "action", id = a.Id, date = a.Due, title = a.Subject, status = a.Status });
            foreach (var p in db.Projects.Where(p => p.ArchivedAt == null).ToList())
                foreach (var (key, label, d) in new[] { ("start_date", "شروع پروژه", p.StartDate), ("end_date", "پایان پروژه", p.EndDate) })
                    if (d != "" && (month == "" || d.StartsWith(month, StringComparison.Ordinal)))
                        events.Add(new { type = "project", id = p.Id, date = d, title = $"{label}: {p.Name}", kind = key });
            return Http.Ok(events);
        });
    }
}
