using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MeetingManager.Data;

namespace MeetingManager.Core;

public class ApiException : Exception
{
    public int Status { get; }
    public ApiException(int status, string detail) : base(detail) { Status = status; }
}

public static class Security
{
    // Same format as Python's hash_pw: "<salt>$<pbkdf2-sha256 hex>", salt is a hex string used as its own ASCII bytes, 210000 rounds.
    public static string HashPw(string p, string? salt = null)
    {
        salt ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var d = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(p), Encoding.UTF8.GetBytes(salt), 210000, HashAlgorithmName.SHA256, 32);
        return $"{salt}${Convert.ToHexString(d).ToLowerInvariant()}";
    }
    public static bool VerifyPw(string p, string h)
    {
        try
        {
            var i = h.IndexOf('$'); if (i < 0) return false;
            var salt = h[..i];
            var calc = HashPw(p, salt);
            return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(calc), Encoding.ASCII.GetBytes(h));
        }
        catch { return false; }
    }
    public static string TokenHash(string t) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(t))).ToLowerInvariant();
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static readonly Dictionary<string, (string Title, string Module)> Perms = new()
    {
        ["dashboard.view"] = ("مشاهده داشبورد", "داشبورد"), ["meetings.view"] = ("مشاهده جلسات", "جلسات"), ["meetings.manage"] = ("مدیریت جلسات", "جلسات"), ["meetings.export"] = ("خروجی جلسات", "جلسات"),
        ["actions.view"] = ("مشاهده اقدامات", "اقدامات"), ["actions.manage"] = ("مدیریت اقدامات", "اقدامات"), ["actions.export"] = ("خروجی اقدامات", "اقدامات"),
        ["projects.view"] = ("مشاهده پروژه‌ها", "پروژه‌ها"), ["projects.manage"] = ("مدیریت پروژه‌ها", "پروژه‌ها"), ["people.manage"] = ("مدیریت افراد", "افراد و تیم‌ها"), ["teams.manage"] = ("مدیریت تیم‌ها", "افراد و تیم‌ها"), ["locations.manage"] = ("مدیریت مکان‌ها", "افراد و تیم‌ها"),
        ["users.manage"] = ("مدیریت کاربران", "دسترسی‌ها و مجوزها"), ["permissions.manage"] = ("مدیریت مجوزها", "دسترسی‌ها و مجوزها"), ["backup.manage"] = ("پشتیبان‌گیری و بازیابی", "خروجی و پشتیبان‌گیری"),
    };
    public static readonly Dictionary<string, HashSet<string>> DefaultRole = new()
    {
        ["admin"] = new(Perms.Keys),
        ["manager"] = new() { "dashboard.view", "meetings.view", "meetings.manage", "meetings.export", "actions.view", "actions.manage", "actions.export", "projects.view", "projects.manage", "people.manage", "teams.manage", "locations.manage", "users.manage" },
        ["user"] = new() { "dashboard.view", "meetings.view", "actions.view", "actions.manage", "actions.export", "projects.view" },
    };

    public static User? FromToken(AppDb db, string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var h = TokenHash(token);
        var us = db.Sessions.Include(s => s.User).FirstOrDefault(s => s.TokenHash == h);
        if (us == null || us.ExpiresAt < Clock.Now() || us.User == null || !us.User.IsActive) return null;
        return us.User;
    }

    public static string? TokenOf(HttpRequest r)
    {
        if (r.Headers.TryGetValue("X-Session-Token", out var t) && t.Count > 0 && t[0] != "") return t[0];
        var a = r.Headers.Authorization.ToString();
        return a.StartsWith("bearer ", StringComparison.OrdinalIgnoreCase) ? a[7..] : null;
    }

    public static User Current(HttpContext c, AppDb db) =>
        FromToken(db, TokenOf(c.Request)) ?? throw new ApiException(401, "نیاز به ورود به سامانه است.");

    public static bool Has(AppDb db, User u, string code)
    {
        if (u.Role == "admin") return true;
        return db.RolePermissions.Any(rp => rp.Role == u.Role && rp.Enabled && rp.Permission!.Code == code);
    }
    public static User Require(HttpContext c, AppDb db, string code)
    {
        var u = Current(c, db);
        if (!Has(db, u, code)) throw new ApiException(403, "مجوز انجام این عملیات را ندارید.");
        return u;
    }
    public static User RequireAdmin(HttpContext c, AppDb db)
    {
        var u = Current(c, db);
        if (u.Role != "admin") throw new ApiException(403, "دسترسی مدیر سامانه لازم است.");
        return u;
    }

    /// <summary>Admins and managers see every action; others only those they own, are assigned to or collaborate on.</summary>
    public static bool ActionAllowed(AppDb db, User u, ActionItem a)
    {
        if (u.Role is "admin" or "manager") return true;
        if (!Has(db, u, "actions.view")) return false;
        if (u.PersonId == null) return false;
        return a.OwnerId == u.PersonId || a.AssigneeId == u.PersonId || a.Collaborators.Any(p => p.Id == u.PersonId);
    }
}
