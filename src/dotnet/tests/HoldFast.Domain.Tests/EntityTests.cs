using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;

namespace HoldFast.Domain.Tests;

// ── Project VerboseId Tests ──────────────────────────────────────────

public class ProjectVerboseIdTests
{
    [Fact]
    public void VerboseId_EncodesId_ReturnsNonEmpty()
    {
        var project = new Project { Id = 1 };
        Assert.False(string.IsNullOrEmpty(project.VerboseId));
    }

    [Fact]
    public void VerboseId_MinLength8()
    {
        var project = new Project { Id = 1 };
        Assert.True(project.VerboseId.Length >= 8);
    }

    [Fact]
    public void VerboseId_OnlyLowercaseAlphanumeric()
    {
        var project = new Project { Id = 42 };
        Assert.Matches("^[a-z0-9]+$", project.VerboseId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(999)]
    [InlineData(123456)]
    [InlineData(int.MaxValue)]
    public void VerboseId_RoundTrip(int id)
    {
        var project = new Project { Id = id };
        var decoded = Project.FromVerboseId(project.VerboseId);
        Assert.Equal(id, decoded);
    }

    [Fact]
    public void VerboseId_DifferentIdsProduceDifferentHashes()
    {
        var p1 = new Project { Id = 1 };
        var p2 = new Project { Id = 2 };
        Assert.NotEqual(p1.VerboseId, p2.VerboseId);
    }

    [Fact]
    public void VerboseId_SameIdProducesSameHash()
    {
        var p1 = new Project { Id = 42 };
        var p2 = new Project { Id = 42 };
        Assert.Equal(p1.VerboseId, p2.VerboseId);
    }

    [Fact]
    public void VerboseId_IdZero_StillEncodes()
    {
        // Id=0 is the default, but Hashids still encodes it
        var project = new Project { Id = 0 };
        Assert.False(string.IsNullOrEmpty(project.VerboseId));
    }
}

public class ProjectFromVerboseIdTests
{
    [Fact]
    public void FromVerboseId_ValidHashId_ReturnsCorrectId()
    {
        var project = new Project { Id = 42 };
        var verboseId = project.VerboseId;
        var decoded = Project.FromVerboseId(verboseId);
        Assert.Equal(42, decoded);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("42", 42)]
    [InlineData("0", 0)]
    [InlineData("999999", 999999)]
    public void FromVerboseId_PlainIntegerFallback(string input, int expected)
    {
        var decoded = Project.FromVerboseId(input);
        Assert.Equal(expected, decoded);
    }

    [Fact]
    public void FromVerboseId_InvalidInput_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Project.FromVerboseId("not-a-valid-hashid-xyz"));
    }

    [Fact]
    public void FromVerboseId_EmptyString_ThrowsArgumentException()
    {
        // Empty string is not a valid int, and not a valid hashid
        Assert.Throws<ArgumentException>(() => Project.FromVerboseId(""));
    }

    [Fact]
    public void FromVerboseId_NegativeInteger_ReturnsNegative()
    {
        // int.TryParse handles negatives
        var decoded = Project.FromVerboseId("-5");
        Assert.Equal(-5, decoded);
    }

    [Fact]
    public void FromVerboseId_WhitespaceOnly_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Project.FromVerboseId("   "));
    }

    [Fact]
    public void FromVerboseId_LargeInt_Fallback()
    {
        var decoded = Project.FromVerboseId("2147483647"); // int.MaxValue
        Assert.Equal(int.MaxValue, decoded);
    }
}

// ── BaseEntity Tests ────────────────────────────────────────────────

public class BaseEntityDetailTests
{
    [Fact]
    public void BaseEntity_DefaultId_IsZero()
    {
        var ws = new Workspace();
        Assert.Equal(0, ws.Id);
    }

    [Fact]
    public void BaseEntity_DefaultCreatedAt_IsMinValue()
    {
        var ws = new Workspace();
        Assert.Equal(default(DateTime), ws.CreatedAt);
    }

    [Fact]
    public void BaseEntity_DefaultUpdatedAt_IsMinValue()
    {
        var ws = new Workspace();
        Assert.Equal(default(DateTime), ws.UpdatedAt);
    }

