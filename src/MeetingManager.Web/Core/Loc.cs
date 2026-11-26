using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MeetingManager.Core;

/// <summary>Persian -> English for server-made text (reports, error messages). The program is written in Persian;
/// English is applied per request (query "lang=en" or header "X-Lang: en") from Core/i18n/en.json, the same file
/// the web page uses. Phrases are replaced whole-word, longest first, so interpolated messages work too.</summary>
public static class Loc
{
    static readonly AsyncLocal<bool> en = new();
    public static bool En { get => en.Value; set => en.Value = value; }

    sealed record Dict(Dictionary<string, string> Map, Regex? Re, string Json);

    static readonly Lazy<Dict> Data = new(() =>
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("i18n.en.json")
            ?? throw new InvalidOperationException("en.json resource is missing");
        using var r = new StreamReader(s);
        var json = r.ReadToEnd();
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        var keys = map.Keys.Where(k => k != "" && !k.Contains('{') && !k.Contains('}')).OrderByDescending(k => k.Length).ToList();
        const string L = "\\u0600-\\u06FF\\u200c";
        var re = keys.Count == 0 ? null : new Regex($"(?<![{L}])(?:{string.Join("|", keys.Select(Regex.Escape))})(?![{L}])", RegexOptions.Compiled);
        return new Dict(map, re, json);
    });

    static readonly Regex Persian = new("[؀-ۿ]", RegexOptions.Compiled);

    /// <summary>The raw dictionary, for the web page.</summary>
    public static string Json => Data.Value.Json;

    public static string Sep => En ? ", " : "، ";

    public static string Digits(string s) =>
        new string(s.Select(c => c >= '۰' && c <= '۹' ? (char)('0' + (c - '۰')) : c >= '٠' && c <= '٩' ? (char)('0' + (c - '٠')) : c).ToArray());

    /// <summary>Translates when the request asked for English; otherwise returns the text unchanged.</summary>
    public static string T(string? s)
    {
        if (s == null) return "";
        if (!En || !Persian.IsMatch(s)) return s;
        var d = Data.Value;
        if (d.Map.TryGetValue(s, out var exact)) return exact;
        return Digits(d.Re == null ? s : d.Re.Replace(s, m => d.Map[m.Value]));
    }
}
