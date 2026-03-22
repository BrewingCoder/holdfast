using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HoldFast.Shared.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HoldFast.Shared.Tests.Notifications;

// ═════════════════════════════════════════════════════════════════════════
// AlertNotificationBuilder tests
// ═════════════════════════════════════════════════════════════════════════

public class AlertNotificationBuilderTests
{
    private static AlertNotification MakeNotification(
        string alertType = "error",
        string? title = "Test Alert",
        string? description = "Something happened",
        string? projectName = "MyProject",
        string? severity = "critical",
        int? count = 42,
        Dictionary<string, string>? metadata = null)
    {
        return new AlertNotification
        {
            AlertType = alertType,
            Title = title,
            Description = description,
            ProjectName = projectName,
            Severity = severity,
            Count = count,
            Timestamp = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc),
            Metadata = metadata ?? new Dictionary<string, string>
            {
                ["Service"] = "api-server",
                ["Environment"] = "production",
            },
        };
    }

    // ── Color mapping ───────────────────────────────────────────────────

    [Theory]
    [InlineData("error", "#961e13")]
    [InlineData("session", "#2eb886")]
    [InlineData("log", "#f2c94c")]
    [InlineData("metric", "#1e40af")]
    [InlineData("trace", "#f2994a")]
    [InlineData("event", "#7e5bef")]
    [InlineData("unknown", "#808080")]
    [InlineData("", "#808080")]
    public void GetSlackColor_ReturnsCorrectColor(string alertType, string expectedColor)
    {
        Assert.Equal(expectedColor, AlertNotificationBuilder.GetSlackColor(alertType));
    }

    [Fact]
    public void GetSlackColor_Null_ReturnsGray()
    {
        Assert.Equal("#808080", AlertNotificationBuilder.GetSlackColor(null));
    }

    [Theory]
    [InlineData("error", 0x961e13)]
    [InlineData("session", 0x2eb886)]
    [InlineData("log", 0xf2c94c)]
    [InlineData("metric", 0x1e40af)]
    [InlineData("trace", 0xf2994a)]
    [InlineData("event", 0x7e5bef)]
    [InlineData("unknown", 0x808080)]
    public void GetDiscordColor_ReturnsCorrectColor(string alertType, int expectedColor)
    {
        Assert.Equal(expectedColor, AlertNotificationBuilder.GetDiscordColor(alertType));
    }

    [Fact]
    public void GetDiscordColor_Null_ReturnsGray()
    {
        Assert.Equal(0x808080, AlertNotificationBuilder.GetDiscordColor(null));
    }

    [Theory]
    [InlineData("error", "Attention")]
    [InlineData("session", "Good")]
    [InlineData("log", "Warning")]
    [InlineData("metric", "Accent")]
    [InlineData("trace", "Warning")]
    [InlineData("event", "Accent")]
    [InlineData("unknown", "Default")]
    public void GetTeamsColor_ReturnsCorrectColor(string alertType, string expected)
    {
        Assert.Equal(expected, AlertNotificationBuilder.GetTeamsColor(alertType));
    }

    [Fact]
    public void GetTeamsColor_Null_ReturnsDefault()
    {
        Assert.Equal("Default", AlertNotificationBuilder.GetTeamsColor(null));
    }

    // ── Color mapping is case-insensitive ───────────────────────────────

    [Theory]
    [InlineData("ERROR")]
    [InlineData("Error")]
    [InlineData("eRrOr")]
    public void GetSlackColor_CaseInsensitive(string alertType)
    {
        Assert.Equal("#961e13", AlertNotificationBuilder.GetSlackColor(alertType));
    }

    // ── Slack message building ──────────────────────────────────────────

    [Fact]
    public void BuildSlackMessage_FullNotification_HasAllParts()
    {
        var msg = AlertNotificationBuilder.BuildSlackMessage(MakeNotification());

        Assert.Equal("Test Alert", msg.Text);
        Assert.NotNull(msg.Blocks);
        Assert.Single(msg.Blocks);
        Assert.Equal("section", msg.Blocks[0].Type);
        Assert.Contains("*Test Alert*", msg.Blocks[0].Text!.Text_);

        Assert.NotNull(msg.Attachments);
        Assert.Single(msg.Attachments);
        Assert.Equal("#961e13", msg.Attachments[0].Color);

        var attachBlocks = msg.Attachments[0].Blocks!;
        Assert.True(attachBlocks.Count >= 2); // description + facts at minimum

        // Description block
        Assert.Contains("Something happened", attachBlocks[0].Text!.Text_);

        // Facts block
        var factsText = attachBlocks[1].Text!.Text_;
        Assert.Contains("*Project:* MyProject", factsText);
        Assert.Contains("*Severity:* critical", factsText);
        Assert.Contains("*Count:* 42", factsText);
    }

    [Fact]
    public void BuildSlackMessage_WithMetadata_IncludesMetadata()
    {
        var msg = AlertNotificationBuilder.BuildSlackMessage(MakeNotification());
        var attachBlocks = msg.Attachments![0].Blocks!;
        var metaBlock = attachBlocks.Last();
        Assert.Contains("*Service:* api-server", metaBlock.Text!.Text_);
        Assert.Contains("*Environment:* production", metaBlock.Text!.Text_);
    }

    [Fact]
    public void BuildSlackMessage_NullTitle_DefaultsToHoldFastAlert()
    {
        var msg = AlertNotificationBuilder.BuildSlackMessage(MakeNotification(title: null));
        Assert.Equal("HoldFast Alert", msg.Text);
        Assert.Contains("*HoldFast Alert*", msg.Blocks![0].Text!.Text_);
    }

    [Fact]
    public void BuildSlackMessage_NullDescription_OmitsDescriptionBlock()
    {
        var msg = AlertNotificationBuilder.BuildSlackMessage(MakeNotification(description: null));
        // The attachment blocks should not start with a description block
        var attachBlocks = msg.Attachments![0].Blocks!;
        // First block should be facts (Project/Severity/Count), not empty description
        Assert.Contains("*Project:*", attachBlocks[0].Text!.Text_);
    }

    [Fact]
    public void BuildSlackMessage_EmptyDescription_OmitsDescriptionBlock()
    {
        var msg = AlertNotificationBuilder.BuildSlackMessage(MakeNotification(description: ""));
        var attachBlocks = msg.Attachments![0].Blocks!;
        Assert.Contains("*Project:*", attachBlocks[0].Text!.Text_);
    }

    [Fact]
    public void BuildSlackMessage_NullProjectAndSeverity_FactsOnlyShowCount()
    {
        var msg = AlertNotificationBuilder.BuildSlackMessage(
            MakeNotification(projectName: null, severity: null, metadata: new()));
        var attachBlocks = msg.Attachments![0].Blocks!;
        // Should have description + count facts
        var factsBlock = attachBlocks.FirstOrDefault(b => b.Text?.Text_?.Contains("*Count:*") == true);
        Assert.NotNull(factsBlock);
        Assert.DoesNotContain("*Project:*", factsBlock.Text!.Text_);
        Assert.DoesNotContain("*Severity:*", factsBlock.Text!.Text_);
    }

    [Fact]
    public void BuildSlackMessage_NoMetadata_NoMetadataBlock()
    {
        var msg = AlertNotificationBuilder.BuildSlackMessage(
            MakeNotification(metadata: new()));
        var attachBlocks = msg.Attachments![0].Blocks!;
        // No block should contain "Service:" or "Environment:"
        foreach (var block in attachBlocks)
        {
            Assert.DoesNotContain("*Service:*", block.Text?.Text_ ?? "");
        }
    }

    [Fact]
    public void BuildSlackMessage_NullCount_OmitsCountInFacts()
    {
        var msg = AlertNotificationBuilder.BuildSlackMessage(
            MakeNotification(count: null, metadata: new()));
        var attachBlocks = msg.Attachments![0].Blocks!;
        foreach (var block in attachBlocks)
        {
            if (block.Text?.Text_ != null)
                Assert.DoesNotContain("*Count:*", block.Text.Text_);
        }
    }

    [Fact]
    public void BuildSlackMessage_AllAlertTypes_ProduceValidMessages()
    {
        foreach (var alertType in new[] { "error", "session", "log", "metric", "trace", "event" })
        {
            var msg = AlertNotificationBuilder.BuildSlackMessage(MakeNotification(alertType: alertType));
            Assert.NotNull(msg);
            Assert.NotNull(msg.Attachments);
            Assert.NotEmpty(msg.Attachments);
            Assert.NotEmpty(msg.Attachments[0].Color!);
        }
    }

    // ── Discord message building ────────────────────────────────────────

    [Fact]
    public void BuildDiscordMessage_FullNotification_HasAllParts()
    {
        var msg = AlertNotificationBuilder.BuildDiscordMessage(MakeNotification());

        Assert.Equal("**Test Alert**", msg.Content);
        Assert.NotNull(msg.Embeds);
        Assert.Single(msg.Embeds);

        var embed = msg.Embeds[0];
        Assert.Equal("Test Alert", embed.Title);
        Assert.Equal("Something happened", embed.Description);
        Assert.Equal(0x961e13, embed.Color);
        Assert.NotNull(embed.Fields);

        var fieldNames = embed.Fields.Select(f => f.Name).ToList();
        Assert.Contains("Project", fieldNames);
        Assert.Contains("Severity", fieldNames);
        Assert.Contains("Count", fieldNames);
        Assert.Contains("Service", fieldNames);
        Assert.Contains("Environment", fieldNames);
    }

    [Fact]
    public void BuildDiscordMessage_NullTitle_DefaultsToHoldFastAlert()
    {
        var msg = AlertNotificationBuilder.BuildDiscordMessage(MakeNotification(title: null));
        Assert.Equal("**HoldFast Alert**", msg.Content);
        Assert.Equal("HoldFast Alert", msg.Embeds![0].Title);
    }

    [Fact]
    public void BuildDiscordMessage_NullDescription_EmptyString()
    {
        var msg = AlertNotificationBuilder.BuildDiscordMessage(MakeNotification(description: null));
        Assert.Equal(string.Empty, msg.Embeds![0].Description);
    }

    [Fact]
    public void BuildDiscordMessage_LongTitle_Truncated()
    {
        var longTitle = new string('A', 300);
        var msg = AlertNotificationBuilder.BuildDiscordMessage(MakeNotification(title: longTitle));
        Assert.True(msg.Embeds![0].Title!.Length <= 256 + 3); // 256 + "..."
    }

    [Fact]
    public void BuildDiscordMessage_LongDescription_Truncated()
    {
        var longDesc = new string('B', 3000);
        var msg = AlertNotificationBuilder.BuildDiscordMessage(MakeNotification(description: longDesc));
        Assert.True(msg.Embeds![0].Description!.Length <= 2048 + 3);
    }

    [Fact]
    public void BuildDiscordMessage_NoOptionalFields_MinimalEmbed()
    {
        var msg = AlertNotificationBuilder.BuildDiscordMessage(
            MakeNotification(projectName: null, severity: null, count: null, metadata: new()));
        var embed = msg.Embeds![0];
        Assert.Null(embed.Fields); // No fields when all optional data is missing
    }

    [Fact]
    public void BuildDiscordMessage_HasTimestamp()
    {
        var msg = AlertNotificationBuilder.BuildDiscordMessage(MakeNotification());
        Assert.NotNull(msg.Embeds![0].Timestamp);
        Assert.Contains("2025-06-15", msg.Embeds[0].Timestamp);
    }

    [Fact]
    public void BuildDiscordMessage_AllAlertTypes_ProduceValidMessages()
    {
        foreach (var alertType in new[] { "error", "session", "log", "metric", "trace", "event" })
        {
            var msg = AlertNotificationBuilder.BuildDiscordMessage(MakeNotification(alertType: alertType));
            Assert.NotNull(msg);
            Assert.NotNull(msg.Embeds);
            Assert.True(msg.Embeds[0].Color > 0);
        }
    }

    // ── Teams message building ──────────────────────────────────────────

    [Fact]
    public void BuildTeamsMessage_FullNotification_HasAdaptiveCard()
    {
        var msg = AlertNotificationBuilder.BuildTeamsMessage(MakeNotification());

        Assert.Equal("message", msg.Type);
        Assert.NotNull(msg.Attachments);
        Assert.Single(msg.Attachments);
        Assert.Equal("application/vnd.microsoft.card.adaptive", msg.Attachments[0].ContentType);

        var card = msg.Attachments[0].Content!;
        Assert.Equal("AdaptiveCard", card.Type);
        Assert.Equal("1.4", card.Version);
        Assert.NotNull(card.Body);
        Assert.True(card.Body.Count >= 2); // Title + description at minimum

        // Title TextBlock
        Assert.Equal("TextBlock", card.Body[0].AdaptiveType);
        Assert.Equal("Test Alert", card.Body[0].Text);
        Assert.Equal("Bolder", card.Body[0].Weight);
        Assert.Equal("Medium", card.Body[0].Size);
        Assert.Equal("Attention", card.Body[0].Color); // "error" => Attention
    }

    [Fact]
    public void BuildTeamsMessage_HasFactSet()
    {
        var msg = AlertNotificationBuilder.BuildTeamsMessage(MakeNotification());
        var card = msg.Attachments![0].Content!;

        var factSet = card.Body!.FirstOrDefault(e => e.AdaptiveType == "FactSet");
        Assert.NotNull(factSet);
        Assert.NotNull(factSet.Facts);

        var factTitles = factSet.Facts.Select(f => f.Title).ToList();
        Assert.Contains("Type", factTitles);
        Assert.Contains("Project", factTitles);
        Assert.Contains("Severity", factTitles);
        Assert.Contains("Count", factTitles);
        Assert.Contains("Service", factTitles);
        Assert.Contains("Environment", factTitles);
    }

    [Fact]
    public void BuildTeamsMessage_NullTitle_DefaultsToHoldFastAlert()
    {
        var msg = AlertNotificationBuilder.BuildTeamsMessage(MakeNotification(title: null));
        Assert.Equal("HoldFast Alert", msg.Attachments![0].Content!.Body![0].Text);
    }

    [Fact]
    public void BuildTeamsMessage_NullDescription_OmitsDescriptionElement()
    {
        var msg = AlertNotificationBuilder.BuildTeamsMessage(MakeNotification(description: null));
        var body = msg.Attachments![0].Content!.Body!;
        // Should have title and factset, but no description TextBlock
        Assert.Equal(2, body.Count); // title + factset
    }

    [Fact]
    public void BuildTeamsMessage_EmptyDescription_OmitsDescriptionElement()
    {
        var msg = AlertNotificationBuilder.BuildTeamsMessage(MakeNotification(description: ""));
        var body = msg.Attachments![0].Content!.Body!;
        Assert.Equal(2, body.Count);
    }

    [Fact]
    public void BuildTeamsMessage_NoFactData_NoFactSet()
    {
        var notification = new AlertNotification
        {
            AlertType = "",
            Title = "Test",
            Description = "Desc",
            Timestamp = DateTime.UtcNow,
        };
        var msg = AlertNotificationBuilder.BuildTeamsMessage(notification);
        var body = msg.Attachments![0].Content!.Body!;
        Assert.DoesNotContain(body, e => e.AdaptiveType == "FactSet");
    }

    [Fact]
    public void BuildTeamsMessage_AllAlertTypes_ProduceValidMessages()
    {
        foreach (var alertType in new[] { "error", "session", "log", "metric", "trace", "event" })
        {
            var msg = AlertNotificationBuilder.BuildTeamsMessage(MakeNotification(alertType: alertType));
            Assert.NotNull(msg);
            Assert.NotNull(msg.Attachments);
            Assert.NotNull(msg.Attachments[0].Content);
        }
    }

    // ── Serialization round-trip ────────────────────────────────────────

    [Fact]
    public void BuildSlackMessage_SerializesToValidJson()
    {
        var msg = AlertNotificationBuilder.BuildSlackMessage(MakeNotification());
        var json = JsonSerializer.Serialize(msg);
        Assert.NotNull(json);
        Assert.Contains("\"text\"", json);
        Assert.Contains("\"blocks\"", json);
    }

    [Fact]
    public void BuildDiscordMessage_SerializesToValidJson()
    {
        var msg = AlertNotificationBuilder.BuildDiscordMessage(MakeNotification());
        var json = JsonSerializer.Serialize(msg);
        Assert.Contains("\"embeds\"", json);
        Assert.Contains("\"color\"", json);
    }

    [Fact]
    public void BuildTeamsMessage_SerializesToValidJson()
    {
        var msg = AlertNotificationBuilder.BuildTeamsMessage(MakeNotification());
        var json = JsonSerializer.Serialize(msg);
        Assert.Contains("\"AdaptiveCard\"", json);
        Assert.Contains("\"1.4\"", json);
    }
}

