using SokiSoko.SapBridge.Sql;
using Xunit;

namespace SokiSoko.SapBridge.Tests;

public class SqlMapperTests
{
    [Fact]
    public void Stamp_ReadsUpdateTsAsHHmmss()
    {
        // Real values observed in OITM: 141654 is 14:16:54, 121114 is 12:11:14.
        Assert.Equal("2026-09-14T14:16:54", SqlMapper.Stamp(new DateTime(2026, 9, 14), 141654));
        Assert.Equal("2026-09-14T12:11:14", SqlMapper.Stamp(new DateTime(2026, 9, 14), 121114));
        Assert.Equal("2026-09-14T00:00:00", SqlMapper.Stamp(new DateTime(2026, 9, 14), 0));
    }

    [Fact]
    public void Stamp_HandlesDroppedLeadingZeroBeforeTenAm()
    {
        // 11647 is 01:16:47, NOT 11:64:7 - the leading zero on the hour is not stored.
        // Treating this as HHmm would corrupt every record changed before 10:00.
        Assert.Equal("2026-09-14T01:16:47", SqlMapper.Stamp(new DateTime(2026, 9, 14), 11647));
        Assert.Equal("2026-09-14T00:00:01", SqlMapper.Stamp(new DateTime(2026, 9, 14), 1));
        Assert.Equal("2026-09-14T22:38:29", SqlMapper.Stamp(new DateTime(2026, 9, 14), 223829));
    }

    [Fact]
    public void Stamp_FallsBackToDateOnlyWhenTimeIsNonsense()
    {
        Assert.Equal("2026-09-14", SqlMapper.Stamp(new DateTime(2026, 9, 14), 999999));
    }

    [Fact]
    public void Stamp_EmptyWhenNoDate()
    {
        Assert.Equal("", SqlMapper.Stamp(null, 930));
    }

    [Fact]
    public void Stamp_SortsOrdinallyInChronologicalOrder()
    {
        // CursorStore only advances when the new value sorts greater, so the format
        // must order correctly under CompareOrdinal.
        var earlyMorning = SqlMapper.Stamp(new DateTime(2026, 9, 14), 11647);   // 01:16:47
        var later = SqlMapper.Stamp(new DateTime(2026, 9, 14), 141654);         // 14:16:54
        var nextDay = SqlMapper.Stamp(new DateTime(2026, 9, 15), 1000);         // 00:10:00
        Assert.True(string.CompareOrdinal(later, earlyMorning) > 0);
        Assert.True(string.CompareOrdinal(nextDay, later) > 0);
    }

    [Fact]
    public void S_TrimsB1ColumnPadding()
    {
        Assert.Equal("ABC", SqlMapper.S("  ABC  "));
        Assert.Equal("", SqlMapper.S(null));
        Assert.Equal("", SqlMapper.S(DBNull.Value));
    }

    [Fact]
    public void N_UsesInvariantDecimalsRegardlessOfCulture()
    {
        Assert.Equal("20.6413", SqlMapper.N(20.6413m));
        Assert.Equal("0", SqlMapper.N(0m));
        Assert.Equal("", SqlMapper.N(DBNull.Value));
    }

    [Fact]
    public void AddressType_MapsB1Codes()
    {
        Assert.Equal("billing", SqlMapper.AddressType("B"));
        Assert.Equal("shipping", SqlMapper.AddressType("S"));
        Assert.Equal("billing", SqlMapper.AddressType(" b "));
    }

    [Fact]
    public void SplitCursor_RoundTripsAStampBackToUpdateTsShape()
    {
        // Must yield HHmmss, matching what UpdateTS holds, or the >= in the delta
        // predicate compares against a different magnitude entirely.
        var (date, time) = SqlSource.SplitCursor("2026-09-14T14:16:54");
        Assert.Equal(new DateTime(2026, 9, 14), date);
        Assert.Equal(141654, time);

        var (d2, t2) = SqlSource.SplitCursor("2026-09-14T01:16:47");
        Assert.Equal(new DateTime(2026, 9, 14), d2);
        Assert.Equal(11647, t2);
    }

    [Fact]
    public void SplitCursor_AndStamp_AreInverses()
    {
        foreach (var ts in new[] { 1, 11647, 121114, 141654, 223829 })
        {
            var stamp = SqlMapper.Stamp(new DateTime(2026, 9, 14), ts);
            var (_, back) = SqlSource.SplitCursor(stamp);
            Assert.Equal(ts, back);
        }
    }

    [Fact]
    public void DeltaWhere_IsEmptyForAFullBackfill()
    {
        Assert.Equal("", SqlSource.DeltaWhere("", "T0"));
        Assert.Contains("UpdateDate", SqlSource.DeltaWhere("2026-09-14T09:30:00", "T0"));
    }

    [Fact]
    public void DeltaWhere_ReReadsTheCursorMinuteSoNoRecordIsLost()
    {
        // >= on the same-day time, not >, mirroring B1Client.DeltaFilter.
        var w = SqlSource.DeltaWhere("2026-09-14T09:30:00", "T0");
        Assert.Contains("UpdateTS >= @sinceTime", w);
        Assert.Contains("UpdateDate > @sinceDate", w);
    }
}

