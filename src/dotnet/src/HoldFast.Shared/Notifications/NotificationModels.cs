using System.Text.Json.Serialization;

namespace HoldFast.Shared.Notifications;

// ── Slack ────────────────────────────────────────────────────────────────

public class SlackMessage
{
    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("blocks")]
    public List<SlackBlock>? Blocks { get; set; }

    [JsonPropertyName("attachments")]
    public List<SlackAttachment>? Attachments { get; set; }
}

public class SlackBlock
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "section";

    [JsonPropertyName("text")]
    public SlackTextObject? Text { get; set; }
}

public class SlackTextObject
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "mrkdwn";

    [JsonPropertyName("text")]
    public string Text_ { get; set; } = string.Empty;
}

public class SlackAttachment
{
    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("blocks")]
    public List<SlackBlock>? Blocks { get; set; }
}

// ── Discord ──────────────────────────────────────────────────────────────

public class DiscordMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("embeds")]
    public List<DiscordEmbed>? Embeds { get; set; }
}

public class DiscordEmbed
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Decimal representation of a hex color (e.g. 0xFF0000 = 16711680 for red).
    /// </summary>
    [JsonPropertyName("color")]
    public int? Color { get; set; }

    [JsonPropertyName("fields")]
    public List<DiscordEmbedField>? Fields { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }
}

public class DiscordEmbedField
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("inline")]
    public bool Inline { get; set; }
}

// ── Microsoft Teams (Adaptive Card) ──────────────────────────────────────

public class TeamsMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "message";

    [JsonPropertyName("attachments")]
    public List<TeamsAttachment>? Attachments { get; set; }
}

public class TeamsAttachment
{
    [JsonPropertyName("contentType")]
    public string ContentType { get; set; } = "application/vnd.microsoft.card.adaptive";

    [JsonPropertyName("content")]
    public AdaptiveCard? Content { get; set; }
}

public class AdaptiveCard
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "AdaptiveCard";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.4";

    [JsonPropertyName("body")]
    public List<AdaptiveCardElement>? Body { get; set; }
}

/// <summary>
/// Generic Adaptive Card element. The <see cref="AdaptiveType"/> discriminator
/// determines which properties are relevant (TextBlock, FactSet, etc.).
/// </summary>
public class AdaptiveCardElement
{
    [JsonPropertyName("type")]
    public string AdaptiveType { get; set; } = "TextBlock";

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("weight")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Weight { get; set; }

    [JsonPropertyName("size")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Size { get; set; }

    [JsonPropertyName("wrap")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Wrap { get; set; }

    [JsonPropertyName("color")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Color { get; set; }

    [JsonPropertyName("facts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AdaptiveFact>? Facts { get; set; }
}

public class AdaptiveFact
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

// ── Generic alert notification (platform-agnostic) ───────────────────────

/// <summary>
/// A platform-agnostic representation of an alert notification.
/// Used by <see cref="AlertNotificationBuilder"/> to produce platform-specific messages.
/// </summary>
public class AlertNotification
{
    /// <summary>
    /// Alert type: "error", "session", "log", "metric", "trace", "event".
    /// </summary>
    public string AlertType { get; set; } = string.Empty;

    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? ProjectName { get; set; }
    public string? Url { get; set; }

    /// <summary>
    /// Severity label: "critical", "warning", "info".
    /// </summary>
    public string? Severity { get; set; }

    public int? Count { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional key-value metadata (service name, environment, etc.).
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }
}
