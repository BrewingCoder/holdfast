using HoldFast.Shared.Runtime;
using Xunit;

namespace HoldFast.Api.Tests;

/// <summary>
/// Tests for the RuntimeMode enum and its extensions.
/// Mirrors the Go backend's runtime flag system (all, graph, public-graph, private-graph, worker).
/// </summary>
public class RuntimeModeTests
{
    // ══════════════════════════════════════════════════════════════════
    // Parse — valid inputs
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("all", RuntimeMode.All)]
    [InlineData("ALL", RuntimeMode.All)]
    [InlineData("All", RuntimeMode.All)]
    [InlineData("", RuntimeMode.All)]
    [InlineData(null, RuntimeMode.All)]
    [InlineData("graph", RuntimeMode.Graph)]
    [InlineData("GRAPH", RuntimeMode.Graph)]
    [InlineData("public-graph", RuntimeMode.PublicGraph)]
    [InlineData("PUBLIC-GRAPH", RuntimeMode.PublicGraph)]
    [InlineData("publicgraph", RuntimeMode.PublicGraph)]
    [InlineData("private-graph", RuntimeMode.PrivateGraph)]
    [InlineData("PRIVATE-GRAPH", RuntimeMode.PrivateGraph)]
    [InlineData("privategraph", RuntimeMode.PrivateGraph)]
    [InlineData("worker", RuntimeMode.Worker)]
    [InlineData("WORKER", RuntimeMode.Worker)]
    public void Parse_ValidValues(string? input, RuntimeMode expected)
    {
        Assert.Equal(expected, RuntimeModeExtensions.Parse(input));
    }

    [Fact]
    public void Parse_WhitespaceAroundValue_Trimmed()
    {
        Assert.Equal(RuntimeMode.Worker, RuntimeModeExtensions.Parse("  worker  "));
        Assert.Equal(RuntimeMode.Graph, RuntimeModeExtensions.Parse("\tgraph\t"));
    }

    // ══════════════════════════════════════════════════════════════════
    // Parse — invalid inputs
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("invalid")]
    [InlineData("dev")]
    [InlineData("frontend")]
    [InlineData("api")]
    [InlineData("public")]
    [InlineData("private")]
    public void Parse_InvalidValues_ThrowsArgumentException(string input)
    {
        var ex = Assert.Throws<ArgumentException>(() => RuntimeModeExtensions.Parse(input));
        Assert.Contains(input, ex.Message);
        Assert.Contains("Valid values", ex.Message);
    }

    // ══════════════════════════════════════════════════════════════════
    // IsPublicGraph
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(RuntimeMode.All, true)]
    [InlineData(RuntimeMode.Graph, true)]
    [InlineData(RuntimeMode.PublicGraph, true)]
    [InlineData(RuntimeMode.PrivateGraph, false)]
    [InlineData(RuntimeMode.Worker, false)]
    public void IsPublicGraph_CorrectForEachMode(RuntimeMode mode, bool expected)
    {
        Assert.Equal(expected, mode.IsPublicGraph());
    }

    // ══════════════════════════════════════════════════════════════════
    // IsPrivateGraph
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(RuntimeMode.All, true)]
    [InlineData(RuntimeMode.Graph, true)]
    [InlineData(RuntimeMode.PublicGraph, false)]
    [InlineData(RuntimeMode.PrivateGraph, true)]
    [InlineData(RuntimeMode.Worker, false)]
    public void IsPrivateGraph_CorrectForEachMode(RuntimeMode mode, bool expected)
    {
        Assert.Equal(expected, mode.IsPrivateGraph());
    }

    // ══════════════════════════════════════════════════════════════════
    // IsWorker
    // ══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(RuntimeMode.All, true)]
    [InlineData(RuntimeMode.Graph, false)]
    [InlineData(RuntimeMode.PublicGraph, false)]
    [InlineData(RuntimeMode.PrivateGraph, false)]
    [InlineData(RuntimeMode.Worker, true)]
    public void IsWorker_CorrectForEachMode(RuntimeMode mode, bool expected)
    {
        Assert.Equal(expected, mode.IsWorker());
    }

    // ══════════════════════════════════════════════════════════════════
    // Mode combinations — verify no mode is both worker and graph-only
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void WorkerMode_DoesNotServeGraphQL()
    {
        var mode = RuntimeMode.Worker;
        Assert.True(mode.IsWorker());
        Assert.False(mode.IsPublicGraph());
        Assert.False(mode.IsPrivateGraph());
    }

    [Fact]
    public void GraphMode_DoesNotRunWorkers()
    {
        var mode = RuntimeMode.Graph;
        Assert.False(mode.IsWorker());
        Assert.True(mode.IsPublicGraph());
        Assert.True(mode.IsPrivateGraph());
    }

    [Fact]
    public void AllMode_EnablesEverything()
    {
        var mode = RuntimeMode.All;
        Assert.True(mode.IsWorker());
        Assert.True(mode.IsPublicGraph());
        Assert.True(mode.IsPrivateGraph());
    }

    [Fact]
    public void PublicGraphMode_OnlyPublic()
    {
        var mode = RuntimeMode.PublicGraph;
        Assert.False(mode.IsWorker());
        Assert.True(mode.IsPublicGraph());
        Assert.False(mode.IsPrivateGraph());
    }

    [Fact]
    public void PrivateGraphMode_OnlyPrivate()
    {
        var mode = RuntimeMode.PrivateGraph;
        Assert.False(mode.IsWorker());
        Assert.False(mode.IsPublicGraph());
        Assert.True(mode.IsPrivateGraph());
    }

    // ══════════════════════════════════════════════════════════════════
    // Exhaustiveness — every enum value is handled
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void AllEnumValues_HaveConsistentBehavior()
    {
        foreach (RuntimeMode mode in Enum.GetValues<RuntimeMode>())
        {
            // Each mode must enable at least one capability
            var hasCapability = mode.IsPublicGraph() || mode.IsPrivateGraph() || mode.IsWorker();
            Assert.True(hasCapability, $"Mode {mode} enables nothing");
        }
    }

    [Fact]
    public void AllEnumValues_CanBeRoundTripped()
    {
        // Every mode except All should be parseable from its standard string form
        Assert.Equal(RuntimeMode.Graph, RuntimeModeExtensions.Parse("graph"));
        Assert.Equal(RuntimeMode.PublicGraph, RuntimeModeExtensions.Parse("public-graph"));
        Assert.Equal(RuntimeMode.PrivateGraph, RuntimeModeExtensions.Parse("private-graph"));
        Assert.Equal(RuntimeMode.Worker, RuntimeModeExtensions.Parse("worker"));
        Assert.Equal(RuntimeMode.All, RuntimeModeExtensions.Parse("all"));
    }
}
