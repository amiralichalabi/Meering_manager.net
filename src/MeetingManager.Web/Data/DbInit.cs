using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MeetingManager.Core;

namespace MeetingManager.Data;

public static class Paths
{
    public static string DataDir { get; private set; } = "";
    public static string DbPath => Path.Combine(DataDir, "meeting_manager.db");
    public static void Init()
    {
        var env = Environment.GetEnvironmentVariable("MEETING_MANAGER_DATA_DIR");
        DataDir = !string.IsNullOrEmpty(env) ? env : Path.Combine(Directory.GetCurrentDirectory(), "data");
        Directory.CreateDirectory(DataDir);
    }
}

public static class DbInit
{
    static readonly string[] SyncTables = { "person_categories", "people", "teams", "locations", "projects", "meetings", "previous_items", "actions", "progress_history", "action_notes", "checklist_items", "comments" };

    public static string ConnectionString() => new SqliteConnectionStringBuilder { DataSource = Paths.DbPath, ForeignKeys = false }.ToString();

    static string Schema()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("schema.sql")!;
        using var r = new StreamReader(s);
        var sql = r.ReadToEnd();
        sql = Regex.Replace(sql, @"CREATE TABLE ", "CREATE TABLE IF NOT EXISTS ");
        sql = Regex.Replace(sql, @"CREATE UNIQUE INDEX ", "CREATE UNIQUE INDEX IF NOT EXISTS ");
        sql = Regex.Replace(sql, @"CREATE INDEX ", "CREATE INDEX IF NOT EXISTS ");
        return sql;
    }

    static void Exec(SqliteConnection c, string sql, params (string, object?)[] ps)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
    static List<string> Rows(SqliteConnection c, string sql, int col = 0)
    {
        using var cmd = c.CreateCommand(); cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var l = new List<string>();
        while (r.Read()) l.Add(r.IsDBNull(col) ? "" : r.GetValue(col).ToString()!);
        return l;
    }

    public static void Migrate()
    {
        using var c = new SqliteConnection(ConnectionString());
        c.Open();
        var tables = Rows(c, "SELECT name FROM sqlite_master WHERE type='table'").ToHashSet();
        var fresh = tables.Count == 0;
        // New database: the reference schema. Existing one: only what is missing (CREATE ... IF NOT EXISTS), then ALTERs below.
        if (fresh) Exec(c, Schema());
        else
        {
            // Old databases may lack whole tables (notes, sprints, tombstones, attendees); index creation needs the columns, so create tables first.
            foreach (var stmt in Schema().Split(";\n", StringSplitOptions.RemoveEmptyEntries))
            {
                var t = stmt.Trim(); if (t == "") continue;
                if (t.StartsWith("CREATE TABLE")) Exec(c, t);
            }
        }
        tables = Rows(c, "SELECT name FROM sqlite_master WHERE type='table'").ToHashSet();

        var alters = new Dictionary<string, (string, string)[]>
        {
            ["users"] = new[] { ("person_id", "INTEGER"), ("last_login", "DATETIME"), ("birth_date", "VARCHAR(30) NOT NULL DEFAULT ''"), ("avatar", "TEXT NOT NULL DEFAULT ''") },
            ["projects"] = new[] { ("manager_id", "INTEGER"), ("start_date", "VARCHAR(30)"), ("end_date", "VARCHAR(30)"), ("progress", "INTEGER DEFAULT 0"), ("archived_at", "DATETIME"), ("color", "VARCHAR(9) NOT NULL DEFAULT ''") },
            ["meetings"] = new[] { ("summary", "TEXT"), ("project_id", "INTEGER") },
            ["people"] = new[] { ("category_id", "INTEGER") },
            ["actions"] = new[] { ("parent_id", "INTEGER"), ("start_date", "VARCHAR(30)"), ("sprint", "VARCHAR(100) NOT NULL DEFAULT ''"), ("carried_over_from", "VARCHAR(100) NOT NULL DEFAULT ''"), ("ado_id", "VARCHAR(30) NOT NULL DEFAULT ''"), ("done_date", "VARCHAR(30) NOT NULL DEFAULT ''"), ("work_item_type", "VARCHAR(30) NOT NULL DEFAULT ''"), ("parent_ref", "VARCHAR(300) NOT NULL DEFAULT ''"), ("ado_priority", "INTEGER"), ("ado_notes", "TEXT NOT NULL DEFAULT ''"), ("archived_at", "DATETIME") },
        };
        foreach (var (table, cols) in alters)
        {
            if (!tables.Contains(table)) continue;
            var existing = Rows(c, $"PRAGMA table_info({table})", 1).ToHashSet();
            foreach (var (col, typ) in cols) if (!existing.Contains(col)) Exec(c, $"ALTER TABLE {table} ADD COLUMN {col} {typ}");
        }
        foreach (var (t, col) in new[] { ("meetings", "summary"), ("projects", "start_date"), ("projects", "end_date"), ("actions", "start_date") })
            if (tables.Contains(t)) try { Exec(c, $"UPDATE {t} SET {col}='' WHERE {col} IS NULL"); } catch { }

        var nowStr = Clock.Now().ToString("yyyy-MM-dd HH:mm:ss.ffffff");
        foreach (var table in SyncTables)
        {
            if (!tables.Contains(table)) continue;
            var existing = Rows(c, $"PRAGMA table_info({table})", 1).ToHashSet();
            foreach (var (col, typ) in new[] { ("uuid", "VARCHAR(36)"), ("updated_at", "DATETIME"), ("deleted_at", "DATETIME") })
                if (!existing.Contains(col)) Exec(c, $"ALTER TABLE {table} ADD COLUMN {col} {typ}");
            foreach (var id in Rows(c, $"SELECT id FROM {table} WHERE uuid IS NULL OR uuid=''"))
                Exec(c, $"UPDATE {table} SET uuid=$u WHERE id=$i", ("$u", Guid.NewGuid().ToString()), ("$i", long.Parse(id)));
            var expr = existing.Contains("created_at") ? "COALESCE(created_at, $now)" : "$now";
            Exec(c, $"UPDATE {table} SET updated_at={expr} WHERE updated_at IS NULL", ("$now", nowStr));
            Exec(c, $"CREATE UNIQUE INDEX IF NOT EXISTS ix_{table}_uuid ON {table}(uuid)");
        }
    }

    public static void Seed(AppDb db)
    {
        foreach (var (code, (title, module)) in Security.Perms)
            if (!db.Permissions.Any(p => p.Code == code)) db.Permissions.Add(new Permission { Code = code, Title = title, Module = module });
        db.SaveChanges();
        var all = db.Permissions.ToList();
        foreach (var (role, codes) in Security.DefaultRole)
            foreach (var p in all)
                if (!db.RolePermissions.Any(rp => rp.Role == role && rp.PermissionId == p.Id))
                    db.RolePermissions.Add(new RolePermission { Role = role, PermissionId = p.Id, Enabled = codes.Contains(p.Code) });
        if (!db.Users.Any())
            db.Users.Add(new User { Username = "admin", FullName = "مدیر سامانه", PasswordHash = Security.HashPw("Admin@123"), Role = "admin", IsActive = true });
        db.SaveChanges();
    }
}
