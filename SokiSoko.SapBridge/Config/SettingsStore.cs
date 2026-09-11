using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SokiSoko.SapBridge.Config;

/// <summary>
/// Persists bridge settings to settings.json next to the executable. Secrets (B1 password,
/// Sokisoko API key) are DPAPI-protected (CurrentUser scope) so the file is readable only
/// by the Windows account running the service — reusable by any company without plaintext
/// secrets in appsettings.json.
/// </summary>
public sealed class SettingsStore
{
    private readonly string _path;
    private readonly ILogger<SettingsStore> _log;
    private readonly object _lock = new();

    public SettingsStore(IHostEnvironment env, ILogger<SettingsStore> log)
    {
        _log = log;
        _path = Path.Combine(env.ContentRootPath, "settings.json");
    }

    public BridgeSettings Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_path)) return new BridgeSettings();
                var s = JsonSerializer.Deserialize<BridgeSettings>(File.ReadAllText(_path)) ?? new BridgeSettings();
                s.SapB1Password = Unprotect(s.SapB1Password);
                s.OdooPassword = Unprotect(s.OdooPassword);
                s.SokisokoApiKey = Unprotect(s.SokisokoApiKey);
                return s;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "could not read {Path}; starting unconfigured", _path);
                return new BridgeSettings();
            }
        }
    }

    public void Save(BridgeSettings s)
    {
        lock (_lock)
        {
            var copy = s with
            {
                SapB1Password = Protect(s.SapB1Password),
                OdooPassword = Protect(s.OdooPassword),
                SokisokoApiKey = Protect(s.SokisokoApiKey),
            };
            File.WriteAllText(_path, JsonSerializer.Serialize(copy, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static string Protect(string plain)
    {
        if (plain.Length == 0 || plain.StartsWith("dpapi:", StringComparison.Ordinal)) return plain;
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
        return "dpapi:" + Convert.ToBase64String(bytes);
    }

    private static string Unprotect(string stored)
    {
        if (!stored.StartsWith("dpapi:", StringComparison.Ordinal)) return stored;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(stored["dpapi:".Length..]), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (CryptographicException)
        {
            return ""; // protected by a different Windows account — treat as unset
        }
    }
}

/// <summary>Flat settings record edited via the setup console; mirrors BridgeOptions.</summary>
public sealed record BridgeSettings
{
    /// <summary>Which ERP to pull from: "sap_b1" or "odoo".</summary>
    public string ErpType { get; set; } = "sap_b1";

    public string SapB1BaseUrl { get; set; } = "";
    public string SapB1CompanyDb { get; set; } = "";
    public string SapB1Username { get; set; } = "";
    public string SapB1Password { get; set; } = "";
    public bool SapB1AllowUntrustedCertificate { get; set; }
    public int SapB1PriceList { get; set; } = 1;
    public string SapB1Currency { get; set; } = "";

    public string OdooBaseUrl { get; set; } = "";
    public string OdooDatabase { get; set; } = "";
    public string OdooUsername { get; set; } = "";
    public string OdooPassword { get; set; } = "";
    public string OdooCurrency { get; set; } = "";
    public string OdooWarehouse { get; set; } = "";

    public string SokisokoBaseUrl { get; set; } = "";
    public string SokisokoApiKey { get; set; } = "";
    public long SokisokoConnectionId { get; set; }
    public int IntervalMinutes { get; set; } = 5;
    public int BatchSize { get; set; } = 200;

    public bool ErpConfigured => ErpType == "odoo"
        ? OdooBaseUrl.Length > 0 && OdooDatabase.Length > 0 && OdooUsername.Length > 0
        : SapB1BaseUrl.Length > 0 && SapB1CompanyDb.Length > 0 && SapB1Username.Length > 0;

    public bool IsConfigured =>
        ErpConfigured && SokisokoBaseUrl.Length > 0 && SokisokoApiKey.Length > 0 && SokisokoConnectionId > 0;
}
