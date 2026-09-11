using SokiSoko.SapBridge.B1;
using SokiSoko.SapBridge.Config;
using SokiSoko.SapBridge.Odoo;
using SokiSoko.SapBridge.Sync;
using SokiSoko.SapBridge.Ui;
using Serilog;

// Rolling file log next to the exe (logs/bridge-YYYYMMDD.log, 14 days) plus console —
// "send us the logs" support without opening Event Viewer.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(AppContext.BaseDirectory, "logs", "bridge-.log"),
        rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
    .CreateLogger();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSerilog();

// Run as a Windows Service when installed (sc.exe), as a console app otherwise.
builder.Services.AddWindowsService(o => o.ServiceName = "SokiSoko SAP Bridge");

builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<RuntimeSettings>();
builder.Services.AddSingleton<CursorStore>();
builder.Services.AddSingleton<SyncStatus>();
// On-prem B1 Service Layer commonly uses a self-signed cert; opt out in settings only.
// Both the login and the paged reads must use this, or the very first TLS handshake
// (Login, from B1SessionManager) fails however the setting is set.
static HttpClientHandler B1Handler(IServiceProvider sp) => new()
{
    ServerCertificateCustomValidationCallback = (req, cert, chain, errors) =>
    {
        var allow = sp.GetRequiredService<RuntimeSettings>().Current.SapB1AllowUntrustedCertificate;
        return allow || errors == System.Net.Security.SslPolicyErrors.None;
    }
};

// B1SessionManager caches the session cookie, so it stays a singleton and takes the
// named client by hand — AddHttpClient<T> would register it as a transient.
builder.Services.AddHttpClient("b1").ConfigurePrimaryHttpMessageHandler(B1Handler);
builder.Services.AddSingleton<B1SessionManager>(sp => ActivatorUtilities.CreateInstance<B1SessionManager>(
    sp, sp.GetRequiredService<IHttpClientFactory>().CreateClient("b1")));
builder.Services.AddHttpClient<B1Client>().ConfigurePrimaryHttpMessageHandler(B1Handler);
builder.Services.AddHttpClient<OdooClient>();
builder.Services.AddHttpClient<SokisokoClient>();
builder.Services.AddSingleton<B1Source>();
builder.Services.AddSingleton<OdooSource>();
builder.Services.AddSingleton<ErpSourceFactory>();

builder.Services.AddHostedService<SyncWorker>();
builder.Services.AddHostedService<SetupConsole>();

await builder.Build().RunAsync();
