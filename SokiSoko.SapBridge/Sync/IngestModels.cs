using System.Text.Json.Serialization;

namespace SokiSoko.SapBridge.Sync;

// Wire shapes posted to /admin/erp/connections/{id}/ingest — field names match the Go
// ingestBatch JSON tags exactly.

public sealed class IngestBatch
{
    [JsonPropertyName("products")] public List<InboundProduct> Products { get; set; } = new();
    [JsonPropertyName("stock")] public List<InboundStock> Stock { get; set; } = new();
    [JsonPropertyName("prices")] public List<InboundPrice> Prices { get; set; } = new();
    [JsonPropertyName("customers")] public List<InboundCustomer> Customers { get; set; } = new();
    /// <summary>Names the ERP gives its own codes, so mapping screens show "101 — First Floor
    /// Warehouse" rather than a bare number.</summary>
    [JsonPropertyName("codes")] public List<InboundCode> Codes { get; set; } = new();
    [JsonPropertyName("cursors")] public Dictionary<string, string> Cursors { get; set; } = new();
    /// <summary>Without this a bridge that cannot reach its ERP is silent and merely looks idle.</summary>
    [JsonPropertyName("agent")] public AgentReport Agent { get; set; } = new();
}

public sealed class AgentReport
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("error")] public string Error { get; set; } = "";
}

public sealed class InboundCode
{
    /// <summary>"warehouse", "item_group", "manufacturer" or "supplier".</summary>
    [JsonPropertyName("Kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("Code")] public string Code { get; set; } = "";
    [JsonPropertyName("Name")] public string Name { get; set; } = "";
}

public static class CodeKinds
{
    public const string Warehouse = "warehouse";
    public const string ItemGroup = "item_group";
    public const string Manufacturer = "manufacturer";
    public const string Supplier = "supplier";
}

public sealed class InboundProduct
{
    [JsonPropertyName("ExternalID")] public string ExternalID { get; set; } = "";
    [JsonPropertyName("SKU")] public string SKU { get; set; } = "";
    [JsonPropertyName("Name")] public string Name { get; set; } = "";
    [JsonPropertyName("Description")] public string Description { get; set; } = "";
    [JsonPropertyName("BaseUnit")] public string BaseUnit { get; set; } = "";
    [JsonPropertyName("Cost")] public string Cost { get; set; } = "";
    [JsonPropertyName("Attributes")] public Dictionary<string, string>? Attributes { get; set; }
    [JsonPropertyName("QuantityOnHand")] public string QuantityOnHand { get; set; } = "";
}

public sealed class InboundStock
{
    [JsonPropertyName("SKU")] public string SKU { get; set; } = "";
    [JsonPropertyName("ExternalID")] public string ExternalID { get; set; } = "";
    [JsonPropertyName("Plant")] public string Plant { get; set; } = "";
    [JsonPropertyName("QuantityOnHand")] public string QuantityOnHand { get; set; } = "";
}

public sealed class InboundPrice
{
    [JsonPropertyName("SKU")] public string SKU { get; set; } = "";
    [JsonPropertyName("ExternalID")] public string ExternalID { get; set; } = "";
    [JsonPropertyName("CustomerExternalID")] public string CustomerExternalID { get; set; } = "";
    [JsonPropertyName("Currency")] public string Currency { get; set; } = "";
    [JsonPropertyName("Amount")] public string Amount { get; set; } = "";
    [JsonPropertyName("Unit")] public string Unit { get; set; } = "";
    [JsonPropertyName("MinQty")] public string MinQty { get; set; } = "";
    [JsonPropertyName("ValidFrom")] public string ValidFrom { get; set; } = "";
    [JsonPropertyName("ValidTo")] public string ValidTo { get; set; } = "";
    [JsonPropertyName("ConditionType")] public string ConditionType { get; set; } = "";
}

public sealed class InboundCustomer
{
    [JsonPropertyName("ExternalID")] public string ExternalID { get; set; } = "";
    [JsonPropertyName("Name")] public string Name { get; set; } = "";
    [JsonPropertyName("TaxID")] public string TaxID { get; set; } = "";
    [JsonPropertyName("PaymentTermsDays")] public string PaymentTermsDays { get; set; } = "";
    [JsonPropertyName("CreditLimit")] public string CreditLimit { get; set; } = "";
    [JsonPropertyName("GroupName")] public string GroupName { get; set; } = "";
    [JsonPropertyName("Addresses")] public List<InboundAddress> Addresses { get; set; } = new();
}

public sealed class InboundAddress
{
    [JsonPropertyName("Type")] public string Type { get; set; } = "";
    [JsonPropertyName("Line1")] public string Line1 { get; set; } = "";
    [JsonPropertyName("Line2")] public string Line2 { get; set; } = "";
    [JsonPropertyName("City")] public string City { get; set; } = "";
    [JsonPropertyName("Region")] public string Region { get; set; } = "";
    [JsonPropertyName("PostalCode")] public string PostalCode { get; set; } = "";
    [JsonPropertyName("Country")] public string Country { get; set; } = "";
}
