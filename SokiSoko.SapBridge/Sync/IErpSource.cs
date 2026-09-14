namespace SokiSoko.SapBridge.Sync;

/// <summary>
/// One ERP the bridge can pull from. Implementations poll changed records since a
/// per-entity cursor and hand them to apply callbacks; the returned cursor is persisted
/// only after the batch lands in SokiSoko.
/// </summary>
public interface IErpSource
{
    /// <summary>Provider name on the SokiSoko connection ("sap_b1", "odoo").</summary>
    string Provider { get; }

    Task<string> PollProductsAsync(string since, Func<InboundProduct, Task> apply, CancellationToken ct);
    Task<string> PollCustomersAsync(string since, Func<InboundCustomer, Task> apply, CancellationToken ct);
    Task<string> PollPricesAsync(string since, Func<InboundPrice, Task> apply, CancellationToken ct);

    /// <summary>Stock is a full scan on every ERP (no reliable delta); cursor unused.</summary>
    Task PollStockAsync(Func<InboundStock, Task> apply, CancellationToken ct);

    /// <summary>Cheap connectivity check for the setup console's test button.</summary>
    Task<string> TestAsync(CancellationToken ct);
}

/// <summary>
/// Sources that can name their own codes. Optional: a source without it still syncs, the
/// mapping screens just show bare codes.
/// </summary>
public interface IErpCodeSource
{
    Task<List<InboundCode>> PollCodesAsync(CancellationToken ct);
}
