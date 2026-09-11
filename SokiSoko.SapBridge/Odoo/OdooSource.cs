using SokiSoko.SapBridge.Config;
using SokiSoko.SapBridge.Sync;

namespace SokiSoko.SapBridge.Odoo;

/// <summary>Odoo source: JSON-RPC search_read delta polls mapped to ingest records.</summary>
public sealed class OdooSource : IErpSource
{
    private readonly OdooClient _odoo;
    private readonly RuntimeSettings _settings;

    public OdooSource(OdooClient odoo, RuntimeSettings settings)
    {
        _odoo = odoo;
        _settings = settings;
    }

    public string Provider => "odoo";

    public async Task<string> TestAsync(CancellationToken ct)
    {
        var uid = await _odoo.GetUidAsync(ct);
        return $"login ok (uid {uid})";
    }

    public async Task<string> PollProductsAsync(string since, Func<InboundProduct, Task> apply, CancellationToken ct)
    {
        var cursor = since;
        var domain = OdooMapper.DeltaDomain(since, new object[] { "sale_ok", "=", true });
        await _odoo.SearchReadAsync("product.product", domain, OdooMapper.ProductFields, "write_date asc", null,
            async m =>
            {
                var (product, changed) = OdooMapper.MapProduct(m);
                if (product.ExternalID.Length > 0) await apply(product);
                if (string.CompareOrdinal(changed, cursor) > 0) cursor = changed;
            }, ct);
        return cursor;
    }

    public async Task<string> PollCustomersAsync(string since, Func<InboundCustomer, Task> apply, CancellationToken ct)
    {
        var cursor = since;
        var domain = OdooMapper.DeltaDomain(since, new object[] { "customer_rank", ">", 0 });
        await _odoo.SearchReadAsync("res.partner", domain, OdooMapper.CustomerFields, "write_date asc", null,
            async m =>
            {
                var (customer, changed) = OdooMapper.MapCustomer(m);
                if (customer.ExternalID.Length > 0) await apply(customer);
                if (string.CompareOrdinal(changed, cursor) > 0) cursor = changed;
            }, ct);
        return cursor;
    }

    public async Task<string> PollPricesAsync(string since, Func<InboundPrice, Task> apply, CancellationToken ct)
    {
        var cursor = since;
        var currency = _settings.Current.OdooCurrency;
        var domain = OdooMapper.DeltaDomain(since, new object[] { "sale_ok", "=", true });
        await _odoo.SearchReadAsync("product.product", domain, OdooMapper.PriceFields, "write_date asc", null,
            async m =>
            {
                var (price, changed) = OdooMapper.MapPrice(m, currency);
                if (price is not null) await apply(price);
                if (string.CompareOrdinal(changed, cursor) > 0) cursor = changed;
            }, ct);
        return cursor;
    }

    public async Task PollStockAsync(Func<InboundStock, Task> apply, CancellationToken ct)
    {
        // Full scan per warehouse (Odoo has no stock delta); the server skips unchanged rows.
        foreach (var wh in await ListWarehousesAsync(ct))
        {
            var domain = new object[] { new object[] { "sale_ok", "=", true } };
            var kwargs = new Dictionary<string, object>
            {
                ["context"] = new Dictionary<string, object> { ["warehouse"] = wh.Id },
            };
            await _odoo.SearchReadAsync("product.product", domain, OdooMapper.StockFields, "id asc", kwargs,
                async m =>
                {
                    if (OdooMapper.MapStock(m, wh.Code) is { } s) await apply(s);
                }, ct);
        }
    }

    private async Task<List<(int Id, string Code)>> ListWarehousesAsync(CancellationToken ct)
    {
        var only = _settings.Current.OdooWarehouse;
        var domain = only.Length > 0
            ? new object[] { new object[] { "code", "=", only } }
            : Array.Empty<object>();
        var outList = new List<(int, string)>();
        await _odoo.SearchReadAsync("stock.warehouse", domain, new[] { "id", "code" }, "id asc", null,
            m =>
            {
                outList.Add((OdooValue.Int(m.GetProperty("id")), OdooValue.Str(m.GetProperty("code"))));
                return Task.CompletedTask;
            }, ct);
        return outList;
    }
}
