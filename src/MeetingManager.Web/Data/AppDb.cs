using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MeetingManager.Data;

public class AppDb : DbContext
{
    public AppDb(DbContextOptions<AppDb> o) : base(o) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserSession> Sessions => Set<UserSession>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<PersonCategory> Categories => Set<PersonCategory>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Meeting> Meetings => Set<Meeting>();
    public DbSet<PreviousItem> PreviousItems => Set<PreviousItem>();
    public DbSet<ActionItem> Actions => Set<ActionItem>();
    public DbSet<ProgressHistory> History => Set<ProgressHistory>();
    public DbSet<ActionNote> ActionNotes => Set<ActionNote>();
    public DbSet<ChecklistItem> Checklist => Set<ChecklistItem>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<AuditLog> Audit => Set<AuditLog>();
    public DbSet<NoteItem> NoteItems => Set<NoteItem>();
    public DbSet<Sprint> Sprints => Set<Sprint>();
    public DbSet<Tombstone> Tombstones => Set<Tombstone>();

    public static string Snake(string s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsUpper(c)) { if (i > 0) sb.Append('_'); sb.Append(char.ToLowerInvariant(c)); }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().ToTable("users");
        b.Entity<UserSession>().ToTable("user_sessions");
        b.Entity<Permission>().ToTable("permissions");
        b.Entity<RolePermission>().ToTable("role_permissions");
        b.Entity<PersonCategory>().ToTable("person_categories");
        b.Entity<Team>().ToTable("teams");
        b.Entity<Location>().ToTable("locations");
        b.Entity<Person>().ToTable("people");
        b.Entity<Project>().ToTable("projects");
        b.Entity<Meeting>().ToTable("meetings");
        b.Entity<PreviousItem>().ToTable("previous_items");
        b.Entity<ActionItem>().ToTable("actions");
        b.Entity<ProgressHistory>().ToTable("progress_history");
        b.Entity<ActionNote>().ToTable("action_notes");
        b.Entity<ChecklistItem>().ToTable("checklist_items");
        b.Entity<Comment>().ToTable("comments");
        b.Entity<AuditLog>().ToTable("audit_logs");
        b.Entity<NoteItem>().ToTable("note_items");
        b.Entity<Sprint>().ToTable("sprints");
        b.Entity<Tombstone>().ToTable("sync_tombstones");

