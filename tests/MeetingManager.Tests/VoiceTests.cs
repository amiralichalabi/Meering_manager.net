using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MeetingManager.Core;
using MeetingManager.Data;
using Xunit;

namespace MeetingManager.Tests;

[Collection("assistant")]
public class VoiceTests : IDisposable
{
    readonly ApiFixture _f = new();
    readonly HttpClient _c;
    public VoiceTests() { _c = _f.Client; Reset(); }
    public void Dispose() { Reset(); _f.Dispose(); }

    static void Reset()
    {
        Voice.TranscribeOverride = null;
        Assistant.RawOverride = null;
        foreach (var k in new[] { "MM_STT_URL", "MM_STT_MODEL", "MM_STT_TIMEOUT" }) Environment.SetEnvironmentVariable(k, null);
    }

    async Task<string> Admin()
    {
        var r = await _c.PostAsync("/api/auth/login", new StringContent(JsonSerializer.Serialize(new { username = "admin", password = "Admin@123" }), Encoding.UTF8, "application/json"));
        using var d = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return d.RootElement.GetProperty("token").GetString()!;
    }

    Task<HttpResponseMessage> Post(string url, string token, HttpContent body, string? filename = null)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, url) { Content = body };
        r.Headers.Add("X-Session-Token", token);
        if (filename != null) r.Headers.Add("X-Filename", Uri.EscapeDataString(filename));
        return _c.SendAsync(r);
    }

    static int AddPerson(WebApplicationFactory<Program> f, string name)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var p = new Person { Name = name };
        db.People.Add(p); db.SaveChanges();
        return p.Id;
    }

    [Fact]
    public async Task VoiceDraft_Builds_Prefill_From_Fake_Stt_And_Llm()
    {
        var ali = AddPerson(_f.Factory, "علی رضایی");
        string? gotName = null; long gotLen = 0;
        Voice.TranscribeOverride = (data, name, ct) => { gotName = name; gotLen = data.Length; return Task.FromResult("سلام. امروز درباره‌ی گزارش فصلی صحبت کردیم و قرار شد علی رضایی گزارش را تا ده روز دیگر آماده کند."); };
        Assistant.RawOverride = (sys, user) => Task.FromResult(
            "<think>x</think>```json\n{\"title\":\"جلسه گزارش\",\"summary\":\"درباره گزارش بحث شد.\",\"decisions\":[\"گزارش آماده شود\"],\"attendees\":[\"علی رضایی\"],\"actions\":[{\"subject\":\"آماده‌سازی گزارش\",\"owner\":\"علی\",\"due\":\"1405/01/10\",\"decision\":\"\"},{\"subject\":\"\",\"owner\":\"\"}]}\n```");
        var t = await Admin();
        var r = await Post("/api/meetings/voice-draft", t, new ByteArrayContent(new byte[] { 1, 2, 3, 4 }), "جلسه.webm");
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.OK, text);
        Assert.Equal("جلسه.webm", gotName);
        Assert.Equal(4, gotLen);
        using var d = JsonDocument.Parse(text);
        var root = d.RootElement;
        Assert.Equal("جلسه گزارش", root.GetProperty("title").GetString());
        Assert.Contains("مصوبات:", root.GetProperty("summary").GetString());
        Assert.Equal(ali, root.GetProperty("attendee_ids")[0].GetInt32());
        var acts = root.GetProperty("actions");
        Assert.Equal(1, acts.GetArrayLength());
        Assert.Equal(ali, acts[0].GetProperty("owner_id").GetInt32());
        Assert.Equal("1405/01/10", acts[0].GetProperty("due").GetString());
        Assert.Contains("گزارش فصلی", root.GetProperty("transcript").GetString());
    }

    [Fact]
    public async Task TextDraft_Rejects_Short_And_Bad_Model_Output()
    {
        var t = await Admin();
        var r = await Post("/api/meetings/text-draft", t, new StringContent("{\"transcript\":\"کوتاه\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assistant.RawOverride = (s, u) => Task.FromResult("این JSON نیست");
        var r2 = await Post("/api/meetings/text-draft", t, new StringContent(JsonSerializer.Serialize(new { transcript = string.Concat(Enumerable.Repeat("الف ", 20)) }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r2.StatusCode);
    }

    [Fact]
    public async Task VoiceDraft_Requires_Login_And_Config_Requires_Admin()
    {
        var r = await _c.PostAsync("/api/meetings/voice-draft", new ByteArrayContent(new byte[] { 1 }));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        var g = await _c.GetAsync("/api/voice/config");
        Assert.Equal(HttpStatusCode.Unauthorized, g.StatusCode);
    }

    [Fact]
    public async Task Config_Roundtrip_And_Validation()
    {
        var t = await Admin();
        var bad = new HttpRequestMessage(HttpMethod.Put, "/api/voice/config") { Content = new StringContent("{\"url\":\"not a url\"}", Encoding.UTF8, "application/json") };
        bad.Headers.Add("X-Session-Token", t);
        Assert.Equal(HttpStatusCode.BadRequest, (await _c.SendAsync(bad)).StatusCode);
        var ok = new HttpRequestMessage(HttpMethod.Put, "/api/voice/config") { Content = new StringContent("{\"url\":\"http://10.0.0.5:8000/v1\",\"model\":\"m\",\"timeout\":\"60\"}", Encoding.UTF8, "application/json") };
        ok.Headers.Add("X-Session-Token", t);
        var r = await _c.SendAsync(ok);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var d = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        Assert.Equal("http://10.0.0.5:8000", d.RootElement.GetProperty("url").GetString());
        try { File.Delete(Path.Combine(Paths.DataDir, "stt.json")); } catch { }
    }

    [Fact]
    public async Task DraftXlsx_Is_The_Azure_Sheet_And_Round_Trips()
    {
        var t = await Admin();
        async Task<JsonElement> Make(string url, object body)
        {
            var r = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
            r.Headers.Add("X-Session-Token", t);
            var resp = await _c.SendAsync(r);
            using var d = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return d.RootElement.Clone();
        }
        var pid = (await Make("/api/projects", new { name = "پروژه الف" })).GetProperty("id").GetInt32();
        var ali = (await Make("/api/people", new { name = "علی رضایی" })).GetProperty("id").GetInt32();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/meetings/draft-xlsx")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { project_id = pid, sprint = "Sprint 3", title = "جلسه گزارش", actions = new object[] { new { subject = "آماده‌سازی گزارش", decision = "تا هفته آینده", due = "1405/09/10", owner_id = ali, assignee_id = ali }, new { subject = "  " } } }), Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-Session-Token", t);
        var r = await _c.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var bytes = await r.Content.ReadAsByteArrayAsync();
        using (var wb = new ClosedXML.Excel.XLWorkbook(new MemoryStream(bytes)))
        {
            var ws = wb.Worksheet("Items");
            Assert.Equal("پروژه الف", ws.Cell(2, 1).GetString());
            Assert.Equal("آماده‌سازی گزارش", ws.Cell(2, 2).GetString());
            Assert.Equal("علی رضایی", ws.Cell(2, 3).GetString());
            Assert.Equal("New", ws.Cell(2, 4).GetString());
            Assert.Equal("Sprint 3", ws.Cell(2, 5).GetString());
            Assert.Contains("جلسه گزارش", ws.Cell(2, 12).GetString());
            Assert.Equal(2, ws.LastRowUsed()!.RowNumber());
        }
        var imp = new HttpRequestMessage(HttpMethod.Post, "/api/import/tasks") { Content = new ByteArrayContent(bytes) };
        imp.Headers.Add("X-Session-Token", t);
        var ir = await _c.SendAsync(imp);
        using var id = JsonDocument.Parse(await ir.Content.ReadAsStringAsync());
        Assert.Equal(1, id.RootElement.GetProperty("created").GetInt32());
        var empty = new HttpRequestMessage(HttpMethod.Post, "/api/meetings/draft-xlsx") { Content = new StringContent("{\"actions\":[]}", Encoding.UTF8, "application/json") };
        empty.Headers.Add("X-Session-Token", t);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _c.SendAsync(empty)).StatusCode);
    }
}
