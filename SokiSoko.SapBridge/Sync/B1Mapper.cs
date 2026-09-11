using System.Globalization;
using SokiSoko.SapBridge.B1;

namespace SokiSoko.SapBridge.Sync;

/// <summary>
/// B1 → ingest record mapping. Mirrors the server-side sap_b1 provider's normalization so
/// agent-fed and server-polled data land identically.
/// </summary>
public static class B1Mapper
{
    public static (InboundProduct Product, string Changed) MapItem(B1Item m)
    {
        var name = string.IsNullOrEmpty(m.ItemName) ? m.ItemCode : m.ItemName!;
        var p = new InboundProduct
        {
            ExternalID = m.ItemCode,
            SKU = m.ItemCode,
            Name = name,
            Description = m.ForeignName ?? "",
            BaseUnit = m.InventoryUOM ?? "",
        };
        if (m.ItemsGroupCode != 0)
            p.Attributes = new Dictionary<string, string> { ["b1_items_group"] = m.ItemsGroupCode.ToString() };
        return (p, B1Client.Stamp(m.UpdateDate, m.UpdateTime));
    }

    public static IEnumerable<InboundStock> MapStock(B1Item m)
    {
        if (string.IsNullOrEmpty(m.ItemCode)) yield break;
        foreach (var w in m.Warehouses)
        {
            if (string.IsNullOrEmpty(w.WarehouseCode)) continue;
            yield return new InboundStock
            {
                SKU = m.ItemCode,
                ExternalID = m.ItemCode,
                Plant = w.WarehouseCode!,
                QuantityOnHand = Num(w.InStock),
            };
        }
    }

    public static (InboundCustomer Customer, string Changed) MapBusinessPartner(B1BusinessPartner m)
    {
        var name = string.IsNullOrEmpty(m.CardName) ? m.CardCode : m.CardName!;
        var c = new InboundCustomer
        {
            ExternalID = m.CardCode,
            Name = name,
            TaxID = m.FederalTaxID ?? "",
            CreditLimit = Num(m.CreditLimit),
        };
        foreach (var a in m.BPAddresses)
        {
            if (string.IsNullOrEmpty(a.Street) || string.IsNullOrEmpty(a.City)) continue;
            c.Addresses.Add(new InboundAddress
            {
                Line1 = a.Street!,
                Line2 = a.Block ?? "",
                City = a.City!,
                Region = a.State ?? "",
                PostalCode = a.ZipCode ?? "",
                Country = a.Country ?? "",
            });
        }
        return (c, B1Client.Stamp(m.UpdateDate, m.UpdateTime));
    }

    public static (InboundPrice? Price, string Changed) MapItemPrice(B1Item m, int priceList, string defaultCurrency)
    {
        var changed = B1Client.Stamp(m.UpdateDate, m.UpdateTime);
        if (string.IsNullOrEmpty(m.ItemCode)) return (null, changed);
        foreach (var pr in m.ItemPrices)
        {
            if (pr.PriceList != priceList || pr.Price <= 0) continue;
            var cur = string.IsNullOrEmpty(pr.Currency) ? defaultCurrency : pr.Currency!;
            if (string.IsNullOrEmpty(cur)) return (null, changed);
            return (new InboundPrice
            {
                SKU = m.ItemCode,
                ExternalID = $"{m.ItemCode}/{pr.PriceList}",
                Currency = cur,
                Amount = Num(pr.Price),
                Unit = "each",
            }, changed);
        }
        return (null, changed);
    }

    public static (InboundPrice? Price, string Changed) MapSpecialPrice(B1SpecialPrice m, string defaultCurrency)
    {
        var changed = B1Client.Stamp(m.UpdateDate, m.UpdateTime);
        if (string.IsNullOrEmpty(m.ItemCode) || string.IsNullOrEmpty(m.CardCode)) return (null, changed);
        // Discount-percent-only specials resolve live at checkout; the mirror skips 0s.
        if (m.Price <= 0) return (null, changed);
        var cur = string.IsNullOrEmpty(m.Currency) ? defaultCurrency : m.Currency!;
        if (string.IsNullOrEmpty(cur)) return (null, changed);
        return (new InboundPrice
        {
            SKU = m.ItemCode,
            ExternalID = $"{m.CardCode}/{m.ItemCode}",
            CustomerExternalID = m.CardCode!,
            Currency = cur,
            Amount = Num(m.Price),
            Unit = "each",
        }, changed);
    }

    /// <summary>Decimal string; "" for 0 keeps optional fields untouched by the apply funnel.</summary>
    private static string Num(decimal v) =>
        v == 0 ? "" : v.ToString("0.####", CultureInfo.InvariantCulture);
}
