using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;

namespace HoldFast.Domain.Tests;

public class WorkspaceTests
{
    [Fact]
    public void Workspace_DefaultsToEnterprise()
    {
        var workspace = new Workspace();
        Assert.Equal("Enterprise", workspace.PlanTier);
    }

    [Fact]
    public void Workspace_DefaultsToUnlimitedMembers()
    {
        var workspace = new Workspace();
        Assert.True(workspace.UnlimitedMembers);
    }

    [Fact]
    public void Workspace_HasNoBillingLimitFields()
    {
        var type = typeof(Workspace);
        Assert.Null(type.GetProperty("MonthlySessionLimit"));
        Assert.Null(type.GetProperty("MonthlyErrorsLimit"));
        Assert.Null(type.GetProperty("StripeCustomerID"));
        Assert.Null(type.GetProperty("PromoCode"));
        Assert.Null(type.GetProperty("AllowMeterOverage"));
    }

    [Fact]
    public void Workspace_HasNoStripeFields()
    {
        var type = typeof(Workspace);
        Assert.Null(type.GetProperty("StripeCustomerID"));
        Assert.Null(type.GetProperty("StripeSubscriptionID"));
        Assert.Null(type.GetProperty("StripePriceID"));
    }

    [Fact]
    public void Workspace_HasNoMonthlyLimitFields()
    {
        var type = typeof(Workspace);
        Assert.Null(type.GetProperty("MonthlySessionLimit"));
        Assert.Null(type.GetProperty("MonthlyErrorsLimit"));
        Assert.Null(type.GetProperty("MonthlyLogsLimit"));
        Assert.Null(type.GetProperty("MonthlyTracesLimit"));
        Assert.Null(type.GetProperty("MonthlyMetricsLimit"));
        Assert.Null(type.GetProperty("MonthlyMembersLimit"));
    }

    [Fact]
    public void Workspace_MaxCentsStubsReturnZero()
    {
        // *MaxCents fields exist as [NotMapped] stubs for Go schema compatibility.
        // HoldFast has no billing caps — all stubs must return 0.
        var workspace = new Workspace();
        Assert.Equal(0L, workspace.SessionsMaxCents);
        Assert.Equal(0L, workspace.ErrorsMaxCents);
        Assert.Equal(0L, workspace.LogsMaxCents);
        Assert.Equal(0L, workspace.TracesMaxCents);
        Assert.Equal(0L, workspace.MetricsMaxCents);
    }

    [Fact]
    public void Workspace_DefaultRetentionPeriods()
    {
        var workspace = new Workspace();
        Assert.Equal(RetentionPeriod.SixMonths, workspace.RetentionPeriod);
        Assert.Equal(RetentionPeriod.SixMonths, workspace.ErrorsRetentionPeriod);
        Assert.Equal(RetentionPeriod.ThirtyDays, workspace.LogsRetentionPeriod);
        Assert.Equal(RetentionPeriod.ThirtyDays, workspace.TracesRetentionPeriod);
        Assert.Equal(RetentionPeriod.ThirtyDays, workspace.MetricsRetentionPeriod);
    }

    [Fact]
    public void Workspace_NavigationCollectionsInitialized()
    {
        var workspace = new Workspace();
        Assert.NotNull(workspace.Admins);
        Assert.NotNull(workspace.Projects);
        Assert.Empty(workspace.Admins);
        Assert.Empty(workspace.Projects);
    }

    [Fact]
    public void Workspace_CanSetName()
    {
        var workspace = new Workspace { Name = "Test Workspace" };
        Assert.Equal("Test Workspace", workspace.Name);
    }

    [Fact]
    public void Workspace_CanSetEmptyName()
    {
        var workspace = new Workspace { Name = "" };
        Assert.Equal("", workspace.Name);
    }