// ═════════════════════════════════════════════════════════════════════════
// NotificationService tests (HTTP layer)
// ═════════════════════════════════════════════════════════════════════════

public class NotificationServiceTests
{
    private readonly RecordingHandler _handler;
    private readonly NotificationService _service;
    private readonly FakeLogger _logger;

    public NotificationServiceTests()
    {
        _handler = new RecordingHandler();
        _logger = new FakeLogger();
        var factory = new FakeHttpClientFactory(_handler);
        _service = new NotificationService(factory, _logger);
    }

    // ── Slack ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendSlackMessage_PostsToCorrectUrl()
    {
        var msg = new SlackMessage { Text = "hello" };
        await _service.SendSlackMessageAsync("xoxb-token", "C12345", msg, CancellationToken.None);

        Assert.Single(_handler.Requests);
        Assert.Equal("https://slack.com/api/chat.postMessage", _handler.Requests[0].Url);
    }

    [Fact]
    public async Task SendSlackMessage_SetsAuthorizationHeader()
    {
        var msg = new SlackMessage { Text = "hi" };
        await _service.SendSlackMessageAsync("xoxb-my-token", "C12345", msg, CancellationToken.None);

        var authHeader = _handler.Requests[0].AuthorizationHeader;
        Assert.NotNull(authHeader);
        Assert.Equal("Bearer", authHeader.Scheme);
        Assert.Equal("xoxb-my-token", authHeader.Parameter);
    }

