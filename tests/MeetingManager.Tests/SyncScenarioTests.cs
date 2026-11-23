using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MeetingManager.Tests;

/// <summary>The merge scenarios pinned by the Python app (golden/sync.json): local file + remote file -> stats + resulting file.</summary>
[Collection("assistant")]
public class SyncScenarioTests : IDisposable
{
    readonly string? _old = Environment.GetEnvironmentVariable("MEETING_MANAGER_DATA_DIR");
    readonly WebApplicationFactory<Program> _f;
    readonly HttpClient _c;

    public SyncScenarioTests()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mm_sc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("MEETING_MANAGER_DATA_DIR", dir);
        _f = new WebApplicationFactory<Program>();
        _c = _f.CreateClient();
    }
    public void Dispose() { _c.Dispose(); _f.Dispose(); Environment.SetEnvironmentVariable("MEETING_MANAGER_DATA_DIR", _old); }

    async Task<string> Send(HttpMethod m, string url, string t, string? body = null)
    {
        var req = new HttpRequestMessage(m, url); req.Headers.Add("X-Session-Token", t);
        if (body != null) req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        var r = await _c.SendAsync(req);
        var s = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{url}: {(int)r.StatusCode} {s}");
        return s;
    }

    static JsonNode Pin(JsonNode n)
    {
        var o = n.AsObject();
        foreach (var rows in o["tables"]!.AsObject()) foreach (var r in rows.Value!.AsArray())
            if (r!["deleted_at"] != null) { r["updated_at"] = 1; r["deleted_at"] = 1; }
        return o["tables"]!;
    }

    public static IEnumerable<object[]> Indexes() => Enumerable.Range(0, 5).Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(Indexes))]
    public async Task Scenario_Matches_Python(int i)
    {
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "golden", "sync.json")))!;
        var sc = root["scenarios"]![i]!;
        var login = await _c.PostAsync("/api/auth/login", new StringContent("{\"username\":\"admin\",\"password\":\"Admin@123\"}", Encoding.UTF8, "application/json"));
        var t = JsonDocument.Parse(await login.Content.ReadAsStringAsync()).RootElement.GetProperty("token").GetString()!;
        await Send(HttpMethod.Post, "/api/sync/import", t, sc["local"]!.ToJsonString());
        var stats = JsonNode.Parse(await Send(HttpMethod.Post, "/api/sync/import", t, sc["remote"]!.ToJsonString()))!;
        var name = sc["name"]!.GetValue<string>();
        foreach (var k in new[] { "inserted", "updated", "deleted", "skipped", "orphaned" })
            Assert.True(sc["stats"]![k]!.GetValue<int>() == stats[k]!.GetValue<int>(), $"{name}: stats.{k} expected {sc["stats"]![k]} got {stats[k]}");
        var actual = Pin(JsonNode.Parse(await Send(HttpMethod.Get, "/api/sync/export", t))!);
        var expected = Pin(sc["result"]!);
        foreach (var table in expected.AsObject())
        {
            var exp = table.Value!.AsArray().ToDictionary(r => r!["uuid"]!.GetValue<string>(), r => r!);
            var act = actual[table.Key]!.AsArray().ToDictionary(r => r!["uuid"]!.GetValue<string>(), r => r!);
            Assert.True(exp.Keys.OrderBy(x => x).SequenceEqual(act.Keys.OrderBy(x => x)), $"{name}/{table.Key}: uuids differ\nexpected {string.Join(",", exp.Keys.OrderBy(x => x))}\nactual   {string.Join(",", act.Keys.OrderBy(x => x))}");
            foreach (var (k, v) in exp)
                Assert.True(JsonNode.DeepEquals(v, act[k]), $"{name}/{table.Key}/{k}\nexpected {v.ToJsonString()}\nactual   {act[k]!.ToJsonString()}");
        }
    }
}
