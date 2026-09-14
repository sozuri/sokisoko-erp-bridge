using SokiSoko.SapBridge.Sql;
using Xunit;

namespace SokiSoko.SapBridge.Tests;

public class SqlMapperTests
{
    [Fact]
    public void Stamp_JoinsDateAndHHmm_MatchingTheServiceLayerCursorFormat()
    {
        // B1 stores UpdateTime as an int: 930 means 09:30, not 00:09:30.
        Assert.Equal("2026-09-14T09:30:00", SqlMapper.Stamp(new DateTime(2026, 9, 14), 930));
        Assert.Equal("2026-09-14T00:00:00", SqlMapper.Stamp(new DateTime(2026, 9, 14), 0));
        Assert.Equal("2026-09-14T23:59:00", SqlMapper.Stamp(new DateTime(2026, 9, 14), 2359));
    }

    [Fact]
    public void Stamp_HandlesSixDigitTimesFromNewerBuilds()
    {
        Assert.Equal("2026-09-14T14:30:45", SqlMapper.Stamp(new DateTime(2026, 9, 14), 143045));
    }

    [Fact]
    public void Stamp_FallsBackToDateOnlyWhenTimeIsNonsense()
    {
        Assert.Equal("2026-09-14", SqlMapper.Stamp(new DateTime(2026, 9, 14), 9999));
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
        var earlier = SqlMapper.Stamp(new DateTime(2026, 9, 14), 930);
        var later = SqlMapper.Stamp(new DateTime(2026, 9, 14), 1015);
        var nextDay = SqlMapper.Stamp(new DateTime(2026, 9, 15), 100);
        Assert.True(string.CompareOrdinal(later, earlier) > 0);
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
    public void SplitCursor_RoundTripsAStamp()
    {
        var (date, time) = SqlSource.SplitCursor("2026-09-14T09:30:00");
        Assert.Equal(new DateTime(2026, 9, 14), date);
        Assert.Equal(930, time);
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
        Assert.Contains("UpdateTime >= @sinceTime", w);
        Assert.Contains("UpdateDate > @sinceDate", w);
    }
}
