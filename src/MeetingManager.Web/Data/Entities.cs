namespace MeetingManager.Data;

// Entities mirror the existing SQLite schema (see schema.sql) so databases from earlier releases open unchanged.
// Column names are derived from property names (PascalCase -> snake_case) in AppDb.OnModelCreating.

public interface ISync
{
    string Uuid { get; set; }
    DateTime UpdatedAt { get; set; }
    DateTime? DeletedAt { get; set; }
}

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "user";
    public bool IsActive { get; set; } = true;
    public int? PersonId { get; set; }
    public DateTime CreatedAt { get; set; } = Clock.Now();
    public DateTime? LastLogin { get; set; }
    public string BirthDate { get; set; } = "";
    public string Avatar { get; set; } = "";
}

public class UserSession
{
    public int Id { get; set; }
    public string TokenHash { get; set; } = "";
    public int UserId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = Clock.Now();
    public User? User { get; set; }
}

public class Permission
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Module { get; set; } = "";
}

public class RolePermission
{
    public int Id { get; set; }
    public string Role { get; set; } = "";
    public int PermissionId { get; set; }
    public bool Enabled { get; set; } = true;
    public Permission? Permission { get; set; }
}

public class PersonCategory : ISync
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Uuid { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class Team : ISync
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int? ManagerId { get; set; }
    public bool IsActive { get; set; } = true;
    public string Uuid { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Person? Manager { get; set; }
    public List<Person> Members { get; set; } = new();
}

public class Location : ISync
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public string Uuid { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class Person : ISync
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Department { get; set; } = "";
    public string Role { get; set; } = "";
    public string Category { get; set; } = "";
    public int? CategoryId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = Clock.Now();
    public string Uuid { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class Project : ISync
{
    public int Id { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "فعال";
    public int? ManagerId { get; set; }
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
    public int Progress { get; set; }
    public DateTime CreatedAt { get; set; } = Clock.Now();
    public string Color { get; set; } = "";
    public string Uuid { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Person? Manager { get; set; }
    public List<Person> Members { get; set; } = new();
    public List<ActionItem> Actions { get; set; } = new();
}

public class Meeting : ISync
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string MeetingDate { get; set; } = "";
    public string Time { get; set; } = "";
    public string Place { get; set; } = "";
    public string Description { get; set; } = "";
    public string Summary { get; set; } = "";
    public int? ProjectId { get; set; }
    public DateTime CreatedAt { get; set; } = Clock.Now();
    public int? CreatedBy { get; set; }
    public string Uuid { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Project? Project { get; set; }
    public List<Person> Attendees { get; set; } = new();
    public List<ActionItem> Actions { get; set; } = new();
    public List<ActionItem> LinkedActions { get; set; } = new();
    public List<PreviousItem> PreviousItems { get; set; } = new();
    public List<Comment> Comments { get; set; } = new();
}

public class PreviousItem : ISync
{
    public int Id { get; set; }
    public int MeetingId { get; set; }
    public string Activity { get; set; } = "";
    public int? OwnerId { get; set; }
    public string OwnerNameSnapshot { get; set; } = "";
    public string Status { get; set; } = "";
    public int Progress { get; set; }
    public string Note { get; set; } = "";
    public string Uuid { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Person? OwnerPerson { get; set; }
}

public class ActionItem : ISync
{
    public int Id { get; set; }
    public int? MeetingId { get; set; }
    public int? ProjectId { get; set; }
    public string StartDate { get; set; } = "";
    public int? ParentId { get; set; }
    public string Subject { get; set; } = "";
    public string Decision { get; set; } = "";
    public int? OwnerId { get; set; }
    public int? AssigneeId { get; set; }
    public string Due { get; set; } = "";
    public string Status { get; set; } = "جدید";
    public string Priority { get; set; } = "متوسط";
    public int Progress { get; set; }
    public DateTime CreatedAt { get; set; } = Clock.Now();
    public string Sprint { get; set; } = "";
    public string CarriedOverFrom { get; set; } = "";
    public string AdoId { get; set; } = "";
    public string DoneDate { get; set; } = "";
    public string WorkItemType { get; set; } = "";
    public string ParentRef { get; set; } = "";
    public int? AdoPriority { get; set; }
    public string AdoNotes { get; set; } = "";
    public DateTime? ArchivedAt { get; set; }
    public string Uuid { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Meeting? Meeting { get; set; }
    public Project? Project { get; set; }
    public Person? OwnerPerson { get; set; }
    public Person? AssigneePerson { get; set; }
    public ActionItem? Parent { get; set; }
    public List<ActionItem> Subtasks { get; set; } = new();
    public List<Person> Collaborators { get; set; } = new();
    public List<Meeting> LinkedMeetings { get; set; } = new();
    public List<ProgressHistory> History { get; set; } = new();
    public List<ActionNote> Notes { get; set; } = new();
    public List<ChecklistItem> Checklists { get; set; } = new();
    public List<Comment> Comments { get; set; } = new();
}

public class ProgressHistory : ISync
{
    public int Id { get; set; }
    public int ActionId { get; set; }
    public string UpdateDate { get; set; } = "";
    public int OldProgress { get; set; }
    public int NewProgress { get; set; }
    public string Note { get; set; } = "";
    public int? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = Clock.Now();
    public string Uuid { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class ActionNote : ISync
{
    public int Id { get; set; }
    public int ActionId { get; set; }
    public int? UserId { get; set; }
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; } = Clock.Now();
    public string Uuid { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public User? User { get; set; }
}

public class ChecklistItem : ISync
{
    public int Id { get; set; }
    public int ActionId { get; set; }
    public string Title { get; set; } = "";
    public bool IsDone { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = Clock.Now();
    public string Uuid { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class Comment : ISync
{
    public int Id { get; set; }
    public int? ActionId { get; set; }
    public int? MeetingId { get; set; }
    public int? UserId { get; set; }
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; } = Clock.Now();
    public string Uuid { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public User? User { get; set; }
}

public class AuditLog
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string Entity { get; set; } = "";
    public int? EntityId { get; set; }
    public string Operation { get; set; } = "";
    public string Detail { get; set; } = "";
    public DateTime CreatedAt { get; set; } = Clock.Now();
}

public class NoteItem
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Category { get; set; } = "شخصی";
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    public DateTime CreatedAt { get; set; } = Clock.Now();
}

/// <summary>A scheduled happening that is not a meeting (interview, hand-over, visit...): no minutes, no actions. Part of the sync file.</summary>
public class Event : ISync
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "مصاحبه";
    public string EventDate { get; set; } = "";
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";
    public string Place { get; set; } = "";
    public string Description { get; set; } = "";
    public int? ProjectId { get; set; }
    public DateTime CreatedAt { get; set; } = Clock.Now();
    public int? CreatedBy { get; set; }
    public Project? Project { get; set; }
    public List<Person> Attendees { get; set; } = new();
    public string Uuid { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class Sprint
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
    public int? ProjectId { get; set; }
    public DateTime CreatedAt { get; set; } = Clock.Now();
}

public class Tombstone
{
    public int Id { get; set; }
    public string TableName { get; set; } = "";
    public string Uuid { get; set; } = "";
    public DateTime DeletedAt { get; set; } = Clock.Now();
}

public static class Clock
{
    /// <summary>UTC "now" without a kind, microseconds kept.</summary>
    public static DateTime Now() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
}
