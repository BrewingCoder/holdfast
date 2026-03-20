namespace HoldFast.Domain.Enums;

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

public enum ErrorGroupState
{
    Open,
    Resolved,
    Ignored
}

public enum AdminRole
{
    Admin,
    Member
}

public enum SessionCommentType
{
    Admin,
    Feedback
}
