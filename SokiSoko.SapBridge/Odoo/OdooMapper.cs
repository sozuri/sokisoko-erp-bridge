using System.Text.Json;
using SokiSoko.SapBridge.Sync;

namespace SokiSoko.SapBridge.Odoo;

/// <summary>
/// Odoo → ingest record mapping. Mirrors the server-side odoo provider's normalization
/// (external id = numeric Odoo id as string; cursor = write_date "yyyy-MM-dd HH:mm:ss").
/// </summary>
public static class OdooMapper
{
    public static readonly string[] ProductFields =
        { "id", "default_code", "name", "description_sale", "uom_id", "standard_price", "categ_id", "barcode", "write_date" };

    public static readonly string[] CustomerFields =
        { "id", "name", "vat", "credit_limit", "street", "street2", "city", "state_id", "zip", "country_id", "write_date" };

    public static readonly string[] PriceFields =
        { "id", "default_code", "list_price", "currency_id", "write_date" };

    public static readonly string[] StockFields =
        { "id", "default_code", "qty_available" };

    public static (InboundProduct Product, string Changed) MapProduct(JsonElement m)
    {
        var id = OdooValue.Int(m.GetProperty("id"));
        if (id == 0) return (new InboundProduct(), "");
        var ext = id.ToString();
        var sku = OdooValue.Str(m.GetProperty("default_code"));
        var name = OdooValue.Str(m.GetProperty("name"));
        if (name.Length == 0) name = sku.Length > 0 ? sku : ext;

        var attrs = new Dictionary<string, string>();
        if (OdooValue.Str(m.GetProperty("uom_id")) is { Length: > 0 } uom) attrs["odoo_uom"] = uom;
        if (OdooValue.Str(m.GetProperty("categ_id")) is { Length: > 0 } cat) attrs["odoo_category"] = cat;
        if (OdooValue.Str(m.GetProperty("barcode")) is { Length: > 0 } bc) attrs["barcode"] = bc;

        var p = new InboundProduct
        {
            ExternalID = ext,
            SKU = sku,
            Name = name,
            Description = OdooValue.Str(m.GetProperty("description_sale")),
            Cost = OdooValue.NumStr(m.GetProperty("standard_price")),
            Attributes = attrs.Count > 0 ? attrs : null,
        };
        return (p, OdooValue.Str(m.GetProperty("write_date")));
    }

    public static (InboundCustomer Customer, string Changed) MapCustomer(JsonElement m)
    {
        var id = OdooValue.Int(m.GetProperty("id"));
        if (id == 0) return (new InboundCustomer(), "");
        var ext = id.ToString();
        var name = OdooValue.Str(m.GetProperty("name"));
        if (name.Length == 0) name = ext;

        var c = new InboundCustomer
        {
            ExternalID = ext,
            Name = name,
            TaxID = OdooValue.Str(m.GetProperty("vat")),
            CreditLimit = OdooValue.NumStr(m.GetProperty("credit_limit")),
        };
        if (OdooValue.Str(m.GetProperty("street")) is { Length: > 0 } line1)
        {
            c.Addresses.Add(new InboundAddress
            {
                Line1 = line1,
                Line2 = OdooValue.Str(m.GetProperty("street2")),
                City = OdooValue.Str(m.GetProperty("city")),
                Region = OdooValue.Str(m.GetProperty("state_id")),
                PostalCode = OdooValue.Str(m.GetProperty("zip")),
                Country = OdooValue.Str(m.GetProperty("country_id")),
            });
        }
        return (c, OdooValue.Str(m.GetProperty("write_date")));
    }

    public static (InboundPrice? Price, string Changed) MapPrice(JsonElement m, string defaultCurrency)
    {
        var changed = OdooValue.Str(m.GetProperty("write_date"));
        var id = OdooValue.Int(m.GetProperty("id"));
        var price = OdooValue.Num(m.GetProperty("list_price"));
        if (id == 0 || price <= 0) return (null, changed);
        var cur = OdooValue.Str(m.GetProperty("currency_id"));
        if (cur.Length == 0) cur = defaultCurrency;
        if (cur.Length == 0) return (null, changed);
        var ext = id.ToString();
        return (new InboundPrice
        {
            SKU = ext,
            ExternalID = ext,
            Currency = cur,
            Amount = price.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            Unit = "each",
        }, changed);
    }

    public static InboundStock? MapStock(JsonElement m, string warehouseCode)
    {
        var id = OdooValue.Int(m.GetProperty("id"));
        if (id == 0) return null;
        return new InboundStock
        {
            SKU = OdooValue.Str(m.GetProperty("default_code")),
            ExternalID = id.ToString(),
            Plant = warehouseCode,
            QuantityOnHand = OdooValue.Num(m.GetProperty("qty_available"))
                .ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Delta domain: write_date >= since plus extra clauses (Odoo domain polish notation).</summary>
    public static object[] DeltaDomain(string since, params object[][] extra)
    {
        var d = new List<object>(extra.Cast<object>());
        if (since.Length > 0) d.Add(new object[] { "write_date", ">=", since });
        return d.ToArray();
    }
}
