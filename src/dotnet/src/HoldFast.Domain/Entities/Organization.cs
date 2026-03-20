using HoldFast.Domain.Enums;

namespace HoldFast.Domain.Entities;

public class Organization : BaseEntity
{
    public string? Name { get; set; }
    public string? BillingEmail { get; set; }
    public string? Secret { get; set; }
    public DateTime? TrialEndDate { get; set; }
    public string? SlackAccessToken { get; set; }
    public string? SlackWebhookUrl { get; set; }
    public string? SlackWebhookChannel { get; set; }
    public string? SlackWebhookChannelId { get; set; }
    public string? SlackChannels { get; set; }

    // Navigation
    public ICollection<Admin> Admins { get; set; } = [];
}
