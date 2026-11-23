using System.Text.Json;
using System.Text.Json.Nodes;
using MeetingManager.Api;
using MeetingManager.Core;
using MeetingManager.Data;
using Xunit;

namespace MeetingManager.Tests;

public class ReportGoldenTests
{
    static string Str(JsonNode? n) => n == null ? "" : n.GetValue<string>();

    [Fact]
    public void Actions_Report_Model_Matches_Python()
    {
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "golden", "reports.json")))!;
        var today = root["today"]!.GetValue<string>();
        int n = 0;
        foreach (var c in root["actions"]!.AsArray())
        {
            var input = c!["input"]!;
            var acts = new List<ActionItem>();
            foreach (var a in input["actions"]!.AsArray())
            {
                var x = new ActionItem
                {
                    Id = a!["id"]!.GetValue<int>(), Subject = Str(a["subject"]), Decision = Str(a["decision"]), Due = Str(a["due"]), Priority = Str(a["priority"]),
                    Progress = a["progress"]?.GetValue<int>() ?? 0, Status = a["status"] == null ? "جدید" : Str(a["status"]),
                };
                if (Str(a["project"]) != "") x.Project = new Project { Name = Str(a["project"]) };
                if (Str(a["meeting"]) != "") x.Meeting = new Meeting { Title = Str(a["meeting"]) };
                if (Str(a["owner"]) != "") x.OwnerPerson = new Person { Name = Str(a["owner"]) };
                if (Str(a["assignee"]) != "") x.AssigneePerson = new Person { Name = Str(a["assignee"]) };
                if (a["collaborators"] != null) foreach (var p in a["collaborators"]!.AsArray()) x.Collaborators.Add(new Person { Name = p!.GetValue<string>() });
                acts.Add(x);
            }
            var model = ReportModel.Actions(acts, Str(input["user"]), today);
            var actual = JsonNode.Parse(JsonSerializer.Serialize(model, Http.Json))!;
            Assert.True(JsonNode.DeepEquals(c["expected"], actual), $"{Str(input["name"])}\nexpected {c["expected"]!.ToJsonString()}\nactual   {actual.ToJsonString()}");
            n++;
        }
        Assert.True(n >= 3);
    }
}

[Collection("assistant")]
public class StaticFilesTests : IDisposable
{
    readonly string? _old = Environment.GetEnvironmentVariable("MEETING_MANAGER_DATA_DIR");
    readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _f;
    public StaticFilesTests()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mm_st_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("MEETING_MANAGER_DATA_DIR", dir);
        _f = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>();
    }
    public void Dispose() { _f.Dispose(); Environment.SetEnvironmentVariable("MEETING_MANAGER_DATA_DIR", _old); }

    [Fact]
    public async Task Index_And_Assets_Are_Served()
    {
        var c = _f.CreateClient();
        var html = await c.GetStringAsync("/");
        Assert.Contains("<html lang=\"fa\"", html);
        foreach (var path in new[] { "/static/assets/js/app.js", "/static/assets/css/app.css", "/static/assets/js/jalali.js" })
            Assert.Equal(System.Net.HttpStatusCode.OK, (await c.GetAsync(path)).StatusCode);
        var health = await c.GetStringAsync("/api/health");
        Assert.Contains("\"ok\":true", health);
    }
}
