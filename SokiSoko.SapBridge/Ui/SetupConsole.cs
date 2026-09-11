using System.Net;
using System.Text;
using System.Text.Json;
using SokiSoko.SapBridge.B1;
using SokiSoko.SapBridge.Config;
using SokiSoko.SapBridge.Sync;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SokiSoko.SapBridge.Ui;

/// <summary>
/// Loopback-only setup console on http://127.0.0.1:8735: edit settings (secrets DPAPI-sealed
/// on save), test the B1 and Sokisoko connections, and watch sync status. No auth — it only
/// listens on localhost, same trust boundary as editing the file as the service account.
/// </summary>
public sealed class SetupConsole : BackgroundService
{
    private readonly RuntimeSettings _settings;
    private readonly ErpSourceFactory _sources;
    private readonly SokisokoClient _sokisoko;
    private readonly SyncStatus _status;
    private readonly ILogger<SetupConsole> _log;

    public SetupConsole(RuntimeSettings settings, ErpSourceFactory sources, SokisokoClient sokisoko,
        SyncStatus status, ILogger<SetupConsole> log)
    {
        _settings = settings;
        _sources = sources;
        _sokisoko = sokisoko;
        _status = status;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add("http://127.0.0.1:8735/");
        try
        {
            listener.Start();
            _log.LogInformation("setup console on http://127.0.0.1:8735");
        }
        catch (HttpListenerException ex)
        {
            _log.LogWarning(ex, "setup console could not bind 127.0.0.1:8735; continuing without it");
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync().WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            _ = Task.Run(() => HandleAsync(ctx), stoppingToken);
        }
        listener.Stop();
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            var (code, body, contentType) = (ctx.Request.HttpMethod, path) switch
            {
                ("GET", "/") => (200, Page(), "text/html; charset=utf-8"),
                ("GET", "/api/health") => Json(GetHealth()),
                ("GET", "/api/settings") => Json(GetSettings()),
                ("POST", "/api/settings") => Json(await SaveSettingsAsync(ctx.Request)),
                ("POST", "/api/test-erp") => Json(await TestErpAsync()),
                ("POST", "/api/test-sokisoko") => Json(await TestSokisokoAsync()),
                ("GET", "/api/status") => Json(GetStatus()),
                _ => (404, "not found", "text/plain"),
            };
            ctx.Response.StatusCode = code;
            ctx.Response.ContentType = contentType;
            var bytes = Encoding.UTF8.GetBytes(body);
            await ctx.Response.OutputStream.WriteAsync(bytes);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "console request failed");
            ctx.Response.StatusCode = 500;
        }
        finally
        {
            ctx.Response.Close();
        }
    }

    /// <summary>The console's JS reads camelCase; without this the default PascalCase
    /// names deserialize to undefined and every field in the form renders blank.</summary>
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static (int, string, string) Json(object o) =>
        (200, JsonSerializer.Serialize(o, JsonOpts), "application/json");

    private object GetSettings()
    {
        var s = _settings.Current;
        return new
        {
            s.ErpType,
            s.SapB1BaseUrl, s.SapB1CompanyDb, s.SapB1Username,
            sapB1PasswordSet = s.SapB1Password.Length > 0,
            s.SapB1AllowUntrustedCertificate, s.SapB1PriceList, s.SapB1Currency,
            s.OdooBaseUrl, s.OdooDatabase, s.OdooUsername,
            odooPasswordSet = s.OdooPassword.Length > 0,
            s.OdooCurrency, s.OdooWarehouse,
            s.SokisokoBaseUrl, sokisokoApiKeySet = s.SokisokoApiKey.Length > 0,
            s.SokisokoConnectionId, s.IntervalMinutes, s.BatchSize, s.IsConfigured,
        };
    }

    private async Task<object> SaveSettingsAsync(HttpListenerRequest req)
    {
        var patch = await JsonSerializer.DeserializeAsync<Dictionary<string, JsonElement>>(req.InputStream)
                    ?? new Dictionary<string, JsonElement>();
        var s = _settings.Current;
        string Str(string key, string cur) => patch.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? cur : cur;
        int Num(string key, int cur) => patch.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : cur;
        long Lng(string key, long cur) => patch.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : cur;
        bool Bool(string key, bool cur) => patch.TryGetValue(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : cur;

        var next = s with
        {
            ErpType = Str("erpType", s.ErpType) is "odoo" ? "odoo" : "sap_b1",
            SapB1BaseUrl = Str("sapB1BaseUrl", s.SapB1BaseUrl),
            SapB1CompanyDb = Str("sapB1CompanyDb", s.SapB1CompanyDb),
            SapB1Username = Str("sapB1Username", s.SapB1Username),
            SapB1Password = Str("sapB1Password", s.SapB1Password),
            SapB1AllowUntrustedCertificate = Bool("sapB1AllowUntrustedCertificate", s.SapB1AllowUntrustedCertificate),
            SapB1PriceList = Num("sapB1PriceList", s.SapB1PriceList),
            SapB1Currency = Str("sapB1Currency", s.SapB1Currency),
            OdooBaseUrl = Str("odooBaseUrl", s.OdooBaseUrl),
            OdooDatabase = Str("odooDatabase", s.OdooDatabase),
            OdooUsername = Str("odooUsername", s.OdooUsername),
            OdooPassword = Str("odooPassword", s.OdooPassword),
            OdooCurrency = Str("odooCurrency", s.OdooCurrency),
            OdooWarehouse = Str("odooWarehouse", s.OdooWarehouse),
            SokisokoBaseUrl = Str("sokisokoBaseUrl", s.SokisokoBaseUrl),
            SokisokoApiKey = Str("sokisokoApiKey", s.SokisokoApiKey),
            SokisokoConnectionId = Lng("sokisokoConnectionId", s.SokisokoConnectionId),
            IntervalMinutes = Num("intervalMinutes", s.IntervalMinutes),
            BatchSize = Num("batchSize", s.BatchSize),
        };
        _settings.Update(next);
        return new { ok = true, next.IsConfigured };
    }

    private async Task<object> TestErpAsync()
    {
        var s = _settings.Current;
        try
        {
            var detail = await _sources.Current().TestAsync(CancellationToken.None);
            return new { ok = true, detail };
        }
        catch (Exception ex)
        {
            return new { ok = false, detail = s.ErpType == "odoo" ? Diagnostics.ExplainOdoo(ex) : Diagnostics.ExplainB1(ex) };
        }
    }

    private async Task<object> TestSokisokoAsync()
    {
        try
        {
            await _sokisoko.IngestAsync(new IngestBatch(), CancellationToken.None);
            return new { ok = true, detail = "API key accepted; connection reachable" };
        }
        catch (Exception ex)
        {
            return new { ok = false, detail = Diagnostics.ExplainSokisoko(ex) };
        }
    }

    /// <summary>Plain JSON health for external monitors (Uptime Kuma etc.) — no secrets.</summary>
    private object GetHealth() => new
    {
        status = _settings.Current.IsConfigured ? "healthy" : "unconfigured",
        version = typeof(SetupConsole).Assembly.GetName().Version?.ToString() ?? "0.0.0",
        configured = _settings.Current.IsConfigured,
        running = _status.Running,
        lastRunOk = _status.LastRunOk,
        lastRunAt = _status.LastRunAt?.ToString("o"),
        timestamp = DateTimeOffset.UtcNow,
    };

    private object GetStatus() => new
    {
        _status.Running, _status.LastRunOk, _status.LastRunRecords,
        lastRunAt = _status.LastRunAt?.ToString("yyyy-MM-dd HH:mm:ss"),
        _status.LastError,
        recent = _status.Recent,
        configured = _settings.Current.IsConfigured,
    };

    private static string Page() => """
<!doctype html><html><head><meta charset="utf-8"><title>SokiSoko ERP Bridge</title>
<meta name="viewport" content="width=device-width,initial-scale=1">
<style>
body{font-family:system-ui,sans-serif;max-width:760px;margin:2rem auto;padding:0 1rem;color:#1a1a1a}
h1{font-size:1.3rem}h2{font-size:1rem;margin-top:1.6rem}
label{display:block;font-size:.85rem;margin-top:.7rem}
input,select{width:100%;padding:.45rem;border:1px solid #bbb;border-radius:6px;box-sizing:border-box}
button{margin-top:1rem;padding:.5rem 1rem;border:0;border-radius:6px;background:#0b5fff;color:#fff;cursor:pointer}
button.ghost{background:#eee;color:#1a1a1a}
#msg{margin-top:.8rem;font-size:.9rem;white-space:pre-wrap}
.ok{color:#0a7a2f}.bad{color:#b00020}
.card{border:1px solid #ddd;border-radius:10px;padding:1rem;margin-top:1rem}
.row{display:flex;gap:.6rem}.row>*{flex:1}
code{background:#f3f3f3;padding:.1rem .3rem;border-radius:4px}
.hidden{display:none}
</style></head><body>
<h1>SokiSoko ERP Bridge</h1>
<div id="status" class="card">loading…</div>

<div class="card"><h2>ERP system</h2>
<label>Sync from<select id="erptype" onchange="showErp()">
<option value="sap_b1">SAP Business One</option>
<option value="odoo">Odoo</option>
</select></label></div>

<div class="card" id="card-b1"><h2>SAP Business One</h2>
<label>Service Layer URL<input id="b1url" placeholder="https://b1-host:50000"></label>
<div class="row"><div><label>Company database<input id="b1db" placeholder="SBO_COMPANY"></label></div>
<div><label>Username<input id="b1user"></label></div></div>
<label>Password <span id="b1pwset"></span><input id="b1pw" type="password" placeholder="leave blank to keep current"></label>
<div class="row"><div><label>Base price list #<input id="b1pl" type="number" value="1"></label></div>
<div><label>Default currency<input id="b1cur" placeholder="KES"></label></div>
<div><label>Untrusted cert<select id="b1cert"><option value="false">no</option><option value="true">yes</option></select></label></div></div></div>

<div class="card hidden" id="card-odoo"><h2>Odoo</h2>
<label>Odoo URL<input id="odurl" placeholder="https://mycompany.odoo.com"></label>
<div class="row"><div><label>Database<input id="oddb" placeholder="mycompany"></label></div>
<div><label>Username (email)<input id="oduser" placeholder="admin@mycompany.com"></label></div></div>
<label>Password or API key <span id="odpwset"></span><input id="odpw" type="password" placeholder="leave blank to keep current"></label>
<div class="row"><div><label>Default currency<input id="odcur" placeholder="KES"></label></div>
<div><label>Warehouse code<input id="odwh" placeholder="blank = all warehouses"></label></div></div></div>

<div class="card"><button class="ghost" onclick="testErp()">Test ERP connection</button></div>

<div class="card"><h2>SokiSoko</h2>
<label>Base URL<input id="skurl" placeholder="https://your-sokisoko.example.com"></label>
<label>API key (erp.manage) <span id="skkeyset"></span><input id="skkey" type="password" placeholder="tgk_… — leave blank to keep current"></label>
<label>Connection ID<input id="skconn" type="number" placeholder="from Settings → ERP sync"></label>
<div class="row"><div><label>Interval (min)<input id="interval" type="number" value="5"></label></div>
<div><label>Batch size<input id="batch" type="number" value="200"></label></div></div>
<button class="ghost" onclick="testSokisoko()">Test SokiSoko connection</button></div>

<button onclick="save()">Save settings</button>
<div id="msg"></div>
<script>
const $=id=>document.getElementById(id);
function showErp(){const t=erptype.value;$('card-b1').classList.toggle('hidden',t!=='sap_b1');$('card-odoo').classList.toggle('hidden',t!=='odoo');}
async function api(path,method){const r=await fetch(path,{method:method||'GET',headers:{'Content-Type':'application/json'},body:method==='POST'?'{}':undefined});return r.json();}
async function loadSettings(){
 const s=await api('/api/settings');
 erptype.value=s.erpType||'sap_b1';showErp();
 b1url.value=s.sapB1BaseUrl||'';b1db.value=s.sapB1CompanyDb||'';b1user.value=s.sapB1Username||'';
 b1pwset.textContent=s.sapB1PasswordSet?'(set)':'(not set)';b1pl.value=s.sapB1PriceList||1;b1cur.value=s.sapB1Currency||'';
 b1cert.value=s.sapB1AllowUntrustedCertificate?'true':'false';
 odurl.value=s.odooBaseUrl||'';oddb.value=s.odooDatabase||'';oduser.value=s.odooUsername||'';
 odpwset.textContent=s.odooPasswordSet?'(set)':'(not set)';odcur.value=s.odooCurrency||'';odwh.value=s.odooWarehouse||'';
 skurl.value=s.sokisokoBaseUrl||'';skkeyset.textContent=s.sokisokoApiKeySet?'(set)':'(not set)';
 skconn.value=s.sokisokoConnectionId||'';interval.value=s.intervalMinutes||5;batch.value=s.batchSize||200;
}
async function loadStatus(){
 const st=await api('/api/status');
 // must be $('status'): bare `status` resolves to window.status (a string), not this element
 $('status').innerHTML='<h2>Status</h2>'+(st.configured?'':'<p class="bad">Not configured yet — fill both sections and Save.</p>')
  +'<p>'+(st.running?'sync running…':(st.lastRunAt?((st.lastRunOk?'<span class="ok">last run ok':'<span class="bad">last run failed')+'</span> — '+st.lastRunAt+' — '+st.lastRunRecords+' records'):'no sync run yet'))+'</p>'
  +(st.lastError?'<p class="bad">'+st.lastError+'</p>':'')
  +'<p>Recent:</p><code>'+(st.recent||[]).join('<br>')+'</code>';
}
async function save(){
 const body={erpType:erptype.value,
  sapB1BaseUrl:b1url.value,sapB1CompanyDb:b1db.value,sapB1Username:b1user.value,
  sapB1AllowUntrustedCertificate:b1cert.value==='true',sapB1PriceList:+b1pl.value,sapB1Currency:b1cur.value,
  odooBaseUrl:odurl.value,odooDatabase:oddb.value,odooUsername:oduser.value,
  odooCurrency:odcur.value,odooWarehouse:odwh.value,
  sokisokoBaseUrl:skurl.value,sokisokoConnectionId:+skconn.value,intervalMinutes:+interval.value,batchSize:+batch.value};
 if(b1pw.value)body.sapB1Password=b1pw.value;
 if(odpw.value)body.odooPassword=odpw.value;
 if(skkey.value)body.sokisokoApiKey=skkey.value;
 const r=await fetch('/api/settings',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});
 const j=await r.json();msg.className=j.ok?'ok':'bad';msg.textContent=j.ok?'saved — the next sync run uses these settings':'save failed';
 b1pw.value='';skkey.value='';load();
}
async function testErp(){msg.className='';msg.textContent='testing ERP…';const j=await api('/api/test-erp','POST');msg.className=j.ok?'ok':'bad';msg.textContent=(j.ok?'ERP OK — ':'ERP FAILED — ')+j.detail;}
async function testSokisoko(){msg.className='';msg.textContent='testing SokiSoko…';const j=await api('/api/test-sokisoko','POST');msg.className=j.ok?'ok':'bad';msg.textContent=(j.ok?'SokiSoko OK — ':'SokiSoko FAILED — ')+j.detail;}
async function load(){await loadSettings();await loadStatus();}
load();setInterval(loadStatus,15000);
</script></body></html>
""";
}
