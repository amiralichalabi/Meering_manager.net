using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using MeetingManager.Core;
using MeetingManager.Data;

namespace MeetingManager.Api;

public static class VoiceApi
{
    static (List<(int, string)> people, List<(int, string)> projects) Known(AppDb db) => (
        db.People.Where(p => p.DeletedAt == null).OrderBy(p => p.Id).AsEnumerable().Select(p => (p.Id, p.Name)).ToList(),
        db.Projects.Where(p => p.DeletedAt == null && p.ArchivedAt == null).OrderBy(p => p.Id).AsEnumerable().Select(p => (p.Id, p.Name)).ToList());

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/voice/config", (HttpContext c, AppDb db) =>
        {
            Security.RequireAdmin(c, db);
            var cfg = Voice.LoadConfig();
            return Http.Ok(new { url = cfg.Url, model = cfg.Model, timeout = cfg.Timeout });
        });

        app.MapPut("/api/voice/config", async (HttpContext c, AppDb db) =>
        {
            Security.RequireAdmin(c, db);
            var x = await Http.Body<AssistantConfigIn>(c);
            Voice.Config cfg;
            try { cfg = Voice.SaveConfig(x.Url, x.Model, x.Timeout); }
            catch (ArgumentException e) { throw new ApiException(400, e.Message); }
            return Http.Ok(new { url = cfg.Url, model = cfg.Model, timeout = cfg.Timeout });
        });

        app.MapGet("/api/voice/status", async (HttpContext c, AppDb db) => { Security.Current(c, db); return Http.Ok(await Voice.StatusInfo()); });

        app.MapPost("/api/meetings/voice-draft", async (HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "meetings.manage");
            var limit = c.Features.Get<IHttpMaxRequestBodySizeFeature>(); // absent on the in-memory test server
            if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = Voice.MaxAudioBytes + 1024;
            using var ms = new MemoryStream();
            await c.Request.Body.CopyToAsync(ms);
            var name = Uri.UnescapeDataString(c.Request.Headers["X-Filename"].FirstOrDefault() ?? "meeting.webm");
            try
            {
                var transcript = await Voice.Transcribe(ms.ToArray(), name, c.Request.ContentType ?? "application/octet-stream");
                var (people, projects) = Known(db);
                var draft = await Voice.BuildDraft(transcript, people, projects);
                Http.Audit(db, u, "meetings", null, "voice-draft", $"{ms.Length / 1024} KB");
                return Http.Ok(draft);
            }
            catch (AssistantException e) { throw new ApiException(503, e.Message); }
        });

        app.MapPost("/api/meetings/text-draft", async (HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "meetings.manage");
            var x = await Http.Body<TextDraftIn>(c);
            try
            {
                var (people, projects) = Known(db);
                var draft = await Voice.BuildDraft(x.Transcript ?? "", people, projects);
                Http.Audit(db, u, "meetings", null, "text-draft", $"{(x.Transcript ?? "").Length} chars");
                return Http.Ok(draft);
            }
            catch (AssistantException e) { throw new ApiException(503, e.Message); }
        });
    }
}
