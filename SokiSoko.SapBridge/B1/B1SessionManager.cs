using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SokiSoko.SapBridge.Config;

namespace SokiSoko.SapBridge.B1;

/// <summary>
/// Holds the B1 Service Layer session cookie, logging in lazily and re-logging in on
/// expiry (B1 sessions default to 30 min; we renew a little early).
/// </summary>
public sealed class B1SessionManager
{
    private static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(2);

    private readonly HttpClient _http;
    private readonly RuntimeSettings _settings;
    private readonly ILogger<B1SessionManager> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _sessionId;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public B1SessionManager(HttpClient http, RuntimeSettings settings, ILogger<B1SessionManager> log)
    {
        _http = http;
        _settings = settings;
        _log = log;
    }

    public async Task<string> GetSessionIdAsync(CancellationToken ct)
    {
        if (_sessionId is not null && DateTimeOffset.UtcNow < _expiresAt - RenewBefore)
            return _sessionId;

        await _gate.WaitAsync(ct);
        try
        {
            if (_sessionId is not null && DateTimeOffset.UtcNow < _expiresAt - RenewBefore)
                return _sessionId;

            var s = _settings.Current;
            var url = $"{s.SapB1BaseUrl.TrimEnd('/')}/b1s/v1/Login";
            var resp = await _http.PostAsJsonAsync(url, new
            {
                CompanyDB = s.SapB1CompanyDb,
                UserName = s.SapB1Username,
                Password = s.SapB1Password,
            }, ct);
            resp.EnsureSuccessStatusCode();

            var body = await resp.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: ct)
                       ?? throw new InvalidOperationException("B1 login returned an empty body");
            _sessionId = body.SessionId ?? throw new InvalidOperationException("B1 login returned no SessionId");
            var minutes = body.SessionTimeout > 0 ? body.SessionTimeout : 30;
            _expiresAt = DateTimeOffset.UtcNow.AddMinutes(minutes);
            _log.LogInformation("B1 login ok; session valid for {Minutes} min", minutes);
            return _sessionId;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Drops the cached session so the next call logs in again (after a 401).</summary>
    public void Invalidate() => _sessionId = null;

    private sealed class LoginResponse
    {
        [JsonPropertyName("SessionId")] public string? SessionId { get; set; }
        [JsonPropertyName("SessionTimeout")] public int SessionTimeout { get; set; }
    }
}