    [Fact]
    public void BaseEntity_DefaultDeletedAt_IsNull()
    {
        var ws = new Workspace();
        Assert.Null(ws.DeletedAt);
    }

    [Fact]
    public void BaseEntity_CanSetId()
    {
        var ws = new Workspace { Id = 99 };
        Assert.Equal(99, ws.Id);
    }

    [Fact]
    public void BaseEntity_CanSetCreatedAt()
    {
        var now = DateTime.UtcNow;
        var ws = new Workspace { CreatedAt = now };
        Assert.Equal(now, ws.CreatedAt);
    }

    [Fact]
    public void BaseEntity_CanSetUpdatedAt()
    {
        var now = DateTime.UtcNow;
        var ws = new Workspace { UpdatedAt = now };
        Assert.Equal(now, ws.UpdatedAt);
    }

    [Fact]
    public void BaseEntity_CanSetDeletedAt()
    {
        var now = DateTime.UtcNow;
        var ws = new Workspace { DeletedAt = now };
        Assert.Equal(now, ws.DeletedAt);
    }

    [Fact]
    public void BaseEntity_DeletedAtCanBeCleared()
    {
        var ws = new Workspace { DeletedAt = DateTime.UtcNow };
        ws.DeletedAt = null;
        Assert.Null(ws.DeletedAt);
    }

    [Fact]
    public void BaseInt64Entity_DefaultId_IsZero()
    {
        // BaseInt64Entity is abstract, no concrete class uses it yet,
        // but we verify the type exists with the expected shape
        var type = typeof(BaseInt64Entity);
        Assert.NotNull(type.GetProperty("Id"));
        Assert.Equal(typeof(long), type.GetProperty("Id")!.PropertyType);
        Assert.NotNull(type.GetProperty("CreatedAt"));
        Assert.NotNull(type.GetProperty("UpdatedAt"));
        Assert.NotNull(type.GetProperty("DeletedAt"));
    }
}

// ── Session Default Value Tests ─────────────────────────────────────

public class SessionDefaultsTests
{
    [Fact]
    public void Session_WithinBillingQuota_DefaultsNull()
    {
        var session = new Session();
        Assert.Null(session.WithinBillingQuota);
    }

    [Fact]
    public void Session_WithinBillingQuota_CanSetTrue()
    {
        var session = new Session { WithinBillingQuota = true };
        Assert.True(session.WithinBillingQuota);
    }

    [Fact]
    public void Session_Processed_DefaultsNull()
    {
        var session = new Session();
        Assert.Null(session.Processed);
    }

    [Fact]
    public void Session_Processed_CanSetFalse()
    {
        var session = new Session { Processed = false };
        Assert.False(session.Processed);
    }

    [Fact]
    public void Session_Excluded_DefaultsNull()
    {
        var session = new Session();
        Assert.Null(session.Excluded);
    }

    [Fact]
    public void Session_Excluded_CanSetTrue()
    {
        var session = new Session { Excluded = true };
        Assert.True(session.Excluded);
    }

    [Fact]
    public void Session_SecureId_DefaultsToEmptyString()
    {
        var session = new Session();
        Assert.Equal(string.Empty, session.SecureId);
    }

    [Fact]
    public void Session_ProjectId_DefaultsToZero()
    {
        var session = new Session();
        Assert.Equal(0, session.ProjectId);
    }

    [Fact]
    public void Session_HasErrors_DefaultsNull()
    {
        var session = new Session();
        Assert.Null(session.HasErrors);
    }

    [Fact]
    public void Session_HasRageClicks_DefaultsNull()
    {
        var session = new Session();
        Assert.Null(session.HasRageClicks);
    }

    [Fact]
    public void Session_Starred_DefaultsNull()
    {
        var session = new Session();
        Assert.Null(session.Starred);
    }

    [Fact]
    public void Session_RetryCount_DefaultsNull()
    {
        var session = new Session();
        Assert.Null(session.RetryCount);
    }

    [Fact]
    public void Session_Chunked_DefaultsNull()
    {
        var session = new Session();
        Assert.Null(session.Chunked);
    }