    [Fact]
    public async Task SendSlackMessage_SetsChannelInBody()
    {
        var msg = new SlackMessage { Text = "hi" };
        await _service.SendSlackMessageAsync("xoxb-token", "C99999", msg, CancellationToken.None);

        var body = _handler.Requests[0].Body;
        Assert.Contains("C99999", body);
    }

    [Fact]
    public async Task SendSlackMessage_ContentTypeIsJson()
    {
        var msg = new SlackMessage { Text = "hi" };
        await _service.SendSlackMessageAsync("xoxb-token", "C12345", msg, CancellationToken.None);

        Assert.Contains("application/json", _handler.Requests[0].ContentType);
    }

    [Fact]
    public async Task SendSlackMessage_BlockKitFormat()
    {
        var notification = new AlertNotification
        {
            AlertType = "error",
            Title = "Error Alert",
            Description = "NPE in handler",
            Timestamp = DateTime.UtcNow,
        };
        var slackMsg = AlertNotificationBuilder.BuildSlackMessage(notification);
        await _service.SendSlackMessageAsync("xoxb-token", "C12345", slackMsg, CancellationToken.None);

        var body = _handler.Requests[0].Body;
        Assert.Contains("\"blocks\"", body);
        Assert.Contains("\"attachments\"", body);
        Assert.Contains("mrkdwn", body);
    }

