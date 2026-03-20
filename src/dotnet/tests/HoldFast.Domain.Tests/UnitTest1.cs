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
        Assert.Null(type.GetProperty("SessionsMaxCents"));
        Assert.Null(type.GetProperty("ErrorsMaxCents"));
        Assert.Null(type.GetProperty("StripeCustomerID"));
        Assert.Null(type.GetProperty("PromoCode"));
        Assert.Null(type.GetProperty("AllowMeterOverage"));
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
        Assert.Empty(project.ExcludedUsers);
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
}
