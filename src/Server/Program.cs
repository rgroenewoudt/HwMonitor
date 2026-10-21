using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using HwMonitor.Firewall;
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

// Without Urls (from settings, --urls or ASPNETCORE_URLS), take the first free port of Ports: 5080 may be used by another app.
int? fallbackFrom = null;
if (string.IsNullOrEmpty(builder.Configuration["Urls"]))
{
    var ports = builder.Configuration.GetSection("Ports").Get<int[]>() is { Length: > 0 } list ? list : [5080];
    var port = ports.FirstOrDefault(IsPortFree);
    if (port == 0)
        throw new InvalidOperationException($"All ports are in use: {string.Join(", ", ports)}. Add a free port to Ports in appsettings.json.");
    if (port != ports[0])
        fallbackFrom = ports[0];
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

builder.Services.Configure<SensorOptions>(builder.Configuration.GetSection(SensorOptions.Section));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ISensorSource>(sp => sp.GetRequiredService<IOptions<SensorOptions>>().Value switch
{
    { UseFake: true } => ActivatorUtilities.CreateInstance<FakeSensorSource>(sp),
    _ => (ISensorSource)ActivatorUtilities.CreateInstance<LhmSensorSource>(sp),
});
builder.Services.AddSingleton<IForegroundApp>(sp => sp.GetRequiredService<IOptions<SensorOptions>>().Value.UseFake
    ? new FakeForegroundApp()
    : ActivatorUtilities.CreateInstance<ForegroundApp>(sp));
builder.Services.AddSingleton<IFrameMonitor>(sp => sp.GetRequiredService<IOptions<SensorOptions>>().Value.UseFake
    ? ActivatorUtilities.CreateInstance<FakeFrameMonitor>(sp)
    : ActivatorUtilities.CreateInstance<EtwFrameMonitor>(sp));
builder.Services.AddSingleton<SensorPoller>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SensorPoller>());
builder.Services.AddSingleton<WindowsFirewall>();
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
if (fallbackFrom is { } usedPort)
    app.Logger.LogWarning("Port {Port} is in use by another app, using the next free port of Ports", usedPort);

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
// The id is a hash from the snapshot, never a path, so other devices cannot read files. The icon of an exe does not change.
app.MapGet("/api/game-icon/{id}", (HttpContext http, string id, IForegroundApp foreground) =>
{
    if (foreground.Icon(id) is not { } png)
        return Results.NotFound();
    http.Response.Headers.CacheControl = "public, max-age=86400";
    return Results.File(png, "image/png");
});
// The firewall warning is only for a browser on this PC: when the port is blocked, other devices cannot get here.
app.MapGet("/api/firewall", (HttpContext http, WindowsFirewall firewall) =>
    IsFromThisPc(http) ? firewall.Check() : FirewallStatus.Ok);
app.MapPost("/api/firewall/open", (HttpContext http, WindowsFirewall firewall, ILogger<WindowsFirewall> logger) =>
{
    if (!IsFromThisPc(http) || !IsSameSiteByIp(http.Request))
        return Results.Problem($"Open this page as http://localhost:{http.Connection.LocalPort} to change the firewall.",
            statusCode: StatusCodes.Status403Forbidden);
    try
    {
        return Results.Ok(firewall.Open());
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Problem("Run HwMonitor as administrator to change the firewall.", statusCode: StatusCodes.Status403Forbidden);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Could not open the firewall port");
        return Results.Problem($"Could not change the firewall: {ex.Message}");
    }
});
// Same options, so "/" and deep links also get "no-cache" for index.html.
app.MapFallbackToFile("index.html", staticFiles);

app.Run();

internal partial class Program
{
    /// <summary>Exclusive bind, like Kestrel on Windows: also fails when another app only listens on one address of the port.</summary>
    private static bool IsPortFree(int port)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = true };
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static bool IsFromThisPc(HttpContext http) =>
        http.Connection.RemoteIpAddress is { } remote
        && (IPAddress.IsLoopback(remote) || remote.Equals(http.Connection.LocalIpAddress));

    /// <summary>
    /// Changing the firewall needs a request from our own page: Origin must match Host (no other web site),
    /// and Host must be localhost or an IP (no DNS rebinding, where evil.example points to 127.0.0.1).
    /// </summary>
    private static bool IsSameSiteByIp(HttpRequest request)
    {
        var host = request.Host.Host;
        if (host != "localhost" && !IPAddress.TryParse(host, out _))
            return false;
        return request.Headers.Origin.ToString() == $"{request.Scheme}://{request.Host}";
    }

    /// <summary>Angular output hashing: name-ABCD1234.ext.</summary>
    [GeneratedRegex(@"-[A-Z0-9]{8}\.[a-z0-9]+$")]
    private static partial Regex HashedFileName();
}
