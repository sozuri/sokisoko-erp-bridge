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
builder.Services.AddSingleton<B1SessionManager>();
builder.Services.AddHttpClient<B1Client>()
    .ConfigurePrimaryHttpMessageHandler(sp => new HttpClientHandler
    {
        // On-prem B1 Service Layer commonly uses a self-signed cert; opt out in settings only.
        ServerCertificateCustomValidationCallback = (req, cert, chain, errors) =>
        {
            var allow = sp.GetRequiredService<RuntimeSettings>().Current.SapB1AllowUntrustedCertificate;
            return allow || errors == System.Net.Security.SslPolicyErrors.None;
        }
    });
builder.Services.AddHttpClient<OdooClient>();
builder.Services.AddHttpClient<SokisokoClient>();
builder.Services.AddSingleton<B1Source>();
builder.Services.AddSingleton<OdooSource>();
builder.Services.AddSingleton<ErpSourceFactory>();

builder.Services.AddHostedService<SyncWorker>();
builder.Services.AddHostedService<SetupConsole>();

await builder.Build().RunAsync();
