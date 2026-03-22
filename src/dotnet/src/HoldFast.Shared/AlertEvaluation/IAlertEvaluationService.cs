using HoldFast.Domain.Entities;

namespace HoldFast.Shared.AlertEvaluation;

/// <summary>
/// Result of evaluating alerts after error grouping.
/// </summary>
public record AlertEvaluationResult(
    int AlertsEvaluated,
    int AlertsTriggered,
    List<int> TriggeredAlertIds);

/// <summary>
/// Evaluates error alerts after an error is grouped.
/// Ported from Go's processErrorAlert / sendErrorAlert.
/// Called inline (synchronously) after GroupErrorAsync completes.
/// </summary>
public interface IAlertEvaluationService
{
    /// <summary>
    /// Evaluate all error alerts for the project, checking if the error group
    /// matches any alert criteria (query, threshold, cooldown).
    /// </summary>
    Task<AlertEvaluationResult> EvaluateErrorAlertsAsync(
        int projectId,
        ErrorGroup errorGroup,
        ErrorObject errorObject,
        CancellationToken ct);
}