    [Fact]
    public void Workspace_CanSetRetentionToAnyValue()
    {
        var workspace = new Workspace
        {
            RetentionPeriod = RetentionPeriod.ThreeYears,
            ErrorsRetentionPeriod = RetentionPeriod.TwoYears,
            LogsRetentionPeriod = RetentionPeriod.TwelveMonths,
            TracesRetentionPeriod = RetentionPeriod.SixMonths,
            MetricsRetentionPeriod = RetentionPeriod.SevenDays,
        };
        Assert.Equal(RetentionPeriod.ThreeYears, workspace.RetentionPeriod);
        Assert.Equal(RetentionPeriod.SevenDays, workspace.MetricsRetentionPeriod);
    }

    [Fact]
    public void Workspace_BaseEntityDefaultId()
    {
        var workspace = new Workspace();
        Assert.Equal(0, workspace.Id);
    }

    [Fact]
    public void Workspace_BaseEntityDefaultTimestamps()
    {
        var workspace = new Workspace();
        Assert.Equal(default, workspace.CreatedAt);
        Assert.Equal(default, workspace.UpdatedAt);
        Assert.Null(workspace.DeletedAt);
    }
}

public class ProjectTests
{
    [Fact]
    public void Project_DefaultRageClickSettings()
    {
        var project = new Project();
        Assert.Equal(5, project.RageClickWindowSeconds);
        Assert.Equal(8, project.RageClickRadiusPixels);
        Assert.Equal(5, project.RageClickCount);
    }

    [Fact]
    public void Project_HasNoBillingFields()
    {
        var type = typeof(Project);
        Assert.Null(type.GetProperty("MonthlySessionLimit"));
    }

    [Fact]
    public void Project_ArrayPropertiesInitialized()
    {
        var project = new Project();
        Assert.NotNull(project.ExcludedUsers);
        Assert.NotNull(project.ErrorFilters);
        Assert.NotNull(project.Platforms);
        Assert.NotNull(project.ErrorJsonPaths);
        Assert.Empty(project.ExcludedUsers);
        Assert.Empty(project.ErrorFilters);
        Assert.Empty(project.Platforms);
        Assert.Empty(project.ErrorJsonPaths);
    }

    [Fact]
    public void Project_DefaultFilterChromeExtension()
    {
        var project = new Project();
        Assert.False(project.FilterChromeExtension);
    }

    [Fact]
    public void Project_BackendSetupDefaultsNull()
    {
        var project = new Project();
        Assert.Null(project.BackendSetup);
    }

    [Fact]
    public void Project_CanSetBackendSetup()
    {
        var project = new Project { BackendSetup = true };
        Assert.True(project.BackendSetup);
    }

    [Fact]
    public void Project_FreeTierDefaultsFalse()
    {
        var project = new Project();
        Assert.False(project.FreeTier);
    }

    [Fact]
    public void Project_CanAddExcludedUsers()
    {
        var project = new Project();
        project.ExcludedUsers.Add("bot@example.com");
        project.ExcludedUsers.Add("test@example.com");
        Assert.Equal(2, project.ExcludedUsers.Count);
    }

    [Fact]
    public void Project_CanAddPlatforms()
    {
        var project = new Project();
        project.Platforms.Add("web");
        project.Platforms.Add("ios");
        project.Platforms.Add("android");
        Assert.Equal(3, project.Platforms.Count);
        Assert.Contains("ios", project.Platforms);
    }
}

public class SessionTests
{
    [Fact]
    public void Session_SecureIdDefaultsToEmpty()
    {
        var session = new Session();
        Assert.Equal(string.Empty, session.SecureId);
    }

    [Fact]
    public void Session_NullableFieldsDefaultToNull()
    {
        var session = new Session();
        Assert.Null(session.Fingerprint);
        Assert.Null(session.OSName);
        Assert.Null(session.BrowserName);
        Assert.Null(session.City);
        Assert.Null(session.Country);
        Assert.Null(session.Identifier);
        Assert.Null(session.IP);
        Assert.Null(session.Environment);
        Assert.Null(session.AppVersion);
        Assert.Null(session.ServiceName);
    }

