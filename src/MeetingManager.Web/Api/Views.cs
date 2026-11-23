using Microsoft.EntityFrameworkCore;
using MeetingManager.Core;
using MeetingManager.Data;

namespace MeetingManager.Api;

/// <summary>JSON shapes the frontend expects (identical to the dict builders in backend/main.py).</summary>
public static class Views
{
    public static string Iso(DateTime? d)
    {
        if (d == null) return "";
        var v = d.Value;
        return v.Ticks % TimeSpan.TicksPerSecond / 10 == 0 ? v.ToString("yyyy-MM-ddTHH:mm:ss") : v.ToString("yyyy-MM-ddTHH:mm:ss.ffffff");
    }
    public static string? IsoOrNull(DateTime? d) => d == null ? null : Iso(d);
    public static string PName(Person? p) => p?.Name ?? "";

    public static object UserMe(User u) => new { id = u.Id, username = u.Username, full_name = u.FullName, role = u.Role, person_id = u.PersonId, birth_date = u.BirthDate, avatar = u.Avatar, last_login = IsoOrNull(u.LastLogin) };
    public static object UserView(User x) => new { id = x.Id, username = x.Username, full_name = x.FullName, role = x.Role, person_id = x.PersonId, is_active = x.IsActive, last_login = IsoOrNull(x.LastLogin) };
    public static object PersonView(Person p) => new { id = p.Id, name = p.Name, department = p.Department, role = p.Role, category = p.Category, category_id = p.CategoryId };
    public static object LocationView(Location x) => new { id = x.Id, name = x.Name, address = x.Address, description = x.Description, is_active = x.IsActive };
    public static object ProjectView(Project p) => new
    {
        id = p.Id, name = p.Name, description = p.Description, status = p.Status, manager_id = p.ManagerId, manager = PName(p.Manager), start_date = p.StartDate, end_date = p.EndDate,
        progress = p.Progress, color = p.Color, member_ids = p.Members.Select(x => x.Id).ToList(), archived = p.ArchivedAt != null
    };
    public static object HistoryRow(ProgressHistory h) => new { id = h.Id, date = h.UpdateDate, from = h.OldProgress, to = h.NewProgress, note = h.Note };

    public static object ActionDict(ActionItem a) => new
    {
        id = a.Id, meeting_id = a.MeetingId, meeting = a.Meeting?.Title ?? "", linked_meetings = a.LinkedMeetings.Select(m => m.Id).ToList(),
        project_id = a.ProjectId, project = a.Project?.Name ?? "", parent_id = a.ParentId, start_date = a.StartDate, sprint = a.Sprint, archived = a.ArchivedAt != null,
        subject = a.Subject, decision = a.Decision, owner_id = a.OwnerId, owner = PName(a.OwnerPerson), assignee_id = a.AssigneeId, assignee = PName(a.AssigneePerson),
        collaborator_ids = a.Collaborators.Select(p => p.Id).ToList(), collaborators = a.Collaborators.Select(p => p.Name).ToList(), due = a.Due, status = a.Status, priority = a.Priority, progress = a.Progress,
        created_at = Iso(a.CreatedAt), updated_at = Iso(a.UpdatedAt),
        history = a.History.OrderBy(h => h.Id).Select(HistoryRow).ToList(),
        notes = a.Notes.OrderBy(n => n.Id).Select(n => new { id = n.Id, body = n.Body, user = n.User?.FullName ?? "", created_at = Iso(n.CreatedAt) }).ToList(),
        checklist = a.Checklists.OrderBy(c => c.Id).Select(c => new { id = c.Id, title = c.Title, done = c.IsDone }).ToList(),
        subtasks = a.Subtasks.Select(x => new { id = x.Id, subject = x.Subject, progress = x.Progress, status = x.Status, assignee = PName(x.AssigneePerson), due = x.Due }).ToList(),
        project_color = a.Project?.Color ?? ""
    };

    public static object PrevDict(PreviousItem x) => new { id = x.Id, activity = x.Activity, owner_id = x.OwnerId, owner = PName(x.OwnerPerson), status = x.Status, progress = x.Progress, note = x.Note };

    /// <summary>A meeting's own actions followed by the actions linked to it (never listed twice).</summary>
    public static List<ActionItem> MeetingActions(Meeting m)
    {
        var own = m.Actions.Select(a => a.Id).ToHashSet();
        return m.Actions.Concat(m.LinkedActions.Where(a => !own.Contains(a.Id))).ToList();
    }

    public static object MeetingDict(Meeting m) => new
    {
        id = m.Id, title = m.Title, date = m.MeetingDate, time = m.Time, place = m.Place, desc = m.Description, summary = m.Summary ?? "", project_id = m.ProjectId, project = m.Project?.Name ?? "",
        attendee_ids = m.Attendees.Select(p => p.Id).ToList(), attendees = m.Attendees.Select(p => p.Name).ToList(),
        prev = m.PreviousItems.OrderBy(p => p.Id).Select(PrevDict).ToList(), current = MeetingActions(m).Select(ActionDict).ToList()
    };

    // ---- query helpers ---------------------------------------------------------------------------------------
    public static IQueryable<ActionItem> ActionQuery(AppDb db) => db.Actions
        .Include(a => a.OwnerPerson).Include(a => a.AssigneePerson).Include(a => a.Collaborators).Include(a => a.Meeting).Include(a => a.LinkedMeetings)
        .Include(a => a.Project).Include(a => a.History).Include(a => a.Notes).ThenInclude(n => n.User)
        .Include(a => a.Checklists).Include(a => a.Subtasks).ThenInclude(s => s.AssigneePerson).AsSplitQuery();

    public static IQueryable<Project> ProjectQuery(AppDb db) => db.Projects.Include(p => p.Manager).Include(p => p.Members).AsSplitQuery();

    /// <summary>Loads meetings with everything MeetingDict/MeetingActions need (own and linked actions, fully loaded).</summary>
    public static List<Meeting> LoadMeetings(AppDb db, IQueryable<Meeting> q)
    {
        var ms = q.Include(m => m.Project).Include(m => m.Attendees).Include(m => m.PreviousItems).ThenInclude(p => p.OwnerPerson).AsSplitQuery().ToList();
        if (ms.Count == 0) return ms;
        var ids = ms.Select(m => m.Id).ToList();
        // Fix-up attaches these actions to Meeting.Actions / Meeting.LinkedActions of the tracked meetings.
        ActionQuery(db).Where(a => (a.MeetingId != null && ids.Contains(a.MeetingId.Value)) || a.LinkedMeetings.Any(l => ids.Contains(l.Id))).ToList();
        return ms;
    }
}
