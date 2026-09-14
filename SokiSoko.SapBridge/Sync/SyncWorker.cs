using SokiSoko.SapBridge.Config;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SokiSoko.SapBridge.Sync;

/// <summary>
/// Periodic loop: poll the configured ERP for changed products/customers/prices (delta on
/// the source's cursor) plus a full stock scan, push each entity's records to SokiSoko's
/// ingest endpoint in batches, then advance the local cursor. A failed push leaves the
/// cursor untouched so the next run retries (at-least-once; the server apply is idempotent).
/// </summary>
public sealed class SyncWorker : BackgroundService
{
    private readonly ErpSourceFactory _sourceFactory;
    private readonly SokisokoClient _sokisoko;
    private readonly CursorStore _cursors;
    private readonly RuntimeSettings _settings;
    private readonly SyncStatus _status;
    private readonly ILogger<SyncWorker> _log;

    public SyncWorker(ErpSourceFactory sourceFactory, SokisokoClient sokisoko, CursorStore cursors,
        RuntimeSettings settings, SyncStatus status, ILogger<SyncWorker> log)
    {
        _sourceFactory = sourceFactory;
        _sokisoko = sokisoko;
        _cursors = cursors;
        _settings = settings;
        _status = status;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var s = _settings.Current;
            if (s.IsConfigured)
            {
                _status.Begin();
                try
                {
                    var records = await RunOnceAsync(stoppingToken);
                    _status.Complete(records);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "sync run failed; retrying at next interval");
                    _status.Fail(ex);
                    // Tell the server why, so a bridge that cannot reach its ERP reads as
                    // broken in the sync log rather than merely idle.
                    await ReportFailureAsync(ex, stoppingToken);
                }
            }
            var interval = TimeSpan.FromMinutes(Math.Clamp(s.IntervalMinutes, 5, 1440));
            await Task.Delay(interval, stoppingToken);
        }
    }

    private static string AgentVersion =>
        typeof(SyncWorker).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    /// <summary>A push carrying only the agent report; the server logs it as a failed poll.</summary>
    private async Task ReportFailureAsync(Exception ex, CancellationToken ct)
    {
        try
        {
            await _sokisoko.IngestAsync(new IngestBatch
            {
                Agent = new AgentReport { Version = AgentVersion, Error = ex.Message },
            }, ct);
        }
        catch (Exception report)
        {
            _log.LogWarning(report, "could not report the failure to SokiSoko either");
        }
    }

    private async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var source = _sourceFactory.Current();
        var n = 0;
        await SyncCodesAsync(source, ct);
        n += await SyncEntityAsync<InboundProduct>(source, "product",
            (src, since, apply) => src.PollProductsAsync(since, apply, ct));
        n += await SyncEntityAsync<InboundCustomer>(source, "customer",
            (src, since, apply) => src.PollCustomersAsync(since, apply, ct));
        n += await SyncStockAsync(source, ct);
        n += await SyncEntityAsync<InboundPrice>(source, "price",
            (src, since, apply) => src.PollPricesAsync(since, apply, ct));
        return n;
    }

    private async Task<int> SyncEntityAsync<T>(IErpSource source, string entity,
        Func<IErpSource, string, Func<T, Task>, Task<string>> poll)
    {
        var since = _cursors.Get(entity);
        var batch = new List<T>();
        var cursor = await poll(source, since, rec =>
        {
            batch.Add(rec);
            return Task.CompletedTask;
        });
        return await PushAsync(entity, batch, cursor, CancellationToken.None);
    }

    /// <summary>
    /// Codes go first so a product applied later can be labelled with its group name, and
    /// so warehouse mapping screens have names before the stock arrives.
    /// </summary>
    private async Task SyncCodesAsync(IErpSource source, CancellationToken ct)
    {
        if (source is not IErpCodeSource lister) return;
        var codes = await lister.PollCodesAsync(ct);
        if (codes.Count == 0) return;
        await _sokisoko.IngestAsync(new IngestBatch
        {
            Codes = codes,
            Agent = new AgentReport { Version = AgentVersion },
        }, ct);
        _log.LogInformation("codes: pushed {Count} names", codes.Count);
    }

    private async Task<int> SyncStockAsync(IErpSource source, CancellationToken ct)
    {
        var batch = new List<InboundStock>();
        await source.PollStockAsync(rec =>
        {
            batch.Add(rec);
            return Task.CompletedTask;
        }, ct);
        return await PushAsync("stock", batch, "", ct);
    }

    private async Task<int> PushAsync<T>(string entity, List<T> records, string cursor, CancellationToken ct)
    {
        if (records.Count == 0)
        {
            if (cursor.Length > 0) _cursors.Set(entity, cursor);
            return 0;
        }
        var size = Math.Max(1, _settings.Current.BatchSize);
        for (var i = 0; i < records.Count; i += size)
        {
            var batch = new IngestBatch { Agent = new AgentReport { Version = AgentVersion } };
            var slice = records.GetRange(i, Math.Min(size, records.Count - i));
            switch (entity)
            {
                case "product": batch.Products = slice.Cast<InboundProduct>().ToList(); break;
                case "customer": batch.Customers = slice.Cast<InboundCustomer>().ToList(); break;
                case "stock": batch.Stock = slice.Cast<InboundStock>().ToList(); break;
                case "price": batch.Prices = slice.Cast<InboundPrice>().ToList(); break;
            }
            // Advance the cursor only with the final chunk: a mid-way failure re-reads the
            // whole delta next run (idempotent), never skips records.
            if (cursor.Length > 0 && i + size >= records.Count)
                batch.Cursors[entity] = cursor;
            await _sokisoko.IngestAsync(batch, ct);
        }
        if (cursor.Length > 0) _cursors.Set(entity, cursor);
        _log.LogInformation("{Entity}: pushed {Count} records, cursor {Cursor}", entity, records.Count, cursor);
        return records.Count;
    }
}
