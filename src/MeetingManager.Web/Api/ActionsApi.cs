using Microsoft.EntityFrameworkCore;
using MeetingManager.Core;
using MeetingManager.Data;

namespace MeetingManager.Api;

public static class ActionsApi
{
    static ActionItem Load(AppDb db, User u, int aid)
    {
        var a = Views.ActionQuery(db).FirstOrDefault(x => x.Id == aid);
        if (a == null || !Security.ActionAllowed(db, u, a)) throw new ApiException(404, "اقدام پیدا نشد.");
        return a;
    }

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/actions", (HttpContext c, AppDb db) =>
        {
            var u = Security.Current(c, db);
            var q = c.Request.Query;
            var qy = Views.ActionQuery(db);
            var archived = q["archived"].ToString(); if (archived == "") archived = "0";
            if (archived == "0") qy = qy.Where(a => a.ArchivedAt == null);
            else if (archived == "1") qy = qy.Where(a => a.ArchivedAt != null);
            var sprint = q["sprint"].ToString(); if (sprint != "") qy = qy.Where(a => a.Sprint == sprint);
            if (int.TryParse(q["project_id"], out var pid)) qy = qy.Where(a => a.ProjectId == pid);
            var status = q["status"].ToString(); if (status != "") qy = qy.Where(a => a.Status == status);
            var priority = q["priority"].ToString(); if (priority != "") qy = qy.Where(a => a.Priority == priority);
            if (int.TryParse(q["assignee_id"], out var asg)) qy = qy.Where(a => a.AssigneeId == asg);
            var text = q["q"].ToString();
            if (text != "")
            {
                var like = $"%{text}%";
                qy = qy.Where(a => EF.Functions.Like(a.Subject, like) || EF.Functions.Like(a.Decision, like) || (a.OwnerPerson != null && EF.Functions.Like(a.OwnerPerson.Name, like)) || (a.AssigneePerson != null && EF.Functions.Like(a.AssigneePerson.Name, like)));
            }
            var list = qy.OrderByDescending(a => a.Id).ToList().Where(a => Security.ActionAllowed(db, u, a)).Select(Views.ActionDict).ToList();
            return Http.Ok(list);
        });

        app.MapPost("/api/actions", async (HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "actions.manage");
            var x = (await Http.Body<ActionIn>(c)).Validate();
            if (u.Role == "user" && u.PersonId == null) throw new ApiException(403, "برای این کاربر هنوز فرد مرتبط تعیین نشده است.");
            if (u.Role == "user" && u.PersonId != null && x.OwnerId != null && x.OwnerId != u.PersonId && x.AssigneeId != null && x.AssigneeId != u.PersonId && !x.CollaboratorIds.Contains(u.PersonId.Value))
                throw new ApiException(403, "کاربر عادی فقط می‌تواند اقدام مرتبط با خودش ایجاد کند.");
            var a = Ops.NewAction(db, x);
            Http.Audit(db, u, "action", a.Id, "create");
            return Http.Ok(Views.ActionDict(Views.ActionQuery(db).First(z => z.Id == a.Id)));
        });

        app.MapPut("/api/actions/{aid:int}", async (int aid, HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "actions.manage");
            var x = (await Http.Body<ActionIn>(c)).Validate();
            var a = Load(db, u, aid);
            var old = Ops.ApplyAction(db, a, x);
            if (old != a.Progress)
                db.History.Add(new ProgressHistory { ActionId = aid, UpdateDate = DateTime.Now.ToString("yyyy-MM-dd"), OldProgress = old, NewProgress = a.Progress, Note = "تغییر پیشرفت از طریق ویرایش اقدام", CreatedBy = u.Id });
            db.SaveChanges();
            Http.Audit(db, u, "action", aid, "update");
            db.ChangeTracker.Clear();
            return Http.Ok(Views.ActionDict(Views.ActionQuery(db).First(z => z.Id == aid)));
        });

        app.MapPost("/api/actions/{aid:int}/progress", async (int aid, HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "actions.manage");
            var x = (await Http.Body<ProgressIn>(c)).Validate();
            var a = Load(db, u, aid);
            var old = a.Progress;
            if (old == x.NewProgress && x.Note.Trim() == "") throw new ApiException(400, "تغییری برای ثبت وجود ندارد.");
            a.Progress = x.NewProgress; a.Status = Status.AfterProgress(x.NewProgress, a.Status);
            db.History.Add(new ProgressHistory { ActionId = aid, UpdateDate = x.UpdateDate.Trim(), OldProgress = old, NewProgress = x.NewProgress, Note = x.Note.Trim(), CreatedBy = u.Id });
            db.SaveChanges();
            Http.Audit(db, u, "action", aid, "progress", $"{old}->{x.NewProgress}");
            db.ChangeTracker.Clear();
            return Http.Ok(Views.ActionDict(Views.ActionQuery(db).First(z => z.Id == aid)));
        });

        app.MapDelete("/api/actions/{aid:int}", (int aid, HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "actions.manage");
            var a = Load(db, u, aid);
            Ops.DeleteAction(db, a);
            db.SaveChanges();
            Http.Audit(db, u, "action", aid, "delete");
            return Http.Done();
        });

        foreach (var (route, on) in new[] { ("archive", true), ("unarchive", false) })
            app.MapPost("/api/actions/{aid:int}/" + route, (int aid, HttpContext c, AppDb db) =>
            {
                var u = Security.Require(c, db, "actions.manage");
                var a = Load(db, u, aid);
                Ops.SetArchived(a, on, a.Subtasks.ToList());
                db.SaveChanges();
                Http.Audit(db, u, "action", aid, route);
                return Http.Done();
            });

        app.MapPost("/api/actions/{aid:int}/notes", async (int aid, HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "actions.manage");
            var x = (await Http.Body<CommentIn>(c)).Validate();
            Load(db, u, aid);
            var n = new ActionNote { ActionId = aid, UserId = u.Id, Body = x.Body.Trim() };
            db.ActionNotes.Add(n); db.SaveChanges();
            return Http.Ok(new { id = n.Id });
        });
        app.MapDelete("/api/actions/{aid:int}/notes/{nid:int}", (int aid, int nid, HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "actions.manage");
            Load(db, u, aid);
            var n = db.ActionNotes.FirstOrDefault(x => x.Id == nid && x.ActionId == aid) ?? throw new ApiException(404, "یادداشت پیدا نشد.");
            // everyone may remove their own note; admins and managers may remove any
            if (n.UserId != u.Id && u.Role is not ("admin" or "manager")) throw new ApiException(403, "فقط نویسنده‌ی یادداشت یا مدیر می‌تواند آن را حذف کند.");
            db.ActionNotes.Remove(n); db.SaveChanges();
            Http.Audit(db, u, "action", aid, "delete_note");
            return Http.Done();
        });
        app.MapPost("/api/actions/{aid:int}/checklist", async (int aid, HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "actions.manage");
            var x = (await Http.Body<ChecklistIn>(c)).Validate();
            Load(db, u, aid);
            var k = new ChecklistItem { ActionId = aid, Title = x.Title.Trim(), IsDone = x.IsDone };
            db.Checklist.Add(k); db.SaveChanges();
            return Http.Ok(new { id = k.Id });
        });
        app.MapPut("/api/checklist/{cid:int}", async (int cid, HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "actions.manage");
            var x = (await Http.Body<ChecklistIn>(c)).Validate();
            var k = db.Checklist.Find(cid) ?? throw new ApiException(404, "مورد پیدا نشد.");
            Load(db, u, k.ActionId);
            k.Title = x.Title.Trim(); k.IsDone = x.IsDone;
            db.SaveChanges();
            return Http.Done();
        });
        app.MapDelete("/api/checklist/{cid:int}", (int cid, HttpContext c, AppDb db) =>
        {
            Security.Require(c, db, "actions.manage");
            var k = db.Checklist.Find(cid) ?? throw new ApiException(404, "مورد پیدا نشد.");
            db.Checklist.Remove(k); db.SaveChanges();
            return Http.Done();
        });
        app.MapPost("/api/actions/{aid:int}/subtasks", async (int aid, HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "actions.manage");
            var x = (await Http.Body<ActionIn>(c)).Validate();
            var a = Load(db, u, aid);
            x.ParentId = aid; x.ProjectId ??= a.ProjectId;
            var z = Ops.NewAction(db, x);
            return Http.Ok(Views.ActionDict(Views.ActionQuery(db).First(q => q.Id == z.Id)));
        });
        app.MapGet("/api/actions/{aid:int}/history", (int aid, HttpContext c, AppDb db) =>
        {
            var u = Security.Current(c, db);
            Load(db, u, aid);
            return Http.Ok(db.History.Where(h => h.ActionId == aid).OrderByDescending(h => h.Id).ToList().Select(Views.HistoryRow).ToList());
        });
        app.MapPost("/api/actions/{aid:int}/comments", async (int aid, HttpContext c, AppDb db) =>
        {
            var u = Security.Current(c, db);
            var x = (await Http.Body<CommentIn>(c)).Validate();
            Load(db, u, aid);
            var k = new Comment { ActionId = aid, UserId = u.Id, Body = x.Body.Trim() };
            db.Comments.Add(k); db.SaveChanges();
            return Http.Ok(new { id = k.Id });
        });
        app.MapGet("/api/actions/{aid:int}/comments", (int aid, HttpContext c, AppDb db) =>
        {
            var u = Security.Current(c, db);
            Load(db, u, aid);
            var list = db.Comments.Include(k => k.User).Where(k => k.ActionId == aid).OrderByDescending(k => k.Id).ToList();
            return Http.Ok(list.Select(k => new { id = k.Id, body = k.Body, user = k.User?.FullName ?? "", created_at = Views.Iso(k.CreatedAt) }).ToList());
        });
    }
}
