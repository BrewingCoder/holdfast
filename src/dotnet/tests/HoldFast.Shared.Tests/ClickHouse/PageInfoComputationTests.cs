using HoldFast.Data.ClickHouse;
using HoldFast.Data.ClickHouse.Models;

namespace HoldFast.Shared.Tests.ClickHouse;

public class PageInfoComputationTests
{
    private static List<string> MakeCursors(int count) =>
        Enumerable.Range(0, count).Select(i => CursorHelper.Encode(
            DateTime.UtcNow.AddMinutes(-i), $"uuid-{i}")).ToList();

    private static List<LogEdge> MakeEdges(int count)
    {
        var edges = new List<LogEdge>();
        for (int i = 0; i < count; i++)
        {
            var row = new LogRow
            {
                Timestamp = DateTime.UtcNow.AddMinutes(-i),
                UUID = $"uuid-{i}",
                Body = $"log-{i}",
            };
            edges.Add(new LogEdge { Node = row, Cursor = row.Cursor });
        }
        return edges;
    }

    // ── No cursor (first page) ─────────────────────────────────────

    [Fact]
    public void FirstPage_UnderLimit_NoNextPage()
    {
        var edges = MakeEdges(5);
        var cursors = edges.Select(e => e.Cursor).ToList();
        var pagination = new ClickHousePagination { Limit = 10 };

        var pageInfo = ClickHouseService.ComputePageInfo(cursors, ref edges, 10, pagination);

        Assert.False(pageInfo.HasNextPage);
        Assert.False(pageInfo.HasPreviousPage);
        Assert.Equal(5, edges.Count);
    }

    [Fact]
    public void FirstPage_ExactlyLimit_NoNextPage()
    {
        var edges = MakeEdges(10);
        var cursors = edges.Select(e => e.Cursor).ToList();
        var pagination = new ClickHousePagination { Limit = 10 };

        var pageInfo = ClickHouseService.ComputePageInfo(cursors, ref edges, 10, pagination);

        Assert.False(pageInfo.HasNextPage);
        Assert.Equal(10, edges.Count);
    }

    [Fact]
    public void FirstPage_OverLimit_HasNextPage_Trimmed()
    {
        // We fetched limit+1 rows, so there's a next page
        var edges = MakeEdges(11);
        var cursors = edges.Select(e => e.Cursor).ToList();
        var pagination = new ClickHousePagination { Limit = 10 };

        var pageInfo = ClickHouseService.ComputePageInfo(cursors, ref edges, 10, pagination);

        Assert.True(pageInfo.HasNextPage);
        Assert.False(pageInfo.HasPreviousPage);
        Assert.Equal(10, edges.Count); // Trimmed to limit
    }

    [Fact]
    public void FirstPage_Empty_NoPages()
    {
        var edges = new List<LogEdge>();
        var cursors = new List<string>();
        var pagination = new ClickHousePagination { Limit = 10 };

        var pageInfo = ClickHouseService.ComputePageInfo(cursors, ref edges, 10, pagination);

        Assert.False(pageInfo.HasNextPage);
        Assert.False(pageInfo.HasPreviousPage);
        Assert.Null(pageInfo.StartCursor);
        Assert.Null(pageInfo.EndCursor);
    }

    // ── After cursor (forward pagination) ──────────────────────────

    [Fact]
    public void AfterCursor_AlwaysHasPreviousPage()
    {
        var edges = MakeEdges(5);
        var cursors = edges.Select(e => e.Cursor).ToList();
        var pagination = new ClickHousePagination
        {
            After = CursorHelper.Encode(DateTime.UtcNow, "some-cursor"),
            Limit = 10,
        };

        var pageInfo = ClickHouseService.ComputePageInfo(cursors, ref edges, 10, pagination);

        Assert.True(pageInfo.HasPreviousPage);
        Assert.False(pageInfo.HasNextPage);
    }

    [Fact]
    public void AfterCursor_OverLimit_HasBothPages()
    {
        var edges = MakeEdges(11);
        var cursors = edges.Select(e => e.Cursor).ToList();
        var pagination = new ClickHousePagination
        {
            After = CursorHelper.Encode(DateTime.UtcNow, "prev-cursor"),
            Limit = 10,
        };

        var pageInfo = ClickHouseService.ComputePageInfo(cursors, ref edges, 10, pagination);

        Assert.True(pageInfo.HasPreviousPage);
        Assert.True(pageInfo.HasNextPage);
        Assert.Equal(10, edges.Count);
    }

    // ── Before cursor (backward pagination) ────────────────────────

