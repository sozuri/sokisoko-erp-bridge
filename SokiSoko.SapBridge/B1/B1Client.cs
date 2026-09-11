using System.Net;
using System.Net.Http.Json;
using System.Web;
using Microsoft.Extensions.Logging;
using SokiSoko.SapBridge.Config;

namespace SokiSoko.SapBridge.B1;

/// <summary>
/// Reads from the B1 Service Layer: paged GETs with the session cookie, delta filters on
/// UpdateDate/UpdateTime. Mirrors the server-side sap_b1 provider's queries so both paths
/// normalize identically.
/// </summary>
public sealed class B1Client
{
    private const int PageSize = 100;

    private readonly HttpClient _http;
    private readonly B1SessionManager _session;
    private readonly RuntimeSettings _settings;
    private readonly ILogger<B1Client> _log;

    public B1Client(HttpClient http, B1SessionManager session, RuntimeSettings settings, ILogger<B1Client> log)
    {
        _http = http;
        _session = session;
        _settings = settings;
        _log = log;
    }

    public Task<B1Page<B1Item>> GetItemsAsync(string since, string select, CancellationToken ct) =>
        GetPageAsync<B1Item>("Items", DeltaFilter(since, null), select, "UpdateDate asc,UpdateTime asc", ct);

    public Task<B1Page<B1Item>> GetItemsStockAsync(CancellationToken ct) =>
        GetPageAsync<B1Item>("Items", null, "ItemCode,ItemWarehouseInfoCollection", null, ct);

    public Task<B1Page<B1BusinessPartner>> GetCustomersAsync(string since, CancellationToken ct) =>
        GetPageAsync<B1BusinessPartner>("BusinessPartners", DeltaFilter(since, "CardType eq 'cCustomer'"),
            "CardCode,CardName,FederalTaxID,CreditLimit,UpdateDate,UpdateTime,BPAddresses", "UpdateDate asc,UpdateTime asc", ct);

    public Task<B1Page<B1SpecialPrice>> GetSpecialPricesAsync(string since, CancellationToken ct) =>
        GetPageAsync<B1SpecialPrice>("SpecialPrices", DeltaFilter(since, null),
            "CardCode,ItemCode,Price,Currency,UpdateDate,UpdateTime", "UpdateDate asc,UpdateTime asc", ct);

    public Task<B1Page<T>> GetNextPageAsync<T>(string nextLink, CancellationToken ct) =>
        GetPageAsync<T>(nextLink, null, null, null, ct);

    private async Task<B1Page<T>> GetPageAsync<T>(string path, string? filter, string? select, string? orderby, CancellationToken ct)
    {
        var query = HttpUtility.ParseQueryString("");
        if (!string.IsNullOrEmpty(filter)) query["$filter"] = filter;
        if (!string.IsNullOrEmpty(select)) query["$select"] = select;
        if (!string.IsNullOrEmpty(orderby)) query["$orderby"] = orderby;
        var qs = query.ToString() ?? "";
        var url = $"{_settings.Current.SapB1BaseUrl.TrimEnd('/')}/b1s/v1/{path}" + (qs.Length > 0 ? "?" + qs : "");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var sessionId = await _session.GetSessionIdAsync(ct);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Accept", "application/json");
            req.Headers.Add("Prefer", $"odata.maxpagesize={PageSize}");
            req.Headers.Add("Cookie", $"B1SESSION={sessionId}");

            using var resp = await _http.SendAsync(req, ct);
            if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _session.Invalidate();
                continue;
            }
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"B1 GET {path} returned {(int)resp.StatusCode}: {Truncate(body, 400)}");
            }
            return await resp.Content.ReadFromJsonAsync<B1Page<T>>(cancellationToken: ct) ?? new B1Page<T>();
        }
        throw new InvalidOperationException($"B1 GET {path} unauthorized after re-login");
    }

    /// <summary>
    /// Cursor is YYYY-MM-DDTHH:MM:SS (or a bare date from older runs). Same-day records are
    /// re-read with UpdateTime ge, so a crash mid-page never loses a record.
    /// </summary>
    public static string DeltaFilter(string since, string? extra)
    {
        var parts = new List<string>();
        if (since.Contains('T'))
        {
            var cut = since.Split('T', 2);
            parts.Add($"(UpdateDate gt '{cut[0]}' or (UpdateDate eq '{cut[0]}' and UpdateTime ge '{cut[1]}'))");
        }
        else if (since.Length > 0)
        {
            parts.Add($"UpdateDate ge '{since}'");
        }
        if (!string.IsNullOrEmpty(extra)) parts.Add(extra);
        return string.Join(" and ", parts);
    }

    /// <summary>Joins B1's UpdateDate + UpdateTime into one comparable cursor stamp.</summary>
    public static string Stamp(string? date, string? time)
    {
        var d = date is { Length: >= 10 } ? date[..10] : date ?? "";
        if (d.Length == 0 || string.IsNullOrEmpty(time)) return d;
        var t = time.Length > 8 ? time[..8] : time;
        return $"{d}T{t}";
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];
}