    [Fact]
    public void Session_CanSetAllOptionalBools()
    {
        var session = new Session
        {
            WithinBillingQuota = true,
            Processed = true,
            Excluded = false,
            HasErrors = true,
            HasRageClicks = false,
            Starred = true,
            Chunked = false,
            ObjectStorageEnabled = true,
            DirectDownloadEnabled = false,
            PayloadUpdated = true,
            EnableStrictPrivacy = false,
            EnableRecordingNetworkContents = true,
        };
        Assert.True(session.WithinBillingQuota);
        Assert.True(session.Processed);
        Assert.False(session.Excluded);
        Assert.True(session.HasErrors);
        Assert.False(session.HasRageClicks);
        Assert.True(session.Starred);
        Assert.False(session.Chunked);
        Assert.True(session.ObjectStorageEnabled);
        Assert.False(session.DirectDownloadEnabled);
        Assert.True(session.PayloadUpdated);
        Assert.False(session.EnableStrictPrivacy);
        Assert.True(session.EnableRecordingNetworkContents);
    }

    [Fact]
    public void Session_CanSetAllStringFields()
    {
        var session = new Session
        {
            Fingerprint = "fp",
            OSName = "Windows",
            OSVersion = "11",
            BrowserName = "Chrome",
            BrowserVersion = "120",
            City = "NYC",
            State = "NY",
            Country = "US",
            Postal = "10001",
            Identifier = "user@test.com",
            Language = "en-US",
            IP = "127.0.0.1",
            Environment = "production",
            AppVersion = "1.0.0",
            ServiceName = "frontend",
            ClientVersion = "3.0",
            FirstloadVersion = "1.0",
            PrivacySetting = "default",
            ClientConfig = "{}",
            ClientID = "client-abc",
            Lock = "lock-123",
            LastUserInteractionTime = "2024-01-01T00:00:00Z",
        };
        Assert.Equal("fp", session.Fingerprint);
        Assert.Equal("Windows", session.OSName);
        Assert.Equal("127.0.0.1", session.IP);
        Assert.Equal("lock-123", session.Lock);
    }
}

// ── ErrorGroup Tests ────────────────────────────────────────────────

public class ErrorGroupDetailTests
{
    [Fact]
    public void ErrorGroup_DefaultState_IsOpen()
    {
        var eg = new ErrorGroup();
        Assert.Equal(ErrorGroupState.Open, eg.State);
    }

    [Fact]
    public void ErrorGroup_SecureId_DefaultsToEmpty()
    {
        var eg = new ErrorGroup();
        Assert.Equal(string.Empty, eg.SecureId);
    }

    [Fact]
    public void ErrorGroup_SecureId_CanBeSet()
    {
        var eg = new ErrorGroup { SecureId = "abc-123-unique" };
        Assert.Equal("abc-123-unique", eg.SecureId);
    }

    [Fact]
    public void ErrorGroup_TwoGroups_DifferentSecureIds()
    {
        var eg1 = new ErrorGroup { SecureId = "secure-1" };
        var eg2 = new ErrorGroup { SecureId = "secure-2" };
        Assert.NotEqual(eg1.SecureId, eg2.SecureId);
    }

    [Fact]
    public void ErrorGroup_CanTransitionStateThroughAllValues()
    {
        var eg = new ErrorGroup();
        Assert.Equal(ErrorGroupState.Open, eg.State);

        eg.State = ErrorGroupState.Resolved;
        Assert.Equal(ErrorGroupState.Resolved, eg.State);

        eg.State = ErrorGroupState.Ignored;
        Assert.Equal(ErrorGroupState.Ignored, eg.State);

        eg.State = ErrorGroupState.Open;
        Assert.Equal(ErrorGroupState.Open, eg.State);
    }

    [Fact]
    public void ErrorGroup_NavigationCollections_Initialized()
    {
        var eg = new ErrorGroup();
        Assert.NotNull(eg.ErrorObjects);
        Assert.NotNull(eg.Fingerprints);
        Assert.Empty(eg.ErrorObjects);
        Assert.Empty(eg.Fingerprints);
    }

    [Fact]
    public void ErrorGroup_IsPublic_DefaultsNull()
    {
        var eg = new ErrorGroup();
        Assert.Null(eg.IsPublic);
    }

