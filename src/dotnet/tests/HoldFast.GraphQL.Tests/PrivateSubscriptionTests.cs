using HoldFast.GraphQL.Private.Subscriptions;
using HoldFast.Shared.SessionProcessing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HoldFast.GraphQL.Tests;

/// <summary>
/// Tests for the private-graph GraphQL subscription system:
/// - SessionPayload output type construction
/// - PrivateSubscription resolver pass-through
/// - HotChocolateSessionEventPublisher (no-op path when sender unavailable)
/// - NoOpSessionEventPublisher
/// </summary>
public class PrivateSubscriptionTests
{
    // ══════════════════════════════════════════════════════════════════
    // SessionPayload — type construction
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void SessionPayload_DefaultsAreEmpty()
    {
        var payload = new SessionPayload();

        Assert.Empty(payload.Events);
        Assert.Empty(payload.Errors);
        Assert.Empty(payload.RageClicks);
        Assert.Empty(payload.SessionComments);
        Assert.Equal(default, payload.LastUserInteractionTime);
    }

    [Fact]
    public void SessionPayload_WithTimestamp()
    {
        var ts = new DateTime(2026, 3, 21, 12, 0, 0, DateTimeKind.Utc);
        var payload = new SessionPayload { LastUserInteractionTime = ts };

        Assert.Equal(ts, payload.LastUserInteractionTime);
        Assert.Empty(payload.Events);
    }

    [Fact]
    public void SessionPayload_WithEvents()
    {
        var payload = new SessionPayload
        {
            Events = ["""{"type":4,"data":{}}""", """{"type":2,"data":{}}"""],
            LastUserInteractionTime = DateTime.UtcNow,
        };

        Assert.Equal(2, payload.Events.Count);
        Assert.Contains(payload.Events, e => e.Contains("\"type\":4"));
    }

    [Fact]
    public void SessionPayload_WithErrors()
    {
        var payload = new SessionPayload
        {
            Errors =
            [
                new SessionPayloadError
                {
                    Id = 1,
                    Event = "TypeError: x is undefined",
                    Type = "TypeError",
                    Source = "app.js",
                    Timestamp = DateTime.UtcNow,
                },
            ],
        };

        Assert.Single(payload.Errors);
        Assert.Equal("TypeError: x is undefined", payload.Errors[0].Event);
        Assert.Equal("TypeError", payload.Errors[0].Type);
    }

    [Fact]
    public void SessionPayload_WithRageClicks()
    {
        var start = new DateTime(2026, 3, 21, 12, 0, 0, DateTimeKind.Utc);
        var payload = new SessionPayload
        {
            RageClicks =
            [
                new SessionPayloadRageClick
                {
                    Id = 42,
                    StartTimestamp = start,
                    EndTimestamp = start.AddSeconds(2),
                    TotalClicks = 5,
                },
            ],
        };

        Assert.Single(payload.RageClicks);
        Assert.Equal(5, payload.RageClicks[0].TotalClicks);
    }

    [Fact]
    public void SessionPayload_WithComments()
    {
        var payload = new SessionPayload
        {
            SessionComments =
            [
                new SessionPayloadComment
                {
                    Id = 7,
                    Text = "This button is confusing",
                    CreatedAt = DateTime.UtcNow,
                },
            ],
        };

        Assert.Single(payload.SessionComments);
        Assert.Equal("This button is confusing", payload.SessionComments[0].Text);
    }

    // ══════════════════════════════════════════════════════════════════
    // PrivateSubscription — resolver pass-through
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void PrivateSubscription_SessionPayloadAppended_ReturnsEventMessage()
    {
        var sub = new PrivateSubscription();
        var ts = new DateTime(2026, 3, 21, 15, 30, 0, DateTimeKind.Utc);
        var payload = new SessionPayload
        {
            Events = ["""{"type":1}"""],
            LastUserInteractionTime = ts,
        };

        // Resolver is a pure pass-through of the event message
        var result = sub.SessionPayloadAppended("secure-abc", 0, payload);

        Assert.Same(payload, result);
        Assert.Equal(ts, result.LastUserInteractionTime);
        Assert.Single(result.Events);
    }

    [Fact]
    public void PrivateSubscription_SessionPayloadAppended_InitialEventsCountIgnored()
    {
        // initialEventsCount is metadata for future filtering — currently passed through
        var sub = new PrivateSubscription();
        var payload = new SessionPayload();

        // Should work with any non-negative count
        var r1 = sub.SessionPayloadAppended("s1", 0, payload);
        var r2 = sub.SessionPayloadAppended("s1", 100, payload);
        var r3 = sub.SessionPayloadAppended("s1", int.MaxValue, payload);

        Assert.Same(payload, r1);
        Assert.Same(payload, r2);
        Assert.Same(payload, r3);
    }

    [Fact]
    public void PrivateSubscription_SessionPayloadAppended_DifferentSecureIds_SamePayload()
    {
        var sub = new PrivateSubscription();
        var payload = new SessionPayload { LastUserInteractionTime = DateTime.UtcNow };

        // Resolver routes by topic, not by inspecting the ID — just returns payload
        var r1 = sub.SessionPayloadAppended("session-1", 0, payload);
        var r2 = sub.SessionPayloadAppended("session-2", 0, payload);

        Assert.Same(r1, r2);
    }

    // ══════════════════════════════════════════════════════════════════
    // NoOpSessionEventPublisher
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NoOpPublisher_DoesNotThrow()
    {
        var publisher = new NoOpSessionEventPublisher();

        // Should complete silently regardless of input
        await publisher.PublishSessionPayloadAsync("any-session", DateTime.UtcNow, default);
    }

    [Fact]
    public async Task NoOpPublisher_MultipleCalls_AllComplete()
    {
        var publisher = new NoOpSessionEventPublisher();
        var ts = DateTime.UtcNow;

        var tasks = Enumerable.Range(0, 10)
            .Select(i => publisher.PublishSessionPayloadAsync($"session-{i}", ts, default));

        await Task.WhenAll(tasks);
        // No assertion needed — just verify no exceptions
    }

    [Fact]
    public async Task NoOpPublisher_CancellationToken_IsIgnored()
    {
        var publisher = new NoOpSessionEventPublisher();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Already-cancelled token should still complete (no-op doesn't await anything)
        await publisher.PublishSessionPayloadAsync("sess", DateTime.UtcNow, cts.Token);
    }

    // ══════════════════════════════════════════════════════════════════
    // SessionPayloadError, RageClick, Comment — edge cases
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void SessionPayloadError_NullOptionalFields()
    {
        var err = new SessionPayloadError
        {
            Id = 1,
            Event = "Error",
            Type = "Error",
            // Source and StackTrace are nullable
            Timestamp = DateTime.UtcNow,
        };

        Assert.Null(err.Source);
        Assert.Null(err.StackTrace);
    }

    [Fact]
    public void SessionPayloadComment_NullText()
    {
        var comment = new SessionPayloadComment
        {
            Id = 1,
            Text = null,
            CreatedAt = DateTime.UtcNow,
        };

        Assert.Null(comment.Text);
    }

    [Fact]
    public void SessionPayloadRageClick_ZeroClicks_IsValid()
    {
        // Edge case: rage click with zero clicks (malformed data)
        var rc = new SessionPayloadRageClick
        {
            Id = 1,
            StartTimestamp = DateTime.UtcNow,
            EndTimestamp = DateTime.UtcNow,
            TotalClicks = 0,
        };

        Assert.Equal(0, rc.TotalClicks);
    }
}
