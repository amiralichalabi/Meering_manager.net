using Microsoft.EntityFrameworkCore;
using MeetingManager.Core;
using MeetingManager.Data;

namespace MeetingManager.Api;

public static class ProjectsApi
{
    static Project Get(AppDb db, int pid) => Views.ProjectQuery(db).FirstOrDefault(p => p.Id == pid) ?? throw new ApiException(404, "پروژه پیدا نشد.");

    /// <summary>Sprints from the table plus any sprint name that only exists on actions (e.g. from an imported sheet).</summary>
    public static List<object> SprintList(AppDb db)
    {
        var counts = db.Actions.Where(a => a.Sprint != "" && a.ArchivedAt == null).GroupBy(a => a.Sprint).Select(g => new { Name = g.Key, N = g.Count() }).ToList().ToDictionary(x => x.Name, x => x.N);
        var rows = db.Sprints.OrderBy(s => s.StartDate).ThenBy(s => s.Name).ToList();
        var outList = new List<object>();
        foreach (var x in rows)
        {
            counts.Remove(x.Name, out var n);
            outList.Add(new { id = (int?)x.Id, name = x.Name, start_date = x.StartDate, end_date = x.EndDate, project_id = x.ProjectId, actions = n });
        }
        foreach (var kv in counts.OrderBy(k => k.Key, StringComparer.Ordinal))
            outList.Add(new { id = (int?)null, name = kv.Key, start_date = "", end_date = "", project_id = (int?)null, actions = kv.Value });
        return outList;
    }

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/projects", (HttpContext c, AppDb db) =>
        {
            Security.Current(c, db);
            var archived = c.Request.Query["archived"].ToString(); if (archived == "") archived = "0";
            var q = Views.ProjectQuery(db);
            if (archived == "0") q = q.Where(p => p.ArchivedAt == null);
            else if (archived == "1") q = q.Where(p => p.ArchivedAt != null);
            return Http.Ok(q.OrderBy(p => p.Name).ToList().Select(Views.ProjectView).ToList());
        });
        app.MapPost("/api/projects", async (HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "projects.manage");
            var x = (await Http.Body<ProjectIn>(c)).Validate();
            var p = new Project { Name = x.Name.Trim(), Description = x.Description.Trim(), Status = x.Status, ManagerId = x.ManagerId, StartDate = x.StartDate, EndDate = x.EndDate, Progress = x.Progress, Color = x.Color, Members = Http.People(db, x.MemberIds) };
            db.Projects.Add(p); db.SaveChanges();
            Http.Audit(db, u, "project", p.Id, "create");
            db.ChangeTracker.Clear();
            return Http.Ok(Views.ProjectView(Get(db, p.Id)));
        });
        app.MapPut("/api/projects/{pid:int}", async (int pid, HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "projects.manage");
            var x = (await Http.Body<ProjectIn>(c)).Validate();
            var p = Get(db, pid);
            p.Name = x.Name.Trim(); p.Description = x.Description.Trim(); p.Status = x.Status; p.ManagerId = x.ManagerId; p.StartDate = x.StartDate; p.EndDate = x.EndDate; p.Progress = x.Progress; p.Color = x.Color;
            p.Members.Clear(); p.Members.AddRange(Http.People(db, x.MemberIds));
            db.SaveChanges();
            Http.Audit(db, u, "project", pid, "update");
            db.ChangeTracker.Clear();
            return Http.Ok(Views.ProjectView(Get(db, pid)));
        });
        foreach (var (route, on) in new[] { ("archive", true), ("unarchive", false) })
            app.MapPost("/api/projects/{pid:int}/" + route, (int pid, HttpContext c, AppDb db) =>
            {
                var u = Security.Require(c, db, "projects.manage");
                var p = db.Projects.Find(pid) ?? throw new ApiException(404, "پروژه پیدا نشد.");
                p.ArchivedAt = on ? Clock.Now() : null;
                db.SaveChanges();
                Http.Audit(db, u, "project", pid, route);
                return Http.Done();
            });
        app.MapDelete("/api/projects/{pid:int}", (int pid, HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "projects.manage");
            var p = Get(db, pid);
            // Actions, meetings and sprints keep existing; they just lose their project.
            foreach (var a in db.Actions.Where(a => a.ProjectId == pid).ToList()) a.ProjectId = null;
            foreach (var m in db.Meetings.Where(m => m.ProjectId == pid).ToList()) m.ProjectId = null;
            foreach (var s in db.Sprints.Where(s => s.ProjectId == pid).ToList()) s.ProjectId = null;
            p.Members.Clear();
            db.SaveChanges();
            db.Projects.Remove(p); db.SaveChanges();
            Http.Audit(db, u, "project", pid, "delete");
            return Http.Done();
        });

        app.MapGet("/api/sprints", (HttpContext c, AppDb db) => { Security.Current(c, db); return Http.Ok(SprintList(db)); });
        app.MapPost("/api/sprints", async (HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "actions.manage");
            var x = (await Http.Body<SprintIn>(c)).Validate();
            var name = x.Name.Trim();
            var lower = name.ToLowerInvariant();
            if (db.Sprints.ToList().Any(s => s.Name.ToLowerInvariant() == lower)) throw new ApiException(400, "اسپرینتی با این نام وجود دارد.");
            Http.ProjectOrThrow(db, x.ProjectId);
            var sp = new Sprint { Name = name, StartDate = x.StartDate, EndDate = x.EndDate, ProjectId = x.ProjectId };
            db.Sprints.Add(sp); db.SaveChanges();
            Http.Audit(db, u, "sprint", sp.Id, "create");
            return Http.Ok(SprintList(db).First(r => (int?)r.GetType().GetProperty("id")!.GetValue(r) == sp.Id));
        });
        app.MapPut("/api/sprints/{sid:int}", async (int sid, HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "actions.manage");
            var x = (await Http.Body<SprintIn>(c)).Validate();
            var sp = db.Sprints.Find(sid) ?? throw new ApiException(404, "اسپرینت پیدا نشد.");
            var name = x.Name.Trim();
            var lower = name.ToLowerInvariant();
            if (db.Sprints.ToList().Any(s => s.Id != sid && s.Name.ToLowerInvariant() == lower)) throw new ApiException(400, "اسپرینتی با این نام وجود دارد.");
            Http.ProjectOrThrow(db, x.ProjectId);
            if (name != sp.Name) // actions point at the sprint by name, so they follow a rename
                foreach (var a in db.Actions.Where(a => a.Sprint == sp.Name).ToList()) a.Sprint = name;
            sp.Name = name; sp.StartDate = x.StartDate; sp.EndDate = x.EndDate; sp.ProjectId = x.ProjectId;
            db.SaveChanges();
            Http.Audit(db, u, "sprint", sid, "update");
            return Http.Ok(SprintList(db).First(r => (int?)r.GetType().GetProperty("id")!.GetValue(r) == sid));
        });
        app.MapDelete("/api/sprints/{sid:int}", (int sid, HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "actions.manage");
            var sp = db.Sprints.Find(sid) ?? throw new ApiException(404, "اسپرینت پیدا نشد.");
            if (db.Actions.Any(a => a.Sprint == sp.Name && a.ArchivedAt == null)) throw new ApiException(400, "این اسپرینت هنوز اقدام دارد؛ ابتدا اقدام‌ها را به اسپرینت دیگری ببرید.");
            db.Sprints.Remove(sp); db.SaveChanges();
            Http.Audit(db, u, "sprint", sid, "delete");
            return Http.Done();
        });
    }
}