    [Fact]
    public void ErrorGroup_SnoozedUntil_DefaultsNull()
    {
        var eg = new ErrorGroup();
        Assert.Null(eg.SnoozedUntil);
    }

    [Fact]
    public void ErrorGroup_NullableStringFields_DefaultNull()
    {
        var eg = new ErrorGroup();
        Assert.Null(eg.Event);
        Assert.Null(eg.Type);
        Assert.Null(eg.MappedStackTrace);
        Assert.Null(eg.StackTrace);
        Assert.Null(eg.Fields);
        Assert.Null(eg.Environments);
        Assert.Null(eg.ServiceName);
    }
}

// ── Workspace Default Tests ─────────────────────────────────────────

public class WorkspaceDefaultDetailTests
{
    [Fact]
    public void Workspace_DefaultRetentionPeriod_IsSixMonths()
    {
        var ws = new Workspace();
        Assert.Equal(RetentionPeriod.SixMonths, ws.RetentionPeriod);
    }

    [Fact]
    public void Workspace_DefaultPlanTier_IsEnterprise()
    {
        var ws = new Workspace();
        Assert.Equal("Enterprise", ws.PlanTier);
    }

    [Fact]
    public void Workspace_DefaultUnlimitedMembers_IsTrue()
    {
        var ws = new Workspace();
        Assert.True(ws.UnlimitedMembers);
    }

    [Fact]
    public void Workspace_AllRetentionPeriods_Configurable()
    {
        var ws = new Workspace
        {
            RetentionPeriod = RetentionPeriod.ThreeYears,
            ErrorsRetentionPeriod = RetentionPeriod.TwoYears,
            LogsRetentionPeriod = RetentionPeriod.TwelveMonths,
            TracesRetentionPeriod = RetentionPeriod.ThreeMonths,
            MetricsRetentionPeriod = RetentionPeriod.SevenDays,
        };
        Assert.Equal(RetentionPeriod.ThreeYears, ws.RetentionPeriod);
        Assert.Equal(RetentionPeriod.TwoYears, ws.ErrorsRetentionPeriod);
        Assert.Equal(RetentionPeriod.TwelveMonths, ws.LogsRetentionPeriod);
        Assert.Equal(RetentionPeriod.ThreeMonths, ws.TracesRetentionPeriod);
        Assert.Equal(RetentionPeriod.SevenDays, ws.MetricsRetentionPeriod);
    }

    [Fact]
    public void Workspace_NullableFields_DefaultNull()
    {
        var ws = new Workspace();
        Assert.Null(ws.Name);
        Assert.Null(ws.Secret);
        Assert.Null(ws.SlackAccessToken);
        Assert.Null(ws.JiraDomain);
        Assert.Null(ws.LinearAccessToken);
        Assert.Null(ws.VercelAccessToken);
        Assert.Null(ws.DiscordGuildId);
        Assert.Null(ws.ClickupAccessToken);
        Assert.Null(ws.TrialEndDate);
        Assert.Null(ws.AllowedAutoJoinEmailOrigins);
        Assert.Null(ws.MigratedFromProjectId);
    }

    [Fact]
    public void Workspace_NavigationCollections_Initialized()
    {
        var ws = new Workspace();
        Assert.NotNull(ws.Admins);
        Assert.NotNull(ws.Projects);
        Assert.Empty(ws.Admins);
        Assert.Empty(ws.Projects);
    }
}

// ── Admin Tests ─────────────────────────────────────────────────────

public class AdminDetailTests
{
    [Fact]
    public void Admin_Uid_DefaultsNull()
    {
        var admin = new Admin();
        Assert.Null(admin.Uid);
    }

    [Fact]
    public void Admin_Email_DefaultsNull()
    {
        var admin = new Admin();
        Assert.Null(admin.Email);
    }

    [Fact]
    public void Admin_EmailVerified_DefaultsFalse()
    {
        var admin = new Admin();
        Assert.False(admin.EmailVerified);
    }

    [Fact]
    public void Admin_CanSetEmailVerified()
    {
        var admin = new Admin { EmailVerified = true };
        Assert.True(admin.EmailVerified);
    }

