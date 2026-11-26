using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using MeetingManager.Api;
using MeetingManager.Core;
using MeetingManager.Data;

Paths.Init();
DbInit.Migrate();

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<AppDb>(o => o.UseSqlite(DbInit.ConnectionString()));
builder.WebHost.ConfigureKestrel(k => { k.AddServerHeader = false; k.Limits.MaxRequestBodySize = 64 * 1024 * 1024; });
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("MM_URLS") ?? "http://127.0.0.1:8000");

var app = builder.Build();
using (var scope = app.Services.CreateScope()) DbInit.Seed(scope.ServiceProvider.GetRequiredService<AppDb>());

// Hardening: no sniffing, no framing, no referrer leaks, no caching of API answers, and a content security policy.
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; font-src 'self' data:; frame-src 'self' blob:; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
    if (ctx.Request.Path.StartsWithSegments("/api")) h["Cache-Control"] = "no-store";
    await next();
});
// Download links carry a short-lived ticket instead of the session header (see Tickets).
app.Use(async (ctx, next) =>
{
    if (HttpMethods.IsGet(ctx.Request.Method) && ctx.Request.Query.ContainsKey("ticket") && Tickets.Resolve(ctx.Request) is int uid) ctx.Items["ticket_uid"] = uid;
    await next();
});

// Language of this request: English when the page (or a download link) says so; everything the server writes is then translated.
app.Use(async (ctx, next) =>
{
    Loc.En = ctx.Request.Query["lang"] == "en" || ctx.Request.Headers["X-Lang"] == "en";
    await next();
});
app.MapGet("/api/i18n/en", () => Results.Text(Loc.Json, "application/json; charset=utf-8"));

// Every error answers with {"detail": "..."}: the UI shows it as a toast.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (ApiException e) { await Write(ctx, e.Status, e.Message); }
    catch (Exception e)
    {
        ErrorLog.Write($"{ctx.Request.Method} {ctx.Request.Path}", e);
        await Write(ctx, 500, $"خطای داخلی سرور ({e.GetType().Name}: {e.Message}). جزئیات در فایل error.log کنار پایگاه داده ذخیره شد.");
    }
});
static async Task Write(HttpContext ctx, int status, string detail)
{
    if (ctx.Response.HasStarted) return;
    ctx.Response.Clear();
    detail = Loc.T(detail);
    ctx.Response.StatusCode = status;
    ctx.Response.ContentType = "application/json; charset=utf-8";
    await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { detail }, Http.Json));
}

var webroot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
if (!Directory.Exists(webroot)) webroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(webroot), RequestPath = "/static" });
app.MapGet("/", () => Results.File(Path.Combine(webroot, "index.html"), "text/html; charset=utf-8"));

AuthApi.Map(app);
PeopleApi.Map(app);
ProjectsApi.Map(app);
MeetingsApi.Map(app);
ActionsApi.Map(app);
ReportsApi.Map(app);
SyncApi.Map(app);
ExportApi.Map(app);
AssistantApi.Map(app);

app.Run();

public partial class Program { }
