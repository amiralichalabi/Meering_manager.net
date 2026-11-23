using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using MeetingManager.Core;
using Xunit;

namespace MeetingManager.Tests;

public class ApiFixture : IDisposable
{
    readonly string? _old;
    public WebApplicationFactory<Program> Factory { get; }
    public HttpClient Client { get; }
    public ApiFixture()
    {
        _old = Environment.GetEnvironmentVariable("MEETING_MANAGER_DATA_DIR");
        var dir = Path.Combine(Path.GetTempPath(), "mm_api_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("MEETING_MANAGER_DATA_DIR", dir);
        Factory = new WebApplicationFactory<Program>();
        Client = Factory.CreateClient();
    }
    public void Dispose() { Client.Dispose(); Factory.Dispose(); Environment.SetEnvironmentVariable("MEETING_MANAGER_DATA_DIR", _old); }
}

[Collection("assistant")]
public class ApiTests : IClassFixture<ApiFixture>
{
    readonly HttpClient _c;
    public ApiTests(ApiFixture f) { _c = f.Client; }

    static StringContent J(object o) => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");
    HttpRequestMessage Req(HttpMethod m, string url, string? token, object? body = null)
    {
        var r = new HttpRequestMessage(m, url);
        if (token != null) r.Headers.Add("X-Session-Token", token);
        if (body != null) r.Content = J(body);
        return r;
    }
    async Task<JsonElement> Json(HttpMethod m, string url, string? token, object? body = null, HttpStatusCode? expect = HttpStatusCode.OK)
    {
        var resp = await _c.SendAsync(Req(m, url, token, body));
        var text = await resp.Content.ReadAsStringAsync();
        if (expect != null) Assert.True(resp.StatusCode == expect, $"{m} {url} -> {(int)resp.StatusCode}: {text}");
        return text.Length > 0 && text[0] is '{' or '[' ? JsonDocument.Parse(text).RootElement.Clone() : default;
    }
    async Task<string> Login(string u = "admin", string p = "Admin@123")
    {
        var r = await Json(HttpMethod.Post, "/api/auth/login", null, new { username = u, password = p });
        return r.GetProperty("token").GetString()!;
    }
    async Task<byte[]> Bytes(string url, string token, HttpStatusCode expect = HttpStatusCode.OK)
    {
        var resp = await _c.SendAsync(Req(HttpMethod.Get, url, token));
        var b = await resp.Content.ReadAsByteArrayAsync();
        Assert.True(resp.StatusCode == expect, $"{url} -> {(int)resp.StatusCode}: {Encoding.UTF8.GetString(b)}");
        return b;
    }

