using HoldFast.Domain.Enums;

namespace HoldFast.Domain.Tests;

// ── ErrorGroupState Tests ───────────────────────────────────────────

public class ErrorGroupStateTests
{
    [Fact]
    public void ErrorGroupState_Open_IsDefined()
    {
        Assert.True(Enum.IsDefined(ErrorGroupState.Open));
    }

    [Fact]
    public void ErrorGroupState_Resolved_IsDefined()
    {
        Assert.True(Enum.IsDefined(ErrorGroupState.Resolved));
    }

    [Fact]
    public void ErrorGroupState_Ignored_IsDefined()
    {
        Assert.True(Enum.IsDefined(ErrorGroupState.Ignored));
    }

    [Fact]
    public void ErrorGroupState_HasExactlyThreeValues()
    {
        Assert.Equal(3, Enum.GetValues<ErrorGroupState>().Length);
    }

    [Fact]
    public void ErrorGroupState_AllDistinctIntValues()
    {
        var values = Enum.GetValues<ErrorGroupState>().Select(v => (int)v).ToList();
        Assert.Equal(values.Count, values.Distinct().Count());
    }

    [Fact]
    public void ErrorGroupState_CanCastToInt()
    {
        Assert.Equal(0, (int)ErrorGroupState.Open);
        Assert.Equal(1, (int)ErrorGroupState.Resolved);
        Assert.Equal(2, (int)ErrorGroupState.Ignored);
    }

    [Fact]
    public void ErrorGroupState_CanCastFromInt()
    {
        Assert.Equal(ErrorGroupState.Open, (ErrorGroupState)0);
        Assert.Equal(ErrorGroupState.Resolved, (ErrorGroupState)1);
        Assert.Equal(ErrorGroupState.Ignored, (ErrorGroupState)2);
    }

    [Fact]
    public void ErrorGroupState_UndefinedValue_NotDefined()
    {
        Assert.False(Enum.IsDefined((ErrorGroupState)99));
    }

    [Theory]
    [InlineData("Open", ErrorGroupState.Open)]
    [InlineData("Resolved", ErrorGroupState.Resolved)]
    [InlineData("Ignored", ErrorGroupState.Ignored)]
    public void ErrorGroupState_ParseFromString(string input, ErrorGroupState expected)
    {
        Assert.Equal(expected, Enum.Parse<ErrorGroupState>(input));
    }

    [Fact]
    public void ErrorGroupState_ParseInvalid_Throws()
    {
        Assert.Throws<ArgumentException>(() => Enum.Parse<ErrorGroupState>("Deleted"));
    }

    [Theory]
    [InlineData(ErrorGroupState.Open, "Open")]
    [InlineData(ErrorGroupState.Resolved, "Resolved")]
    [InlineData(ErrorGroupState.Ignored, "Ignored")]
    public void ErrorGroupState_ToStringRepresentation(ErrorGroupState state, string expected)
    {
        Assert.Equal(expected, state.ToString());
    }
}

// ── RetentionPeriod Tests ───────────────────────────────────────────

public class RetentionPeriodDetailTests
{
    [Theory]
    [InlineData(RetentionPeriod.SevenDays)]
    [InlineData(RetentionPeriod.ThirtyDays)]
    [InlineData(RetentionPeriod.ThreeMonths)]
    [InlineData(RetentionPeriod.SixMonths)]
    [InlineData(RetentionPeriod.TwelveMonths)]
    [InlineData(RetentionPeriod.TwoYears)]
    [InlineData(RetentionPeriod.ThreeYears)]
    public void RetentionPeriod_AllValuesDefined(RetentionPeriod period)
    {
        Assert.True(Enum.IsDefined(period));
    }

    [Fact]
    public void RetentionPeriod_HasExactlySevenValues()
    {
        Assert.Equal(7, Enum.GetValues<RetentionPeriod>().Length);
    }

    [Fact]
    public void RetentionPeriod_AllDistinctIntValues()
    {
        var values = Enum.GetValues<RetentionPeriod>().Select(v => (int)v).ToList();
        Assert.Equal(values.Count, values.Distinct().Count());
    }

    [Fact]
    public void RetentionPeriod_ValuesAreSequential()
    {
        // Default enum numbering: 0, 1, 2, ...
        Assert.Equal(0, (int)RetentionPeriod.SevenDays);
        Assert.Equal(1, (int)RetentionPeriod.ThirtyDays);
        Assert.Equal(2, (int)RetentionPeriod.ThreeMonths);
        Assert.Equal(3, (int)RetentionPeriod.SixMonths);
        Assert.Equal(4, (int)RetentionPeriod.TwelveMonths);
        Assert.Equal(5, (int)RetentionPeriod.TwoYears);
        Assert.Equal(6, (int)RetentionPeriod.ThreeYears);
    }

