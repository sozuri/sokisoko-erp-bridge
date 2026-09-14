using System.Globalization;
using SokiSoko.SapBridge.Sync;

namespace SokiSoko.SapBridge.Sql;

/// <summary>
/// Normalises rows read straight from the B1 company database into the same ingest
/// shapes the Service Layer path produces, so SokiSoko cannot tell which route the
/// data arrived by. Column names are B1's, which are terse but stable across patches.
/// </summary>
public static class SqlMapper
{
    /// <summary>
    /// B1 splits change tracking across UpdateDate (a datetime, time component always
    /// midnight) and UpdateTS (an int). UpdateTS is HHmmss with leading zeros dropped, so
    /// 141654 is 14:16:54 and 11647 is 01:16:47 - never HHmm. Joined into the same
    /// yyyy-MM-ddTHH:mm:ss cursor the Service Layer path emits, so the two providers'
    /// cursors are interchangeable and switching does not force a re-backfill.
    /// </summary>
    public static string Stamp(DateTime? date, int? time)
    {
        if (date is null) return "";
        var d = date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var t = time ?? 0;
        var hh = t / 10000;
        var mm = t / 100 % 100;
        var ss = t % 100;
        if (hh > 23 || mm > 59 || ss > 59) return d;
        return $"{d}T{hh:D2}:{mm:D2}:{ss:D2}";
    }

    /// <summary>B1 pads and right-trims inconsistently; empty and whitespace are the same thing.</summary>
    public static string S(object? v) => v is null || v == DBNull.Value ? "" : v.ToString()?.Trim() ?? "";

    /// <summary>Decimals go over the wire as plain invariant strings, matching the B1 mapper.</summary>
    public static string N(object? v)
    {
        if (v is null || v == DBNull.Value) return "";
        return v switch
        {
            decimal d => d.ToString(CultureInfo.InvariantCulture),
            double db => db.ToString(CultureInfo.InvariantCulture),
            float f => f.ToString(CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            _ => S(v),
        };
    }

    /// <summary>
    /// B1 address types: 'B' billing, 'S' shipping. Mapped to the words the ingest
    /// endpoint expects; anything else passes through lowercased.
    /// </summary>
    public static string AddressType(string adresType) => adresType.Trim().ToUpperInvariant() switch
    {
        "B" => "billing",
        "S" => "shipping",
        var other => other.ToLowerInvariant(),
    };
}