    [Fact]
    public void BeforeCursor_AlwaysHasNextPage()
    {
        var edges = MakeEdges(5);
        var cursors = edges.Select(e => e.Cursor).ToList();
        var pagination = new ClickHousePagination
        {
            Before = CursorHelper.Encode(DateTime.UtcNow, "next-cursor"),
            Limit = 10,
        };

        var pageInfo = ClickHouseService.ComputePageInfo(cursors, ref edges, 10, pagination);

        Assert.True(pageInfo.HasNextPage);
        Assert.False(pageInfo.HasPreviousPage);
    }

    [Fact]
    public void BeforeCursor_OverLimit_HasBothPages_TrimsFirst()
    {
        var edges = MakeEdges(11);
        var cursors = edges.Select(e => e.Cursor).ToList();
        var pagination = new ClickHousePagination
        {
            Before = CursorHelper.Encode(DateTime.UtcNow, "next-cursor"),
            Limit = 10,
        };

        var pageInfo = ClickHouseService.ComputePageInfo(cursors, ref edges, 10, pagination);

        Assert.True(pageInfo.HasNextPage);
        Assert.True(pageInfo.HasPreviousPage);
        // Before trims from the front: edges[1..len-1]
        Assert.Equal(9, edges.Count);
    }

    // ── Cursors in PageInfo ────────────────────────────────────────

    [Fact]
    public void PageInfo_StartAndEndCursors_Set()
    {
        var edges = MakeEdges(3);
        var cursors = edges.Select(e => e.Cursor).ToList();
        var pagination = new ClickHousePagination { Limit = 10 };

        var pageInfo = ClickHouseService.ComputePageInfo(cursors, ref edges, 10, pagination);

        Assert.Equal(cursors[0], pageInfo.StartCursor);
        Assert.Equal(cursors[2], pageInfo.EndCursor);
    }

    [Fact]
    public void PageInfo_SingleEdge_StartEqualsEnd()
    {
        var edges = MakeEdges(1);
        var cursors = edges.Select(e => e.Cursor).ToList();
        var pagination = new ClickHousePagination { Limit = 10 };

        var pageInfo = ClickHouseService.ComputePageInfo(cursors, ref edges, 10, pagination);

        Assert.Equal(pageInfo.StartCursor, pageInfo.EndCursor);
    }

    // ── At cursor (centered window) ────────────────────────────────

    [Fact]
    public void AtCursor_WithMatchingCursor_CentersWindow()
    {
        // Create edges where the "at" cursor is in the middle
        var edges = MakeEdges(5);
        var cursors = edges.Select(e => e.Cursor).ToList();
        var atCursor = cursors[2]; // Middle cursor

        var pagination = new ClickHousePagination
        {
            At = atCursor,
            Limit = 4, // half = 2, so idx=2 means beforeCount=2, afterCount=2
        };

        var pageInfo = ClickHouseService.ComputePageInfo(cursors, ref edges, 4, pagination);

        // beforeCount=2, limit/2+1=3 → no hasPreviousPage
        // afterCount=2, limit/2+1=3 → no hasNextPage
        Assert.False(pageInfo.HasPreviousPage);
        Assert.False(pageInfo.HasNextPage);
    }

    [Fact]
    public void AtCursor_NotFound_NoTrimming()
    {
        var edges = MakeEdges(5);
        var cursors = edges.Select(e => e.Cursor).ToList();

        var pagination = new ClickHousePagination
        {
            At = CursorHelper.Encode(DateTime.UtcNow.AddDays(-100), "nonexistent"),
            Limit = 10,
        };

        var pageInfo = ClickHouseService.ComputePageInfo(cursors, ref edges, 10, pagination);

        Assert.False(pageInfo.HasNextPage);
        Assert.False(pageInfo.HasPreviousPage);
        Assert.Equal(5, edges.Count);
    }

    // ── Edge cases ─────────────────────────────────────────────────

    [Fact]
    public void Limit_One_WorksCorrectly()
    {
        var edges = MakeEdges(2); // limit+1
        var cursors = edges.Select(e => e.Cursor).ToList();
        var pagination = new ClickHousePagination { Limit = 1 };

        var pageInfo = ClickHouseService.ComputePageInfo(cursors, ref edges, 1, pagination);

        Assert.True(pageInfo.HasNextPage);
        Assert.Single(edges);
    }

    [Fact]
    public void LargeLimit_NoTrimming()
    {
        var edges = MakeEdges(50);
        var cursors = edges.Select(e => e.Cursor).ToList();
        var pagination = new ClickHousePagination { Limit = 10000 };

        var pageInfo = ClickHouseService.ComputePageInfo(cursors, ref edges, 10000, pagination);

        Assert.False(pageInfo.HasNextPage);
        Assert.Equal(50, edges.Count);
    }
}
