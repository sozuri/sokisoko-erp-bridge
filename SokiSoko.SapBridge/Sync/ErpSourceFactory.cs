using SokiSoko.SapBridge.B1;
using SokiSoko.SapBridge.Config;
using SokiSoko.SapBridge.Odoo;

namespace SokiSoko.SapBridge.Sync;

/// <summary>Resolves the ERP source for the currently configured ERP type.</summary>
public sealed class ErpSourceFactory
{
    private readonly B1Source _b1;
    private readonly OdooSource _odoo;
    private readonly RuntimeSettings _settings;

    public ErpSourceFactory(B1Source b1, OdooSource odoo, RuntimeSettings settings)
    {
        _b1 = b1;
        _odoo = odoo;
        _settings = settings;
    }

    public IErpSource Current() =>
        _settings.Current.ErpType == "odoo" ? _odoo : _b1;
}
