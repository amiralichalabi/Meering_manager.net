using Microsoft.EntityFrameworkCore;
using MeetingManager.Core;
using MeetingManager.Data;

namespace MeetingManager.Api;

public static class PeopleApi
{
    static object PersonOut(int id, PersonIn x) => new { id, name = x.Name, department = x.Department, role = x.Role, category = x.Category, category_id = x.CategoryId, is_active = x.IsActive };

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/people", (HttpContext c, AppDb db) =>
        {
            Security.Current(c, db);
            return Http.Ok(db.People.Where(p => p.IsActive).OrderBy(p => p.Name).ToList().Select(Views.PersonView).ToList());
        });
        app.MapPost("/api/people", async (HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "people.manage");
            var x = (await Http.Body<PersonIn>(c)).Validate();
            var p = new Person { Name = x.Name, Department = x.Department, Role = x.Role, Category = x.Category, CategoryId = x.CategoryId, IsActive = x.IsActive };
            db.People.Add(p); db.SaveChanges();
            Http.Audit(db, u, "person", p.Id, "create");
            return Http.Ok(PersonOut(p.Id, x));
        });
        app.MapPut("/api/people/{pid:int}", async (int pid, HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "people.manage");
            var x = (await Http.Body<PersonIn>(c)).Validate();
            var p = db.People.Find(pid) ?? throw new ApiException(404, "فرد پیدا نشد.");
            p.Name = x.Name; p.Department = x.Department; p.Role = x.Role; p.Category = x.Category; p.CategoryId = x.CategoryId; p.IsActive = x.IsActive;
            db.SaveChanges();
            Http.Audit(db, u, "person", pid, "update");
            return Http.Ok(PersonOut(p.Id, x));
        });

        app.MapGet("/api/categories", (HttpContext c, AppDb db) =>
        {
            Security.Current(c, db);
            return Http.Ok(db.Categories.OrderBy(x => x.Name).ToList().Select(x => new { id = x.Id, name = x.Name }).ToList());
        });
        app.MapPost("/api/categories", async (HttpContext c, AppDb db) =>
        {
            Security.Require(c, db, "people.manage");
            var x = (await Http.Body<CategoryIn>(c)).Validate();
            var z = new PersonCategory { Name = x.Name.Trim() };
            db.Categories.Add(z); db.SaveChanges();
            return Http.Ok(new { id = z.Id, name = z.Name });
        });

        app.MapGet("/api/teams", (HttpContext c, AppDb db) =>
        {
            Security.Current(c, db);
            var ts = db.Teams.Include(t => t.Manager).Include(t => t.Members).AsSplitQuery().OrderBy(t => t.Name).ToList();
            return Http.Ok(ts.Select(t => new { id = t.Id, name = t.Name, description = t.Description, manager_id = t.ManagerId, manager = Views.PName(t.Manager), member_ids = t.Members.Select(p => p.Id).ToList(), members = t.Members.Select(p => p.Name).ToList() }).ToList());
        });
        app.MapPost("/api/teams", async (HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "teams.manage");
            var x = (await Http.Body<TeamIn>(c)).Validate();
            var t = new Team { Name = x.Name.Trim(), Description = x.Description.Trim(), ManagerId = x.ManagerId, IsActive = x.IsActive, Members = Http.People(db, x.MemberIds) };
            db.Teams.Add(t); db.SaveChanges();
            Http.Audit(db, u, "team", t.Id, "create");
            return Http.Ok(new { id = t.Id });
        });
        app.MapPut("/api/teams/{tid:int}", async (int tid, HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "teams.manage");
            var x = (await Http.Body<TeamIn>(c)).Validate();
            var t = db.Teams.Include(z => z.Members).FirstOrDefault(z => z.Id == tid) ?? throw new ApiException(404, "تیم پیدا نشد.");
            t.Name = x.Name.Trim(); t.Description = x.Description.Trim(); t.ManagerId = x.ManagerId; t.IsActive = x.IsActive;
            t.Members.Clear(); t.Members.AddRange(Http.People(db, x.MemberIds));
            db.SaveChanges();
            Http.Audit(db, u, "team", tid, "update");
            return Http.Ok(new { id = tid });
        });

        app.MapGet("/api/locations", (HttpContext c, AppDb db) =>
        {
            Security.Current(c, db);
            return Http.Ok(db.Locations.OrderBy(x => x.Name).ToList().Select(Views.LocationView).ToList());
        });
        app.MapPost("/api/locations", async (HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "locations.manage");
            var x = (await Http.Body<LocationIn>(c)).Validate();
            var z = new Location { Name = x.Name.Trim(), Address = x.Address.Trim(), Description = x.Description.Trim(), IsActive = x.IsActive };
            db.Locations.Add(z); db.SaveChanges();
            Http.Audit(db, u, "location", z.Id, "create");
            return Http.Ok(Views.LocationView(z));
        });
        app.MapPut("/api/locations/{lid:int}", async (int lid, HttpContext c, AppDb db) =>
        {
            var u = Security.Require(c, db, "locations.manage");
            var x = (await Http.Body<LocationIn>(c)).Validate();
            var z = db.Locations.Find(lid) ?? throw new ApiException(404, "مکان پیدا نشد.");
            z.Name = x.Name.Trim(); z.Address = x.Address.Trim(); z.Description = x.Description.Trim(); z.IsActive = x.IsActive;
            db.SaveChanges();
            Http.Audit(db, u, "location", lid, "update");
            return Http.Ok(Views.LocationView(z));
        });
    }
}
