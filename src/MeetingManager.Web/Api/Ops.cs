using Microsoft.EntityFrameworkCore;
using MeetingManager.Core;
using MeetingManager.Data;

namespace MeetingManager.Api;

/// <summary>Shared write operations (apply/create/delete) used by several endpoint groups.</summary>
public static class Ops
{
    public static int ApplyAction(AppDb db, ActionItem a, ActionIn x)
    {
        var old = a.Progress;
        Http.PersonOrThrow(db, x.OwnerId); Http.PersonOrThrow(db, x.AssigneeId); Http.ProjectOrThrow(db, x.ProjectId);
        a.Subject = x.Subject.Trim(); a.Decision = x.Decision.Trim(); a.StartDate = x.StartDate.Trim(); a.Due = x.Due.Trim(); a.Status = x.Status; a.Priority = x.Priority; a.Progress = x.Progress;
        a.OwnerId = x.OwnerId; a.AssigneeId = x.AssigneeId; a.ProjectId = x.ProjectId; a.ParentId = x.ParentId; a.Sprint = x.Sprint.Trim();
        a.Collaborators.Clear(); a.Collaborators.AddRange(Http.People(db, x.CollaboratorIds));
        a.Status = Status.ForProgress(a.Progress, a.Status);
        return old;
    }

    public static ActionItem NewAction(AppDb db, ActionIn x, int? meetingId = null)
    {
        Http.PersonOrThrow(db, x.OwnerId); Http.PersonOrThrow(db, x.AssigneeId); Http.ProjectOrThrow(db, x.ProjectId);
        var a = new ActionItem
        {
            MeetingId = meetingId, Subject = x.Subject.Trim(), Decision = x.Decision.Trim(), StartDate = x.StartDate.Trim(), OwnerId = x.OwnerId, AssigneeId = x.AssigneeId,
            ProjectId = x.ProjectId, ParentId = x.ParentId, Sprint = x.Sprint.Trim(), Due = x.Due.Trim(), Status = Status.ForProgress(x.Progress, x.Status), Priority = x.Priority, Progress = x.Progress,
        };
        a.Collaborators.AddRange(Http.People(db, x.CollaboratorIds));
        db.Actions.Add(a); db.SaveChanges();
        return a;
    }

    /// <summary>Removes an action with everything that hangs off it (subtasks recursively), so every removed row gets its sync tombstone (the database does not cascade).</summary>
    public static void DeleteAction(AppDb db, ActionItem a)
    {
        var subs = db.Actions.Where(x => x.ParentId == a.Id).ToList();
        foreach (var s in subs) DeleteAction(db, s);
        db.History.RemoveRange(db.History.Where(h => h.ActionId == a.Id).ToList());
        db.ActionNotes.RemoveRange(db.ActionNotes.Where(h => h.ActionId == a.Id).ToList());
        db.Checklist.RemoveRange(db.Checklist.Where(h => h.ActionId == a.Id).ToList());
        db.Comments.RemoveRange(db.Comments.Where(h => h.ActionId == a.Id).ToList());
        var self = db.Actions.Include(x => x.Collaborators).Include(x => x.LinkedMeetings).First(x => x.Id == a.Id);
        self.Collaborators.Clear(); self.LinkedMeetings.Clear();
        db.Actions.Remove(self);
    }

    public static void DeleteMeeting(AppDb db, Meeting m)
    {
        foreach (var a in db.Actions.Where(a => a.MeetingId == m.Id).ToList()) a.MeetingId = null;
        db.PreviousItems.RemoveRange(db.PreviousItems.Where(p => p.MeetingId == m.Id).ToList());
        db.Comments.RemoveRange(db.Comments.Where(p => p.MeetingId == m.Id).ToList());
        var self = db.Meetings.Include(x => x.Attendees).Include(x => x.LinkedActions).First(x => x.Id == m.Id);
        self.Attendees.Clear(); self.LinkedActions.Clear();
        db.Meetings.Remove(self);
    }

    public static void SetArchived(ActionItem a, bool on, List<ActionItem> subs)
    {
        DateTime? stamp = on ? Clock.Now() : null;
        a.ArchivedAt = stamp;
        foreach (var s in subs) s.ArchivedAt = stamp;
    }
}
