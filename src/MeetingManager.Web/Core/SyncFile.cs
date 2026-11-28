using System.Collections;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MeetingManager.Data;

namespace MeetingManager.Core;

/// <summary>A snapshot of all shared data that two devices can exchange and merge.
/// Records are matched by uuid, every link is written as the linked record's uuid, merging is "newest change wins"
/// per record (updated_at in epoch milliseconds, UTC). Deletions travel as tombstones (deleted_at set).</summary>
public class SyncFileError : Exception
{
    public SyncFileError(string message) : base(message) { }
}

public class SyncStats
{
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Deleted { get; set; }
    public int Skipped { get; set; }
    public int Orphaned { get; set; }

    /// <summary>Short text form of the stats, used for the audit log.</summary>
    public override string ToString() =>
        $"{{'inserted': {Inserted}, 'updated': {Updated}, 'deleted': {Deleted}, 'skipped': {Skipped}, 'orphaned': {Orphaned}}}";
}

public static class SyncFile
{
    public const string Format = "meeting-manager-sync";
    public const int Version = 1;

    const string NotSyncFile = "این فایل، فایل هماهنگ‌سازی برنامه نیست.";
    const string BadVersion = "نسخه‌ی فایل هماهنگ‌سازی پشتیبانی نمی‌شود.";
    const string BadRecord = "ساختار فایل هماهنگ‌سازی نامعتبر است.";

    static readonly long EpochTicks = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

    public static readonly JsonSerializerOptions ExportOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    // ------------------------------------------------------------------ table description
    // Field kinds: s string, i int, ni nullable int, b bool, bi bool stored as 0/1 in the file, dt datetime (epoch ms), ndt nullable datetime.
    sealed class Fld
    {
        public string Key = "";
        public PropertyInfo Prop = null!;
        public char K1;      // first letter of the kind is enough to switch on, 'x' = two-letter kinds below
        public string Kind = "";
    }

    sealed class Ref
    {
        public string Key = "";
        public PropertyInfo Fk = null!;
        public string Target = "";
        public bool Required;
    }

    sealed class SetDef
    {
        public string Key = "";
        public string NavName = "";
        public PropertyInfo Nav = null!;
        public string Target = "";
    }

    sealed class Spec
    {
        public string Name = "";
        public Type Type = null!;
        public Func<AppDb, List<ISync>> All = null!;
        public Func<AppDb, List<ISync>> AllFull = null!;
        public Func<ISync> Make = null!;
        public List<Fld> Fields = new();
        public List<Ref> Refs = new();
        public List<SetDef> Sets = new();
        public PropertyInfo? Author;
    }

    static List<ISync> Ls<T>(IQueryable<T> q) where T : class, ISync => q.ToList().Cast<ISync>().ToList();

    static string Pascal(string key) =>
        string.Concat(key.Split('_').Select(p => p.Substring(0, 1).ToUpperInvariant() + p.Substring(1)));

    static PropertyInfo Prop(Type t, string name) =>
        t.GetProperty(name) ?? throw new InvalidOperationException($"SyncFile: {t.Name}.{name} does not exist");

    static readonly Dictionary<string, string[]> RequiredRefs = new()
    {
        ["previous_items"] = new[] { "meeting_uuid" },
        ["progress_history"] = new[] { "action_uuid" },
        ["action_notes"] = new[] { "action_uuid" },
        ["checklist_items"] = new[] { "action_uuid" },
    };

    /// <param name="fields">"key:kind,..."</param><param name="refs">"json_key>FkProperty>target_table,..."</param>
    /// <param name="sets">"json_key>NavProperty>target_table,..."</param>
    static Spec Mk(string name, Type type, Func<AppDb, List<ISync>> all, Func<ISync> make, string fields, string refs, string sets,
                   string? author = null, Func<AppDb, List<ISync>>? full = null)
    {
        var s = new Spec { Name = name, Type = type, All = all, AllFull = full ?? all, Make = make };
        foreach (var part in fields.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(':');
            s.Fields.Add(new Fld { Key = kv[0], Prop = Prop(type, Pascal(kv[0])), Kind = kv[1] });
        }
        foreach (var part in refs.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = part.Split('>');
            var required = RequiredRefs.TryGetValue(name, out var req) && req.Contains(p[0]);
            s.Refs.Add(new Ref { Key = p[0], Fk = Prop(type, p[1]), Target = p[2], Required = required });
        }
        foreach (var part in sets.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = part.Split('>');
            s.Sets.Add(new SetDef { Key = p[0], NavName = p[1], Nav = Prop(type, p[1]), Target = p[2] });
        }
        if (author != null) s.Author = Prop(type, author);
        return s;
    }

