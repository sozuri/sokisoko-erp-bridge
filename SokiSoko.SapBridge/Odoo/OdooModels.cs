using System.Text.Json;
using System.Text.Json.Serialization;

namespace SokiSoko.SapBridge.Odoo;

// JSON-RPC wire shapes for Odoo's /jsonrpc endpoint.

public sealed class OdooRpcRequest
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = "2.0";
    [JsonPropertyName("method")] public string Method { get; set; } = "call";
    [JsonPropertyName("id")] public int Id { get; set; } = Random.Shared.Next(1, int.MaxValue);
    [JsonPropertyName("params")] public required object Params { get; set; }
}

public sealed class OdooRpcResponse
{
    [JsonPropertyName("result")] public JsonElement? Result { get; set; }
    [JsonPropertyName("error")] public OdooRpcError? Error { get; set; }
}

public sealed class OdooRpcError
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("data")] public OdooRpcErrorData? Data { get; set; }
}

public sealed class OdooRpcErrorData
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
}

/// <summary>Odoo record values arrive loosely typed (false instead of "", [id,name] tuples).</summary>
public static class OdooValue
{
    public static string Str(JsonElement v)
    {
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.False or JsonValueKind.Null or JsonValueKind.Undefined => "",
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.Array when v.GetArrayLength() == 2 && v[1].ValueKind == JsonValueKind.String => v[1].GetString() ?? "",
            _ => "",
        };
    }

    public static double Num(JsonElement v) =>
        v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : 0;

    public static int Int(JsonElement v) => (int)Num(v);

    /// <summary>Decimal string; "" for 0 keeps optional fields untouched by the apply funnel.</summary>
    public static string NumStr(JsonElement v)
    {
        var d = Num(v);
        return d == 0 ? "" : d.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    }
}
