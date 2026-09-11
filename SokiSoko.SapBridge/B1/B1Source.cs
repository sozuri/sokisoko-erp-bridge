using SokiSoko.SapBridge.Config;
using SokiSoko.SapBridge.Sync;

namespace SokiSoko.SapBridge.B1;

/// <summary>SAP Business One source: Service Layer delta polls mapped to ingest records.</summary>
public sealed class B1Source : IErpSource
{
    private readonly B1Client _b1;
    private readonly RuntimeSettings _settings;

    public B1Source(B1Client b1, RuntimeSettings settings)
    {
        _b1 = b1;
        _settings = settings;
    }

    public string Provider => "sap_b1";

    public async Task<string> TestAsync(CancellationToken ct)
    {
        var page = await _b1.GetItemsAsync("", "ItemCode", ct);
        return $"login ok; Items readable ({page.Value.Count} on first page)";
    }

    public async Task<string> PollProductsAsync(string since, Func<InboundProduct, Task> apply, CancellationToken ct)
    {
        var cursor = since;
        var page = await _b1.GetItemsAsync(since,
            "ItemCode,ItemName,ForeignName,InventoryUOM,ItemsGroupCode,Valid,UpdateDate,UpdateTime", ct);
        while (true)
        {
            foreach (var item in page.Value)
            {
                var (product, changed) = B1Mapper.MapItem(item);
                if (product.ExternalID.Length > 0) await apply(product);
                if (string.CompareOrdinal(changed, cursor) > 0) cursor = changed;
            }
            var next = page.NextLink ?? page.NextLinkAlt;
            if (string.IsNullOrEmpty(next)) break;
            page = await _b1.GetNextPageAsync<B1Item>(next.TrimPrefix("/b1s/v1/"), ct);
        }
        return cursor;
    }

    public async Task<string> PollCustomersAsync(string since, Func<InboundCustomer, Task> apply, CancellationToken ct)
    {
        var cursor = since;
        var page = await _b1.GetCustomersAsync(since, ct);
        while (true)
        {
            foreach (var bp in page.Value)
            {
                var (customer, changed) = B1Mapper.MapBusinessPartner(bp);
                if (customer.ExternalID.Length > 0) await apply(customer);
                if (string.CompareOrdinal(changed, cursor) > 0) cursor = changed;
            }
            var next = page.NextLink ?? page.NextLinkAlt;
            if (string.IsNullOrEmpty(next)) break;
            page = await _b1.GetNextPageAsync<B1BusinessPartner>(next.TrimPrefix("/b1s/v1/"), ct);
        }
        return cursor;
    }

    public async Task PollStockAsync(Func<InboundStock, Task> apply, CancellationToken ct)
    {
        var page = await _b1.GetItemsStockAsync(ct);
        while (true)
        {
            foreach (var item in page.Value)
                foreach (var s in B1Mapper.MapStock(item))
                    await apply(s);
            var next = page.NextLink ?? page.NextLinkAlt;
            if (string.IsNullOrEmpty(next)) break;
            page = await _b1.GetNextPageAsync<B1Item>(next.TrimPrefix("/b1s/v1/"), ct);
        }
    }

    public async Task<string> PollPricesAsync(string since, Func<InboundPrice, Task> apply, CancellationToken ct)
    {
        var s = _settings.Current;
        var cursor = since;

        var page = await _b1.GetItemsAsync(since, "ItemCode,ItemPrices,UpdateDate,UpdateTime", ct);
        while (true)
        {
            foreach (var item in page.Value)
            {
                var (price, changed) = B1Mapper.MapItemPrice(item, s.SapB1PriceList, s.SapB1Currency);
                if (price is not null) await apply(price);
                if (string.CompareOrdinal(changed, cursor) > 0) cursor = changed;
            }
            var next = page.NextLink ?? page.NextLinkAlt;
            if (string.IsNullOrEmpty(next)) break;
            page = await _b1.GetNextPageAsync<B1Item>(next.TrimPrefix("/b1s/v1/"), ct);
        }

        var spage = await _b1.GetSpecialPricesAsync(since, ct);
        while (true)
        {
            foreach (var sp in spage.Value)
            {
                var (price, changed) = B1Mapper.MapSpecialPrice(sp, s.SapB1Currency);
                if (price is not null) await apply(price);
                if (string.CompareOrdinal(changed, cursor) > 0) cursor = changed;
            }
            var next = spage.NextLink ?? spage.NextLinkAlt;
            if (string.IsNullOrEmpty(next)) break;
            spage = await _b1.GetNextPageAsync<B1SpecialPrice>(next.TrimPrefix("/b1s/v1/"), ct);
        }
        return cursor;
    }
}