    // Parents before children; an action before its subtasks (handled in Import).
    static readonly List<Spec> Specs = new()
    {
        Mk("person_categories", typeof(PersonCategory), db => Ls(db.Categories.OrderBy(x => x.Id)), () => new PersonCategory(),
            "name:s", "", ""),
        Mk("people", typeof(Person), db => Ls(db.People.OrderBy(x => x.Id)), () => new Person(),
            "name:s,department:s,role:s,category:s,is_active:bi,created_at:dt", "category_uuid>CategoryId>person_categories", ""),
        Mk("teams", typeof(Team), db => Ls(db.Teams.OrderBy(x => x.Id)), () => new Team(),
            "name:s,description:s,is_active:b", "manager_uuid>ManagerId>people", "member_uuids>Members>people",
            null, db => Ls(db.Teams.Include(x => x.Members).OrderBy(x => x.Id))),
        Mk("locations", typeof(Location), db => Ls(db.Locations.OrderBy(x => x.Id)), () => new Location(),
            "name:s,address:s,description:s,is_active:b", "", ""),
        Mk("projects", typeof(Project), db => Ls(db.Projects.OrderBy(x => x.Id)), () => new Project(),
            "name:s,description:s,status:s,start_date:s,end_date:s,progress:i,created_at:dt,archived_at:ndt,color:s",
            "manager_uuid>ManagerId>people", "member_uuids>Members>people",
            null, db => Ls(db.Projects.Include(x => x.Members).OrderBy(x => x.Id))),
        Mk("meetings", typeof(Meeting), db => Ls(db.Meetings.OrderBy(x => x.Id)), () => new Meeting(),
            "title:s,meeting_date:s,time:s,place:s,description:s,summary:s,created_at:dt",
            "project_uuid>ProjectId>projects", "attendee_uuids>Attendees>people,linked_action_uuids>LinkedActions>actions",
            null, db => Ls(db.Meetings.Include(x => x.Attendees).Include(x => x.LinkedActions).AsSplitQuery().OrderBy(x => x.Id))),
        Mk("events", typeof(Event), db => Ls(db.Events.OrderBy(x => x.Id)), () => new Event(),
            "title:s,kind:s,event_date:s,start_time:s,end_time:s,place:s,description:s,created_at:dt",
            "project_uuid>ProjectId>projects", "attendee_uuids>Attendees>people",
            null, db => Ls(db.Events.Include(x => x.Attendees).OrderBy(x => x.Id))),
        Mk("previous_items", typeof(PreviousItem), db => Ls(db.PreviousItems.OrderBy(x => x.Id)), () => new PreviousItem(),
            "activity:s,owner_name_snapshot:s,status:s,progress:i,note:s",
            "meeting_uuid>MeetingId>meetings,owner_uuid>OwnerId>people", ""),
        Mk("actions", typeof(ActionItem), db => Ls(db.Actions.OrderBy(x => x.Id)), () => new ActionItem(),
            "start_date:s,subject:s,decision:s,due:s,status:s,priority:s,progress:i,created_at:dt,sprint:s,carried_over_from:s,ado_id:s,done_date:s," +
            "work_item_type:s,parent_ref:s,ado_priority:ni,ado_notes:s,archived_at:ndt",
            "meeting_uuid>MeetingId>meetings,project_uuid>ProjectId>projects,parent_uuid>ParentId>actions,owner_uuid>OwnerId>people,assignee_uuid>AssigneeId>people",
            "collaborator_uuids>Collaborators>people",
            null, db => Ls(db.Actions.Include(x => x.Collaborators).OrderBy(x => x.Id))),
        Mk("progress_history", typeof(ProgressHistory), db => Ls(db.History.OrderBy(x => x.Id)), () => new ProgressHistory(),
            "update_date:s,old_progress:i,new_progress:i,note:s,created_at:dt", "action_uuid>ActionId>actions", "", "CreatedBy"),
        Mk("action_notes", typeof(ActionNote), db => Ls(db.ActionNotes.OrderBy(x => x.Id)), () => new ActionNote(),
            "body:s,created_at:dt", "action_uuid>ActionId>actions", "", "UserId"),
        Mk("checklist_items", typeof(ChecklistItem), db => Ls(db.Checklist.OrderBy(x => x.Id)), () => new ChecklistItem(),
            "title:s,is_done:b,sort_order:i,created_at:dt", "action_uuid>ActionId>actions", ""),
        Mk("comments", typeof(Comment), db => Ls(db.Comments.OrderBy(x => x.Id)), () => new Comment(),
            "body:s,created_at:dt", "action_uuid>ActionId>actions,meeting_uuid>MeetingId>meetings", "", "UserId"),
    };