    [Fact]
    public async Task Login_Fails_With_Persian_Detail()
    {
        var r = await Json(HttpMethod.Post, "/api/auth/login", null, new { username = "admin", password = "bad" }, HttpStatusCode.Unauthorized);
        Assert.Equal("نام کاربری یا رمز عبور صحیح نیست.", r.GetProperty("detail").GetString());
        await Json(HttpMethod.Get, "/api/bootstrap", null, null, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Validation_Errors_Are_422_With_Detail()
    {
        var t = await Login();
        var r = await Json(HttpMethod.Post, "/api/projects", t, new { name = "x", start_date = "1405/05/10", end_date = "1405/05/01" }, HttpStatusCode.UnprocessableEntity);
        Assert.Contains("تاریخ پایان", r.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Full_Flow_Meeting_Action_Dashboard_Exports_Sync()
    {
        var t = await Login();
        var p1 = await Json(HttpMethod.Post, "/api/people", t, new { name = "علی رضایی" });
        var p2 = await Json(HttpMethod.Post, "/api/people", t, new { name = "سارا احمدی" });
        int id1 = p1.GetProperty("id").GetInt32(), id2 = p2.GetProperty("id").GetInt32();
        var proj = await Json(HttpMethod.Post, "/api/projects", t, new { name = "پروژه آزمون", start_date = "۱۴۰۵/۰۱/۰۱", end_date = "1405/12/29", color = "#112233", member_ids = new[] { id1 } });
        Assert.Equal("1405/01/01", proj.GetProperty("start_date").GetString());
        int pid = proj.GetProperty("id").GetInt32();
        await Json(HttpMethod.Post, "/api/sprints", t, new { name = "اسپرینت ۱", start_date = "1405/01/01", end_date = "1405/01/14" });

        var meeting = await Json(HttpMethod.Post, "/api/meetings", t, new
        {
            title = "جلسه اول", meeting_date = "1405/02/10", time = "۱۰:۳۰", project_id = pid, attendee_ids = new[] { id1, id2 },
            previous_items = new[] { new { activity = "کار قبلی", owner_id = id1, status = "در حال انجام", progress = 40, note = "" } },
            actions = new[] { new { subject = "نوشتن گزارش", owner_id = id1, assignee_id = id2, collaborator_ids = new[] { id1 }, due = "1400/01/01", sprint = "اسپرینت ۱", progress = 0, project_id = pid } },
        });
        Assert.Equal("10:30", meeting.GetProperty("time").GetString());
        Assert.Single(meeting.GetProperty("current").EnumerateArray());
        int aid = meeting.GetProperty("current")[0].GetProperty("id").GetInt32();
        Assert.Equal("#112233", meeting.GetProperty("current")[0].GetProperty("project_color").GetString());

        var boot = await Json(HttpMethod.Get, "/api/bootstrap", t);
        Assert.Equal(2, boot.GetProperty("people").GetArrayLength());
        Assert.Single(boot.GetProperty("meetings").EnumerateArray());
        Assert.Equal(15, boot.GetProperty("permissions").GetArrayLength());

        var dash = await Json(HttpMethod.Get, "/api/dashboard", t);
        Assert.Equal(1, dash.GetProperty("actions").GetInt32());
        Assert.Equal(1, dash.GetProperty("overdue").GetInt32());

        var upd = await Json(HttpMethod.Post, $"/api/actions/{aid}/progress", t, new { new_progress = 50, update_date = "1405/02/11", note = "نیمه" });
        Assert.Equal("در حال انجام", upd.GetProperty("status").GetString());
        Assert.Equal(1, upd.GetProperty("history").GetArrayLength());
        var done = await Json(HttpMethod.Post, $"/api/actions/{aid}/progress", t, new { new_progress = 100, update_date = "1405/02/12" });
        Assert.Equal("انجام شده", done.GetProperty("status").GetString());
        var reopened = await Json(HttpMethod.Post, $"/api/actions/{aid}/progress", t, new { new_progress = 20, update_date = "1405/02/13" });
        Assert.Equal("در حال انجام", reopened.GetProperty("status").GetString());

        await Json(HttpMethod.Post, $"/api/actions/{aid}/checklist", t, new { title = "مورد", is_done = false });
        await Json(HttpMethod.Post, $"/api/actions/{aid}/notes", t, new { body = "یادداشت" });
        await Json(HttpMethod.Post, $"/api/actions/{aid}/comments", t, new { body = "نظر" });
        await Json(HttpMethod.Post, $"/api/actions/{aid}/subtasks", t, new { subject = "زیرکار" });
        var list = await Json(HttpMethod.Get, "/api/actions?q=" + Uri.EscapeDataString("گزارش"), t);
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal(1, list[0].GetProperty("checklist").GetArrayLength());
        Assert.Equal(1, list[0].GetProperty("subtasks").GetArrayLength());

        var cal = await Json(HttpMethod.Get, "/api/calendar?month=1405/02", t);
        Assert.Contains(cal.EnumerateArray(), e => e.GetProperty("type").GetString() == "meeting");

        // exports
        Assert.StartsWith("PK", Encoding.ASCII.GetString((await Bytes("/api/export/actions.xlsx", t))[..2]));
        Assert.StartsWith("PK", Encoding.ASCII.GetString((await Bytes("/api/export/meetings.xlsx", t))[..2]));
        Assert.StartsWith("PK", Encoding.ASCII.GetString((await Bytes("/api/export/actions.docx", t))[..2]));
        Assert.StartsWith("PK", Encoding.ASCII.GetString((await Bytes("/api/export/meetings.docx", t))[..2]));
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString((await Bytes("/api/export/actions.pdf", t))[..4]));
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString((await Bytes("/api/export/meetings.pdf?date_from=1405/01/01&date_to=1405/12/29", t))[..4]));
        var tasks = await Bytes("/api/export/tasks.xlsx?sprints=" + Uri.EscapeDataString("اسپرینت ۱"), t);
        var imp = await _c.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/import/tasks") { Headers = { { "X-Session-Token", t } }, Content = new ByteArrayContent(tasks) });
        Assert.Equal(HttpStatusCode.OK, imp.StatusCode);
        await Json(HttpMethod.Get, "/api/export/actions.xlsx?date_by=bad", t, null, HttpStatusCode.BadRequest);

        // sync + backup
        var syncBytes = await Bytes("/api/sync/export", t);
        var doc = JsonDocument.Parse(syncBytes).RootElement;
        Assert.Equal("meeting-manager-sync", doc.GetProperty("format").GetString());
        var resp = await _c.SendAsync(Req(HttpMethod.Post, "/api/sync/import", t));
        resp.Dispose();
        var stats = await Json(HttpMethod.Post, "/api/sync/import", t, doc);
        Assert.Equal(0, stats.GetProperty("inserted").GetInt32());
        Assert.True((await Bytes("/api/backup", t)).Length > 1000);

        // archive / delete
        await Json(HttpMethod.Post, $"/api/actions/{aid}/archive", t);
        Assert.Equal(0, (await Json(HttpMethod.Get, "/api/actions", t)).GetArrayLength());
        Assert.Equal(1, (await Json(HttpMethod.Get, "/api/actions?archived=1", t)).GetArrayLength());
        await Json(HttpMethod.Delete, $"/api/actions/{aid}", t);
        await Json(HttpMethod.Delete, $"/api/projects/{pid}", t);
        await Json(HttpMethod.Delete, $"/api/meetings/{meeting.GetProperty("id").GetInt32()}", t);
        var after = await Json(HttpMethod.Get, "/api/sync/export", t);
        Assert.True(after.GetProperty("tables").GetProperty("actions").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Role_Permissions_Are_Enforced()
    {
        var t = await Login();
        var person = await Json(HttpMethod.Post, "/api/people", t, new { name = "کاربر آزمون" });
        await Json(HttpMethod.Post, "/api/users", t, new { username = "tester", full_name = "کاربر", password = "secret1", role = "user", person_id = person.GetProperty("id").GetInt32() });
        var ut = await Login("tester", "secret1");
        await Json(HttpMethod.Get, "/api/users", ut, null, HttpStatusCode.Forbidden);
        await Json(HttpMethod.Post, "/api/projects", ut, new { name = "ممنوع" }, HttpStatusCode.Forbidden);
        await Json(HttpMethod.Get, "/api/sync/export", ut, null, HttpStatusCode.Forbidden);
        await Json(HttpMethod.Get, "/api/assistant/config", ut, null, HttpStatusCode.Forbidden);
        var mine = await Json(HttpMethod.Post, "/api/actions", ut, new { subject = "کار من", owner_id = person.GetProperty("id").GetInt32() });
        var other = await Json(HttpMethod.Post, "/api/actions", t, new { subject = "کار دیگران" });
        var visible = await Json(HttpMethod.Get, "/api/actions", ut);
        Assert.Equal(1, visible.GetArrayLength());
        Assert.Equal(mine.GetProperty("id").GetInt32(), visible[0].GetProperty("id").GetInt32());
        await Json(HttpMethod.Put, $"/api/actions/{other.GetProperty("id").GetInt32()}", ut, new { subject = "x" }, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Personal_Notes_And_Profile()
    {
        var t = await Login();
        var n = await Json(HttpMethod.Post, "/api/notes", t, new { text = "خرید", category = "شخصی" });
        int id = n.GetProperty("id").GetInt32();
        await Json(HttpMethod.Put, $"/api/notes/{id}", t, new { text = "خرید", category = "شخصی", done = true });
        Assert.True((await Json(HttpMethod.Get, "/api/notes", t))[0].GetProperty("done").GetBoolean());
        await Json(HttpMethod.Put, "/api/auth/profile", t, new { birth_date = "۱۳۷۰/۰۵/۰۵", avatar = "data:image/png;base64,AAAA" });
        var me = await Json(HttpMethod.Get, "/api/auth/me", t);
        Assert.Equal("1370/05/05", me.GetProperty("birth_date").GetString());
        await Json(HttpMethod.Put, "/api/auth/profile", t, new { avatar = "http://x" }, HttpStatusCode.UnprocessableEntity);
        await Json(HttpMethod.Delete, $"/api/notes/{id}", t);
    }
}

public class GoldenTests
{
    static string Golden(string n) => Path.Combine(AppContext.BaseDirectory, "golden", n);

    [Fact]
    public void Jalali_Matches_Python_Conversions()
    {
        var doc = JsonDocument.Parse(File.ReadAllText(Golden("jalali.json"))).RootElement;
        int n = 0;
        foreach (var c in doc.GetProperty("conversions").EnumerateArray())
        {
            var j = c.GetProperty("jalali").GetString()!;
            var g = DateOnly.ParseExact(c.GetProperty("gregorian").GetString()!, "yyyy-MM-dd");
            var parts = j.Split('/').Select(int.Parse).ToArray();
            Assert.Equal(g, Jalali.ToGregorian(parts[0], parts[1], parts[2]));
            Assert.Equal(j, Jalali.Format(Jalali.ToJalali(g).y, Jalali.ToJalali(g).m, Jalali.ToJalali(g).d));
            Assert.Equal(c.GetProperty("ordinal").GetInt32(), g.DayNumber + 1);
            n++;
        }
        Assert.True(n > 10);
    }

    [Fact]
    public void Effective_Status_Matches_Python()
    {
        var doc = JsonDocument.Parse(File.ReadAllText(Golden("status.json"))).RootElement;
        foreach (var c in doc.GetProperty("cases").EnumerateArray())
        {
            var a = new MeetingManager.Data.ActionItem { Status = c.GetProperty("status").GetString()!, Due = c.GetProperty("due").GetString()! };
            Assert.Equal(c.GetProperty("expected").GetString(), Status.Effective(a, c.GetProperty("today").GetString()));
        }
    }

    [Fact]
    public void Password_Hash_Is_Compatible_With_Python_Format()
    {
        // pbkdf2_hmac('sha256', b'Admin@123', b'abc', 210000).hex() computed by the Python app
        var h = Security.HashPw("Admin@123", "abc");
        Assert.Equal("abc$aa82190bf898b2930dcb3cc85d0c5b7b2f4e44508af9d69e90fd3b8ed4e40c5e", h);
        Assert.True(Security.VerifyPw("Admin@123", h));
        Assert.False(Security.VerifyPw("x", h));
    }
}
