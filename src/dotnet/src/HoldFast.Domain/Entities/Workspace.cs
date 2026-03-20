using HoldFast.Domain.Enums;

namespace HoldFast.Domain.Entities;

/// <summary>
/// Primary organizational unit. Self-hosted: all workspaces are Enterprise tier.
/// Billing fields (Monthly*Limit, *MaxCents, Stripe*, PromoCode) have been removed.
/// </summary>
public class Workspace : BaseEntity
{
    public string? Name { get; set; }
    public string? Secret { get; set; }

    // Slack
    public string? SlackAccessToken { get; set; }
    public string? SlackWebhookUrl { get; set; }
    public string? SlackWebhookChannel { get; set; }
    public string? SlackWebhookChannelId { get; set; }
    public string? SlackChannels { get; set; }

    // Integrations
    public string? JiraDomain { get; set; }
    public string? JiraCloudId { get; set; }
    public string? MicrosoftTeamsTenantId { get; set; }
    public string? LinearAccessToken { get; set; }
    public string? VercelAccessToken { get; set; }
    public string? VercelTeamId { get; set; }
    public string? CloudflareProxy { get; set; }
    public string? DiscordGuildId { get; set; }
    public string? ClickupAccessToken { get; set; }

    // Plan & limits
    public string PlanTier { get; set; } = "Enterprise";
    public bool UnlimitedMembers { get; set; } = true;
    public int? MigratedFromProjectId { get; set; }

    // Retention
    public RetentionPeriod RetentionPeriod { get; set; } = RetentionPeriod.SixMonths;
    public RetentionPeriod ErrorsRetentionPeriod { get; set; } = RetentionPeriod.SixMonths;
    public RetentionPeriod LogsRetentionPeriod { get; set; } = RetentionPeriod.ThirtyDays;
    public RetentionPeriod TracesRetentionPeriod { get; set; } = RetentionPeriod.ThirtyDays;
    public RetentionPeriod MetricsRetentionPeriod { get; set; } = RetentionPeriod.ThirtyDays;

    // Trial (to be removed in issue #32)
    public DateTime? TrialEndDate { get; set; }
    public bool EligibleForTrialExtension { get; set; }
    public bool TrialExtensionEnabled { get; set; }

    public bool ClearbitEnabled { get; set; }
    public string? AllowedAutoJoinEmailOrigins { get; set; }

    // Navigation
    public ICollection<Admin> Admins { get; set; } = [];
    public ICollection<Project> Projects { get; set; } = [];
}
