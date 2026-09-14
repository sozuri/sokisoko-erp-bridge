using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SokiSoko.SapBridge.Config;
using SokiSoko.SapBridge.Sync;

namespace SokiSoko.SapBridge.Sql;

/// <summary>
/// Reads B1 master data straight from the company database, for installations where the
/// Service Layer is unavailable. Read-only by construction: every statement here is a
/// SELECT, and the login it connects as should hold SELECT and nothing else.
///
/// Emits the same records as <see cref="B1.B1Source"/> and the same cursor format, so the
/// two providers are interchangeable without re-backfilling.
/// </summary>
public sealed class SqlSource : IErpSource, IErpCodeSource
{
    private readonly RuntimeSettings _settings;
    private readonly ILogger<SqlSource> _log;

    public SqlSource(RuntimeSettings settings, ILogger<SqlSource> log)
    {
        _settings = settings;
        _log = log;
    }

    /// <summary>Reported to SokiSoko as sap_b1 - it is the same data from the same ERP.</summary>
    public string Provider => "sap_b1";

    private string ConnectionString()
    {
        var s = _settings.Current;
        var b = new SqlConnectionStringBuilder
        {
            DataSource = s.SqlServer,
            InitialCatalog = s.SqlDatabase,
            ConnectTimeout = 15,
            CommandTimeout = 180,
            ApplicationName = "SokiSoko ERP Bridge",
            TrustServerCertificate = true,
            Encrypt = false,
        };
        if (s.SqlIntegratedSecurity)
        {
            b.IntegratedSecurity = true;
        }
        else
        {
            b.UserID = s.SqlUsername;
            b.Password = s.SqlPassword;
        }
        return b.ConnectionString;
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var c = new SqlConnection(ConnectionString());
        await c.OpenAsync(ct);
        return c;
    }

    public async Task<string> TestAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM OITM";
        var n = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        return $"connected as {_settings.Current.SqlUsername}; Items readable ({n} rows)";
    }

    // ---- products -------------------------------------------------------------------

    private const string ProductSql = @"
SELECT T0.ItemCode, T0.ItemName, T0.FrgnName, T0.InvntryUom, T0.ItmsGrpCod,
       T0.validFor, T0.LastPurPrc, T0.UpdateDate, UpdateTime = T0.UpdateTS,
       GroupName = T1.ItmsGrpNam
FROM OITM T0
LEFT JOIN OITB T1 ON T1.ItmsGrpCod = T0.ItmsGrpCod
{0}
ORDER BY T0.UpdateDate, T0.UpdateTS, T0.ItemCode";

    public async Task<string> PollProductsAsync(string since, Func<InboundProduct, Task> apply, CancellationToken ct)
    {
        var cursor = since;
        await using var c = await OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = string.Format(ProductSql, DeltaWhere(since, "T0"));
        AddDeltaParams(cmd, since);
        cmd.CommandTimeout = 180;

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var p = new InboundProduct
            {
                ExternalID = SqlMapper.S(rd["ItemCode"]),
                SKU = SqlMapper.S(rd["ItemCode"]),
                Name = SqlMapper.S(rd["ItemName"]),
                Description = SqlMapper.S(rd["FrgnName"]),
                BaseUnit = SqlMapper.S(rd["InvntryUom"]),
                Cost = SqlMapper.N(rd["LastPurPrc"]),
                // Numeric code, not the name - the server resolves names from the codes
                // list and uses this to file the product under its mapped category.
                Attributes = new Dictionary<string, string>
                {
                    ["b1_items_group"] = SqlMapper.S(rd["ItmsGrpCod"]),
                },
            };
            if (p.ExternalID.Length == 0) continue;
            await apply(p);
            var changed = SqlMapper.Stamp(rd["UpdateDate"] as DateTime?, rd["UpdateTime"] as int?);
            if (string.CompareOrdinal(changed, cursor) > 0) cursor = changed;
        }
        return cursor;
    }

    // ---- customers ------------------------------------------------------------------

    private const string CustomerSql = @"
SELECT T0.CardCode, T0.CardName, T0.LicTradNum, T0.CreditLine, T0.UpdateDate, UpdateTime = T0.UpdateTS,
       GroupName = T1.GroupName, PayDays = T2.ExtraDays
FROM OCRD T0
LEFT JOIN OCRG T1 ON T1.GroupCode = T0.GroupCode
LEFT JOIN OCTG T2 ON T2.GroupNum  = T0.GroupNum
WHERE T0.CardType = 'C'{1}
ORDER BY T0.UpdateDate, T0.UpdateTS, T0.CardCode";

    private const string AddressSql = @"
