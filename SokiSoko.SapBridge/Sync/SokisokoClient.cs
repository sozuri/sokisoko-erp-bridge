using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SokiSoko.SapBridge.Config;

namespace SokiSoko.SapBridge.Sync;

/// <summary>Posts normalized batches to the Sokisoko ingest endpoint with the admin API key.</summary>
public sealed class SokisokoClient
{
    private readonly HttpClient _http;
    private readonly RuntimeSettings _settings;
    private readonly ILogger<SokisokoClient> _log;

    public SokisokoClient(HttpClient http, RuntimeSettings settings, ILogger<SokisokoClient> log)
    {
        _http = http;
        _settings = settings;
        _log = log;
    }

    public async Task<int> IngestAsync(IngestBatch batch, CancellationToken ct)
    {
        var s = _settings.Current;
        var url = $"{s.SokisokoBaseUrl.TrimEnd('/')}/admin/erp/connections/{s.SokisokoConnectionId}/ingest";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(batch),
        };
        req.Headers.Add("Authorization", $"Bearer {s.SokisokoApiKey}");

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"ingest returned {(int)resp.StatusCode}: {Truncate(body, 400)}");
        }
        var ack = await resp.Content.ReadFromJsonAsync<IngestAck>(cancellationToken: ct);
        _log.LogInformation("ingest applied {Applied} records", ack?.Applied ?? 0);
        return ack?.Applied ?? 0;
    }

    private sealed class IngestAck
    {
        [System.Text.Json.Serialization.JsonPropertyName("applied")] public int Applied { get; set; }
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];
}
