using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MeetingManager.Core;
using MeetingManager.Data;
using Xunit;

namespace MeetingManager.Tests;

/// <summary>One host (and one temp data dir) per class; the env var is process-wide so the collection never runs in parallel.</summary>
public class AssistantFixture : IDisposable
{
    readonly string? _oldEnv;
    public string Dir { get; }
    public WebApplicationFactory<Program> Factory { get; }
    public HttpClient Client { get; }

    public AssistantFixture()
    {
        _oldEnv = Environment.GetEnvironmentVariable("MEETING_MANAGER_DATA_DIR");
        Dir = Path.Combine(Path.GetTempPath(), "mm_asst_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Dir);
        Environment.SetEnvironmentVariable("MEETING_MANAGER_DATA_DIR", Dir);
        Factory = new WebApplicationFactory<Program>();
        Client = Factory.CreateClient();
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        Environment.SetEnvironmentVariable("MEETING_MANAGER_DATA_DIR", _oldEnv);
    }
}

[CollectionDefinition("assistant", DisableParallelization = true)]
public class AssistantCollection { }

[Collection("assistant")]
public class AssistantTests : IClassFixture<AssistantFixture>
{
    readonly AssistantFixture _f;
    public AssistantTests(AssistantFixture f)
    {
        _f = f;
        ResetConfig();
    }

    static void ResetConfig()
    {
        Assistant.ChatOverride = null;
        Environment.SetEnvironmentVariable("MM_LLM_URL", null);
        Environment.SetEnvironmentVariable("MM_LLM_MODEL", null);
        Environment.SetEnvironmentVariable("MM_LLM_TIMEOUT", null);
        try { var p = Path.Combine(Paths.DataDir, "llm.json"); if (File.Exists(p)) File.Delete(p); } catch { }
    }

    async Task<string> Login(string username, string password)
    {
        var r = await _f.Client.PostAsJsonAsync("/api/auth/login", new { username, password });
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.OK, text);
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.GetProperty("token").GetString()!;
    }

    string CreateUser(string username, string role, int? personId)
    {
        using var scope = _f.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        if (!db.Users.Any(x => x.Username == username))
        {
            db.Users.Add(new User { Username = username, FullName = username, PasswordHash = Security.HashPw("Asst@1234"), Role = role, IsActive = true, PersonId = personId });
            db.SaveChanges();
        }
        return username;
    }

    async Task<HttpResponseMessage> Send(HttpMethod m, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Add("X-Session-Token", token);
        if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return await _f.Client.SendAsync(req);
    }

    Task<string> AdminToken() => Login("admin", "Admin@123");

    [Fact]
    public async Task Context_Is_Role_Filtered()
    {
        int myId;
        using (var scope = _f.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var me = new Person { Name = "نفر الف" };
            var other = new Person { Name = "نفر ب" };
            db.People.AddRange(me, other);
            db.SaveChanges();
            db.Actions.AddRange(new ActionItem { Subject = "کار-مال-من", OwnerId = me.Id }, new ActionItem { Subject = "کار-مال-دیگری", OwnerId = other.Id });
            db.SaveChanges();
            myId = me.Id;
        }
        string seen = "";
        Assistant.ChatOverride = (q, ctx, hist) => { seen = ctx; return ("پاسخ آزمایشی", "fake-model"); };
        try
        {
            var admin = await AdminToken();
            var r = await Send(HttpMethod.Post, "/api/assistant", admin, new { question = "سلام" });
            var text = await r.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            using (var doc = JsonDocument.Parse(text))
            {
                Assert.Equal("پاسخ آزمایشی", doc.RootElement.GetProperty("answer").GetString());
                Assert.Equal("fake-model", doc.RootElement.GetProperty("model").GetString());
            }
            Assert.Contains("کار-مال-من", seen);
            Assert.Contains("کار-مال-دیگری", seen);

            CreateUser("asst_member", "user", myId);
            var member = await Login("asst_member", "Asst@1234");
            var r2 = await Send(HttpMethod.Post, "/api/assistant", member, new { question = "چه کارهایی دارم؟" });
            Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
            Assert.Contains("کار-مال-من", seen);
            Assert.DoesNotContain("کار-مال-دیگری", seen);
        }
        finally { Assistant.ChatOverride = null; }
    }

    [Fact]
    public async Task Unreachable_Model_Gives_503()
    {
        Environment.SetEnvironmentVariable("MM_LLM_URL", "http://127.0.0.1:9");
        Environment.SetEnvironmentVariable("MM_LLM_TIMEOUT", "2");
        try
        {
            var admin = await AdminToken();
            var r = await Send(HttpMethod.Post, "/api/assistant", admin, new { question = "سلام" });
            var text = await r.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
            Assert.Contains("مدل محلی", text);
            var s = await Send(HttpMethod.Get, "/api/assistant/status", admin);
            using var doc = JsonDocument.Parse(await s.Content.ReadAsStringAsync());
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        }
        finally
        {
            Environment.SetEnvironmentVariable("MM_LLM_URL", null);
            Environment.SetEnvironmentVariable("MM_LLM_TIMEOUT", null);
        }
    }

    [Fact]
    public async Task Requires_Login()
    {
        var r = await _f.Client.PostAsJsonAsync("/api/assistant", new { question = "سلام" });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public void Think_Tags_Are_Stripped()
    {
        Assert.Equal("سلام", Assistant.Clean("<think>x</think>سلام"));
        Assert.Equal("a", Assistant.Clean("<think>\nline1\nline2\n</think>\n a "));
        Assert.Equal("", Assistant.Clean(null));
    }

    [Fact]
    public async Task Connection_Settings_Persist_And_Are_Admin_Only()
    {
        CreateUser("asst_user2", "user", null);
        var admin = await AdminToken();
        var user = await Login("asst_user2", "Asst@1234");

        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Put, "/api/assistant/config", user, new { url = "http://h:1" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/assistant/config", user)).StatusCode);

        var r = await Send(HttpMethod.Put, "/api/assistant/config", admin, new { url = "http://192.168.0.116:1234/v1/", model = "qwen3-next-80b-a3b-instruct", timeout = "400" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using (var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync()))
            Assert.Equal("http://192.168.0.116:1234", doc.RootElement.GetProperty("url").GetString());
        var cfg = Assistant.LoadConfig();
        Assert.Equal("qwen3-next-80b-a3b-instruct", cfg.Model);
        Assert.Equal("400", cfg.Timeout);
        Assert.Equal("http://192.168.0.116:1234", Assistant.Base());

        var g = await Send(HttpMethod.Get, "/api/assistant/config", admin);
        Assert.Equal(HttpStatusCode.OK, g.StatusCode);
        using (var doc = JsonDocument.Parse(await g.Content.ReadAsStringAsync()))
            Assert.Equal("qwen3-next-80b-a3b-instruct", doc.RootElement.GetProperty("model").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put, "/api/assistant/config", admin, new { url = "not a url" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put, "/api/assistant/config", admin, new { url = "http://h:1", timeout = "abc" })).StatusCode);
    }

    [Fact]
    public async Task Saved_File_Wins_Over_Env()
    {
        Environment.SetEnvironmentVariable("MM_LLM_URL", "http://127.0.0.1:9");
        try
        {
            Assert.Equal("http://127.0.0.1:9", Assistant.Base());
            Assistant.SaveConfig("http://10.0.0.5:1234", "", "");
            Assert.Equal("http://10.0.0.5:1234", Assistant.Base());
        }
        finally { Environment.SetEnvironmentVariable("MM_LLM_URL", null); }
        await Task.CompletedTask;
    }
}