        b.Entity<UserSession>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        b.Entity<RolePermission>().HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId);
        b.Entity<Team>().HasOne(x => x.Manager).WithMany().HasForeignKey(x => x.ManagerId);
        b.Entity<Project>().HasOne(x => x.Manager).WithMany().HasForeignKey(x => x.ManagerId);
        b.Entity<Project>().HasMany(x => x.Actions).WithOne(x => x.Project).HasForeignKey(x => x.ProjectId);
        b.Entity<Meeting>().HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId);
        b.Entity<Meeting>().HasMany(x => x.Actions).WithOne(x => x.Meeting).HasForeignKey(x => x.MeetingId);
        b.Entity<Meeting>().HasMany(x => x.PreviousItems).WithOne().HasForeignKey(x => x.MeetingId);
        b.Entity<Meeting>().HasMany(x => x.Comments).WithOne().HasForeignKey(x => x.MeetingId);
        b.Entity<PreviousItem>().HasOne(x => x.OwnerPerson).WithMany().HasForeignKey(x => x.OwnerId);
        var a = b.Entity<ActionItem>();
        a.HasOne(x => x.OwnerPerson).WithMany().HasForeignKey(x => x.OwnerId);
        a.HasOne(x => x.AssigneePerson).WithMany().HasForeignKey(x => x.AssigneeId);
        a.HasMany(x => x.Subtasks).WithOne(x => x.Parent).HasForeignKey(x => x.ParentId);
        a.HasMany(x => x.History).WithOne().HasForeignKey(x => x.ActionId);
        a.HasMany(x => x.Notes).WithOne().HasForeignKey(x => x.ActionId);
        a.HasMany(x => x.Checklists).WithOne().HasForeignKey(x => x.ActionId);
        a.HasMany(x => x.Comments).WithOne().HasForeignKey(x => x.ActionId);
        b.Entity<ActionNote>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        b.Entity<Comment>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);

        Join<Team, Person>(b, "team_members", "team_id", "person_id", t => t.Members);
        Join<Project, Person>(b, "project_members", "project_id", "person_id", p => p.Members);
        Join<Meeting, Person>(b, "meeting_attendees", "meeting_id", "person_id", m => m.Attendees);
        Join<ActionItem, Person>(b, "action_collaborators", "action_id", "person_id", x => x.Collaborators);
        b.Entity<Meeting>().HasMany(m => m.LinkedActions).WithMany(x => x.LinkedMeetings).UsingEntity<Dictionary<string, object>>("meeting_action_links",
            r => r.HasOne<ActionItem>().WithMany().HasForeignKey("action_id"),
            l => l.HasOne<Meeting>().WithMany().HasForeignKey("meeting_id"),
            j => { j.ToTable("meeting_action_links"); j.HasKey("meeting_id", "action_id"); });

        foreach (var et in b.Model.GetEntityTypes())
        {
            if (et.ClrType == typeof(Dictionary<string, object>)) continue;
            foreach (var p in et.GetProperties()) p.SetColumnName(Snake(p.Name));
            if (typeof(ISync).IsAssignableFrom(et.ClrType))
                b.Entity(et.ClrType).HasIndex("Uuid").IsUnique().HasDatabaseName($"ix_{et.GetTableName()}_uuid");
        }
        // Python wrote datetimes as 'YYYY-MM-DD HH:MM:SS.ffffff'; keep that text form on write, accept it on read.
        foreach (var p in b.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
        {
            if (p.ClrType == typeof(DateTime)) p.SetValueConverter(DtConv.NonNull);
            else if (p.ClrType == typeof(DateTime?)) p.SetValueConverter(DtConv.Nullable);
        }
    }

    static void Join<TL, TR>(ModelBuilder b, string table, string lk, string rk, System.Linq.Expressions.Expression<Func<TL, IEnumerable<TR>>> nav)
        where TL : class where TR : class
    {
        b.Entity<TL>().HasMany(nav).WithMany().UsingEntity<Dictionary<string, object>>(table,
            r => r.HasOne<TR>().WithMany().HasForeignKey(rk),
            l => l.HasOne<TL>().WithMany().HasForeignKey(lk),
            j => { j.ToTable(table); j.HasKey(lk, rk); });
    }

    // Sync bookkeeping: uuid + updated_at on every shared record, tombstone for every shared record removed.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        Stamp();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        Stamp();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    /// <summary>Set while a sync import runs: the file's own uuid / updated_at / tombstones must be kept as written.</summary>
    public bool RawSync { get; set; }

    void Stamp()
    {
        var now = Clock.Now();
        foreach (var e in ChangeTracker.Entries().ToList())
        {
            if (e.Entity is not ISync s) continue;
            if (e.State == EntityState.Added)
            {
                if (string.IsNullOrEmpty(s.Uuid)) s.Uuid = Guid.NewGuid().ToString();
                if (!RawSync || s.UpdatedAt == default) s.UpdatedAt = now;
            }
            else if (e.State == EntityState.Modified && !RawSync) s.UpdatedAt = now;
            else if (e.State == EntityState.Deleted && !string.IsNullOrEmpty(s.Uuid))
                Tombstones.Add(new Tombstone { TableName = Model.FindEntityType(e.Entity.GetType())!.GetTableName()!, Uuid = s.Uuid, DeletedAt = now });
        }
    }
}

public static class DtConv
{
    const string Fmt = "yyyy-MM-dd HH:mm:ss.ffffff";
    public static readonly Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, string> NonNull =
        new(v => v.ToString(Fmt, System.Globalization.CultureInfo.InvariantCulture), v => Parse(v));
    public static readonly Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, string?> Nullable =
        new(v => v == null ? null : v.Value.ToString(Fmt, System.Globalization.CultureInfo.InvariantCulture), v => v == null ? null : Parse(v));
    public static DateTime Parse(string v) =>
        DateTime.SpecifyKind(DateTime.Parse(v.Replace('T', ' '), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None), DateTimeKind.Unspecified);
}
