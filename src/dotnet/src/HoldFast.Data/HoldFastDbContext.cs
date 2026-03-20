using System.Text.Json;
using HoldFast.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HoldFast.Data;

public class HoldFastDbContext : DbContext
{
    public HoldFastDbContext(DbContextOptions<HoldFastDbContext> options) : base(options) { }

    // Core
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<Admin> Admins => Set<Admin>();
    public DbSet<WorkspaceAdmin> WorkspaceAdmins => Set<WorkspaceAdmin>();
    public DbSet<WorkspaceInviteLink> WorkspaceInviteLinks => Set<WorkspaceInviteLink>();
    public DbSet<WorkspaceAccessRequest> WorkspaceAccessRequests => Set<WorkspaceAccessRequest>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<SetupEvent> SetupEvents => Set<SetupEvent>();
    public DbSet<ProjectFilterSettings> ProjectFilterSettings => Set<ProjectFilterSettings>();
    public DbSet<ProjectClientSamplingSettings> ProjectClientSamplingSettings => Set<ProjectClientSamplingSettings>();
    public DbSet<AllWorkspaceSettings> AllWorkspaceSettings => Set<AllWorkspaceSettings>();

    // Sessions
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SessionInterval> SessionIntervals => Set<SessionInterval>();
    public DbSet<SessionExport> SessionExports => Set<SessionExport>();
    public DbSet<SessionInsight> SessionInsights => Set<SessionInsight>();
    public DbSet<SessionAdminsView> SessionAdminsViews => Set<SessionAdminsView>();
    public DbSet<EventChunk> EventChunks => Set<EventChunk>();
    public DbSet<RageClickEvent> RageClickEvents => Set<RageClickEvent>();

    // Errors
    public DbSet<ErrorGroup> ErrorGroups => Set<ErrorGroup>();
    public DbSet<ErrorObject> ErrorObjects => Set<ErrorObject>();
    public DbSet<ErrorFingerprint> ErrorFingerprints => Set<ErrorFingerprint>();
    public DbSet<ErrorGroupEmbeddings> ErrorGroupEmbeddings => Set<ErrorGroupEmbeddings>();
    public DbSet<ErrorTag> ErrorTags => Set<ErrorTag>();
    public DbSet<ErrorComment> ErrorComments => Set<ErrorComment>();
    public DbSet<ErrorGroupActivityLog> ErrorGroupActivityLogs => Set<ErrorGroupActivityLog>();
    public DbSet<ErrorGroupAdminsView> ErrorGroupAdminsViews => Set<ErrorGroupAdminsView>();
    public DbSet<ExternalAttachment> ExternalAttachments => Set<ExternalAttachment>();

    // Comments
    public DbSet<SessionComment> SessionComments => Set<SessionComment>();
    public DbSet<SessionCommentTag> SessionCommentTags => Set<SessionCommentTag>();
    public DbSet<CommentReply> CommentReplies => Set<CommentReply>();
    public DbSet<CommentFollower> CommentFollowers => Set<CommentFollower>();
    public DbSet<CommentSlackThread> CommentSlackThreads => Set<CommentSlackThread>();

    // Alerts
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<AlertDestination> AlertDestinations => Set<AlertDestination>();
    public DbSet<ErrorAlert> ErrorAlerts => Set<ErrorAlert>();
    public DbSet<ErrorAlertEvent> ErrorAlertEvents => Set<ErrorAlertEvent>();
    public DbSet<SessionAlert> SessionAlerts => Set<SessionAlert>();
    public DbSet<SessionAlertEvent> SessionAlertEvents => Set<SessionAlertEvent>();
    public DbSet<LogAlert> LogAlerts => Set<LogAlert>();
    public DbSet<LogAlertEvent> LogAlertEvents => Set<LogAlertEvent>();
    public DbSet<MetricMonitor> MetricMonitors => Set<MetricMonitor>();