    [Fact]
    public void RetentionPeriod_OrderedByDuration()
    {
        Assert.True(RetentionPeriod.SevenDays < RetentionPeriod.ThirtyDays);
        Assert.True(RetentionPeriod.ThirtyDays < RetentionPeriod.ThreeMonths);
        Assert.True(RetentionPeriod.ThreeMonths < RetentionPeriod.SixMonths);
        Assert.True(RetentionPeriod.SixMonths < RetentionPeriod.TwelveMonths);
        Assert.True(RetentionPeriod.TwelveMonths < RetentionPeriod.TwoYears);
        Assert.True(RetentionPeriod.TwoYears < RetentionPeriod.ThreeYears);
    }

    [Fact]
    public void RetentionPeriod_UndefinedValue_NotDefined()
    {
        Assert.False(Enum.IsDefined((RetentionPeriod)999));
    }

    [Theory]
    [InlineData("SevenDays", RetentionPeriod.SevenDays)]
    [InlineData("ThreeYears", RetentionPeriod.ThreeYears)]
    public void RetentionPeriod_ParseFromString(string input, RetentionPeriod expected)
    {
        Assert.Equal(expected, Enum.Parse<RetentionPeriod>(input));
    }

    [Fact]
    public void RetentionPeriod_ParseInvalid_Throws()
    {
        Assert.Throws<ArgumentException>(() => Enum.Parse<RetentionPeriod>("FiveYears"));
    }

    [Fact]
    public void RetentionPeriod_TryParseInvalid_ReturnsFalse()
    {
        Assert.False(Enum.TryParse<RetentionPeriod>("FiveYears", out _));
    }

    [Fact]
    public void RetentionPeriod_ToString()
    {
        Assert.Equal("SevenDays", RetentionPeriod.SevenDays.ToString());
        Assert.Equal("ThreeYears", RetentionPeriod.ThreeYears.ToString());
    }
}

// ── AlertTypes Tests ────────────────────────────────────────────────

public class AlertTypesTests
{
    [Fact]
    public void AlertTypes_Error_HasExpectedValue()
    {
        Assert.Equal("ERROR_ALERT", AlertTypes.Error);
    }

    [Fact]
    public void AlertTypes_NewUser_HasExpectedValue()
    {
        Assert.Equal("NEW_USER_ALERT", AlertTypes.NewUser);
    }

    [Fact]
    public void AlertTypes_TrackProperties_HasExpectedValue()
    {
        Assert.Equal("TRACK_PROPERTIES_ALERT", AlertTypes.TrackProperties);
    }

    [Fact]
    public void AlertTypes_UserProperties_HasExpectedValue()
    {
        Assert.Equal("USER_PROPERTIES_ALERT", AlertTypes.UserProperties);
    }

    [Fact]
    public void AlertTypes_ErrorFeedback_HasExpectedValue()
    {
        Assert.Equal("ERROR_FEEDBACK_ALERT", AlertTypes.ErrorFeedback);
    }

    [Fact]
    public void AlertTypes_RageClick_HasExpectedValue()
    {
        Assert.Equal("RAGE_CLICK_ALERT", AlertTypes.RageClick);
    }

    [Fact]
    public void AlertTypes_NewSession_HasExpectedValue()
    {
        Assert.Equal("NEW_SESSION_ALERT", AlertTypes.NewSession);
    }

    [Fact]
    public void AlertTypes_Log_HasExpectedValue()
    {
        Assert.Equal("LOG", AlertTypes.Log);
    }

    [Fact]
    public void AlertTypes_Sessions_HasExpectedValue()
    {
        Assert.Equal("SESSIONS_ALERT", AlertTypes.Sessions);
    }

    [Fact]
    public void AlertTypes_Errors_HasExpectedValue()
    {
        Assert.Equal("ERRORS_ALERT", AlertTypes.Errors);
    }

    [Fact]
    public void AlertTypes_Logs_HasExpectedValue()
    {
        Assert.Equal("LOGS_ALERT", AlertTypes.Logs);
    }

    [Fact]
    public void AlertTypes_Traces_HasExpectedValue()
    {
        Assert.Equal("TRACES_ALERT", AlertTypes.Traces);
    }

    [Fact]
    public void AlertTypes_Metrics_HasExpectedValue()
    {
        Assert.Equal("METRICS_ALERT", AlertTypes.Metrics);
    }

