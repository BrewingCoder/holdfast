namespace HoldFast.Shared.Notifications;

/// <summary>
/// Converts a platform-agnostic <see cref="AlertNotification"/> into
/// Slack, Discord, and Teams message formats.
/// Color coding by alert type:
///   error  = red (#961e13 / 0x961e13)
///   session = green (#2eb886 / 0x2eb886)
///   log    = yellow (#f2c94c / 0xf2c94c)
///   metric = blue (#1e40af / 0x1e40af)
///   trace  = orange (#f2994a / 0xf2994a)
///   event  = purple (#7e5bef / 0x7e5bef)
/// </summary>
public static class AlertNotificationBuilder
{
    // Slack hex colors
    private static readonly Dictionary<string, string> SlackColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["error"]   = "#961e13",
        ["session"] = "#2eb886",
        ["log"]     = "#f2c94c",
        ["metric"]  = "#1e40af",
        ["trace"]   = "#f2994a",
        ["event"]   = "#7e5bef",
    };

    // Discord integer colors (decimal representation of hex)
    private static readonly Dictionary<string, int> DiscordColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["error"]   = 0x961e13,
        ["session"] = 0x2eb886,
        ["log"]     = 0xf2c94c,
        ["metric"]  = 0x1e40af,
        ["trace"]   = 0xf2994a,
        ["event"]   = 0x7e5bef,
    };

    // Teams Adaptive Card color strings
    private static readonly Dictionary<string, string> TeamsColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["error"]   = "Attention",
        ["session"] = "Good",
        ["log"]     = "Warning",
        ["metric"]  = "Accent",
        ["trace"]   = "Warning",
        ["event"]   = "Accent",
    };

    public static string GetSlackColor(string? alertType) =>
        alertType != null && SlackColors.TryGetValue(alertType, out var c) ? c : "#808080";

    public static int GetDiscordColor(string? alertType) =>
        alertType != null && DiscordColors.TryGetValue(alertType, out var c) ? c : 0x808080;

    public static string GetTeamsColor(string? alertType) =>
        alertType != null && TeamsColors.TryGetValue(alertType, out var c) ? c : "Default";

    // ── Slack ────────────────────────────────────────────────────────────

    public static SlackMessage BuildSlackMessage(AlertNotification notification)
    {
        var title = notification.Title ?? "HoldFast Alert";
        var description = notification.Description ?? string.Empty;
        var color = GetSlackColor(notification.AlertType);

        var headerBlock = new SlackBlock
        {
            Type = "section",
            Text = new SlackTextObject { Type = "mrkdwn", Text_ = $"*{title}*" },
        };

        var bodyBlocks = new List<SlackBlock>();

        if (!string.IsNullOrEmpty(description))
        {
            bodyBlocks.Add(new SlackBlock
            {
                Type = "section",
                Text = new SlackTextObject { Type = "mrkdwn", Text_ = description },
            });
        }

        // Facts line: project, severity, count
        var facts = new List<string>();
        if (!string.IsNullOrEmpty(notification.ProjectName))
            facts.Add($"*Project:* {notification.ProjectName}");
        if (!string.IsNullOrEmpty(notification.Severity))
            facts.Add($"*Severity:* {notification.Severity}");
        if (notification.Count.HasValue)
            facts.Add($"*Count:* {notification.Count}");

        if (facts.Count > 0)
        {
            bodyBlocks.Add(new SlackBlock
            {
                Type = "section",
                Text = new SlackTextObject { Type = "mrkdwn", Text_ = string.Join("  |  ", facts) },
            });
        }

        // Metadata
        if (notification.Metadata is { Count: > 0 })
        {
            var metaLines = notification.Metadata.Select(kv => $"*{kv.Key}:* {kv.Value}");
            bodyBlocks.Add(new SlackBlock
            {
                Type = "section",
                Text = new SlackTextObject { Type = "mrkdwn", Text_ = string.Join("\n", metaLines) },
            });
        }

        return new SlackMessage
        {
            Text = title,
            Blocks = [headerBlock],
            Attachments =
            [
                new SlackAttachment
                {
                    Color = color,
                    Blocks = bodyBlocks,
                }
            ],
        };
    }

    // ── Discord ──────────────────────────────────────────────────────────

    public static DiscordMessage BuildDiscordMessage(AlertNotification notification)
    {
        var title = notification.Title ?? "HoldFast Alert";
        var description = notification.Description ?? string.Empty;
        var color = GetDiscordColor(notification.AlertType);

        var fields = new List<DiscordEmbedField>();

        if (!string.IsNullOrEmpty(notification.ProjectName))
            fields.Add(new DiscordEmbedField { Name = "Project", Value = notification.ProjectName, Inline = true });
        if (!string.IsNullOrEmpty(notification.Severity))
            fields.Add(new DiscordEmbedField { Name = "Severity", Value = notification.Severity, Inline = true });
        if (notification.Count.HasValue)
            fields.Add(new DiscordEmbedField { Name = "Count", Value = notification.Count.Value.ToString(), Inline = true });

        if (notification.Metadata is { Count: > 0 })
        {
            foreach (var kv in notification.Metadata)
            {
                fields.Add(new DiscordEmbedField { Name = kv.Key, Value = kv.Value, Inline = true });
            }
        }

        return new DiscordMessage
        {
            Content = $"**{title}**",
            Embeds =
            [
                new DiscordEmbed
                {
                    Title = Truncate(title, 256),
                    Description = Truncate(description, 2048),
                    Color = color,
                    Fields = fields.Count > 0 ? fields : null,
                    Timestamp = notification.Timestamp.ToString("o"),
                }
            ],
        };
    }

    // ── Teams ────────────────────────────────────────────────────────────

    public static TeamsMessage BuildTeamsMessage(AlertNotification notification)
    {
        var title = notification.Title ?? "HoldFast Alert";
        var description = notification.Description ?? string.Empty;
        var teamsColor = GetTeamsColor(notification.AlertType);

        var bodyElements = new List<AdaptiveCardElement>
        {
            new()
            {
                AdaptiveType = "TextBlock",
                Text = title,
                Weight = "Bolder",
                Size = "Medium",
                Color = teamsColor,
            },
        };

        if (!string.IsNullOrEmpty(description))
        {
            bodyElements.Add(new AdaptiveCardElement
            {
                AdaptiveType = "TextBlock",
                Text = description,
                Wrap = true,
            });
        }

        var facts = new List<AdaptiveFact>();
        if (!string.IsNullOrEmpty(notification.AlertType))
            facts.Add(new AdaptiveFact { Title = "Type", Value = notification.AlertType });
        if (!string.IsNullOrEmpty(notification.ProjectName))
            facts.Add(new AdaptiveFact { Title = "Project", Value = notification.ProjectName });
        if (!string.IsNullOrEmpty(notification.Severity))
            facts.Add(new AdaptiveFact { Title = "Severity", Value = notification.Severity });
        if (notification.Count.HasValue)
            facts.Add(new AdaptiveFact { Title = "Count", Value = notification.Count.Value.ToString() });

        if (notification.Metadata is { Count: > 0 })
        {
            foreach (var kv in notification.Metadata)
                facts.Add(new AdaptiveFact { Title = kv.Key, Value = kv.Value });
        }

        if (facts.Count > 0)
        {
            bodyElements.Add(new AdaptiveCardElement
            {
                AdaptiveType = "FactSet",
                Facts = facts,
            });
        }

        return new TeamsMessage
        {
            Attachments =
            [
                new TeamsAttachment
                {
                    Content = new AdaptiveCard
                    {
                        Body = bodyElements,
                    },
                }
            ],
        };
    }

    private static string Truncate(string? text, int maxLength) =>
        string.IsNullOrEmpty(text)
            ? string.Empty
            : text.Length <= maxLength ? text : text[..maxLength] + "...";
}
