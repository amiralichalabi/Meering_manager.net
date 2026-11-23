using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using MeetingManager.Core;
using MeetingManager.Data;

namespace MeetingManager.Api;

public static class Http
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static async Task<T> Body<T>(HttpContext c) where T : new()
    {
        try
        {
            var x = await JsonSerializer.DeserializeAsync<T>(c.Request.Body, Json);
            return x ?? throw new ApiException(422, "اطلاعات واردشده معتبر نیست.");
        }
        catch (JsonException) { throw new ApiException(422, "اطلاعات واردشده معتبر نیست."); }
    }

    public static IResult Ok(object value, int status = 200) => Results.Text(JsonSerializer.Serialize(value, Json), "application/json; charset=utf-8", System.Text.Encoding.UTF8, status);
    public static IResult Done() => Ok(new { ok = true });
    public static IResult File(byte[] data, string contentType, string filename) => Results.File(data, contentType, filename);

    public static void Audit(AppDb db, User? u, string entity, int? id, string op, string detail = "")
    {
        db.Audit.Add(new AuditLog { UserId = u?.Id, Entity = entity, EntityId = id, Operation = op, Detail = detail });
        db.SaveChanges();
    }

    public static List<int>? IdList(string? ids) =>
        string.IsNullOrWhiteSpace(ids) ? null : ids.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0 && x.All(char.IsAsciiDigit)).Select(int.Parse).ToList();

    public static Person PersonOrThrow(AppDb db, int? id)
    {
        if (id == null) return null!;
        return db.People.Find(id.Value) ?? throw new ApiException(400, "فرد پیدا نشد.");
    }
    public static Project? ProjectOrThrow(AppDb db, int? id)
    {
        if (id == null) return null;
        return db.Projects.Find(id.Value) ?? throw new ApiException(400, "پروژه پیدا نشد.");
    }
    public static List<Person> People(AppDb db, IEnumerable<int> ids) => ids.Select(i => PersonOrThrow(db, i)).ToList();
}