    [Fact]
    public void AlertTypes_AllValuesUnique()
    {
        var values = new[]
        {
            AlertTypes.Error, AlertTypes.NewUser, AlertTypes.TrackProperties,
            AlertTypes.UserProperties, AlertTypes.ErrorFeedback, AlertTypes.RageClick,
            AlertTypes.NewSession, AlertTypes.Log, AlertTypes.Sessions,
            AlertTypes.Errors, AlertTypes.Logs, AlertTypes.Traces, AlertTypes.Metrics,
        };
        Assert.Equal(values.Length, values.Distinct().Count());
    }

    [Fact]
    public void AlertTypes_AllValuesNonEmpty()
    {
        var values = new[]
        {
            AlertTypes.Error, AlertTypes.NewUser, AlertTypes.TrackProperties,
            AlertTypes.UserProperties, AlertTypes.ErrorFeedback, AlertTypes.RageClick,
            AlertTypes.NewSession, AlertTypes.Log, AlertTypes.Sessions,
            AlertTypes.Errors, AlertTypes.Logs, AlertTypes.Traces, AlertTypes.Metrics,
        };
        Assert.All(values, v => Assert.False(string.IsNullOrWhiteSpace(v)));
    }

    [Fact]
    public void AlertTypes_AllValuesUpperCase()
    {
        var values = new[]
        {
            AlertTypes.Error, AlertTypes.NewUser, AlertTypes.TrackProperties,
            AlertTypes.UserProperties, AlertTypes.ErrorFeedback, AlertTypes.RageClick,
            AlertTypes.NewSession, AlertTypes.Log, AlertTypes.Sessions,
            AlertTypes.Errors, AlertTypes.Logs, AlertTypes.Traces, AlertTypes.Metrics,
        };
        Assert.All(values, v => Assert.Equal(v, v.ToUpperInvariant()));
    }
}

// ── PlanType Tests ──────────────────────────────────────────────────

public class PlanTypeTests
{
    [Fact]
    public void PlanType_HasSevenValues()
    {
        Assert.Equal(7, Enum.GetValues<PlanType>().Length);
    }

    [Theory]
    [InlineData(PlanType.Free)]
    [InlineData(PlanType.Lite)]
    [InlineData(PlanType.Basic)]
    [InlineData(PlanType.Startup)]
    [InlineData(PlanType.Enterprise)]
    [InlineData(PlanType.UsageBased)]
    [InlineData(PlanType.Graduated)]
    public void PlanType_AllValuesDefined(PlanType plan)
    {
        Assert.True(Enum.IsDefined(plan));
    }

    [Fact]
    public void PlanType_AllDistinctIntValues()
    {
        var values = Enum.GetValues<PlanType>().Select(v => (int)v).ToList();
        Assert.Equal(values.Count, values.Distinct().Count());
    }

    [Fact]
    public void PlanType_UndefinedValue_NotDefined()
    {
        Assert.False(Enum.IsDefined((PlanType)999));
    }

    [Theory]
    [InlineData(PlanType.Free, "Free")]
    [InlineData(PlanType.Enterprise, "Enterprise")]
    [InlineData(PlanType.UsageBased, "UsageBased")]
    [InlineData(PlanType.Graduated, "Graduated")]
    public void PlanType_StringRepresentation(PlanType plan, string expected)
    {
        Assert.Equal(expected, plan.ToString());
    }

    [Theory]
    [InlineData("Free", PlanType.Free)]
    [InlineData("Enterprise", PlanType.Enterprise)]
    public void PlanType_ParseFromString(string input, PlanType expected)
    {
        Assert.Equal(expected, Enum.Parse<PlanType>(input));
    }

    [Fact]
    public void PlanType_ParseInvalid_Throws()
    {
        Assert.Throws<ArgumentException>(() => Enum.Parse<PlanType>("Premium"));
    }
}

// ── LogLevel / LogSource Tests (supplemental) ───────────────────────

public class LogLevelDetailTests
{
    [Fact]
    public void LogLevel_HasSixValues()
    {
        Assert.Equal(6, Enum.GetValues<LogLevel>().Length);
    }

    [Fact]
    public void LogLevel_OTelSeverityNumbers()
    {
        Assert.Equal(1, (int)LogLevel.Trace);
        Assert.Equal(5, (int)LogLevel.Debug);
        Assert.Equal(9, (int)LogLevel.Info);
        Assert.Equal(13, (int)LogLevel.Warn);
        Assert.Equal(17, (int)LogLevel.Error);
        Assert.Equal(21, (int)LogLevel.Fatal);
    }

    [Fact]
    public void LogLevel_SeverityIncreases_Monotonically()
    {
        var ordered = Enum.GetValues<LogLevel>().Select(v => (int)v).ToList();
        for (int i = 1; i < ordered.Count; i++)
        {
            Assert.True(ordered[i] > ordered[i - 1],
                $"LogLevel values should increase monotonically: {ordered[i - 1]} >= {ordered[i]}");
        }
    }

