using SokiSoko.SapBridge.B1;
using SokiSoko.SapBridge.Config;
using SokiSoko.SapBridge.Odoo;
using SokiSoko.SapBridge.Sql;

namespace SokiSoko.SapBridge.Sync;

/// <summary>Resolves the ERP source for the currently configured ERP type.</summary>
public sealed class ErpSourceFactory
{
    private readonly B1Source _b1;
    private readonly OdooSource _odoo;
    private readonly SqlSource _sql;
    private readonly RuntimeSettings _settings;

    public ErpSourceFactory(B1Source b1, OdooSource odoo, SqlSource sql, RuntimeSettings settings)
    {
        _b1 = b1;
        _odoo = odoo;
        _sql = sql;
        _settings = settings;
    }

    public IErpSource Current() => _settings.Current.ErpType switch
    {
        "odoo" => _odoo,
        "sql_direct" => _sql,
        _ => _b1,
    };
}
