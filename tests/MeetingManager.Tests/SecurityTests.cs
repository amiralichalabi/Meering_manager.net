using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace MeetingManager.Tests;

[Collection("assistant")]
public class SecurityTests : IDisposable
{
    readonly ApiFixture _f = new();
    HttpClient C => _f.Client;
    public void Dispose() => _f.Dispose();

    static StringContent J(object o) => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");
    async Task<string> Login(string p = "Admin@123")
    {
        var r = await C.PostAsync("/api/auth/login", J(new { username = "admin", password = p }));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("token").GetString()!;
    }
    async Task<string> Ticket(string t, string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/ticket") { Content = J(new { url }) };
        req.Headers.Add("X-Session-Token", t);
        var r = await C.SendAsync(req);
        if (r.StatusCode != HttpStatusCode.OK) return "";
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("ticket").GetString()!;
    }

    [Fact]
    public async Task Ticket_Authorizes_Exactly_One_Download_Url_Without_Header()
    {
        var t = await Login();
        var url = "/api/export/actions.pdf";
        var tk = await Ticket(t, url);
        Assert.NotEqual("", tk);
        var ok = await C.GetAsync($"{url}?ticket={tk}");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("application/pdf", ok.Content.Headers.ContentType!.MediaType);
        Assert.NotNull(ok.Content.Headers.ContentDisposition);
        var inline = await C.GetAsync($"{url}?inline=1&ticket={await Ticket(t, url + "?inline=1")}");
        Assert.Equal(HttpStatusCode.OK, inline.StatusCode);
        Assert.Null(inline.Content.Headers.ContentDisposition);
        // the ticket is bound to its URL, and does not open other endpoints
        Assert.Equal(HttpStatusCode.Unauthorized, (await C.GetAsync($"/api/export/meetings.pdf?ticket={tk}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await C.GetAsync($"/api/users?ticket={tk}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await C.GetAsync($"{url}?ticket=deadbeef")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await C.GetAsync(url)).StatusCode);
        // only downloads can be ticketed
        Assert.Equal("", await Ticket(t, "/api/users"));
        Assert.Equal("", await Ticket(t, "/api/export/../users"));
    }

    [Fact]
    public async Task Security_Headers_Are_Set()
    {
        var r = await C.GetAsync("/");
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("default-src 'self'", r.Headers.GetValues("Content-Security-Policy").Single());
        var api = await C.GetAsync("/api/health");
        Assert.Contains("no-store", api.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Repeated_Failed_Logins_Are_Throttled()
    {
        HttpStatusCode last = 0;
        for (int i = 0; i < 9; i++) last = (await C.PostAsync("/api/auth/login", J(new { username = "throttled", password = "x" + i }))).StatusCode;
        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }

    [Fact]
    public async Task Default_Admin_Password_Is_Flagged_Until_Changed()
    {
        var r = await C.PostAsync("/api/auth/login", J(new { username = "admin", password = "Admin@123" }));
        var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.True(doc.GetProperty("default_password").GetBoolean());
        var t = doc.GetProperty("token").GetString()!;
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/password") { Content = J(new { current_password = "Admin@123", new_password = "Str0ng-pass!" }) };
        req.Headers.Add("X-Session-Token", t);
        Assert.Equal(HttpStatusCode.OK, (await C.SendAsync(req)).StatusCode);
        var again = await C.PostAsync("/api/auth/login", J(new { username = "admin", password = "Str0ng-pass!" }));
        Assert.False(JsonDocument.Parse(await again.Content.ReadAsStringAsync()).RootElement.GetProperty("default_password").GetBoolean());
    }

    [Fact]
    public async Task Avatar_Must_Be_A_Raster_Data_Url()
    {
        var t = await Login();
        foreach (var bad in new[] { "data:image/svg+xml;base64,AAAA", "data:image/png;base64,AA) url(http://x", "javascript:alert(1)" })
        {
            var req = new HttpRequestMessage(HttpMethod.Put, "/api/auth/profile") { Content = J(new { avatar = bad }) };
            req.Headers.Add("X-Session-Token", t);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await C.SendAsync(req)).StatusCode);
        }
    }
}
