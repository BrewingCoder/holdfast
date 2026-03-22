using HoldFast.Data;
using HoldFast.Domain.Entities;
using HotChocolate;
using Microsoft.EntityFrameworkCore;

namespace HoldFast.GraphQL.Public;

/// <summary>
/// Public GraphQL queries — SDK-facing endpoint.
/// Minimal: just ignore (health probe) and sampling config.
/// </summary>
public class PublicQuery
{
    /// <summary>
    /// No-op query used as a health/connectivity check by SDKs.
    /// </summary>
    public string? Ignore(int id) => null;

    /// <summary>
    /// Returns sampling configuration for a project.
    /// SDKs call this to determine client-side sampling ratios.
    /// </summary>
    public async Task<SamplingConfig> GetSampling(
        string organizationVerboseId,
        [Service] HoldFastDbContext db,
        CancellationToken ct)
    {
        // organizationVerboseId is the base36-encoded project ID
        var projectId = Project.FromVerboseId(organizationVerboseId);

        var settings = await db.ProjectClientSamplingSettings
            .FirstOrDefaultAsync(s => s.ProjectId == projectId, ct);

        // Return empty config if no sampling rules defined
        return new SamplingConfig();
    }
}

/// <summary>
/// Sampling configuration returned to SDKs for client-side filtering.
/// </summary>
public record SamplingConfig(
    List<SpanSamplingConfig>? Spans = null,
    List<LogSamplingConfig>? Logs = null);

/// <summary>A match condition: either a regex pattern or an exact value.</summary>
public record MatchConfig(
    string? RegexValue = null,
    object? MatchValue = null);

/// <summary>Matches a span/log attribute by key and value conditions.</summary>
public record AttributeMatchConfig(
    MatchConfig Key,
    MatchConfig Attribute);

/// <summary>Matches a span event by name and/or attributes.</summary>
public record SpanEventMatchConfig(
    MatchConfig? Name = null,
    List<AttributeMatchConfig>? Attributes = null);

/// <summary>Sampling rule for spans: filters by name, attributes, events, and ratio.</summary>
public record SpanSamplingConfig(
    MatchConfig? Name = null,
    List<AttributeMatchConfig>? Attributes = null,
    List<SpanEventMatchConfig>? Events = null,
    int SamplingRatio = 1);

/// <summary>Sampling rule for logs: filters by attributes, message, severity, and ratio.</summary>
public record LogSamplingConfig(
    List<AttributeMatchConfig>? Attributes = null,
    MatchConfig? Message = null,
    MatchConfig? SeverityText = null,
    int SamplingRatio = 1);