    [Fact]
    public void Session_BoolFieldsDefaultToNull()
    {
        var session = new Session();
        Assert.Null(session.Processed);
        Assert.Null(session.Excluded);
        Assert.Null(session.HasErrors);
        Assert.Null(session.HasRageClicks);
        Assert.Null(session.Starred);
        Assert.Null(session.WithinBillingQuota);
    }

    [Fact]
    public void Session_CanSetAllGeoFields()
    {
        var session = new Session
        {
            City = "Washington",
            State = "DC",
            Country = "US",
            Postal = "20001",
            Latitude = 38.8951,
            Longitude = -77.0364,
        };
        Assert.Equal("Washington", session.City);
        Assert.InRange(session.Latitude!.Value, -90, 90);
        Assert.InRange(session.Longitude!.Value, -180, 180);
    }

    [Fact]
    public void Session_CanSetPrivacySettings()
    {
        var session = new Session
        {
            EnableStrictPrivacy = true,
            EnableRecordingNetworkContents = false,
            PrivacySetting = "strict",
        };
        Assert.True(session.EnableStrictPrivacy);
        Assert.False(session.EnableRecordingNetworkContents);
    }
}

public class ErrorGroupTests
{
    [Fact]
    public void ErrorGroup_DefaultStateIsOpen()
    {
        var errorGroup = new ErrorGroup();
        Assert.Equal(ErrorGroupState.Open, errorGroup.State);
    }

    [Fact]
    public void ErrorGroup_NavigationCollectionsInitialized()
    {
        var errorGroup = new ErrorGroup();
        Assert.NotNull(errorGroup.ErrorObjects);
        Assert.NotNull(errorGroup.Fingerprints);
        Assert.Empty(errorGroup.ErrorObjects);
        Assert.Empty(errorGroup.Fingerprints);
    }

    [Theory]
    [InlineData(ErrorGroupState.Open)]
    [InlineData(ErrorGroupState.Resolved)]
    [InlineData(ErrorGroupState.Ignored)]
    public void ErrorGroup_CanSetAnyState(ErrorGroupState state)
    {
        var errorGroup = new ErrorGroup { State = state };
        Assert.Equal(state, errorGroup.State);
    }

    [Fact]
    public void ErrorGroupState_HasThreeValues()
    {
        Assert.Equal(3, Enum.GetValues<ErrorGroupState>().Length);
    }
}

public class AllWorkspaceSettingsTests
{
    [Fact]
    public void AllFeatures_EnabledByDefault()
    {
        var settings = new AllWorkspaceSettings();
        Assert.True(settings.AIApplication);
        Assert.True(settings.AIInsights);
        Assert.True(settings.AIQueryBuilder);
        Assert.True(settings.ErrorEmbeddingsGroup);
        Assert.True(settings.EnableUnlimitedDashboards);
        Assert.True(settings.EnableUnlimitedProjects);
        Assert.True(settings.EnableUnlimitedRetention);
        Assert.True(settings.EnableUnlimitedSeats);
        Assert.True(settings.EnableSSO);
        Assert.True(settings.EnableSessionExport);
        Assert.True(settings.EnableDataDeletion);
        Assert.True(settings.EnableNetworkTraces);
        Assert.True(settings.EnableJiraIntegration);
        Assert.True(settings.EnableTeamsIntegration);
        Assert.True(settings.EnableLogTraceIngestion);
    }

    [Fact]
    public void AllFeatures_NoFieldCanBeFalseByDefault()
    {
        // Self-hosted: every boolean feature must default to true
        var settings = new AllWorkspaceSettings();
        var boolProps = typeof(AllWorkspaceSettings).GetProperties()
            .Where(p => p.PropertyType == typeof(bool));

        foreach (var prop in boolProps)
        {
            Assert.True((bool)prop.GetValue(settings)!,
                $"{prop.Name} should default to true for self-hosted");
        }
    }

