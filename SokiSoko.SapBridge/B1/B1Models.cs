using System.Text.Json.Serialization;

namespace SokiSoko.SapBridge.B1;

// Service Layer wire shapes (only the fields the bridge reads).

public sealed class B1Item
{
    [JsonPropertyName("ItemCode")] public string ItemCode { get; set; } = "";
    [JsonPropertyName("ItemName")] public string? ItemName { get; set; }
    [JsonPropertyName("ForeignName")] public string? ForeignName { get; set; }
    [JsonPropertyName("InventoryUOM")] public string? InventoryUOM { get; set; }
    [JsonPropertyName("ItemsGroupCode")] public int ItemsGroupCode { get; set; }
    [JsonPropertyName("UpdateDate")] public string? UpdateDate { get; set; }
    [JsonPropertyName("UpdateTime")] public string? UpdateTime { get; set; }
    [JsonPropertyName("ItemPrices")] public List<B1ItemPrice> ItemPrices { get; set; } = new();
    [JsonPropertyName("ItemWarehouseInfoCollection")] public List<B1WarehouseRow> Warehouses { get; set; } = new();
}

public sealed class B1ItemPrice
{
    [JsonPropertyName("PriceList")] public int PriceList { get; set; }
    [JsonPropertyName("Price")] public decimal Price { get; set; }
    [JsonPropertyName("Currency")] public string? Currency { get; set; }
}

public sealed class B1WarehouseRow
{
    [JsonPropertyName("WarehouseCode")] public string? WarehouseCode { get; set; }
    [JsonPropertyName("InStock")] public decimal InStock { get; set; }
}

public sealed class B1BusinessPartner
{
    [JsonPropertyName("CardCode")] public string CardCode { get; set; } = "";
    [JsonPropertyName("CardName")] public string? CardName { get; set; }
    [JsonPropertyName("FederalTaxID")] public string? FederalTaxID { get; set; }
    [JsonPropertyName("CreditLimit")] public decimal CreditLimit { get; set; }
    [JsonPropertyName("UpdateDate")] public string? UpdateDate { get; set; }
    [JsonPropertyName("UpdateTime")] public string? UpdateTime { get; set; }
    [JsonPropertyName("BPAddresses")] public List<B1Address> BPAddresses { get; set; } = new();
}

public sealed class B1Address
{
    [JsonPropertyName("Street")] public string? Street { get; set; }
    [JsonPropertyName("Block")] public string? Block { get; set; }
    [JsonPropertyName("City")] public string? City { get; set; }
    [JsonPropertyName("State")] public string? State { get; set; }
    [JsonPropertyName("ZipCode")] public string? ZipCode { get; set; }
    [JsonPropertyName("Country")] public string? Country { get; set; }
}

public sealed class B1SpecialPrice
{
    [JsonPropertyName("CardCode")] public string? CardCode { get; set; }
    [JsonPropertyName("ItemCode")] public string? ItemCode { get; set; }
    [JsonPropertyName("Price")] public decimal Price { get; set; }
    [JsonPropertyName("Currency")] public string? Currency { get; set; }
    [JsonPropertyName("UpdateDate")] public string? UpdateDate { get; set; }
    [JsonPropertyName("UpdateTime")] public string? UpdateTime { get; set; }
}

public sealed class B1Page<T>
{
    [JsonPropertyName("value")] public List<T> Value { get; set; } = new();
    [JsonPropertyName("odata.nextLink")] public string? NextLink { get; set; }
    [JsonPropertyName("@odata.nextLink")] public string? NextLinkAlt { get; set; }
}