    // Dashboards & Visualizations
    public DbSet<Dashboard> Dashboards => Set<Dashboard>();
    public DbSet<DashboardMetric> DashboardMetrics => Set<DashboardMetric>();
    public DbSet<DashboardMetricFilter> DashboardMetricFilters => Set<DashboardMetricFilter>();
    public DbSet<Graph> Graphs => Set<Graph>();
    public DbSet<Visualization> Visualizations => Set<Visualization>();

    // Integrations
    public DbSet<IntegrationProjectMapping> IntegrationProjectMappings => Set<IntegrationProjectMapping>();
    public DbSet<IntegrationWorkspaceMapping> IntegrationWorkspaceMappings => Set<IntegrationWorkspaceMapping>();
    public DbSet<VercelIntegrationConfig> VercelIntegrationConfigs => Set<VercelIntegrationConfig>();
    public DbSet<ResthookSubscription> ResthookSubscriptions => Set<ResthookSubscription>();
    public DbSet<OAuthClientStore> OAuthClientStores => Set<OAuthClientStore>();
    public DbSet<OAuthOperation> OAuthOperations => Set<OAuthOperation>();
    public DbSet<SSOClient> SSOClients => Set<SSOClient>();

    // Miscellaneous
    public DbSet<Field> Fields => Set<Field>();
    public DbSet<EnhancedUserDetails> EnhancedUserDetails => Set<EnhancedUserDetails>();
    public DbSet<RegistrationData> RegistrationData => Set<RegistrationData>();
    public DbSet<SavedSegment> SavedSegments => Set<SavedSegment>();
    public DbSet<SavedAsset> SavedAssets => Set<SavedAsset>();
    public DbSet<ProjectAssetTransform> ProjectAssetTransforms => Set<ProjectAssetTransform>();
    public DbSet<DailySessionCount> DailySessionCounts => Set<DailySessionCount>();
    public DbSet<DailyErrorCount> DailyErrorCounts => Set<DailyErrorCount>();
    public DbSet<EmailSignup> EmailSignups => Set<EmailSignup>();
    public DbSet<EmailOptOut> EmailOptOuts => Set<EmailOptOut>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<DeleteSessionsTask> DeleteSessionsTasks => Set<DeleteSessionsTask>();
    public DbSet<UserJourneyStep> UserJourneySteps => Set<UserJourneyStep>();
    public DbSet<SystemConfiguration> SystemConfigurations => Set<SystemConfiguration>();
    public DbSet<LogAdminsView> LogAdminsViews => Set<LogAdminsView>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Use snake_case table/column naming to match existing PostgreSQL schema
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            // Table names: PascalCase → snake_case (e.g., ErrorGroups → error_groups)
            entity.SetTableName(ToSnakeCase(entity.GetTableName()!));

            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnakeCase(property.GetColumnName()));

            foreach (var key in entity.GetKeys())
                key.SetName(ToSnakeCase(key.GetName()!));

            foreach (var fk in entity.GetForeignKeys())
                fk.SetConstraintName(ToSnakeCase(fk.GetConstraintName()!));

            foreach (var index in entity.GetIndexes())
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
        }

        // Composite key for WorkspaceAdmin
        modelBuilder.Entity<WorkspaceAdmin>()
            .HasKey(wa => new { wa.AdminId, wa.WorkspaceId });

        // Many-to-many: Organization <-> Admin
        modelBuilder.Entity<Organization>()
            .HasMany(o => o.Admins)
            .WithMany(a => a.Organizations)
            .UsingEntity("organization_admins");

        // Many-to-many: Workspace <-> Admin (via WorkspaceAdmin)
        modelBuilder.Entity<WorkspaceAdmin>()
            .HasOne(wa => wa.Admin)
            .WithMany()
            .HasForeignKey(wa => wa.AdminId);

        modelBuilder.Entity<WorkspaceAdmin>()
            .HasOne(wa => wa.Workspace)
            .WithMany()
            .HasForeignKey(wa => wa.WorkspaceId);

        // Ignore computed properties not mapped to database columns
        modelBuilder.Entity<Project>()
            .Ignore(p => p.VerboseId);

        // Unique index for AllWorkspaceSettings
        modelBuilder.Entity<AllWorkspaceSettings>()
            .HasIndex(s => s.WorkspaceId)
            .IsUnique();

        // Unique index for SetupEvent
        modelBuilder.Entity<SetupEvent>()
            .HasIndex(e => new { e.ProjectId, e.Type })
            .IsUnique();

        // Unique index for EnhancedUserDetails
        modelBuilder.Entity<EnhancedUserDetails>()
            .HasIndex(e => e.Email)
            .IsUnique();

        // Unique index for WorkspaceAccessRequest
        modelBuilder.Entity<WorkspaceAccessRequest>()
            .HasIndex(r => r.AdminId)
            .IsUnique();

        // PostgreSQL array columns — only apply when using Npgsql
        if (Database.IsNpgsql())
        {
            modelBuilder.Entity<Project>()
                .Property(p => p.ExcludedUsers)
                .HasColumnType("text[]");

            modelBuilder.Entity<Project>()
                .Property(p => p.ErrorFilters)
                .HasColumnType("text[]");

            modelBuilder.Entity<Project>()
                .Property(p => p.ErrorJsonPaths)
                .HasColumnType("text[]");

            modelBuilder.Entity<Project>()
                .Property(p => p.Platforms)
                .HasColumnType("text[]");

            modelBuilder.Entity<DashboardMetric>()
                .Property(m => m.Groups)
                .HasColumnType("text[]");

            modelBuilder.Entity<WorkspaceAdmin>()
                .Property(wa => wa.ProjectIds)
                .HasColumnType("integer[]");

            modelBuilder.Entity<WorkspaceInviteLink>()
                .Property(wil => wil.ProjectIds)
                .HasColumnType("integer[]");
        }
        else
        {
            // Non-PostgreSQL (SQLite for tests): store lists as JSON strings
            var stringListConverter = new ValueConverter<List<string>, string>(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());

            var intListConverter = new ValueConverter<List<int>?, string>(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<int>?>(v, (JsonSerializerOptions?)null));

            modelBuilder.Entity<Project>().Property(p => p.ExcludedUsers).HasConversion(stringListConverter);
            modelBuilder.Entity<Project>().Property(p => p.ErrorFilters).HasConversion(stringListConverter);
            modelBuilder.Entity<Project>().Property(p => p.ErrorJsonPaths).HasConversion(stringListConverter);
            modelBuilder.Entity<Project>().Property(p => p.Platforms).HasConversion(stringListConverter);
            modelBuilder.Entity<DashboardMetric>().Property(m => m.Groups).HasConversion(stringListConverter);
            modelBuilder.Entity<WorkspaceAdmin>().Property(wa => wa.ProjectIds).HasConversion(intListConverter);
            modelBuilder.Entity<WorkspaceInviteLink>().Property(wil => wil.ProjectIds).HasConversion(intListConverter);
        }

        // RetentionPeriod stored as string
        modelBuilder.Entity<Workspace>()
            .Property(w => w.RetentionPeriod)
            .HasConversion<string>();
        modelBuilder.Entity<Workspace>()
            .Property(w => w.ErrorsRetentionPeriod)
            .HasConversion<string>();
        modelBuilder.Entity<Workspace>()
            .Property(w => w.LogsRetentionPeriod)
            .HasConversion<string>();
        modelBuilder.Entity<Workspace>()
            .Property(w => w.TracesRetentionPeriod)
            .HasConversion<string>();
        modelBuilder.Entity<Workspace>()
            .Property(w => w.MetricsRetentionPeriod)
            .HasConversion<string>();
    }

    private static string ToSnakeCase(string name)
    {
        return string.Concat(name.Select((c, i) =>
            i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1])
                ? "_" + c
                : c.ToString()))
            .ToLowerInvariant();
    }
}