    [Fact]
    public void Admin_CanSetAllFields()
    {
        var admin = new Admin
        {
            Uid = "uid-123",
            Name = "Scott",
            Email = "scott@example.com",
            Phone = "+1234567890",
            PhotoUrl = "https://example.com/photo.jpg",
            AboutYouDetailsFilled = true,
            UserDefinedRole = "Engineer",
            UserDefinedPersona = "Backend",
            Referral = "friend",
            EmailVerified = true,
        };
        Assert.Equal("uid-123", admin.Uid);
        Assert.Equal("Scott", admin.Name);
        Assert.Equal("scott@example.com", admin.Email);
        Assert.Equal("+1234567890", admin.Phone);
        Assert.True(admin.EmailVerified);
    }

    [Fact]
    public void Admin_NavigationCollections_Initialized()
    {
        var admin = new Admin();
        Assert.NotNull(admin.Organizations);
        Assert.NotNull(admin.Workspaces);
        Assert.Empty(admin.Organizations);
        Assert.Empty(admin.Workspaces);
    }
}

// ── WorkspaceAdmin Tests ────────────────────────────────────────────

public class WorkspaceAdminTests
{
    [Fact]
    public void WorkspaceAdmin_Role_DefaultsAdmin()
    {
        var wa = new WorkspaceAdmin();
        Assert.Equal("ADMIN", wa.Role);
    }

    [Fact]
    public void WorkspaceAdmin_CanSetRole()
    {
        var wa = new WorkspaceAdmin { Role = "MEMBER" };
        Assert.Equal("MEMBER", wa.Role);
    }

    [Fact]
    public void WorkspaceAdmin_ProjectIds_DefaultsNull()
    {
        var wa = new WorkspaceAdmin();
        Assert.Null(wa.ProjectIds);
    }

    [Fact]
    public void WorkspaceAdmin_CanSetProjectIds()
    {
        var wa = new WorkspaceAdmin { ProjectIds = [1, 2, 3] };
        Assert.Equal(3, wa.ProjectIds!.Count);
        Assert.Contains(2, wa.ProjectIds);
    }

    [Fact]
    public void WorkspaceAdmin_HasCompositeKeyProperties()
    {
        var wa = new WorkspaceAdmin { AdminId = 10, WorkspaceId = 20 };
        Assert.Equal(10, wa.AdminId);
        Assert.Equal(20, wa.WorkspaceId);
    }

    [Fact]
    public void WorkspaceAdmin_DefaultTimestamps()
    {
        var wa = new WorkspaceAdmin();
        Assert.Equal(default(DateTime), wa.CreatedAt);
        Assert.Equal(default(DateTime), wa.UpdatedAt);
        Assert.Null(wa.DeletedAt);
    }
}

// ── Field Tests ─────────────────────────────────────────────────────

public class FieldTests
{
    [Fact]
    public void Field_Name_DefaultsToEmpty()
    {
        var field = new Field();
        Assert.Equal(string.Empty, field.Name);
    }

    [Fact]
    public void Field_Value_DefaultsToEmpty()
    {
        var field = new Field();
        Assert.Equal(string.Empty, field.Value);
    }

    [Fact]
    public void Field_Type_DefaultsNull()
    {
        var field = new Field();
        Assert.Null(field.Type);
    }

    [Fact]
    public void Field_CanSetAllProperties()
    {
        var field = new Field
        {
            ProjectId = 1,
            Type = "session",
            Name = "browser_name",
            Value = "Chrome",
        };
        Assert.Equal(1, field.ProjectId);
        Assert.Equal("session", field.Type);
        Assert.Equal("browser_name", field.Name);
        Assert.Equal("Chrome", field.Value);
    }

    [Fact]
    public void Field_InheritsBaseEntity()
    {
        var field = new Field();
        Assert.Equal(0, field.Id);
        Assert.Equal(default(DateTime), field.CreatedAt);
        Assert.Null(field.DeletedAt);
    }
}

// ── Navigation Property Compilation Tests ───────────────────────────

public class NavigationPropertyTests
{
    [Fact]
    public void Session_HasProjectNavigation()
    {
        var session = new Session();
        Assert.NotNull(typeof(Session).GetProperty("Project"));
        Assert.Equal(typeof(Project), typeof(Session).GetProperty("Project")!.PropertyType);
    }