    [Fact]
    public void AllFeatures_CanBeDisabledIndividually()
    {
        var settings = new AllWorkspaceSettings { AIApplication = false };
        Assert.False(settings.AIApplication);
        Assert.True(settings.AIInsights); // others unaffected
    }

    [Fact]
    public void AllWorkspaceSettings_HasNoGatedFeatureFlag()
    {
        // No "Enabled" property that gates all features
        var type = typeof(AllWorkspaceSettings);
        Assert.Null(type.GetProperty("Enabled"));
        Assert.Null(type.GetProperty("IsEnabled"));
    }
}

public class AdminTests
{
    [Fact]
    public void Admin_NavigationCollectionsInitialized()
    {
        var admin = new Admin();
        Assert.NotNull(admin.Organizations);
        Assert.NotNull(admin.Workspaces);
        Assert.Empty(admin.Organizations);
        Assert.Empty(admin.Workspaces);
    }

    [Fact]
    public void Admin_CanSetUid()
    {
        var admin = new Admin { Uid = "firebase-uid-123" };
        Assert.Equal("firebase-uid-123", admin.Uid);
    }

    [Fact]
    public void Admin_CanSetEmptyUid()
    {
        var admin = new Admin { Uid = "" };
        Assert.Equal("", admin.Uid);
    }
}

public class RetentionPeriodTests
{
    [Theory]
    [InlineData(RetentionPeriod.SevenDays)]
    [InlineData(RetentionPeriod.ThirtyDays)]
    [InlineData(RetentionPeriod.ThreeMonths)]
    [InlineData(RetentionPeriod.SixMonths)]
    [InlineData(RetentionPeriod.TwelveMonths)]
    [InlineData(RetentionPeriod.TwoYears)]
    [InlineData(RetentionPeriod.ThreeYears)]
    public void RetentionPeriod_AllValuesAreDefined(RetentionPeriod period)
    {
        Assert.True(Enum.IsDefined(period));
    }

    [Fact]
    public void RetentionPeriod_HasSevenValues()
    {
        Assert.Equal(7, Enum.GetValues<RetentionPeriod>().Length);
    }

    [Fact]
    public void RetentionPeriod_UndefinedValueNotDefined()
    {
        Assert.False(Enum.IsDefined((RetentionPeriod)999));
    }

    [Fact]
    public void RetentionPeriod_CanCastToInt()
    {
        // Ensure enum values are distinct integers
        var values = Enum.GetValues<RetentionPeriod>()
            .Select(v => (int)v)
            .ToList();
        Assert.Equal(values.Count, values.Distinct().Count());
    }
}

public class BaseEntityTests
{
    [Fact]
    public void BaseEntity_DefaultValues()
    {
        // Use a concrete derived type
        var workspace = new Workspace();
        Assert.Equal(0, workspace.Id);
        Assert.Equal(default(DateTime), workspace.CreatedAt);
        Assert.Equal(default(DateTime), workspace.UpdatedAt);
        Assert.Null(workspace.DeletedAt);
    }

    [Fact]
    public void BaseEntity_CanSetDeletedAt()
    {
        var workspace = new Workspace { DeletedAt = DateTime.UtcNow };
        Assert.NotNull(workspace.DeletedAt);
    }

    [Fact]
    public void BaseEntity_DeletedAtCanBeCleared()
    {
        var workspace = new Workspace { DeletedAt = DateTime.UtcNow };
        workspace.DeletedAt = null;
        Assert.Null(workspace.DeletedAt);
    }
}

public class ProjectFilterSettingsTests
{
    [Fact]
    public void ProjectFilterSettings_DefaultSamplingRates()
    {
        var settings = new ProjectFilterSettings();
        Assert.Equal(1.0, settings.SessionSamplingRate);
        Assert.Equal(1.0, settings.ErrorSamplingRate);
        Assert.Equal(1.0, settings.LogSamplingRate);
        Assert.Equal(1.0, settings.TraceSamplingRate);
        Assert.Equal(1.0, settings.MetricSamplingRate);
    }

