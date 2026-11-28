using Microsoft.EntityFrameworkCore;
using MeetingManager.Core;
using MeetingManager.Data;

namespace MeetingManager.Api;

/// <summary>Events (interviews, hand-overs, visits...) and the time-conflict check shared with meetings.
/// Meetings only store a start time, so a meeting is assumed to take 60 minutes; an event without a start time is all-day and never conflicts.</summary>
public static class EventsApi
{
    const int MeetingMinutes = 60, DefaultEventMinutes = 60;

    static int? Minutes(string? t)
    {
        if (string.IsNullOrEmpty(t)) return null;
        var p = t.Split(':');
        return p.Length == 2 && int.TryParse(p[0], out var h) && int.TryParse(p[1], out var m) ? h * 60 + m : null;
    }

    static (int, int)? Span(string? start, string? end, int fallback)
    {
        var a = Minutes(start);
        if (a == null) return null;
        var b = Minutes(end);
        if (b == null || b <= a) b = a + fallback;
        return (a.Value, b.Value);
    }

    public static object View(Event e) => new
    {
        id = e.Id, title = e.Title, kind = e.Kind, date = e.EventDate, start_time = e.StartTime, end_time = e.EndTime, place = e.Place, description = e.Description,
        project_id = e.ProjectId, project = e.Project?.Name ?? "", attendee_ids = e.Attendees.Select(p => p.Id).ToList(), attendees = e.Attendees.Select(p => p.Name).ToList(),
    };

    public static List<object> Conflicts(AppDb db, string date, string start, string end, IEnumerable<int> personIds, int? excludeEvent = null, int? excludeMeeting = null)
    {
        var mine = Span(start, end, DefaultEventMinutes);
        var wanted = personIds.ToHashSet();
        var outList = new List<object>();
        if (mine == null || wanted.Count == 0) return outList;
        foreach (var m in db.Meetings.Include(m => m.Attendees).Where(m => m.MeetingDate == date).ToList())
        {
            if (m.Id == excludeMeeting || m.DeletedAt != null) continue;
            var other = Span(m.Time, "", MeetingMinutes);
            var shared = m.Attendees.Where(p => wanted.Contains(p.Id)).Select(p => p.Name).ToList();
            if (other != null && shared.Count > 0 && mine.Value.Item1 < other.Value.Item2 && other.Value.Item1 < mine.Value.Item2)
                outList.Add(new { type = "meeting", id = m.Id, title = m.Title, time = m.Time, people = shared });
        }
        foreach (var e in db.Events.Include(e => e.Attendees).Where(e => e.EventDate == date).ToList())
        {
            if (e.Id == excludeEvent) continue;
            var other = Span(e.StartTime, e.EndTime, DefaultEventMinutes);
            var shared = e.Attendees.Where(p => wanted.Contains(p.Id)).Select(p => p.Name).ToList();
            if (other != null && shared.Count > 0 && mine.Value.Item1 < other.Value.Item2 && other.Value.Item1 < mine.Value.Item2)
                outList.Add(new { type = "event", id = e.Id, title = e.Title, time = e.StartTime, people = shared });
        }
        return outList;
    }

    public static List<object> Upcoming(AppDb db, User u, string today)
    {
        if (!Security.Has(db, u, "meetings.view")) return new();
        return db.Events.Include(e => e.Attendees).Include(e => e.Project).ToList()
            .Where(e => string.CompareOrdinal(e.EventDate, today) >= 0)
            .OrderBy(e => e.EventDate, StringComparer.Ordinal).ThenBy(e => e.StartTime, StringComparer.Ordinal).ThenBy(e => e.Id).Take(8)
            .Select(View).ToList();
    }

    static Event Load(AppDb db, int id) => db.Events.Include(e => e.Attendees).Include(e => e.Project).First(e => e.Id == id);

    static void Fill(AppDb db, Event e, EventIn x)
    {
        Http.ProjectOrThrow(db, x.ProjectId);
        e.Title = x.Title.Trim(); e.Kind = x.Kind; e.EventDate = x.EventDate; e.StartTime = x.StartTime; e.EndTime = x.EndTime;
        e.Place = x.Place.Trim(); e.Description = x.Description.Trim(); e.ProjectId = x.ProjectId;
        e.Attendees = Http.People(db, x.AttendeeIds);
    }

    static object WithConflicts(AppDb db, Event e)
    {
        var v = System.Text.Json.JsonSerializer.SerializeToElement(View(e));
        var d = new Dictionary<string, object?>();
        foreach (var p in v.EnumerateObject()) d[p.Name] = p.Value;
        d["conflicts"] = Conflicts(db, e.EventDate, e.StartTime, e.EndTime, e.Attendees.Select(p => p.Id), excludeEvent: e.Id);
        return d;
    }

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/events", (HttpContext c, AppDb db) =>
        {
            Security.Require(c, db, "meetings.view");
            return Http.Ok(db.Events.Include(e => e.Attendees).Include(e => e.Project).ToList()
                .OrderBy(e => e.EventDate, StringComparer.Ordinal).ThenBy(e => e.StartTime, StringComparer.Ordinal).ThenBy(e => e.Id).Select(View).ToList());
        });

        app.MapPost("/api/events/check", async (HttpContext c, AppDb db) =>
        {
            Security.Require(c, db, "meetings.view");
            var x = (await Http.Body<EventIn>(c)).Validate();
            return Http.Ok(new { conflicts = Conflicts(db, x.EventDate, x.StartTime, x.EndTime, x.AttendeeIds, excludeEvent: x.Id) });
        });

        app.MapPost("/api/events", async (HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "meetings.manage");
            var x = (await Http.Body<EventIn>(c)).Validate();
            var e = new Event { CreatedBy = u.Id };
            Fill(db, e, x);
            db.Events.Add(e); db.SaveChanges();
            Http.Audit(db, u, "event", e.Id, "create");
            return Http.Ok(WithConflicts(db, Load(db, e.Id)));
        });

        app.MapPut("/api/events/{eid:int}", async (int eid, HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "meetings.manage");
            var x = (await Http.Body<EventIn>(c)).Validate();
            var e = db.Events.Include(e => e.Attendees).FirstOrDefault(e => e.Id == eid) ?? throw new ApiException(404, "رویداد پیدا نشد.");
            Fill(db, e, x);
            db.SaveChanges();
            Http.Audit(db, u, "event", eid, "update");
            return Http.Ok(WithConflicts(db, Load(db, eid)));
        });

        app.MapDelete("/api/events/{eid:int}", (int eid, HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "meetings.manage");
            var e = db.Events.Include(e => e.Attendees).FirstOrDefault(e => e.Id == eid) ?? throw new ApiException(404, "رویداد پیدا نشد.");
            e.Attendees.Clear(); db.SaveChanges();
            db.Events.Remove(e); db.SaveChanges();
            Http.Audit(db, u, "event", eid, "delete");
            return Http.Done();
        });
    }
}