    [Fact]
    public void ErrorGroup_HasProjectNavigation()
    {
        Assert.Equal(typeof(Project), typeof(ErrorGroup).GetProperty("Project")!.PropertyType);
    }

    [Fact]
    public void ErrorGroup_HasErrorObjectsCollection()
    {
        var type = typeof(ErrorGroup).GetProperty("ErrorObjects")!.PropertyType;
        Assert.True(type.IsAssignableTo(typeof(IEnumerable<ErrorObject>)));
    }

    [Fact]
    public void ErrorGroup_HasFingerprintsCollection()
    {
        var type = typeof(ErrorGroup).GetProperty("Fingerprints")!.PropertyType;
        Assert.True(type.IsAssignableTo(typeof(IEnumerable<ErrorFingerprint>)));
    }

    [Fact]
    public void ErrorObject_HasErrorGroupNavigation()
    {
        Assert.Equal(typeof(ErrorGroup), typeof(ErrorObject).GetProperty("ErrorGroup")!.PropertyType);
    }

    [Fact]
    public void ErrorObject_HasOptionalSessionNavigation()
    {
        var prop = typeof(ErrorObject).GetProperty("Session");
        Assert.NotNull(prop);
        // Nullable reference type: Session?
    }

    [Fact]
    public void Project_HasWorkspaceNavigation()
    {
        Assert.Equal(typeof(Workspace), typeof(Project).GetProperty("Workspace")!.PropertyType);
    }

    [Fact]
    public void Project_HasSetupEventsCollection()
    {
        var prop = typeof(Project).GetProperty("SetupEvents");
        Assert.NotNull(prop);
    }

    [Fact]
    public void Workspace_HasAdminsCollection()
    {
        var type = typeof(Workspace).GetProperty("Admins")!.PropertyType;
        Assert.True(type.IsAssignableTo(typeof(IEnumerable<Admin>)));
    }

    [Fact]
    public void Workspace_HasProjectsCollection()
    {
        var type = typeof(Workspace).GetProperty("Projects")!.PropertyType;
        Assert.True(type.IsAssignableTo(typeof(IEnumerable<Project>)));
    }

    [Fact]
    public void WorkspaceAdmin_HasAdminNavigation()
    {
        Assert.Equal(typeof(Admin), typeof(WorkspaceAdmin).GetProperty("Admin")!.PropertyType);
    }

    [Fact]
    public void WorkspaceAdmin_HasWorkspaceNavigation()
    {
        Assert.Equal(typeof(Workspace), typeof(WorkspaceAdmin).GetProperty("Workspace")!.PropertyType);
    }

    [Fact]
    public void SessionComment_HasSessionNavigation()
    {
        Assert.Equal(typeof(Session), typeof(SessionComment).GetProperty("Session")!.PropertyType);
    }

    [Fact]
    public void SessionComment_HasAdminNavigation()
    {
        Assert.Equal(typeof(Admin), typeof(SessionComment).GetProperty("Admin")!.PropertyType);
    }

    [Fact]
    public void SessionComment_HasTagsCollection()
    {
        var prop = typeof(SessionComment).GetProperty("Tags");
        Assert.NotNull(prop);
    }

    [Fact]
    public void SessionComment_HasRepliesCollection()
    {
        var prop = typeof(SessionComment).GetProperty("Replies");
        Assert.NotNull(prop);
    }

    [Fact]
    public void SessionComment_HasFollowersCollection()
    {
        var prop = typeof(SessionComment).GetProperty("Followers");
        Assert.NotNull(prop);
    }

    [Fact]
    public void ErrorComment_HasErrorGroupNavigation()
    {
        Assert.Equal(typeof(ErrorGroup), typeof(ErrorComment).GetProperty("ErrorGroup")!.PropertyType);
    }

    [Fact]
    public void ErrorComment_HasAdminNavigation()
    {
        Assert.Equal(typeof(Admin), typeof(ErrorComment).GetProperty("Admin")!.PropertyType);
    }

    [Fact]
    public void Alert_HasDestinationsCollection()
    {
        var prop = typeof(Alert).GetProperty("Destinations");
        Assert.NotNull(prop);
    }

    [Fact]
    public void Alert_HasProjectNavigation()
    {
        Assert.Equal(typeof(Project), typeof(Alert).GetProperty("Project")!.PropertyType);
    }