SELECT CardCode, AdresType, Street, Block, City, State, ZipCode, Country
FROM CRD1
WHERE CardCode IN (SELECT CardCode FROM OCRD WHERE CardType = 'C')";

    public async Task<string> PollCustomersAsync(string since, Func<InboundCustomer, Task> apply, CancellationToken ct)
    {
        var cursor = since;

        // One pass for addresses keeps this to two round trips rather than one per customer.
        var addresses = new Dictionary<string, List<InboundAddress>>(StringComparer.OrdinalIgnoreCase);
        await using (var ac = await OpenAsync(ct))
        await using (var acmd = ac.CreateCommand())
        {
            acmd.CommandText = AddressSql;
            acmd.CommandTimeout = 180;
            await using var ard = await acmd.ExecuteReaderAsync(ct);
            while (await ard.ReadAsync(ct))
            {
                var code = SqlMapper.S(ard["CardCode"]);
                if (code.Length == 0) continue;
                if (!addresses.TryGetValue(code, out var list)) addresses[code] = list = new List<InboundAddress>();
                list.Add(new InboundAddress
                {
                    Type = SqlMapper.AddressType(SqlMapper.S(ard["AdresType"])),
                    Line1 = SqlMapper.S(ard["Street"]),
                    Line2 = SqlMapper.S(ard["Block"]),
                    City = SqlMapper.S(ard["City"]),
                    Region = SqlMapper.S(ard["State"]),
                    PostalCode = SqlMapper.S(ard["ZipCode"]),
                    Country = SqlMapper.S(ard["Country"]),
                });
            }
        }

        await using var c = await OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = string.Format(CustomerSql, "", DeltaAnd(since, "T0"));
        AddDeltaParams(cmd, since);
        cmd.CommandTimeout = 180;

        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var code = SqlMapper.S(rd["CardCode"]);
            if (code.Length == 0) continue;
            await apply(new InboundCustomer
            {
                ExternalID = code,
                Name = SqlMapper.S(rd["CardName"]),
                TaxID = SqlMapper.S(rd["LicTradNum"]),
                CreditLimit = SqlMapper.N(rd["CreditLine"]),
                PaymentTermsDays = SqlMapper.N(rd["PayDays"]),
                GroupName = SqlMapper.S(rd["GroupName"]),
                Addresses = addresses.TryGetValue(code, out var a) ? a : new List<InboundAddress>(),
            });
            var changed = SqlMapper.Stamp(rd["UpdateDate"] as DateTime?, rd["UpdateTime"] as int?);
            if (string.CompareOrdinal(changed, cursor) > 0) cursor = changed;
        }
        return cursor;
    }

    // ---- prices ---------------------------------------------------------------------

    private const string PriceSql = @"
SELECT T0.ItemCode, T0.Price, T0.Currency, T1.UpdateDate, UpdateTime = T1.UpdateTS
FROM ITM1 T0
INNER JOIN OITM T1 ON T1.ItemCode = T0.ItemCode
WHERE T0.PriceList = @priceList AND T0.Price > 0{1}
ORDER BY T1.UpdateDate, T1.UpdateTS, T0.ItemCode";

    private const string SpecialPriceSql = @"
SELECT CardCode, ItemCode, Price, Currency
FROM OSPP";

    public async Task<string> PollPricesAsync(string since, Func<InboundPrice, Task> apply, CancellationToken ct)
    {
        var cursor = since;
        var s = _settings.Current;

        await using (var c = await OpenAsync(ct))
        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = string.Format(PriceSql, "", DeltaAnd(since, "T1"));
            cmd.Parameters.Add(new SqlParameter("@priceList", SqlDbType.Int) { Value = s.SapB1PriceList });
            AddDeltaParams(cmd, since);
            cmd.CommandTimeout = 180;

            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                var sku = SqlMapper.S(rd["ItemCode"]);
                if (sku.Length == 0) continue;
                var cur = SqlMapper.S(rd["Currency"]);
                await apply(new InboundPrice
                {
                    SKU = sku,
                    ExternalID = sku,
                    Amount = SqlMapper.N(rd["Price"]),
                    Currency = cur.Length > 0 ? cur : s.SapB1Currency,
                });
                var changed = SqlMapper.Stamp(rd["UpdateDate"] as DateTime?, rd["UpdateTime"] as int?);
                if (string.CompareOrdinal(changed, cursor) > 0) cursor = changed;
            }
        }

        // Special prices carry no usable change stamp, so they are a full pass each run,
        // exactly as the Service Layer provider treats them.
        await using (var c2 = await OpenAsync(ct))
        await using (var cmd2 = c2.CreateCommand())
        {
            cmd2.CommandText = SpecialPriceSql;
            cmd2.CommandTimeout = 180;
            await using var rd2 = await cmd2.ExecuteReaderAsync(ct);
            while (await rd2.ReadAsync(ct))
            {
                var sku = SqlMapper.S(rd2["ItemCode"]);
                if (sku.Length == 0) continue;
                var cur = SqlMapper.S(rd2["Currency"]);
                await apply(new InboundPrice
                {
                    SKU = sku,
                    ExternalID = sku,
                    CustomerExternalID = SqlMapper.S(rd2["CardCode"]),
                    Amount = SqlMapper.N(rd2["Price"]),
                    Currency = cur.Length > 0 ? cur : s.SapB1Currency,
                });
            }
        }

        return cursor;
    }

    // ---- stock ----------------------------------------------------------------------

    private const string StockSql = @"
