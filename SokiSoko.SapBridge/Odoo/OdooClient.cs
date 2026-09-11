using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SokiSoko.SapBridge.Config;

namespace SokiSoko.SapBridge.Odoo;

/// <summary>
/// Odoo JSON-RPC client: authenticate (cached uid), then search_read pages via
/// execute_kw. Mirrors the server-side odoo provider's queries so both paths
/// normalize identically.
/// </summary>
public sealed class OdooClient
{
    public const int PageSize = 200;

    private readonly HttpClient _http;
    private readonly RuntimeSettings _settings;
    private readonly ILogger<OdooClient> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _uid;

    public OdooClient(HttpClient http, RuntimeSettings settings, ILogger<OdooClient> log)
    {
        _http = http;
        _settings = settings;
        _log = log;
    }

    public async Task<int> GetUidAsync(CancellationToken ct)
    {
        if (_uid != 0) return _uid;
        await _gate.WaitAsync(ct);
        try
        {
            if (_uid != 0) return _uid;
            var s = _settings.Current;
            var result = await RpcAsync(s, "common", "authenticate",
                new object?[] { s.OdooDatabase, s.OdooUsername, s.OdooPassword, new { } }, ct);
            var uid = result.ValueKind == JsonValueKind.Number ? result.GetInt32() : 0;
            if (uid == 0)
                throw new InvalidOperationException("Odoo rejected the login — check database, username and password/API key");
            _uid = uid;
            _log.LogInformation("Odoo login ok (uid {Uid})", uid);
            return _uid;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate() => _uid = 0;

    /// <summary>Pages search_read over a model, invoking apply per record.</summary>
    public async Task SearchReadAsync(string model, object[] domain, string[] fields, string order,
        Dictionary<string, object>? extraKwargs, Func<JsonElement, Task> apply, CancellationToken ct)
    {
        var s = _settings.Current;
        var offset = 0;
        while (true)
        {
            var kwargs = new Dictionary<string, object>
            {
                ["fields"] = fields,
                ["limit"] = PageSize,
                ["offset"] = offset,
            };
            if (order.Length > 0) kwargs["order"] = order;
            if (extraKwargs is not null)
                foreach (var kv in extraKwargs) kwargs[kv.Key] = kv.Value;

            var uid = await GetUidAsync(ct);
            JsonElement result;
            try
            {
                result = await ExecuteKwAsync(s, uid, model, "search_read", new object[] { domain }, kwargs, ct);
            }
            catch (OdooAccessException)
            {
                Invalidate();
                uid = await GetUidAsync(ct);
                result = await ExecuteKwAsync(s, uid, model, "search_read", new object[] { domain }, kwargs, ct);
            }

            var count = 0;
            foreach (var row in result.EnumerateArray())
            {
                await apply(row);
                count++;
            }
            if (count < PageSize) return;
            offset += PageSize;
        }
    }

    private Task<JsonElement> ExecuteKwAsync(Config.BridgeSettings s, int uid, string model, string method,
        object[] args, Dictionary<string, object> kwargs, CancellationToken ct) =>
        RpcAsync(s, "object", "execute_kw",
            new object?[] { s.OdooDatabase, uid, s.OdooPassword, model, method, args, kwargs }, ct);

    private async Task<JsonElement> RpcAsync(Config.BridgeSettings s, string service, string method,
        object?[] args, CancellationToken ct)
    {
        var url = $"{s.OdooBaseUrl.TrimEnd('/')}/jsonrpc";
        var payload = new OdooRpcRequest
        {
            Params = new { service, method, args },
        };
        using var resp = await _http.PostAsJsonAsync(url, payload, ct);
        var body = await resp.Content.ReadFromJsonAsync<OdooRpcResponse>(cancellationToken: ct)
                   ?? throw new InvalidOperationException("Odoo returned an unreadable response");
        if (body.Error is not null)
        {
            var detail = body.Error.Data?.Message ?? body.Error.Message;
            if (body.Error.Code == 100 || detail.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase))
                throw new OdooAccessException(detail);
            throw new InvalidOperationException($"Odoo {service}.{method} failed: {detail}");
        }
        return body.Result ?? default;
    }
}

public sealed class OdooAccessException : Exception
{
    public OdooAccessException(string message) : base(message) { }
}
