using Microsoft.EntityFrameworkCore;
using MeetingManager.Core;
using MeetingManager.Data;

namespace MeetingManager.Api;

public static class MeetingsApi
{
    static object Reload(AppDb db, int id)
    {
        db.ChangeTracker.Clear();
        return Views.MeetingDict(Views.LoadMeetings(db, db.Meetings.Where(m => m.Id == id)).First());
    }

    static void AddPrev(AppDb db, Meeting m, PreviousIn p)
    {
        var o = Http.PersonOrThrow(db, p.OwnerId);
        db.PreviousItems.Add(new PreviousItem { MeetingId = m.Id, Activity = p.Activity.Trim(), OwnerId = p.OwnerId, OwnerNameSnapshot = Views.PName(o), Status = p.Status, Progress = p.Progress, Note = p.Note.Trim() });
    }

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/meetings/available-actions", (HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "meetings.manage");
            var list = Views.ActionQuery(db).Where(a => a.Status != Status.Done && a.ArchivedAt == null).OrderByDescending(a => a.Id).ToList();
            return Http.Ok(list.Where(a => Security.ActionAllowed(db, u, a)).Select(Views.ActionDict).ToList());
        });

        app.MapPost("/api/meetings", async (HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "meetings.manage");
            var x = (await Http.Body<MeetingIn>(c)).Validate();
            var m = new Meeting { Title = x.Title.Trim(), MeetingDate = x.MeetingDate.Trim(), Time = x.Time.Trim(), Place = x.Place.Trim(), Description = x.Description.Trim(), Summary = x.Summary.Trim(), ProjectId = x.ProjectId, CreatedBy = u.Id };
            m.Attendees = Http.People(db, x.AttendeeIds);
            db.Meetings.Add(m); db.SaveChanges();
            foreach (var p in x.PreviousItems) AddPrev(db, m, p);
            foreach (var a in x.Actions) Ops.NewAction(db, a, m.Id);
            foreach (var aid in x.LinkedActionIds)
            {
                var a = db.Actions.Find(aid);
                if (a != null && !m.LinkedActions.Any(l => l.Id == a.Id)) m.LinkedActions.Add(a);
            }
            db.SaveChanges();
            Http.Audit(db, u, "meeting", m.Id, "create");
            return Http.Ok(Reload(db, m.Id));
        });

        app.MapPut("/api/meetings/{mid:int}", async (int mid, HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "meetings.manage");
            var x = (await Http.Body<MeetingIn>(c)).Validate();
            var m = db.Meetings.Include(z => z.Attendees).Include(z => z.LinkedActions).Include(z => z.PreviousItems).FirstOrDefault(z => z.Id == mid) ?? throw new ApiException(404, "جلسه پیدا نشد.");
            m.Title = x.Title.Trim(); m.MeetingDate = x.MeetingDate.Trim(); m.Time = x.Time.Trim(); m.Place = x.Place.Trim(); m.Description = x.Description.Trim(); m.Summary = x.Summary.Trim(); m.ProjectId = x.ProjectId;
            m.Attendees.Clear(); m.Attendees.AddRange(Http.People(db, x.AttendeeIds));
            db.PreviousItems.RemoveRange(m.PreviousItems.ToList());
            foreach (var p in x.PreviousItems) AddPrev(db, m, p);
            var existing = db.Actions.Include(a => a.Collaborators).Where(a => a.MeetingId == mid).ToDictionary(a => a.Id);
            var incoming = x.Actions.Where(a => a.Id != null && a.Id != 0).Select(a => a.Id!.Value).ToHashSet();
            foreach (var (aid, a) in existing) if (!incoming.Contains(aid)) a.MeetingId = null;
            foreach (var item in x.Actions)
            {
                if (item.Id is int id && id != 0 && existing.TryGetValue(id, out var a))
                {
                    var old = Ops.ApplyAction(db, a, item);
                    if (old != a.Progress)
                        db.History.Add(new ProgressHistory { ActionId = a.Id, UpdateDate = m.MeetingDate, OldProgress = old, NewProgress = a.Progress, Note = "تغییر هنگام ویرایش جلسه", CreatedBy = u.Id });
                }
                else if (item.Id is int id2 && id2 != 0)
                {
                    var a2 = db.Actions.Find(id2);
                    if (a2 != null && !m.LinkedActions.Any(l => l.Id == a2.Id)) m.LinkedActions.Add(a2);
                }
                else Ops.NewAction(db, item, m.Id);
            }
            var keep = x.LinkedActionIds.ToHashSet();
            foreach (var l in m.LinkedActions.Where(l => !keep.Contains(l.Id) && !incoming.Contains(l.Id)).ToList()) m.LinkedActions.Remove(l);
            foreach (var aid in x.LinkedActionIds)
            {
                var a = db.Actions.Find(aid);
                if (a != null && !m.LinkedActions.Any(l => l.Id == a.Id)) m.LinkedActions.Add(a);
            }
            db.SaveChanges();
            Http.Audit(db, u, "meeting", mid, "update");
            return Http.Ok(Reload(db, mid));
        });

        app.MapDelete("/api/meetings/{mid:int}", (int mid, HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "meetings.manage");
            var m = db.Meetings.Find(mid) ?? throw new ApiException(404, "جلسه پیدا نشد.");
            Ops.DeleteMeeting(db, m);
            db.SaveChanges();
            Http.Audit(db, u, "meeting", mid, "delete");
            return Http.Done();
        });

        app.MapPost("/api/meetings/{mid:int}/comments", async (int mid, HttpContext c, AppDb db) =>
        {
            var u = Security.Current(c, db);
            var x = (await Http.Body<CommentIn>(c)).Validate();
            if (db.Meetings.Find(mid) == null) throw new ApiException(404, "جلسه پیدا نشد.");
            var k = new Comment { MeetingId = mid, UserId = u.Id, Body = x.Body.Trim() };
            db.Comments.Add(k); db.SaveChanges();
            return Http.Ok(new { id = k.Id });
        });
    }
}
