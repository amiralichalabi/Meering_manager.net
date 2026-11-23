using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MeetingManager.Tests;

/// <summary>A database written by an earlier release (golden/legacy_made.db) must open unchanged and export the same sync file.</summary>
[Collection("assistant")]
public class LegacyDatabaseTests : IDisposable
{
    readonly string? _old = Environment.GetEnvironmentVariable("MEETING_MANAGER_DATA_DIR");
    readonly string _dir = Path.Combine(Path.GetTempPath(), "mm_py_" + Guid.NewGuid().ToString("N"));
    readonly WebApplicationFactory<Program> _f;
    readonly HttpClient _c;

    public LegacyDatabaseTests()
    {
        Directory.CreateDirectory(_dir);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "golden", "legacy_made.db"), Path.Combine(_dir, "meeting_manager.db"));
        Environment.SetEnvironmentVariable("MEETING_MANAGER_DATA_DIR", _dir);
        _f = new WebApplicationFactory<Program>();
        _c = _f.CreateClient();
    }
    public void Dispose() { _c.Dispose(); _f.Dispose(); Environment.SetEnvironmentVariable("MEETING_MANAGER_DATA_DIR", _old); }

    async Task<string> Token()
    {
        var r = await _c.PostAsync("/api/auth/login", new StringContent("{\"username\":\"admin\",\"password\":\"Admin@123\"}", Encoding.UTF8, "application/json"));
        r.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("token").GetString()!;
    }
    async Task<string> Get(string url, string t)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url); req.Headers.Add("X-Session-Token", t);
        var r = await _c.SendAsync(req);
        var s = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{url}: {(int)r.StatusCode} {s}");
        return s;
    }

    static JsonNode Norm(JsonNode? n)
    {
        // Tombstones carry the wall clock; pin them like the generator does. Sort keys for a stable compare.
        var o = n!.AsObject();
        foreach (var rows in o["tables"]!.AsObject()) foreach (var r in rows.Value!.AsArray())
            if (r!["deleted_at"] != null) { r["updated_at"] = 1; r["deleted_at"] = 1; }
        o.Remove("device"); o.Remove("exported_at");
        return o;
    }

    [Fact]
    public async Task Legacy_Database_Opens_And_Exports_Same_Sync_File()
    {
        var t = await Token();
        var actual = Norm(JsonNode.Parse(await Get("/api/sync/export", t)));
        var expected = Norm(JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "golden", "legacy_made.sync.json"))));
        foreach (var table in expected["tables"]!.AsObject())
        {
            var exp = table.Value!.AsArray().ToDictionary(r => r!["uuid"]!.GetValue<string>(), r => r!.ToJsonString());
            var act = actual["tables"]![table.Key]!.AsArray().ToDictionary(r => r!["uuid"]!.GetValue<string>(), r => r!.ToJsonString());
            Assert.Equal(exp.Keys.OrderBy(x => x), act.Keys.OrderBy(x => x));
            foreach (var (k, v) in exp)
                Assert.True(JsonNode.DeepEquals(JsonNode.Parse(v), JsonNode.Parse(act[k])), $"{table.Key}/{k}\nexpected {v}\nactual   {act[k]}");
        }
    }

    [Fact]
    public async Task Legacy_Database_Works_Through_The_Api()
    {
        var t = await Token();
        var boot = JsonDocument.Parse(await Get("/api/bootstrap", t)).RootElement;
        Assert.Equal(2, boot.GetProperty("meetings").GetArrayLength());
        Assert.True(boot.GetProperty("people").GetArrayLength() >= 2);
        foreach (var url in new[] { "/api/dashboard", "/api/actions", "/api/teams", "/api/projects", "/api/sprints", "/api/calendar", "/api/export/actions.xlsx", "/api/export/meetings.docx", "/api/export/meetings.pdf" })
            await Get(url, t);
    }
}