    [Fact]
    public async Task SendSlackMessage_EmptyToken_Skipped()
    {
        var msg = new SlackMessage { Text = "hi" };
        await _service.SendSlackMessageAsync("", "C12345", msg, CancellationToken.None);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task SendSlackMessage_NullToken_Skipped()
    {
        var msg = new SlackMessage { Text = "hi" };
        await _service.SendSlackMessageAsync(null!, "C12345", msg, CancellationToken.None);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task SendSlackMessage_EmptyChannel_Skipped()
    {
        var msg = new SlackMessage { Text = "hi" };
        await _service.SendSlackMessageAsync("xoxb-token", "", msg, CancellationToken.None);
        Assert.Empty(_handler.Requests);
    }

    // ── Discord ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SendDiscordMessage_PostsToWebhookUrl()
    {
        var msg = new DiscordMessage { Content = "hello" };
        await _service.SendDiscordMessageAsync("https://discord.com/api/webhooks/1234/abcd", msg, CancellationToken.None);

        Assert.Single(_handler.Requests);
        Assert.Equal("https://discord.com/api/webhooks/1234/abcd", _handler.Requests[0].Url);
    }

    [Fact]
    public async Task SendDiscordMessage_EmbedFormat()
    {
        var notification = new AlertNotification
        {
            AlertType = "error",
            Title = "Error Alert",
            Timestamp = DateTime.UtcNow,
        };
        var discordMsg = AlertNotificationBuilder.BuildDiscordMessage(notification);
        await _service.SendDiscordMessageAsync("https://discord.com/api/webhooks/1/x", discordMsg, CancellationToken.None);

        var body = _handler.Requests[0].Body;
        Assert.Contains("\"embeds\"", body);
        Assert.Contains("\"color\"", body);
    }

    [Fact]
    public async Task SendDiscordMessage_EmptyUrl_Skipped()
    {
        var msg = new DiscordMessage { Content = "hi" };
        await _service.SendDiscordMessageAsync("", msg, CancellationToken.None);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task SendDiscordMessage_NullUrl_Skipped()
    {
        var msg = new DiscordMessage { Content = "hi" };
        await _service.SendDiscordMessageAsync(null!, msg, CancellationToken.None);
        Assert.Empty(_handler.Requests);
    }

    // ── Teams ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendTeamsMessage_PostsToWebhookUrl()
    {
        var msg = new TeamsMessage();
        await _service.SendTeamsMessageAsync("https://teams.webhook.office.com/hook", msg, CancellationToken.None);

        Assert.Single(_handler.Requests);
        Assert.Equal("https://teams.webhook.office.com/hook", _handler.Requests[0].Url);
    }

    [Fact]
    public async Task SendTeamsMessage_AdaptiveCardFormat()
    {
        var notification = new AlertNotification
        {
            AlertType = "log",
            Title = "Log Alert",
            Timestamp = DateTime.UtcNow,
        };
        var teamsMsg = AlertNotificationBuilder.BuildTeamsMessage(notification);
        await _service.SendTeamsMessageAsync("https://teams.webhook.office.com/hook", teamsMsg, CancellationToken.None);

        var body = _handler.Requests[0].Body;
        Assert.Contains("AdaptiveCard", body);
        Assert.Contains("application/vnd.microsoft.card.adaptive", body);
    }

    [Fact]
    public async Task SendTeamsMessage_EmptyUrl_Skipped()
    {
        var msg = new TeamsMessage();
        await _service.SendTeamsMessageAsync("", msg, CancellationToken.None);
        Assert.Empty(_handler.Requests);
    }

    // ── Generic webhook ─────────────────────────────────────────────────

    [Fact]
    public async Task SendWebhook_PostsJsonToUrl()
    {
        var payload = new { alert = "test", count = 5 };
        await _service.SendWebhookAsync("https://example.com/webhook", payload, CancellationToken.None);

        Assert.Single(_handler.Requests);
        Assert.Equal("https://example.com/webhook", _handler.Requests[0].Url);
        Assert.Contains("application/json", _handler.Requests[0].ContentType);
    }

    [Fact]
    public async Task SendWebhook_PayloadIsSerializedAsJson()
    {
        var payload = new { name = "Error Alert", value = 42 };
        await _service.SendWebhookAsync("https://example.com/hook", payload, CancellationToken.None);

        var body = _handler.Requests[0].Body;
        Assert.Contains("\"name\"", body);
        Assert.Contains("Error Alert", body);
        Assert.Contains("42", body);
    }

    [Fact]
    public async Task SendWebhook_EmptyUrl_Skipped()
    {
        await _service.SendWebhookAsync("", new { }, CancellationToken.None);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task SendWebhook_NullUrl_Skipped()
    {
        await _service.SendWebhookAsync(null!, new { }, CancellationToken.None);
        Assert.Empty(_handler.Requests);
    }

    // ── Error handling: HTTP 500 ────────────────────────────────────────

    [Fact]
    public async Task SendSlackMessage_Http500_LogsNotThrows()
    {
        _handler.ResponseStatusCode = HttpStatusCode.InternalServerError;
        var msg = new SlackMessage { Text = "hi" };

        // Should NOT throw
        await _service.SendSlackMessageAsync("xoxb-token", "C12345", msg, CancellationToken.None);

        Assert.True(_logger.ErrorCount > 0);
    }

    [Fact]
    public async Task SendDiscordMessage_Http500_LogsNotThrows()
    {
        _handler.ResponseStatusCode = HttpStatusCode.InternalServerError;
        var msg = new DiscordMessage { Content = "hi" };

        await _service.SendDiscordMessageAsync("https://discord.com/api/webhooks/1/x", msg, CancellationToken.None);

        Assert.True(_logger.ErrorCount > 0);
    }

    [Fact]
    public async Task SendTeamsMessage_Http500_LogsNotThrows()
    {
        _handler.ResponseStatusCode = HttpStatusCode.InternalServerError;
        var msg = new TeamsMessage();

        await _service.SendTeamsMessageAsync("https://teams.webhook.office.com/hook", msg, CancellationToken.None);

        Assert.True(_logger.ErrorCount > 0);
    }

    [Fact]
    public async Task SendWebhook_Http500_LogsNotThrows()
    {
        _handler.ResponseStatusCode = HttpStatusCode.InternalServerError;

        await _service.SendWebhookAsync("https://example.com/hook", new { }, CancellationToken.None);

        Assert.True(_logger.ErrorCount > 0);
    }

    // ── Error handling: network failure ─────────────────────────────────

    [Fact]
    public async Task SendSlackMessage_NetworkFailure_LogsNotThrows()
    {
        _handler.ThrowOnSend = true;
        var msg = new SlackMessage { Text = "hi" };

        await _service.SendSlackMessageAsync("xoxb-token", "C12345", msg, CancellationToken.None);

        Assert.True(_logger.ErrorCount > 0 || _logger.WarningCount > 0);
    }

    [Fact]
    public async Task SendWebhook_NetworkFailure_LogsNotThrows()
    {
        _handler.ThrowOnSend = true;

        await _service.SendWebhookAsync("https://example.com/hook", new { }, CancellationToken.None);

        Assert.True(_logger.ErrorCount > 0 || _logger.WarningCount > 0);
    }

    // ── Retry logic ─────────────────────────────────────────────────────

    [Fact]
    public async Task SendSlackMessage_FirstAttemptFails_SecondSucceeds()
    {
        _handler.FailCount = 1; // First call throws, second succeeds
        var msg = new SlackMessage { Text = "hi" };

        await _service.SendSlackMessageAsync("xoxb-token", "C12345", msg, CancellationToken.None);

        Assert.Equal(2, _handler.Requests.Count); // Retried once
        Assert.True(_logger.WarningCount > 0); // Logged the first failure
        Assert.Equal(0, _logger.ErrorCount); // No final error (second attempt succeeded)
    }

    [Fact]
    public async Task SendWebhook_FirstAttemptFails_SecondSucceeds()
    {
        _handler.FailCount = 1;

        await _service.SendWebhookAsync("https://example.com/hook", new { }, CancellationToken.None);

        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task SendDiscordMessage_BothAttemptsFail_LogsError()
    {
        _handler.ThrowOnSend = true; // All attempts fail
        var msg = new DiscordMessage { Content = "hi" };

        await _service.SendDiscordMessageAsync("https://discord.com/api/webhooks/1/x", msg, CancellationToken.None);

        Assert.Equal(2, _handler.Requests.Count); // Attempted twice
        Assert.True(_logger.ErrorCount > 0);
    }

    // ── HTTP 4xx errors (not transient — no retry) ──────────────────────

    [Fact]
    public async Task SendSlackMessage_Http403_FailsAfterRetry()
    {
        // 403 triggers EnsureSuccessStatusCode -> HttpRequestException, which IS retried
        _handler.ResponseStatusCode = HttpStatusCode.Forbidden;
        var msg = new SlackMessage { Text = "hi" };

        await _service.SendSlackMessageAsync("xoxb-token", "C12345", msg, CancellationToken.None);

        // HttpRequestException is retried once
        Assert.Equal(2, _handler.Requests.Count);
        Assert.True(_logger.ErrorCount > 0);
    }

    // ── ExecuteWithRetryAsync direct tests ──────────────────────────────

    [Fact]
    public async Task ExecuteWithRetry_SuccessOnFirstAttempt_NoRetry()
    {
        int callCount = 0;
        await _service.ExecuteWithRetryAsync(() =>
        {
            callCount++;
            return Task.CompletedTask;
        }, "Test", CancellationToken.None);

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task ExecuteWithRetry_HttpRequestException_RetriedOnce()
    {
        int callCount = 0;
        await _service.ExecuteWithRetryAsync(() =>
        {
            callCount++;
            if (callCount == 1) throw new HttpRequestException("connection refused");
            return Task.CompletedTask;
        }, "Test", CancellationToken.None);

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ExecuteWithRetry_TaskCanceledException_NotUserCancellation_Retried()
    {
        int callCount = 0;
        await _service.ExecuteWithRetryAsync(() =>
        {
            callCount++;
            if (callCount == 1) throw new TaskCanceledException("timeout");
            return Task.CompletedTask;
        }, "Test", CancellationToken.None);

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ExecuteWithRetry_UnexpectedException_NoRetry()
    {
        int callCount = 0;
        await _service.ExecuteWithRetryAsync(() =>
        {
            callCount++;
            throw new InvalidOperationException("unexpected");
        }, "Test", CancellationToken.None);

        Assert.Equal(1, callCount);
        Assert.True(_logger.ErrorCount > 0);
    }

    [Fact]
    public async Task ExecuteWithRetry_UserCancellation_ExitsWithoutError()
    {
        // When the CancellationToken is already cancelled, TaskCanceledException
        // is treated as user-requested cancellation — no retry, no error logged.
        var cts = new CancellationTokenSource();
        cts.Cancel();

        int callCount = 0;
        await _service.ExecuteWithRetryAsync(() =>
        {
            callCount++;
            throw new TaskCanceledException("user cancelled");
        }, "Test", cts.Token);

        Assert.Equal(1, callCount);
        Assert.Equal(0, _logger.ErrorCount); // No error should be logged
    }

    [Fact]
    public async Task ExecuteWithRetry_SuccessAfterOneFailure()
    {
        int callCount = 0;
        await _service.ExecuteWithRetryAsync(() =>
        {
            callCount++;
            if (callCount == 1) throw new HttpRequestException("transient");
            return Task.CompletedTask;
        }, "Test", CancellationToken.None);

        Assert.Equal(2, callCount); // First attempt fails, retry succeeds
    }

    [Fact]
    public async Task ExecuteWithRetry_HttpRequestException_BothAttemptsFail()
    {
        int callCount = 0;
        await _service.ExecuteWithRetryAsync(() =>
        {
            callCount++;
            throw new HttpRequestException("persistent");
        }, "Test", CancellationToken.None);

        Assert.Equal(2, callCount); // Original + 1 retry
        Assert.True(_logger.ErrorCount > 0);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Test infrastructure: fakes and helpers
    // ═════════════════════════════════════════════════════════════════════

    private class RecordedRequest
    {
        public string Url { get; set; } = "";
        public string Body { get; set; } = "";
        public string ContentType { get; set; } = "";
        public AuthenticationHeaderValue? AuthorizationHeader { get; set; }
    }

    private class RecordingHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];
        public HttpStatusCode ResponseStatusCode { get; set; } = HttpStatusCode.OK;
        public bool ThrowOnSend { get; set; }
        public int FailCount { get; set; } // Number of initial requests that should throw
        private int _callIndex;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var recorded = new RecordedRequest
            {
                Url = request.RequestUri?.ToString() ?? "",
                Body = request.Content != null ? await request.Content.ReadAsStringAsync(ct) : "",
                ContentType = request.Content?.Headers?.ContentType?.ToString() ?? "",
                AuthorizationHeader = request.Headers.Authorization,
            };
            Requests.Add(recorded);

            _callIndex++;

            if (ThrowOnSend || (FailCount > 0 && _callIndex <= FailCount))
                throw new HttpRequestException("simulated network failure");

            return new HttpResponseMessage(ResponseStatusCode);
        }
    }

    private class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private class FakeLogger : ILogger<NotificationService>
    {
        public int ErrorCount { get; private set; }
        public int WarningCount { get; private set; }
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (logLevel == LogLevel.Error) ErrorCount++;
            if (logLevel == LogLevel.Warning) WarningCount++;
        }
    }
}

// ═════════════════════════════════════════════════════════════════════════
// Notification model tests
// ═════════════════════════════════════════════════════════════════════════

public class NotificationModelTests
{
    [Fact]
    public void AlertNotification_DefaultValues()
    {
        var n = new AlertNotification();
        Assert.Equal(string.Empty, n.AlertType);
        Assert.Null(n.Title);
        Assert.Null(n.Description);
        Assert.Null(n.ProjectName);
        Assert.Null(n.Url);
        Assert.Null(n.Severity);
        Assert.Null(n.Count);
        Assert.Null(n.Metadata);
    }

    [Fact]
    public void SlackMessage_DefaultValues()
    {
        var m = new SlackMessage();
        Assert.Null(m.Channel);
        Assert.Null(m.Text);
        Assert.Null(m.Blocks);
        Assert.Null(m.Attachments);
    }

    [Fact]
    public void DiscordMessage_DefaultValues()
    {
        var m = new DiscordMessage();
        Assert.Null(m.Content);
        Assert.Null(m.Embeds);
    }

    [Fact]
    public void TeamsMessage_DefaultValues()
    {
        var m = new TeamsMessage();
        Assert.Equal("message", m.Type);
        Assert.Null(m.Attachments);
    }

    [Fact]
    public void AdaptiveCard_DefaultValues()
    {
        var c = new AdaptiveCard();
        Assert.Equal("AdaptiveCard", c.Type);
        Assert.Equal("1.4", c.Version);
        Assert.Null(c.Body);
    }

    [Fact]
    public void SlackMessage_Serialization_RoundTrip()
    {
        var original = new SlackMessage
        {
            Channel = "C123",
            Text = "Hello",
            Blocks = [new SlackBlock { Type = "section", Text = new SlackTextObject { Text_ = "world" } }],
        };
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<SlackMessage>(json)!;
        Assert.Equal("C123", deserialized.Channel);
        Assert.Equal("Hello", deserialized.Text);
        Assert.Single(deserialized.Blocks!);
    }

    [Fact]
    public void DiscordEmbed_Fields_PreserveInlineFlag()
    {
        var embed = new DiscordEmbed
        {
            Fields =
            [
                new DiscordEmbedField { Name = "A", Value = "1", Inline = true },
                new DiscordEmbedField { Name = "B", Value = "2", Inline = false },
            ]
        };
        var json = JsonSerializer.Serialize(embed);
        var back = JsonSerializer.Deserialize<DiscordEmbed>(json)!;
        Assert.True(back.Fields![0].Inline);
        Assert.False(back.Fields![1].Inline);
    }

    [Fact]
    public void TeamsAttachment_DefaultContentType()
    {
        var att = new TeamsAttachment();
        Assert.Equal("application/vnd.microsoft.card.adaptive", att.ContentType);
    }

    [Fact]
    public void AdaptiveCardElement_JsonIgnoresNullProperties()
    {
        var element = new AdaptiveCardElement { AdaptiveType = "TextBlock", Text = "Hello" };
        var json = JsonSerializer.Serialize(element);
        Assert.DoesNotContain("\"weight\"", json);
        Assert.DoesNotContain("\"size\"", json);
        Assert.DoesNotContain("\"color\"", json);
        Assert.DoesNotContain("\"facts\"", json);
    }

    [Fact]
    public void AdaptiveCardElement_WrapDefaultFalse_OmittedWhenDefault()
    {
        var element = new AdaptiveCardElement { AdaptiveType = "TextBlock" };
        var json = JsonSerializer.Serialize(element);
        Assert.DoesNotContain("\"wrap\"", json);
    }

    [Fact]
    public void AdaptiveCardElement_WrapTrue_Included()
    {
        var element = new AdaptiveCardElement { AdaptiveType = "TextBlock", Wrap = true };
        var json = JsonSerializer.Serialize(element);
        Assert.Contains("\"wrap\":true", json);
    }
}