    [Fact]
    public void Dashboard_HasMetricsCollection()
    {
        var prop = typeof(Dashboard).GetProperty("Metrics");
        Assert.NotNull(prop);
    }

    [Fact]
    public void DashboardMetric_HasDashboardNavigation()
    {
        Assert.Equal(typeof(Dashboard), typeof(DashboardMetric).GetProperty("Dashboard")!.PropertyType);
    }

    [Fact]
    public void DashboardMetric_HasFiltersCollection()
    {
        var prop = typeof(DashboardMetric).GetProperty("Filters");
        Assert.NotNull(prop);
    }

    [Fact]
    public void Graph_HasProjectNavigation()
    {
        Assert.Equal(typeof(Project), typeof(Graph).GetProperty("Project")!.PropertyType);
    }

    [Fact]
    public void Visualization_HasGraphsCollection()
    {
        var prop = typeof(Visualization).GetProperty("Graphs");
        Assert.NotNull(prop);
    }
}

// ── ErrorObject / ErrorFingerprint / ErrorTag Tests ─────────────────

public class ErrorObjectTests
{
    [Fact]
    public void ErrorObject_SessionId_Nullable()
    {
        var eo = new ErrorObject();
        Assert.Null(eo.SessionId);
    }

    [Fact]
    public void ErrorObject_TraceId_Nullable()
    {
        var eo = new ErrorObject();
        Assert.Null(eo.TraceId);
    }

    [Fact]
    public void ErrorObject_DefaultTimestamp()
    {
        var eo = new ErrorObject();
        Assert.Equal(default(DateTime), eo.Timestamp);
    }

    [Fact]
    public void ErrorObject_CanSetAllFields()
    {
        var eo = new ErrorObject
        {
            ProjectId = 1,
            SessionId = 2,
            ErrorGroupId = 3,
            Event = "TypeError: undefined",
            Type = "TypeError",
            Url = "https://example.com/page",
            Source = "app.js",
            LineColumnNumber = "42:10",
            StackTrace = "at main() line 42",
            Payload = "{}",
            Environment = "production",
            OS = "Windows",
            Browser = "Chrome",
            RequestId = "req-123",
            IsBeacon = true,
            ServiceName = "web",
            ServiceVersion = "1.0",
            SpanId = "span-abc",
            TraceExternalId = "trace-xyz",
        };
        Assert.Equal("TypeError: undefined", eo.Event);
        Assert.True(eo.IsBeacon);
        Assert.Equal("span-abc", eo.SpanId);
    }
}

public class ErrorFingerprintTests
{
    [Fact]
    public void ErrorFingerprint_Type_DefaultsToEmpty()
    {
        var fp = new ErrorFingerprint();
        Assert.Equal(string.Empty, fp.Type);
    }

    [Fact]
    public void ErrorFingerprint_Value_DefaultsToEmpty()
    {
        var fp = new ErrorFingerprint();
        Assert.Equal(string.Empty, fp.Value);
    }

    [Fact]
    public void ErrorFingerprint_Index_DefaultsNull()
    {
        var fp = new ErrorFingerprint();
        Assert.Null(fp.Index);
    }
}

public class ErrorTagTests
{
    [Fact]
    public void ErrorTag_Title_DefaultsToEmpty()
    {
        var tag = new ErrorTag();
        Assert.Equal(string.Empty, tag.Title);
    }

    [Fact]
    public void ErrorTag_Description_DefaultsNull()
    {
        var tag = new ErrorTag();
        Assert.Null(tag.Description);
    }
}

// ── SessionInterval / EventChunk / RageClickEvent Tests ─────────────

public class SessionRelatedEntityTests
{
    [Fact]
    public void SessionInterval_HasSessionNavigation()
    {
        Assert.Equal(typeof(Session), typeof(SessionInterval).GetProperty("Session")!.PropertyType);
    }

    [Fact]
    public void SessionInterval_DefaultValues()
    {
        var si = new SessionInterval();
        Assert.Equal(0, si.StartTime);
        Assert.Equal(0, si.EndTime);
        Assert.Equal(0, si.Duration);
        Assert.False(si.Active);
    }

