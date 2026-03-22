using System.Text.Json.Serialization;

namespace HoldFast.Shared.Notifications;

// ── Slack ────────────────────────────────────────────────────────────────

/// <summary>
/// Slack chat.postMessage payload. Maps to the Slack Web API JSON structure.
/// See https://api.slack.com/methods/chat.postMessage.
/// </summary>
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

/// <summary>
/// A single block element in Slack's Block Kit layout.
/// Typically a "section" block with a <see cref="SlackTextObject"/> child.
/// </summary>
public class SlackBlock
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "section";

    [JsonPropertyName("text")]
    public SlackTextObject? Text { get; set; }
}

/// <summary>
/// Slack text composition object. Supports "mrkdwn" or "plain_text" types.
/// </summary>
public class SlackTextObject
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "mrkdwn";

    [JsonPropertyName("text")]
    public string Text_ { get; set; } = string.Empty;
}

/// <summary>
/// Slack message attachment. Used to add a colored sidebar and additional blocks.
/// </summary>
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

/// <summary>
/// Discord webhook message payload. See https://discord.com/developers/docs/resources/webhook.
/// </summary>
public class DiscordMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("embeds")]
    public List<DiscordEmbed>? Embeds { get; set; }
}

/// <summary>
/// Discord rich embed object displayed beneath the message content.
/// </summary>
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

/// <summary>
/// A single name/value field within a <see cref="DiscordEmbed"/>.
/// </summary>
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

/// <summary>
/// Microsoft Teams incoming webhook message payload wrapping an Adaptive Card.
/// </summary>
public class TeamsMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "message";

    [JsonPropertyName("attachments")]
    public List<TeamsAttachment>? Attachments { get; set; }
}

/// <summary>
/// Teams message attachment containing an <see cref="AdaptiveCard"/>.
/// </summary>
public class TeamsAttachment
{
    [JsonPropertyName("contentType")]
    public string ContentType { get; set; } = "application/vnd.microsoft.card.adaptive";

    [JsonPropertyName("content")]
    public AdaptiveCard? Content { get; set; }
}

/// <summary>
/// Microsoft Adaptive Card v1.4 payload. Used within Teams webhook messages.
/// See https://adaptivecards.io/explorer/.
/// </summary>
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

/// <summary>
/// A title/value pair displayed within an Adaptive Card FactSet element.
/// </summary>
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

    /// <summary>Short, human-readable alert title shown as the message heading.</summary>
    public string? Title { get; set; }

    /// <summary>Longer description with context about what triggered the alert.</summary>
    public string? Description { get; set; }

    /// <summary>Name of the HoldFast project that generated this alert.</summary>
    public string? ProjectName { get; set; }

    /// <summary>Deep link into the HoldFast dashboard for the relevant resource.</summary>
    public string? Url { get; set; }

    /// <summary>
    /// Severity label: "critical", "warning", "info".
    /// </summary>
    public string? Severity { get; set; }

    /// <summary>Number of occurrences that triggered this alert (e.g., error count in the window).</summary>
    public int? Count { get; set; }

    /// <summary>When the alert was generated. Defaults to <see cref="DateTime.UtcNow"/>.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional key-value metadata (service name, environment, etc.).
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }
}
