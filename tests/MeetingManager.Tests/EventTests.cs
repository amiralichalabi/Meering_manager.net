using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace MeetingManager.Tests;

[Collection("assistant")]
public class EventTests : IDisposable
{
    readonly ApiFixture _f = new();
    readonly HttpClient _c;
    string _t = "";
    public EventTests() { _c = _f.Client; }
    public void Dispose() => _f.Dispose();

    async Task<(HttpStatusCode, JsonElement)> Call(HttpMethod m, string url, object? body = null)
    {
        if (_t == "")
        {
            var lr = await _c.PostAsync("/api/auth/login", new StringContent(JsonSerializer.Serialize(new { username = "admin", password = "Admin@123" }), Encoding.UTF8, "application/json"));
            using var d = JsonDocument.Parse(await lr.Content.ReadAsStringAsync());
            _t = d.RootElement.GetProperty("token").GetString()!;
        }
        var r = new HttpRequestMessage(m, url);
        r.Headers.Add("X-Session-Token", _t);
        if (body != null) r.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var resp = await _c.SendAsync(r);
        var text = await resp.Content.ReadAsStringAsync();
        return (resp.StatusCode, text.Length > 0 && text[0] is '{' or '[' ? JsonDocument.Parse(text).RootElement.Clone() : default);
    }

    async Task<int> Person(string name) => (await Call(HttpMethod.Post, "/api/people", new { name })).Item2.GetProperty("id").GetInt32();

    Task<(HttpStatusCode, JsonElement)> Event(string title = "مصاحبه", string date = "1405/09/10", string start = "10:00", string end = "11:00", int[]? people = null, string kind = "مصاحبه")
        => Call(HttpMethod.Post, "/api/events", new { title, kind, event_date = date, start_time = start, end_time = end, attendee_ids = people ?? Array.Empty<int>() });

    [Fact]
    public async Task Crud_And_Validation()
    {
        var (st, ev) = await Event();
        Assert.Equal(HttpStatusCode.OK, st);
        var id = ev.GetProperty("id").GetInt32();
        Assert.Equal(0, ev.GetProperty("conflicts").GetArrayLength());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Event(kind: "نامعتبر")).Item1);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Event(start: "11:00", end: "10:00")).Item1);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Event(start: "", end: "10:00")).Item1);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Event(title: "  ")).Item1);
        var list = (await Call(HttpMethod.Get, "/api/events")).Item2;
        Assert.Equal(id, list[0].GetProperty("id").GetInt32());
        var (_, up) = await Call(HttpMethod.Put, $"/api/events/{id}", new { title = "جدید", kind = "تحویل", event_date = "۱۴۰۵/۰۹/۱۱" });
        Assert.Equal("جدید", up.GetProperty("title").GetString());
        Assert.Equal("1405/09/11", up.GetProperty("date").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Call(HttpMethod.Delete, $"/api/events/{id}")).Item1);
        Assert.Equal(0, (await Call(HttpMethod.Get, "/api/events")).Item2.GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await Call(HttpMethod.Delete, $"/api/events/{id}")).Item1);
    }

    [Fact]
    public async Task Conflicts_With_Meetings_And_Events()
    {
        int ali = await Person("علی"), sara = await Person("سارا"), reza = await Person("رضا");
        await Call(HttpMethod.Post, "/api/meetings", new { title = "جلسه تیم", meeting_date = "1405/09/10", time = "10:30", attendee_ids = new[] { ali } });
        var (_, r) = await Event(people: new[] { ali, sara });
        var c = r.GetProperty("conflicts");
        Assert.Equal(1, c.GetArrayLength());
        Assert.Equal("meeting", c[0].GetProperty("type").GetString());
        Assert.Equal("علی", c[0].GetProperty("people")[0].GetString());
        Assert.Equal(0, (await Event(people: new[] { reza })).Item2.GetProperty("conflicts").GetArrayLength());
        Assert.Equal(0, (await Event(date: "1405/09/11", people: new[] { ali })).Item2.GetProperty("conflicts").GetArrayLength());
        Assert.Equal(0, (await Event(start: "11:30", end: "12:30", people: new[] { ali })).Item2.GetProperty("conflicts").GetArrayLength());
        Assert.Equal(0, (await Event(start: "", end: "", people: new[] { ali })).Item2.GetProperty("conflicts").GetArrayLength());
        var first = (await Event("اول", start: "14:00", end: "15:00", people: new[] { ali })).Item2.GetProperty("id").GetInt32();
        var second = (await Event("دوم", start: "14:30", end: "15:30", people: new[] { ali })).Item2;
        Assert.Equal(first, second.GetProperty("conflicts")[0].GetProperty("id").GetInt32());
        var (_, chk) = await Call(HttpMethod.Post, "/api/events/check", new { id = first, title = "اول", event_date = "1405/09/10", start_time = "14:00", end_time = "15:00", attendee_ids = new[] { ali } });
        Assert.Equal(second.GetProperty("id").GetInt32(), chk.GetProperty("conflicts")[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Calendar_Dashboard_And_Auth()
    {
        var id = (await Event()).Item2.GetProperty("id").GetInt32();
        var (_, cal) = await Call(HttpMethod.Get, "/api/calendar?month=1405/09");
        Assert.Contains(cal.EnumerateArray(), e => e.GetProperty("type").GetString() == "event" && e.GetProperty("id").GetInt32() == id);
        var (_, dash) = await Call(HttpMethod.Get, "/api/dashboard");
        Assert.True(dash.TryGetProperty("upcoming_events", out _));
        var anon = await _c.GetAsync("/api/events");
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
    }
}
