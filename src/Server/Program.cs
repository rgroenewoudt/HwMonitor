using System.Text.RegularExpressions;
using HwMonitor.Sensors;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

// Content root = exe folder, so appsettings.json is found also when started from another folder.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    // Files come from the embedded provider below; this only stops a "wwwroot not found" warning.
    WebRootPath = AppContext.BaseDirectory,
});

builder.Services.Configure<SensorOptions>(builder.Configuration.GetSection(SensorOptions.Section));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ISensorSource>(sp => sp.GetRequiredService<IOptions<SensorOptions>>().Value switch
{
    { UseFake: true } => ActivatorUtilities.CreateInstance<FakeSensorSource>(sp),
    { Source: SensorSourceKind.Lhm } => ActivatorUtilities.CreateInstance<LhmSensorSource>(sp),
    _ => (ISensorSource)ActivatorUtilities.CreateInstance<AutoSensorSource>(sp),
});
builder.Services.AddSingleton<SensorPoller>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SensorPoller>());
builder.Services.AddSignalR();

var app = builder.Build();

// The Angular app is embedded in the exe, so the published app is one single file.
// A Debug build has no Angular files embedded; use `ng serve` then.
var assembly = typeof(Program).Assembly;
var hasClient = assembly.GetManifestResourceNames().Contains("Microsoft.Extensions.FileProviders.Embedded.Manifest.xml");
app.Environment.WebRootFileProvider = hasClient
    ? new ManifestEmbeddedFileProvider(assembly, "wwwroot")
    : new NullFileProvider();
if (!hasClient)
    app.Logger.LogWarning("No web UI embedded in this build. Build with -c Release, or use `ng serve` in src/Client.");

var staticFiles = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Only files with a build hash in the name (main-XIAXEOH3.js) may be cached forever.
        // All others (index.html, service worker, icons, favicon) are checked again on each use.
        ctx.Context.Response.Headers.CacheControl = HashedFileName().IsMatch(ctx.File.Name)
            ? "public, max-age=31536000, immutable"
            : "no-cache";
    },
};
app.UseDefaultFiles();
app.UseStaticFiles(staticFiles);

app.MapHub<SensorHub>("/hub/sensors");
app.MapGet("/api/snapshot", (SensorPoller poller) =>
    poller.Latest is { } snapshot ? Results.Ok(snapshot) : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
// Same options, so "/" and deep links also get "no-cache" for index.html.
app.MapFallbackToFile("index.html", staticFiles);

app.Run();

internal partial class Program
{
    /// <summary>Angular output hashing: name-ABCD1234.ext.</summary>
    [GeneratedRegex(@"-[A-Z0-9]{8}\.[a-z0-9]+$")]
    private static partial Regex HashedFileName();
}
