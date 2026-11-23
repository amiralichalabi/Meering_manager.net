using MeetingManager.Core;
using MeetingManager.Data;

namespace MeetingManager.Api;

public static class AssistantApi
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/assistant/config", (HttpContext c, AppDb db) =>
        {
            Security.RequireAdmin(c, db);
            var cfg = Assistant.LoadConfig();
            return Http.Ok(new { url = cfg.Url, model = cfg.Model, timeout = cfg.Timeout });
        });

        app.MapPut("/api/assistant/config", async (HttpContext c, AppDb db) =>
        {
            Security.RequireAdmin(c, db);
            var x = await Http.Body<AssistantConfigIn>(c);
            Assistant.Config cfg;
            try { cfg = Assistant.SaveConfig(x.Url, x.Model, x.Timeout); }
            catch (ArgumentException e) { throw new ApiException(400, e.Message); }
            return Http.Ok(new { url = cfg.Url, model = cfg.Model, timeout = cfg.Timeout });
        });

        app.MapGet("/api/assistant/status", async (HttpContext c, AppDb db) =>
        {
            Security.Current(c, db);
            return Http.Ok(await Assistant.StatusInfo());
        });

        app.MapPost("/api/assistant", async (HttpContext c, AppDb db) =>
        {
            var u = Security.Current(c, db);
            var x = (await Http.Body<AssistantIn>(c)).Validate();
            var question = x.Question.Trim();
            if (question == "") throw new ApiException(400, "سؤال خالی است.");
            var ctx = Assistant.BuildContext(db, u);
            (string answer, string model) r;
            try { r = await Assistant.Chat(question, ctx, x.History); }
            catch (AssistantException e) { throw new ApiException(503, e.Message); }
            Http.Audit(db, u, "assistant", null, "ask", question.Length > 200 ? question[..200] : question);
            return Http.Ok(new { answer = r.answer, model = r.model });
        });
    }
}