SELECT T0.ItemCode, T0.WhsCode, T0.OnHand
FROM OITW T0
INNER JOIN OITM T1 ON T1.ItemCode = T0.ItemCode
WHERE T0.OnHand <> 0 OR T0.IsCommited <> 0 OR T0.OnOrder <> 0";

    public async Task PollStockAsync(Func<InboundStock, Task> apply, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = StockSql;
        cmd.CommandTimeout = 180;
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var sku = SqlMapper.S(rd["ItemCode"]);
            if (sku.Length == 0) continue;
            await apply(new InboundStock
            {
                SKU = sku,
                ExternalID = sku,
                Plant = SqlMapper.S(rd["WhsCode"]),
                QuantityOnHand = SqlMapper.N(rd["OnHand"]),
            });
        }
    }

    // ---- codes ----------------------------------------------------------------------

    public async Task<List<InboundCode>> PollCodesAsync(CancellationToken ct)
    {
        var codes = new List<InboundCode>();
        await using var c = await OpenAsync(ct);

        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT WhsCode, WhsName FROM OWHS ORDER BY WhsCode";
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                var code = SqlMapper.S(rd["WhsCode"]);
                if (code.Length == 0) continue;
                codes.Add(new InboundCode { Kind = CodeKinds.Warehouse, Code = code, Name = SqlMapper.S(rd["WhsName"]) });
            }
        }

        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT ItmsGrpCod, ItmsGrpNam FROM OITB ORDER BY ItmsGrpCod";
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                var code = SqlMapper.S(rd["ItmsGrpCod"]);
                if (code.Length == 0) continue;
                codes.Add(new InboundCode { Kind = CodeKinds.ItemGroup, Code = code, Name = SqlMapper.S(rd["ItmsGrpNam"]) });
            }
        }

        return codes;
    }

    // ---- delta helpers --------------------------------------------------------------

    /// <summary>
    /// Cursor is yyyy-MM-ddTHH:mm:ss. Same-day rows are re-read from the cursor time so a
    /// crash mid-batch never drops a record - identical semantics to B1Client.DeltaFilter.
    /// </summary>
    public static string DeltaWhere(string since, string alias) =>
        since.Length == 0 ? "" : $"WHERE ({alias}.UpdateDate > @sinceDate OR ({alias}.UpdateDate = @sinceDate AND {alias}.UpdateTS >= @sinceTime))";

    public static string DeltaAnd(string since, string alias) =>
        since.Length == 0 ? "" : $" AND ({alias}.UpdateDate > @sinceDate OR ({alias}.UpdateDate = @sinceDate AND {alias}.UpdateTS >= @sinceTime))";

    public static (DateTime date, int time) SplitCursor(string since)
    {
        var parts = since.Split('T', 2);
        var date = DateTime.TryParse(parts[0], out var d) ? d.Date : DateTime.MinValue;
        var time = 0;
        if (parts.Length == 2)
        {
            // Must produce the same HHmmss shape UpdateTS holds, or the >= comparison
            // in the delta predicate silently compares against the wrong magnitude.
            var hms = parts[1].Split(':');
            if (hms.Length >= 2 && int.TryParse(hms[0], out var hh) && int.TryParse(hms[1], out var mm))
            {
                var ss = hms.Length >= 3 && int.TryParse(hms[2], out var s) ? s : 0;
                time = hh * 10000 + mm * 100 + ss;
            }
        }
        return (date, time);
    }

    private static void AddDeltaParams(SqlCommand cmd, string since)
    {
        if (since.Length == 0) return;
        var (date, time) = SplitCursor(since);
        cmd.Parameters.Add(new SqlParameter("@sinceDate", SqlDbType.DateTime) { Value = date });
        cmd.Parameters.Add(new SqlParameter("@sinceTime", SqlDbType.Int) { Value = time });
    }
}


