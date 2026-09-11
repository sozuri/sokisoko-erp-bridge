using System.Text.Json;
using SokiSoko.SapBridge.Odoo;
using Xunit;

namespace SokiSoko.SapBridge.Tests;

public class OdooMapperTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void MapProduct_UsesNumericIdAsExternalId()
    {
        var (p, changed) = OdooMapper.MapProduct(Parse("""
        {"id": 42, "default_code": "BOLT-1", "name": "Hex Bolt", "description_sale": "M8",
         "uom_id": [1, "Units"], "standard_price": 3.5, "categ_id": [4, "Hardware"],
         "barcode": "5901234123457", "write_date": "2026-09-10 13:45:00"}
        """));

        Assert.Equal("42", p.ExternalID);
        Assert.Equal("BOLT-1", p.SKU);
        Assert.Equal("Hex Bolt", p.Name);
        Assert.Equal("M8", p.Description);
        Assert.Equal("3.5", p.Cost);
        Assert.Equal("Units", p.Attributes!["odoo_uom"]);
        Assert.Equal("Hardware", p.Attributes["odoo_category"]);
        Assert.Equal("5901234123457", p.Attributes["barcode"]);
        Assert.Equal("2026-09-10 13:45:00", changed);
    }

    [Fact]
    public void MapProduct_FalseFieldsBecomeEmpty()
    {
        var (p, _) = OdooMapper.MapProduct(Parse("""
        {"id": 7, "default_code": false, "name": "Widget", "description_sale": false,
         "uom_id": false, "standard_price": 0, "categ_id": false, "barcode": false,
         "write_date": "2026-09-10 08:00:00"}
        """));

        Assert.Equal("7", p.ExternalID);
        Assert.Equal("", p.SKU);
        Assert.Equal("Widget", p.Name);
        Assert.Equal("", p.Description);
        Assert.Equal("", p.Cost); // 0 → "" leaves the field untouched
        Assert.Null(p.Attributes);
    }

    [Fact]
    public void MapCustomer_MapsVatCreditAndAddress()
    {
        var (c, _) = OdooMapper.MapCustomer(Parse("""
        {"id": 99, "name": "Acme Ltd", "vat": "P051234567X", "credit_limit": 250000,
         "street": "1 Main St", "street2": false, "city": "Nairobi", "state_id": [1, "Nairobi"],
         "zip": "00100", "country_id": [2, "Kenya"], "write_date": "2026-09-09 10:00:00"}
        """));

        Assert.Equal("99", c.ExternalID);
        Assert.Equal("Acme Ltd", c.Name);
        Assert.Equal("P051234567X", c.TaxID);
        Assert.Equal("250000", c.CreditLimit);
        Assert.Single(c.Addresses);
        Assert.Equal("1 Main St", c.Addresses[0].Line1);
        Assert.Equal("Nairobi", c.Addresses[0].Region);
        Assert.Equal("Kenya", c.Addresses[0].Country);
    }

    [Fact]
    public void MapPrice_UsesListPriceWithCurrencyFallback()
    {
        var (price, _) = OdooMapper.MapPrice(Parse("""
        {"id": 42, "default_code": "BOLT-1", "list_price": 50.25, "currency_id": false,
         "write_date": "2026-09-10 13:45:00"}
        """), "KES");

        Assert.NotNull(price);
        Assert.Equal("42", price!.ExternalID);
        Assert.Equal("42", price.SKU);
        Assert.Equal("50.25", price.Amount);
        Assert.Equal("KES", price.Currency);
        Assert.Equal("each", price.Unit);
    }

    [Fact]
    public void MapPrice_SkipsZeroPrice()
    {
        var (price, _) = OdooMapper.MapPrice(Parse("""
        {"id": 42, "list_price": 0, "currency_id": [1, "KES"], "write_date": "2026-09-10 13:45:00"}
        """), "KES");
        Assert.Null(price);
    }

    [Fact]
    public void MapStock_TagsWarehouseCode()
    {
        var s = OdooMapper.MapStock(Parse("""
        {"id": 42, "default_code": "BOLT-1", "qty_available": 12.5}
        """), "WH");

        Assert.NotNull(s);
        Assert.Equal("42", s!.ExternalID);
        Assert.Equal("BOLT-1", s.SKU);
        Assert.Equal("WH", s.Plant);
        Assert.Equal("12.5", s.QuantityOnHand);
    }

    [Fact]
    public void DeltaDomain_AddsWriteDateClause()
    {
        var d = OdooMapper.DeltaDomain("2026-09-10 13:45:00", new object[] { "sale_ok", "=", true });
        Assert.Equal(2, d.Length);
        var clause = Assert.IsType<object[]>(d[1]);
        Assert.Equal("write_date", clause[0]);
        Assert.Equal(">=", clause[1]);
        Assert.Equal("2026-09-10 13:45:00", clause[2]);
    }

    [Fact]
    public void DeltaDomain_EmptySinceOmitsClause()
    {
        var d = OdooMapper.DeltaDomain("", new object[] { "sale_ok", "=", true });
        Assert.Single(d);
    }
}
