namespace HoldFast.Api.DevSeed;

/// <summary>
/// Configuration for the dev-instance seeder.
/// Enable via DevSeed:Enabled=true (e.g., in docker compose env).
/// </summary>
public class DevSeedOptions
{
    public bool Enabled { get; set; }
    public string AdminEmail { get; set; } = "dev@holdfast.local";
    public string AdminName { get; set; } = "Dev Admin";

    /// <summary>
    /// Workspace names to create. Each gets one default project named "default".
    /// </summary>
    public List<string> Workspaces { get; set; } =
    [
        "HoldFast Dev",
        "Koinon Dev",
        "SignalClaude Dev",
        "The Brewery Dev",
    ];
}