    [Fact]
    public void LogLevel_CanCompare()
    {
        Assert.True(LogLevel.Trace < LogLevel.Fatal);
        Assert.True(LogLevel.Error > LogLevel.Warn);
        var info = LogLevel.Info;
        Assert.True(info >= LogLevel.Info);
    }

    [Theory]
    [InlineData("Trace", LogLevel.Trace)]
    [InlineData("Error", LogLevel.Error)]
    [InlineData("Fatal", LogLevel.Fatal)]
    public void LogLevel_ParseFromString(string input, LogLevel expected)
    {
        Assert.Equal(expected, Enum.Parse<LogLevel>(input));
    }

    [Fact]
    public void LogLevel_UndefinedValue()
    {
        Assert.False(Enum.IsDefined((LogLevel)0));
        Assert.False(Enum.IsDefined((LogLevel)2));
        Assert.False(Enum.IsDefined((LogLevel)99));
    }
}

// ── AdminRole Tests ─────────────────────────────────────────────────

public class AdminRoleTests
{
    [Fact]
    public void AdminRole_HasTwoValues()
    {
        Assert.Equal(2, Enum.GetValues<AdminRole>().Length);
    }

    [Theory]
    [InlineData(AdminRole.Admin)]
    [InlineData(AdminRole.Member)]
    public void AdminRole_AllValuesDefined(AdminRole role)
    {
        Assert.True(Enum.IsDefined(role));
    }

    [Fact]
    public void AdminRole_DistinctValues()
    {
        Assert.NotEqual((int)AdminRole.Admin, (int)AdminRole.Member);
    }

    [Fact]
    public void AdminRole_ToString()
    {
        Assert.Equal("Admin", AdminRole.Admin.ToString());
        Assert.Equal("Member", AdminRole.Member.ToString());
    }
}

// ── SessionCommentType Tests ────────────────────────────────────────

public class SessionCommentTypeTests
{
    [Fact]
    public void SessionCommentType_HasTwoValues()
    {
        Assert.Equal(2, Enum.GetValues<SessionCommentType>().Length);
    }

    [Theory]
    [InlineData(SessionCommentType.Admin)]
    [InlineData(SessionCommentType.Feedback)]
    public void SessionCommentType_AllValuesDefined(SessionCommentType type)
    {
        Assert.True(Enum.IsDefined(type));
    }

    [Fact]
    public void SessionCommentType_DistinctValues()
    {
        Assert.NotEqual((int)SessionCommentType.Admin, (int)SessionCommentType.Feedback);
    }
}

// ── SpanKind / MetricAggregator / ProductType supplemental ──────────

public class SpanKindDetailTests
{
    [Fact]
    public void SpanKind_SequentialValues()
    {
        Assert.Equal(0, (int)SpanKind.Internal);
        Assert.Equal(1, (int)SpanKind.Server);
        Assert.Equal(2, (int)SpanKind.Client);
        Assert.Equal(3, (int)SpanKind.Producer);
        Assert.Equal(4, (int)SpanKind.Consumer);
    }

    [Fact]
    public void SpanKind_ParseFromString()
    {
        Assert.Equal(SpanKind.Server, Enum.Parse<SpanKind>("Server"));
    }
}

public class MetricAggregatorDetailTests
{
    [Fact]
    public void MetricAggregator_SequentialValues()
    {
        Assert.Equal(0, (int)MetricAggregator.Count);
        Assert.Equal(9, (int)MetricAggregator.P99);
    }

    [Fact]
    public void MetricAggregator_AllPercentilesPresent()
    {
        var names = Enum.GetNames<MetricAggregator>();
        Assert.Contains("P50", names);
        Assert.Contains("P90", names);
        Assert.Contains("P95", names);
        Assert.Contains("P99", names);
    }
}

public class ProductTypeDetailTests
{
    [Fact]
    public void ProductType_HasSixValues()
    {
        Assert.Equal(6, Enum.GetValues<ProductType>().Length);
    }

    [Fact]
    public void ProductType_IncludesEvents()
    {
        Assert.True(Enum.IsDefined(ProductType.Events));
    }

    [Fact]
    public void ProductType_ToString()
    {
        Assert.Equal("Sessions", ProductType.Sessions.ToString());
        Assert.Equal("Errors", ProductType.Errors.ToString());
        Assert.Equal("Logs", ProductType.Logs.ToString());
        Assert.Equal("Traces", ProductType.Traces.ToString());
        Assert.Equal("Metrics", ProductType.Metrics.ToString());
        Assert.Equal("Events", ProductType.Events.ToString());
    }
}
