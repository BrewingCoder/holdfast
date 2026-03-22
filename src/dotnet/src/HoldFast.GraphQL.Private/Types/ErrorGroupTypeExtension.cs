using HoldFast.Domain.Entities;
using HotChocolate;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;

namespace HoldFast.GraphQL.Private.Types;

/// <summary>
/// HC type extension for ErrorGroup — adds fields that are computed or
/// require navigation queries rather than direct column reads.
/// Keeps the domain entity clean of GraphQL concerns.
/// </summary>
[ExtendObjectType(typeof(ErrorGroup))]
public class ErrorGroupTypeExtension
{
    /// <summary>
    /// Structured (parsed) stack trace frames.
    /// Returns empty for now; full parsing is a future enhancement.
    /// </summary>
    [GraphQLName("structured_stack_trace")]
    public List<ErrorTrace?> GetStructuredStackTrace([Parent] ErrorGroup group)
        => [];

    /// <summary>
    /// Time-series error occurrence counts for sparkline charts.
    /// Returns empty for now; ClickHouse aggregation is a future enhancement.
    /// </summary>
    [GraphQLName("error_frequency")]
    public List<long> GetErrorFrequency([Parent] ErrorGroup group)
        => group.ErrorFrequency;

    /// <summary>
    /// Error distribution metrics (date-bucketed counts).
    /// Returns empty for now.
    /// </summary>
    [GraphQLName("error_metrics")]
    public List<ErrorDistributionItem> GetErrorMetrics([Parent] ErrorGroup group)
        => [];

    /// <summary>
    /// The tag assigned to this error group, if any.
    /// </summary>
    [GraphQLName("error_tag")]
    public async Task<ErrorTag?> GetErrorTagAsync(
        [Parent] ErrorGroup group,
        [Service] HoldFast.Data.HoldFastDbContext db,
        CancellationToken ct)
    {
        return await db.Set<ErrorTag>()
            .FirstOrDefaultAsync(t => t.ErrorGroupId == group.Id, ct);
    }
}
