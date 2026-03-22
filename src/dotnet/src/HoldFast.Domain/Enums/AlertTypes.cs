namespace HoldFast.Domain.Enums;

/// <summary>
/// String constants for alert product types. Deprecated types are from the legacy per-type
/// alert system; current types are used by the unified <see cref="HoldFast.Domain.Entities.Alert"/> entity.
/// </summary>
public static class AlertTypes
{
    // Deprecated alert types
    public const string Error = "ERROR_ALERT";
    public const string NewUser = "NEW_USER_ALERT";
    public const string TrackProperties = "TRACK_PROPERTIES_ALERT";
    public const string UserProperties = "USER_PROPERTIES_ALERT";
    public const string ErrorFeedback = "ERROR_FEEDBACK_ALERT";
    public const string RageClick = "RAGE_CLICK_ALERT";
    public const string NewSession = "NEW_SESSION_ALERT";
    public const string Log = "LOG";

    // Current alert types
    public const string Sessions = "SESSIONS_ALERT";
    public const string Errors = "ERRORS_ALERT";
    public const string Logs = "LOGS_ALERT";
    public const string Traces = "TRACES_ALERT";
    public const string Metrics = "METRICS_ALERT";
}

/// <summary>
/// Lifecycle state of an error group: Open (new/active), Resolved (fixed), or Ignored (suppressed).
/// </summary>
public enum ErrorGroupState
{
    Open,
    Resolved,
    Ignored
}

/// <summary>
/// Role of an admin within a workspace. Admin has full access; Member has restricted access.
/// </summary>
public enum AdminRole
{
    Admin,
    Member
}

/// <summary>
/// Source of a session comment: Admin (created by a team member) or Feedback (from end-user SDK).
/// </summary>
public enum SessionCommentType
{
    Admin,
    Feedback
}