    static readonly Dictionary<string, Spec> ByName = Specs.ToDictionary(s => s.Name);
    static readonly Dictionary<Type, Spec> ByType = Specs.ToDictionary(s => s.Type);

    public static IReadOnlyList<string> TableOrder => Specs.Select(s => s.Name).ToList();

    // ------------------------------------------------------------------ time helpers
    /// <summary>Naive UTC datetime -> epoch milliseconds.</summary>
    public static long? ToMs(DateTime? dt)
    {
        if (dt == null) return null;
        return (dt.Value.Ticks - EpochTicks) / 10000;
    }

    public static DateTime? FromMs(long? ms)
    {
        if (ms == null) return null;
        try { return new DateTime(EpochTicks + ms.Value * 10000, DateTimeKind.Unspecified); }
        catch (ArgumentOutOfRangeException) { throw new SyncFileError(BadRecord); }
    }

    /// <summary>Last change wins; on a tie the local record stays (so re-importing a file is a no-op).</summary>
    public static bool ShouldApply(long localMs, long remoteMs) => remoteMs > localMs;

    static int IdOf(ISync o) => o switch
    {
        PersonCategory x => x.Id,
        Person x => x.Id,
        Team x => x.Id,
        Location x => x.Id,
        Project x => x.Id,
        Meeting x => x.Id,
        Event x => x.Id,
        PreviousItem x => x.Id,
        ActionItem x => x.Id,
        ProgressHistory x => x.Id,
        ActionNote x => x.Id,
        ChecklistItem x => x.Id,
        Comment x => x.Id,
        _ => throw new InvalidOperationException("SyncFile: unknown record type " + o.GetType().Name),
    };

    // ------------------------------------------------------------------ export
    /// <summary>Collects every record of every shared table (tombstones included) into a JSON-ready dictionary.</summary>
    public static Dictionary<string, object?> Export(AppDb db, string device = "desktop", long? nowMs = null)
    {
        var loaded = new Dictionary<string, List<ISync>>();
        var idToUuid = new Dictionary<string, Dictionary<int, string>>();
        foreach (var spec in Specs)
        {
            var list = spec.AllFull(db);
            loaded[spec.Name] = list;
            var map = new Dictionary<int, string>();
            foreach (var o in list) map[IdOf(o)] = o.Uuid;
            idToUuid[spec.Name] = map;
        }
        var users = new Dictionary<int, string>();
        foreach (var u in db.Users.ToList()) users[u.Id] = u.FullName;

        var tables = new Dictionary<string, List<Dictionary<string, object?>>>();
        foreach (var spec in Specs)
        {
            var rows = new List<Dictionary<string, object?>>();
            foreach (var o in loaded[spec.Name])
            {
                var rec = new Dictionary<string, object?>();
                rec["uuid"] = o.Uuid;
                rec["updated_at"] = ToMs(o.UpdatedAt);
                rec["deleted_at"] = ToMs(o.DeletedAt);
                foreach (var f in spec.Fields)
                {
                    var v = f.Prop.GetValue(o);
                    switch (f.Kind)
                    {
                        case "dt":
                        case "ndt": rec[f.Key] = ToMs((DateTime?)v); break;
                        case "bi": rec[f.Key] = ((bool)v!) ? 1 : 0; break;
                        default: rec[f.Key] = v; break;
                    }
                }
                foreach (var r in spec.Refs)
                {
                    var fk = (int?)r.Fk.GetValue(o);
                    string? uuid = null;
                    if (fk != null && idToUuid[r.Target].TryGetValue(fk.Value, out var found)) uuid = found;
                    rec[r.Key] = uuid;
                }
                foreach (var sd in spec.Sets)
                {
                    var coll = (IEnumerable)sd.Nav.GetValue(o)!;
                    var uuids = coll.Cast<ISync>().Select(x => x.Uuid).ToList();
                    uuids.Sort(StringComparer.Ordinal);
                    rec[sd.Key] = uuids;
                }
                if (spec.Author != null)
                {
                    var uid = (int?)spec.Author.GetValue(o);
                    rec["author_name"] = uid != null && users.TryGetValue(uid.Value, out var fn) ? fn : "";
                }
                rows.Add(rec);
            }
            var live = new HashSet<string>(rows.Select(r => (string)r["uuid"]!));
            foreach (var ts in db.Tombstones.Where(x => x.TableName == spec.Name).OrderBy(x => x.Id).ToList())
            {
                if (live.Contains(ts.Uuid)) continue;
                var ms = ToMs(ts.DeletedAt);
                rows.Add(new Dictionary<string, object?> { ["uuid"] = ts.Uuid, ["updated_at"] = ms, ["deleted_at"] = ms });
            }
            tables[spec.Name] = rows;
        }
        return new Dictionary<string, object?>
        {
            ["format"] = Format,
            ["version"] = Version,
            ["device"] = device,
            ["exported_at"] = nowMs ?? ToMs(Clock.Now()),
            ["tables"] = tables,
        };
    }

