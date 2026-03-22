namespace HoldFast.Shared.Runtime;

/// <summary>
/// Mirrors the Go backend's runtime mode system. Each mode controls which
/// services (HTTP endpoints, workers) are started by the process.
/// </summary>
public enum RuntimeMode
{
    /// <summary>All endpoints and all workers — default for development and hobby deployments.</summary>
    All,

    /// <summary>Both public and private GraphQL endpoints, no workers.</summary>
    Graph,

    /// <summary>Only public GraphQL (SDK data ingestion), no workers.</summary>
    PublicGraph,

    /// <summary>Only private GraphQL (dashboard API), no workers.</summary>
    PrivateGraph,

    /// <summary>Only background workers (Kafka consumers, scheduled tasks), no HTTP endpoints.</summary>
    Worker,
}

public static class RuntimeModeExtensions
{
    /// <summary>Should the public GraphQL endpoint be registered?</summary>
    public static bool IsPublicGraph(this RuntimeMode mode) =>
        mode is RuntimeMode.PublicGraph or RuntimeMode.Graph or RuntimeMode.All;

    /// <summary>Should the private GraphQL endpoint be registered?</summary>
    public static bool IsPrivateGraph(this RuntimeMode mode) =>
        mode is RuntimeMode.PrivateGraph or RuntimeMode.Graph or RuntimeMode.All;

    /// <summary>Should background workers be started?</summary>
    public static bool IsWorker(this RuntimeMode mode) =>
        mode is RuntimeMode.Worker or RuntimeMode.All;

    /// <summary>
    /// Parse a runtime mode string (case-insensitive). Matches the Go backend's
    /// flag values: "all", "graph", "public-graph", "private-graph", "worker".
    /// </summary>
    public static RuntimeMode Parse(string? value) => (value?.Trim().ToLowerInvariant()) switch
    {
        "all" or "" or null => RuntimeMode.All,
        "graph" => RuntimeMode.Graph,
        "public-graph" or "publicgraph" => RuntimeMode.PublicGraph,
        "private-graph" or "privategraph" => RuntimeMode.PrivateGraph,
        "worker" => RuntimeMode.Worker,
        _ => throw new ArgumentException($"Unknown runtime mode: '{value}'. Valid values: all, graph, public-graph, private-graph, worker"),
    };
}
