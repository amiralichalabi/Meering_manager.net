using System.Text.Json;
using MeetingManager.Core;
using MeetingManager.Data;

namespace MeetingManager.Api;

public static class SyncApi
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/sync/export", (HttpContext c, AppDb db) =>
        {
            Security.Require(c, db, "backup.manage");
            var json = SyncFile.ToJson(SyncFile.Export(db));
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            c.Response.Headers.ContentDisposition = $"attachment; filename=\"meeting_manager_sync_{stamp}.json\"";
            return Results.Text(json, "application/json; charset=utf-8");
        });
        app.MapPost("/api/sync/import", async (HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "backup.manage");
            JsonElement data;
            try { data = (await JsonDocument.ParseAsync(c.Request.Body)).RootElement.Clone(); }
            catch (JsonException) { throw new ApiException(422, "اطلاعات واردشده معتبر نیست."); }
            SyncStats stats;
            try { stats = SyncFile.Import(db, data); }
            catch (SyncFileError e) { throw new ApiException(400, e.Message); }
            Http.Audit(db, u, "sync", null, "import", stats.ToString());
            return Http.Ok(new { inserted = stats.Inserted, updated = stats.Updated, deleted = stats.Deleted, skipped = stats.Skipped, orphaned = stats.Orphaned });
        });
        app.MapGet("/api/backup", (HttpContext c, AppDb db) =>
        {
            Security.Require(c, db, "backup.manage");
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            // SQL Server has no single file to copy: the backup is the full sync file, which the sync import restores.
            if (DbConfig.SqlServer)
                return Results.File(System.Text.Encoding.UTF8.GetBytes(SyncFile.ToJson(SyncFile.Export(db))), "application/json", $"backup_{stamp}.json");
            var dest = Path.Combine(Paths.DataDir, $"backup_{stamp}.db");
            using (var src = new FileStream(Paths.DbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var dst = new FileStream(dest, FileMode.Create, FileAccess.Write))
                src.CopyTo(dst);
            return Results.File(File.ReadAllBytes(dest), "application/octet-stream", Path.GetFileName(dest));
        });
    }
}
