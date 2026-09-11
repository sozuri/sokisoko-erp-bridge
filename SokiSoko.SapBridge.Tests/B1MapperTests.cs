using SokiSoko.SapBridge.B1;
using SokiSoko.SapBridge.Sync;
using Xunit;

namespace SokiSoko.SapBridge.Tests;

public class B1MapperTests
{
    [Fact]
    public void MapItem_UsesItemCodeAsSkuAndExternalId()
    {
        var (p, changed) = B1Mapper.MapItem(new B1Item
        {
            ItemCode = "A100",
            ItemName = "Widget",
            ForeignName = "Widget FR",
            InventoryUOM = "EA",
            ItemsGroupCode = 7,
            UpdateDate = "2026-09-10",
            UpdateTime = "13:45:00",
        });

        Assert.Equal("A100", p.ExternalID);
        Assert.Equal("A100", p.SKU);
        Assert.Equal("Widget", p.Name);
        Assert.Equal("Widget FR", p.Description);
        Assert.Equal("EA", p.BaseUnit);
        Assert.Equal("7", p.Attributes!["b1_items_group"]);
        Assert.Equal("2026-09-10T13:45:00", changed);
    }

    [Fact]
    public void MapItem_FallsBackToItemCodeForName()
    {
        var (p, _) = B1Mapper.MapItem(new B1Item { ItemCode = "A100", ItemName = "" });
        Assert.Equal("A100", p.Name);
    }

    [Fact]
    public void MapStock_EmitsOneRowPerWarehouse()
    {
        var rows = B1Mapper.MapStock(new B1Item
        {
            ItemCode = "A100",
            Warehouses =
            {
                new B1WarehouseRow { WarehouseCode = "01", InStock = 12.5m },
                new B1WarehouseRow { WarehouseCode = "", InStock = 3m }, // skipped
                new B1WarehouseRow { WarehouseCode = "02", InStock = 0m },
            },
        }).ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal("01", rows[0].Plant);
        Assert.Equal("12.5", rows[0].QuantityOnHand);
        Assert.Equal("02", rows[1].Plant);
        Assert.Equal("", rows[1].QuantityOnHand); // 0 → "" leaves the field untouched
    }

    [Fact]
    public void MapItemPrice_PicksConfiguredListOnly()
    {
        var item = new B1Item
        {
            ItemCode = "A100",
            ItemPrices =
            {
                new B1ItemPrice { PriceList = 2, Price = 99m, Currency = "USD" },
                new B1ItemPrice { PriceList = 1, Price = 50.25m, Currency = "KES" },
            },
        };
        var (price, _) = B1Mapper.MapItemPrice(item, priceList: 1, defaultCurrency: "KES");

        Assert.NotNull(price);
        Assert.Equal("A100/1", price!.ExternalID);
        Assert.Equal("50.25", price.Amount);
        Assert.Equal("KES", price.Currency);
        Assert.Equal("each", price.Unit);
        Assert.Equal("", price.CustomerExternalID);
    }

    [Fact]
    public void MapItemPrice_UsesDefaultCurrencyWhenBlank()
    {
        var item = new B1Item
        {
            ItemCode = "A100",
            ItemPrices = { new B1ItemPrice { PriceList = 1, Price = 10m, Currency = "" } },
        };
        var (price, _) = B1Mapper.MapItemPrice(item, 1, "KES");
        Assert.Equal("KES", price!.Currency);
    }

    [Fact]
    public void MapItemPrice_SkipsWhenListMissing()
    {
        var item = new B1Item
        {
            ItemCode = "A100",
            ItemPrices = { new B1ItemPrice { PriceList = 2, Price = 10m, Currency = "KES" } },
        };
        var (price, _) = B1Mapper.MapItemPrice(item, 1, "KES");
        Assert.Null(price);
    }

    [Fact]
    public void MapSpecialPrice_SetsCustomerExternalId()
    {
        var (price, _) = B1Mapper.MapSpecialPrice(new B1SpecialPrice
        {
            CardCode = "C200",
            ItemCode = "A100",
            Price = 42m,
            Currency = "KES",
        }, "KES");

        Assert.NotNull(price);
        Assert.Equal("C200", price!.CustomerExternalID);
        Assert.Equal("C200/A100", price.ExternalID);
        Assert.Equal("42", price.Amount);
    }

    [Fact]
    public void MapSpecialPrice_SkipsZeroPrice()
    {
        var (price, _) = B1Mapper.MapSpecialPrice(new B1SpecialPrice
        {
            CardCode = "C200", ItemCode = "A100", Price = 0m, Currency = "KES",
        }, "KES");
        Assert.Null(price);
    }

    [Fact]
    public void MapBusinessPartner_MapsAddressesAndCredit()
    {
        var (c, _) = B1Mapper.MapBusinessPartner(new B1BusinessPartner
        {
            CardCode = "C200",
            CardName = "Acme Ltd",
            FederalTaxID = "P051234567X",
            CreditLimit = 250000m,
            BPAddresses =
            {
                new B1Address { Street = "1 Main St", City = "Nairobi", Country = "KE", ZipCode = "00100" },
                new B1Address { Street = "", City = "Nowhere" }, // skipped
            },
        });

        Assert.Equal("C200", c.ExternalID);
        Assert.Equal("Acme Ltd", c.Name);
        Assert.Equal("P051234567X", c.TaxID);
        Assert.Equal("250000", c.CreditLimit);
        Assert.Single(c.Addresses);
        Assert.Equal("1 Main St", c.Addresses[0].Line1);
        Assert.Equal("KE", c.Addresses[0].Country);
    }

    [Theory]
    [InlineData("2026-09-10", "13:45:00", "2026-09-10T13:45:00")]
    [InlineData("2026-09-10", "", "2026-09-10")]
    [InlineData("2026-09-10T00:00:00", "09:00:00", "2026-09-10T09:00:00")]
    public void Stamp_JoinsDateAndTime(string date, string time, string want)
    {
        Assert.Equal(want, B1Client.Stamp(date, time));
    }

    [Theory]
    [InlineData("", "(UpdateDate gt '2026-09-10' or (UpdateDate eq '2026-09-10' and UpdateTime ge '13:45:00'))")]
    [InlineData("CardType eq 'cCustomer'", "(UpdateDate gt '2026-09-10' or (UpdateDate eq '2026-09-10' and UpdateTime ge '13:45:00')) and CardType eq 'cCustomer'")]
    public void DeltaFilter_SubDayCursor(string extra, string want)
    {
        Assert.Equal(want, B1Client.DeltaFilter("2026-09-10T13:45:00", extra.Length == 0 ? null : extra));
    }

    [Fact]
    public void DeltaFilter_DateOnlyCursor()
    {
        Assert.Equal("UpdateDate ge '2026-09-10'", B1Client.DeltaFilter("2026-09-10", null));
        Assert.Equal("", B1Client.DeltaFilter("", null));
    }
}