    [Fact]
    public void EventChunk_HasSessionNavigation()
    {
        Assert.Equal(typeof(Session), typeof(EventChunk).GetProperty("Session")!.PropertyType);
    }

    [Fact]
    public void EventChunk_DefaultValues()
    {
        var ec = new EventChunk();
        Assert.Equal(0, ec.SessionId);
        Assert.Equal(0, ec.ChunkIndex);
        Assert.Equal(0, ec.Timestamp);
    }

    [Fact]
    public void RageClickEvent_HasSessionNavigation()
    {
        Assert.Equal(typeof(Session), typeof(RageClickEvent).GetProperty("Session")!.PropertyType);
    }
}

// ── Miscellaneous Entity Tests ──────────────────────────────────────

public class MiscellaneousEntityTests
{
    [Fact]
    public void Service_Name_DefaultsToEmpty()
    {
        var svc = new Service();
        Assert.Equal(string.Empty, svc.Name);
    }

    [Fact]
    public void EmailSignup_Email_DefaultsToEmpty()
    {
        var signup = new EmailSignup();
        Assert.Equal(string.Empty, signup.Email);
    }

    [Fact]
    public void EmailOptOut_Category_DefaultsToEmpty()
    {
        var opt = new EmailOptOut();
        Assert.Equal(string.Empty, opt.Category);
    }

    [Fact]
    public void SavedSegment_DefaultsNull()
    {
        var seg = new SavedSegment();
        Assert.Null(seg.Name);
        Assert.Null(seg.Params);
        Assert.Null(seg.EntityType);
    }

    [Fact]
    public void DashboardMetric_Aggregator_DefaultsP50()
    {
        var dm = new DashboardMetric();
        Assert.Equal("P50", dm.Aggregator);
    }

    [Fact]
    public void DashboardMetric_Groups_InitializedEmpty()
    {
        var dm = new DashboardMetric();
        Assert.NotNull(dm.Groups);
        Assert.Empty(dm.Groups);
    }

    [Fact]
    public void DashboardMetricFilter_Op_DefaultsEquals()
    {
        var filter = new DashboardMetricFilter();
        Assert.Equal("equals", filter.Op);
    }

    [Fact]
    public void AllWorkspaceSettings_ErrorEmbeddingsThreshold_Default()
    {
        var s = new AllWorkspaceSettings();
        Assert.Equal(0.2, s.ErrorEmbeddingsThreshold);
    }

    [Fact]
    public void ProjectFilterSettings_DefaultSamplingRates_AllOne()
    {
        var pfs = new ProjectFilterSettings();
        Assert.Equal(1.0, pfs.SessionSamplingRate);
        Assert.Equal(1.0, pfs.ErrorSamplingRate);
        Assert.Equal(1.0, pfs.LogSamplingRate);
        Assert.Equal(1.0, pfs.TraceSamplingRate);
        Assert.Equal(1.0, pfs.MetricSamplingRate);
    }

    [Fact]
    public void SystemConfiguration_Active_DefaultsFalse()
    {
        var sc = new SystemConfiguration();
        Assert.False(sc.Active);
    }

    [Fact]
    public void SystemConfiguration_WorkerCounts_DefaultNull()
    {
        var sc = new SystemConfiguration();
        Assert.Null(sc.MainWorkerCount);
        Assert.Null(sc.LogsWorkerCount);
        Assert.Null(sc.TracesWorkerCount);
    }

    [Fact]
    public void OAuthClientStore_ClientId_DefaultsToEmpty()
    {
        var store = new OAuthClientStore();
        Assert.Equal(string.Empty, store.ClientId);
    }

    [Fact]
    public void SSOClient_HasWorkspaceNavigation()
    {
        Assert.Equal(typeof(Workspace), typeof(SSOClient).GetProperty("Workspace")!.PropertyType);
    }

    [Fact]
    public void CommentReply_BothCommentIds_Nullable()
    {
        var reply = new CommentReply();
        Assert.Null(reply.SessionCommentId);
        Assert.Null(reply.ErrorCommentId);
    }

    [Fact]
    public void CommentFollower_HasMuted_DefaultsFalse()
    {
        var follower = new CommentFollower();
        Assert.False(follower.HasMuted);
    }
}
