using Microsoft.EntityFrameworkCore;
using MeetingManager.Core;
using MeetingManager.Data;

namespace MeetingManager.Api;

public static class AuthApi
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/auth/login", async (HttpContext c, AppDb db) =>
        {
            var x = await Http.Body<LoginIn>(c);
            var name = x.Username.Trim();
            var u = db.Users.FirstOrDefault(z => z.Username == name);
            if (u == null || !Security.VerifyPw(x.Password, u.PasswordHash) || !u.IsActive) throw new ApiException(401, "نام کاربری یا رمز عبور صحیح نیست.");
            var tok = Security.NewToken();
            db.Sessions.Add(new UserSession { TokenHash = Security.TokenHash(tok), UserId = u.Id, ExpiresAt = Clock.Now().AddDays(7) });
            u.LastLogin = Clock.Now();
            db.SaveChanges();
            return Http.Ok(new { token = tok, user = Views.UserMe(u) });
        });
        app.MapPost("/api/auth/logout", (HttpContext c, AppDb db) =>
        {
            var tok = Security.TokenOf(c.Request);
            if (tok != null)
            {
                var h = Security.TokenHash(tok);
                db.Sessions.RemoveRange(db.Sessions.Where(s => s.TokenHash == h).ToList());
                db.SaveChanges();
            }
            return Http.Done();
        });
        app.MapGet("/api/auth/me", (HttpContext c, AppDb db) => Http.Ok(Views.UserMe(Security.Current(c, db))));
        app.MapPost("/api/auth/password", async (HttpContext c, AppDb db) =>
        {
            var u = Security.Current(c, db);
            var x = (await Http.Body<PasswordIn>(c)).Validate();
            if (!Security.VerifyPw(x.CurrentPassword, u.PasswordHash)) throw new ApiException(400, "رمز عبور فعلی صحیح نیست.");
            u.PasswordHash = Security.HashPw(x.NewPassword);
            db.SaveChanges();
            return Http.Done();
        });
        app.MapPut("/api/auth/profile", async (HttpContext c, AppDb db) =>
        {
            var u = Security.Current(c, db);
            var x = (await Http.Body<ProfileIn>(c)).Validate();
            u.BirthDate = x.BirthDate; u.Avatar = x.Avatar;
            db.SaveChanges();
            return Http.Done();
        });

        app.MapPost("/api/users", async (HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "users.manage");
            var x = (await Http.Body<UserIn>(c)).Validate();
            if (db.Users.Any(z => z.Username == x.Username)) throw new ApiException(409, "این نام کاربری قبلاً ثبت شده است.");
            if (x.PersonId != null) Http.PersonOrThrow(db, x.PersonId);
            var z = new User { Username = x.Username, FullName = x.FullName, PasswordHash = Security.HashPw(x.Password), Role = x.Role, PersonId = x.PersonId, IsActive = x.IsActive };
            db.Users.Add(z); db.SaveChanges();
            Http.Audit(db, u, "user", z.Id, "create");
            return Http.Ok(Views.UserView(z));
        });
        app.MapGet("/api/users", (HttpContext c, AppDb db) =>
        {
            Security.Require(c, db, "users.manage");
            return Http.Ok(db.Users.OrderBy(x => x.FullName).ToList().Select(Views.UserView).ToList());
        });
        app.MapPut("/api/users/{uid:int}", async (int uid, HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "users.manage");
            var x = (await Http.Body<UserUpdateIn>(c)).Validate();
            var z = db.Users.Find(uid) ?? throw new ApiException(404, "کاربر پیدا نشد.");
            if (x.PersonId != null && x.PersonId != 0) Http.PersonOrThrow(db, x.PersonId);
            z.Username = x.Username; z.FullName = x.FullName; z.Role = x.Role; z.PersonId = x.PersonId; z.IsActive = x.IsActive;
            if (!string.IsNullOrEmpty(x.Password)) z.PasswordHash = Security.HashPw(x.Password);
            db.SaveChanges();
            Http.Audit(db, u, "user", uid, "update");
            return Http.Ok(Views.UserView(z));
        });

        app.MapGet("/api/permissions", (HttpContext c, AppDb db) =>
        {
            Security.Require(c, db, "permissions.manage");
            var perms = db.Permissions.OrderBy(p => p.Module).ThenBy(p => p.Code).ToList();
            var enabled = db.RolePermissions.Where(r => r.Enabled).Include(r => r.Permission).ToList();
            var roles = new Dictionary<string, List<string>>();
            foreach (var role in Security.DefaultRole.Keys)
                roles[role] = perms.Where(p => enabled.Any(r => r.Role == role && r.PermissionId == p.Id)).Select(p => p.Code).ToList();
            return Http.Ok(new { permissions = perms.Select(p => new { code = p.Code, title = p.Title, module = p.Module }).ToList(), roles });
        });
        app.MapPut("/api/permissions", async (HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "permissions.manage");
            var x = await Http.Body<PermissionIn>(c);
            var codes = x.PermissionCodes.ToHashSet();
            foreach (var p in db.Permissions.ToList())
            {
                var rp = db.RolePermissions.FirstOrDefault(r => r.Role == x.Role && r.PermissionId == p.Id);
                if (rp == null) db.RolePermissions.Add(new RolePermission { Role = x.Role, PermissionId = p.Id, Enabled = codes.Contains(p.Code) });
                else rp.Enabled = codes.Contains(p.Code);
            }
            db.SaveChanges();
            Http.Audit(db, u, "permission", null, "update", x.Role);
            return Http.Done();
        });

        // personal checklist notes (private to the user)
        app.MapGet("/api/notes", (HttpContext c, AppDb db) =>
        {
            var u = Security.Current(c, db);
            return Http.Ok(db.NoteItems.Where(n => n.UserId == u.Id).OrderBy(n => n.Id).ToList().Select(NoteDict).ToList());
        });
        app.MapPost("/api/notes", async (HttpContext c, AppDb db) =>
        {
            var u = Security.Current(c, db);
            var x = (await Http.Body<PersonalNoteIn>(c)).Validate();
            var n = new NoteItem { UserId = u.Id, Text = x.Text.Trim(), Category = x.Category.Trim(), Done = x.Done };
            db.NoteItems.Add(n); db.SaveChanges();
            return Http.Ok(NoteDict(n));
        });
        app.MapPut("/api/notes/{nid:int}", async (int nid, HttpContext c, AppDb db) =>
        {
            var u = Security.Current(c, db);
            var x = (await Http.Body<PersonalNoteIn>(c)).Validate();
            var n = Own(db, u, nid);
            n.Text = x.Text.Trim(); n.Category = x.Category.Trim(); n.Done = x.Done;
            db.SaveChanges();
            return Http.Ok(NoteDict(n));
        });
        app.MapDelete("/api/notes/{nid:int}", (int nid, HttpContext c, AppDb db) =>
        {
            var u = Security.Current(c, db);
            db.NoteItems.Remove(Own(db, u, nid)); db.SaveChanges();
            return Http.Done();
        });
    }

    static object NoteDict(NoteItem n) => new { id = n.Id, text = n.Text, category = n.Category, done = n.Done };
    static NoteItem Own(AppDb db, User u, int id)
    {
        var n = db.NoteItems.Find(id);
        if (n == null || n.UserId != u.Id) throw new ApiException(404, "یادداشت پیدا نشد.");
        return n;
    }
}
