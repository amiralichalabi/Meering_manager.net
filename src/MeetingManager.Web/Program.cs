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
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("MM_URLS") ?? "http://127.0.0.1:8000");

var app = builder.Build();
using (var scope = app.Services.CreateScope()) DbInit.Seed(scope.ServiceProvider.GetRequiredService<AppDb>());

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