    [Fact]
    public void ProjectFilterSettings_DefaultRateLimitsNull()
    {
        var settings = new ProjectFilterSettings();
        Assert.Null(settings.SessionMinuteRateLimit);
        Assert.Null(settings.ErrorMinuteRateLimit);
        Assert.Null(settings.LogMinuteRateLimit);
        Assert.Null(settings.TraceMinuteRateLimit);
        Assert.Null(settings.MetricMinuteRateLimit);
    }

    [Fact]
    public void ProjectFilterSettings_DefaultExclusionQueriesNull()
    {
        var settings = new ProjectFilterSettings();
        Assert.Null(settings.SessionExclusionQuery);
        Assert.Null(settings.ErrorExclusionQuery);
        Assert.Null(settings.LogExclusionQuery);
        Assert.Null(settings.TraceExclusionQuery);
        Assert.Null(settings.MetricExclusionQuery);
    }

    [Fact]
    public void ProjectFilterSettings_CanSetSamplingBelowOne()
    {
        var settings = new ProjectFilterSettings { SessionSamplingRate = 0.1 };
        Assert.Equal(0.1, settings.SessionSamplingRate);
    }

    [Fact]
    public void ProjectFilterSettings_CanSetSamplingToZero()
    {
        var settings = new ProjectFilterSettings { SessionSamplingRate = 0.0 };
        Assert.Equal(0.0, settings.SessionSamplingRate);
    }

    [Fact]
    public void ProjectFilterSettings_DefaultFilterSessionsWithoutError()
    {
        var settings = new ProjectFilterSettings();
        Assert.False(settings.FilterSessionsWithoutError);
    }
}

public class SessionCommentTests
{
    [Fact]
    public void SessionComment_DefaultType()
    {
        var comment = new SessionComment();
        Assert.Null(comment.Type);
    }

    [Fact]
    public void SessionComment_CanSetAdminType()
    {
        var comment = new SessionComment { Type = "ADMIN" };
        Assert.Equal("ADMIN", comment.Type);
    }

    [Fact]
    public void SessionComment_CanSetFeedbackType()
    {
        var comment = new SessionComment { Type = "FEEDBACK" };
        Assert.Equal("FEEDBACK", comment.Type);
    }
}

public class AlertTests
{
    [Fact]
    public void Alert_DefaultDisabledFalse()
    {
        var alert = new Alert();
        Assert.False(alert.Disabled);
    }

    [Fact]
    public void Alert_CanSetThresholds()
    {
        var alert = new Alert
        {
            BelowThreshold = 0.5,
            AboveThreshold = 100.0,
            ThresholdWindow = 300,
        };
        Assert.Equal(0.5, alert.BelowThreshold);
        Assert.Equal(100.0, alert.AboveThreshold);
        Assert.Equal(300, alert.ThresholdWindow);
    }

    [Fact]
    public void Alert_ThresholdsCanBeNull()
    {
        var alert = new Alert();
        Assert.Null(alert.BelowThreshold);
        Assert.Null(alert.AboveThreshold);
        Assert.Null(alert.ThresholdWindow);
    }
}

public class RageClickEventTests
{
    [Fact]
    public void RageClickEvent_DefaultValues()
    {
        var evt = new RageClickEvent();
        Assert.Equal(0, evt.TotalClicks);
        Assert.Null(evt.Selector);
    }

    [Fact]
    public void RageClickEvent_CanSetFields()
    {
        var evt = new RageClickEvent
        {
            TotalClicks = 12,
            Selector = "button.submit",
            StartTimestamp = 1000,
            EndTimestamp = 2000,
        };
        Assert.Equal(12, evt.TotalClicks);
        Assert.Equal("button.submit", evt.Selector);
        Assert.Equal(1000, evt.StartTimestamp);
    }
}