    public static string ToJson(Dictionary<string, object?> snapshot) => JsonSerializer.Serialize(snapshot, ExportOptions);

    // ------------------------------------------------------------------ JSON reading helpers
    static bool HasValue(JsonElement rec, string key) =>
        rec.ValueKind == JsonValueKind.Object && rec.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null && v.ValueKind != JsonValueKind.Undefined;

    static string? OptStr(JsonElement rec, string key) =>
        rec.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static long ToLong(JsonElement v)
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.Number:
                if (v.TryGetInt64(out var l)) return l;
                return (long)Math.Round(v.GetDouble());
            case JsonValueKind.True: return 1;
            case JsonValueKind.False: return 0;
            case JsonValueKind.String:
                if (long.TryParse(v.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var p)) return p;
                break;
        }
        throw new SyncFileError(BadRecord);
    }

    static long ReqLong(JsonElement rec, string key)
    {
        if (!rec.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null) throw new SyncFileError(BadRecord);
        return ToLong(v);
    }

    static string ReqStr(JsonElement rec, string key)
    {
        if (!rec.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String) throw new SyncFileError(BadRecord);
        return v.GetString() ?? "";
    }

    static string AsString(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Null => "",
        JsonValueKind.Undefined => "",
        _ => v.GetRawText(),
    };

    static bool AsBool(JsonElement v)
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            case JsonValueKind.Null: return false;
            case JsonValueKind.Number: return ToLong(v) != 0;
            case JsonValueKind.String:
                var s = (v.GetString() ?? "").Trim().ToLowerInvariant();
                return s == "true" || s == "1";
        }
        return false;
    }

    static void ApplyField(ISync obj, Fld f, JsonElement v)
    {
        switch (f.Kind)
        {
            case "s": f.Prop.SetValue(obj, AsString(v)); break;
            case "i": f.Prop.SetValue(obj, v.ValueKind == JsonValueKind.Null ? 0 : (int)ToLong(v)); break;
            case "ni":
                if (v.ValueKind == JsonValueKind.Null) f.Prop.SetValue(obj, null);
                else f.Prop.SetValue(obj, (int?)(int)ToLong(v));
                break;
            case "b":
            case "bi": f.Prop.SetValue(obj, AsBool(v)); break;
            case "dt":
                if (v.ValueKind != JsonValueKind.Null) f.Prop.SetValue(obj, FromMs(ToLong(v)));
                break;
            case "ndt":
                if (v.ValueKind == JsonValueKind.Null) f.Prop.SetValue(obj, null);
                else f.Prop.SetValue(obj, FromMs(ToLong(v)));
                break;
            default: throw new InvalidOperationException("SyncFile: unknown field kind " + f.Kind);
        }
    }

    // ------------------------------------------------------------------ import
    static Dictionary<string, Dictionary<string, ISync>> LoadIndex(AppDb db)
    {
        var idx = new Dictionary<string, Dictionary<string, ISync>>();
        foreach (var spec in Specs)
        {
            var d = new Dictionary<string, ISync>();
            foreach (var o in spec.All(db)) d[o.Uuid] = o;
            idx[spec.Name] = d;
        }
        return idx;
    }

    static string Resolve(Dictionary<string, string> alias, string table, string uuid) =>
        table == "person_categories" && alias.TryGetValue(uuid, out var a) ? a : uuid;

    static List<JsonElement> RecordsOf(JsonElement data, string table)
    {
        var list = new List<JsonElement>();
        if (!data.TryGetProperty("tables", out var tables) || tables.ValueKind != JsonValueKind.Object) return list;
        if (!tables.TryGetProperty(table, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var r in arr.EnumerateArray()) list.Add(r);
        return list;
    }

    /// <summary>Merges a snapshot into the database. Returns counts: inserted / updated / deleted / skipped / orphaned.</summary>
    public static SyncStats Import(AppDb db, JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || OptStr(data, "format") != Format) throw new SyncFileError(NotSyncFile);
        if (!data.TryGetProperty("version", out var ver) || ver.ValueKind != JsonValueKind.Number || ver.GetDouble() != Version)
            throw new SyncFileError(BadVersion);

        var stats = new SyncStats();
        var oldRaw = db.RawSync;
        db.RawSync = true;     // keep the file's uuid / updated_at as written
        try
        {
            using var tx = db.Database.BeginTransaction();
            var idx = LoadIndex(db);
            var alias = new Dictionary<string, string>();      // category uuid in the file -> the local category with the same name
            var users = new Dictionary<string, int>();
            foreach (var u in db.Users.ToList()) users[u.FullName] = u.Id;
            var applied = new List<(Spec spec, ISync obj, JsonElement rec)>();   // whose sets still have to be written

            foreach (var spec in Specs)
            {
                var records = RecordsOf(data, spec.Name);
                if (spec.Name == "actions")   // a parent action must exist before its subtasks (stable sort)
                    records = records.OrderBy(r => r.ValueKind == JsonValueKind.Object && HasValue(r, "parent_uuid")).ToList();
                foreach (var rec in records)
                {
                    if (rec.ValueKind != JsonValueKind.Object) throw new SyncFileError(BadRecord);
                    var uuid = ReqStr(rec, "uuid");
                    idx[spec.Name].TryGetValue(uuid, out var obj);

                    if (obj == null && spec.Name == "person_categories")
                    {
                        // Category names are unique, so two devices that each created "the same" category would collide:
                        // treat them as one record and let links in the file point at the local one.
                        var nm = OptStr(rec, "name");
                        var same = idx[spec.Name].Values.OfType<PersonCategory>().FirstOrDefault(o => o.Name == nm);
                        if (same != null)
                        {
                            alias[uuid] = same.Uuid;
                            stats.Skipped++;
                            continue;
                        }
                    }
                    var remoteMs = ReqLong(rec, "updated_at");
                    if (obj != null && !ShouldApply(ToMs(obj.UpdatedAt)!.Value, remoteMs))
                    {
                        stats.Skipped++;
                        continue;
                    }
                    if (HasValue(rec, "deleted_at"))
                    {
                        if (obj == null)
                        {
                            stats.Skipped++;
                        }
                        else
                        {
                            var gone = DeleteTree(db, obj);   // deleting cascades to dependent rows
                            db.SaveChanges();
                            foreach (var g in gone)
                                if (ByType.TryGetValue(g.GetType(), out var gs)) idx[gs.Name].Remove(g.Uuid);
                            var delAt = FromMs(ReqLong(rec, "deleted_at"));
                            foreach (var ts in db.Tombstones.Where(x => x.Uuid == uuid).ToList())
                                if (delAt != null) ts.DeletedAt = delAt.Value;
                            db.SaveChanges();
                            stats.Deleted++;
                        }
                        continue;
                    }

                    var refs = new List<(Ref r, int? id)>();
                    var missingRequired = false;
                    foreach (var r in spec.Refs)
                    {
                        string? ru = rec.TryGetProperty(r.Key, out var rv) && rv.ValueKind == JsonValueKind.String ? rv.GetString() : null;
                        ISync? target = null;
                        if (!string.IsNullOrEmpty(ru)) idx[r.Target].TryGetValue(Resolve(alias, r.Target, ru), out target);
                        refs.Add((r, target == null ? (int?)null : IdOf(target)));
                        if (r.Required && target == null) missingRequired = true;
                    }
                    if (spec.Name == "comments" && refs.All(x => x.id == null)) missingRequired = true;
                    if (missingRequired)
                    {
                        stats.Orphaned++;
                        continue;
                    }

                    var isNew = obj == null;
                    if (obj == null)
                    {
                        obj = spec.Make();
                        obj.Uuid = uuid;
                        stats.Inserted++;
                    }
                    else stats.Updated++;
                    foreach (var f in spec.Fields)
                        if (rec.TryGetProperty(f.Key, out var fv)) ApplyField(obj, f, fv);
                    foreach (var (r, id) in refs)
                        r.Fk.SetValue(obj, id == null ? null : (object)id.Value);
                    if (spec.Author != null)
                    {
                        var an = OptStr(rec, "author_name");
                        if (an != null && users.TryGetValue(an, out var uid)) spec.Author.SetValue(obj, (int?)uid);
                        else spec.Author.SetValue(obj, null);
                    }
                    obj.UpdatedAt = FromMs(remoteMs)!.Value;
                    obj.DeletedAt = null;
                    if (isNew)
                    {
                        db.Tombstones.RemoveRange(db.Tombstones.Where(x => x.Uuid == uuid).ToList());   // it lives again
                        db.Add((object)obj);
                        db.SaveChanges();     // gives it its id, needed by records that link to it
                    }
                    idx[spec.Name][uuid] = obj;
                    applied.Add((spec, obj, rec));
                }
            }
            db.SaveChanges();

            // Writing the link tables does not touch the owner's row, so updated_at stays what the file said.
            foreach (var (spec, obj, rec) in applied)
            {
                if (db.Entry(obj).State == EntityState.Detached) continue;
                foreach (var sd in spec.Sets)
                {
                    if (!rec.TryGetProperty(sd.Key, out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
                    var members = new List<ISync>();
                    foreach (var item in arr.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String) continue;
                        var key = Resolve(alias, sd.Target, item.GetString() ?? "");
                        if (idx[sd.Target].TryGetValue(key, out var m) && !members.Contains(m)) members.Add(m);
                    }
                    ApplySet(db, obj, sd, members);
                }
            }
            db.SaveChanges();
            tx.Commit();
        }
        finally
        {
            db.RawSync = oldRaw;
        }
        return stats;
    }

    static void ApplySet(AppDb db, ISync owner, SetDef sd, List<ISync> members)
    {
        var entry = db.Entry(owner).Collection(sd.NavName);
        if (!entry.IsLoaded) entry.Load();
        var list = (IList)sd.Nav.GetValue(owner)!;
        var current = list.Cast<object>().ToList();
        foreach (var c in current)
            if (!members.Contains((ISync)c)) list.Remove(c);
        foreach (var m in members)
            if (!current.Contains(m)) list.Add(m);
    }

    // ------------------------------------------------------------------ deletion with dependents
    // The database connection has foreign keys off, so cascades are done here (parents take their children with them,
    // and links to removed records are cleared); every removed shared row gets its tombstone from AppDb.Stamp().
    static void Kill(AppDb db, List<ISync> gone, ISync x)
    {
        gone.Add(x);
        db.Remove((object)x);
    }

    static void KillAction(AppDb db, List<ISync> gone, HashSet<int> seen, ActionItem a)
    {
        if (!seen.Add(a.Id)) return;
        var id = a.Id;
        foreach (var sub in db.Actions.Where(q => q.ParentId == id).ToList()) KillAction(db, gone, seen, sub);
        foreach (var h in db.History.Where(q => q.ActionId == id).ToList()) Kill(db, gone, h);
        foreach (var n in db.ActionNotes.Where(q => q.ActionId == id).ToList()) Kill(db, gone, n);
        foreach (var c in db.Checklist.Where(q => q.ActionId == id).ToList()) Kill(db, gone, c);
        foreach (var cm in db.Comments.Where(q => q.ActionId == id).ToList()) Kill(db, gone, cm);
        var e = db.Entry(a);
        e.Collection(q => q.Collaborators).Load();
        a.Collaborators.Clear();
        e.Collection(q => q.LinkedMeetings).Load();
        a.LinkedMeetings.Clear();
        Kill(db, gone, a);
    }

    static List<ISync> DeleteTree(AppDb db, ISync obj)
    {
        var gone = new List<ISync>();
        if (obj is ActionItem act)
        {
            KillAction(db, gone, new HashSet<int>(), act);
        }
        else if (obj is Meeting mt)
        {
            var id = mt.Id;
            foreach (var p in db.PreviousItems.Where(q => q.MeetingId == id).ToList()) Kill(db, gone, p);
            foreach (var c in db.Comments.Where(q => q.MeetingId == id).ToList()) Kill(db, gone, c);
            foreach (var a in db.Actions.Where(q => q.MeetingId == id).ToList()) { a.MeetingId = null; a.UpdatedAt = Clock.Now(); }
            var e = db.Entry(mt);
            e.Collection(q => q.LinkedActions).Load();
            mt.LinkedActions.Clear();
            e.Collection(q => q.Attendees).Load();
            mt.Attendees.Clear();
            Kill(db, gone, mt);
        }
        else if (obj is Event ev)
        {
            var e = db.Entry(ev);
            e.Collection(q => q.Attendees).Load();
            ev.Attendees.Clear();
            Kill(db, gone, ev);
        }
        else if (obj is Project pr)
        {
            var id = pr.Id;
            foreach (var evp in db.Events.Where(q => q.ProjectId == id).ToList()) evp.ProjectId = null;
            foreach (var a in db.Actions.Where(q => q.ProjectId == id).ToList()) { a.ProjectId = null; a.UpdatedAt = Clock.Now(); }
            foreach (var m in db.Meetings.Where(q => q.ProjectId == id).ToList()) m.ProjectId = null;
            var e = db.Entry(pr);
            e.Collection(q => q.Members).Load();
            pr.Members.Clear();
            Kill(db, gone, pr);
        }
        else if (obj is Person pe)
        {
            var id = pe.Id;
            foreach (var a in db.Actions.Where(q => q.OwnerId == id || q.AssigneeId == id).ToList())
            {
                if (a.OwnerId == id) a.OwnerId = null;
                if (a.AssigneeId == id) a.AssigneeId = null;
                a.UpdatedAt = Clock.Now();
            }
            foreach (var a in db.Actions.Include(q => q.Collaborators).Where(q => q.Collaborators.Any(w => w.Id == id)).ToList())
                a.Collaborators.RemoveAll(w => w.Id == id);
            foreach (var t in db.Teams.Include(q => q.Members).Where(q => q.ManagerId == id || q.Members.Any(w => w.Id == id)).ToList())
            {
                if (t.ManagerId == id) t.ManagerId = null;
                t.Members.RemoveAll(w => w.Id == id);
            }
            foreach (var p in db.Projects.Include(q => q.Members).Where(q => q.ManagerId == id || q.Members.Any(w => w.Id == id)).ToList())
            {
                if (p.ManagerId == id) p.ManagerId = null;
                p.Members.RemoveAll(w => w.Id == id);
            }
            foreach (var m in db.Meetings.Include(q => q.Attendees).Where(q => q.Attendees.Any(w => w.Id == id)).ToList())
                m.Attendees.RemoveAll(w => w.Id == id);
            foreach (var evp in db.Events.Include(q => q.Attendees).Where(q => q.Attendees.Any(w => w.Id == id)).ToList())
                evp.Attendees.RemoveAll(w => w.Id == id);
            foreach (var pi in db.PreviousItems.Where(q => q.OwnerId == id).ToList()) pi.OwnerId = null;
            Kill(db, gone, pe);
        }
        else if (obj is Team tm)
        {
            var e = db.Entry(tm);
            e.Collection(q => q.Members).Load();
            tm.Members.Clear();
            Kill(db, gone, tm);
        }
        else if (obj is PersonCategory cat)
        {
            var id = cat.Id;
            foreach (var p in db.People.Where(q => q.CategoryId == id).ToList()) { p.CategoryId = null; p.UpdatedAt = Clock.Now(); }
            Kill(db, gone, cat);
        }
        else
        {
            Kill(db, gone, obj);     // locations, previous items, history, notes, checklist items, comments: no dependents
        }
        return gone;
    }
}
