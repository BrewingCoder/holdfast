package pricing

import (
	"context"
	"database/sql"
	"fmt"
	"time"

	"github.com/BrewingCoder/holdfast/src/backend/clickhouse"
	"github.com/BrewingCoder/holdfast/src/backend/model"
	backend "github.com/BrewingCoder/holdfast/src/backend/private-graph/graph/model"
	"github.com/BrewingCoder/holdfast/src/backend/redis"
	"github.com/BrewingCoder/holdfast/src/backend/util"

	"github.com/openlyinc/pointy"
	e "github.com/pkg/errors"
	"github.com/samber/lo"
	log "github.com/sirupsen/logrus"
	"gorm.io/gorm"
)

type GraduatedPriceItem struct {
	Rate  float64
	Count int64
}

type ProductPricing struct {
	Included int64
	Items    []GraduatedPriceItem
}

var ProductPrices = map[backend.PlanType]map[model.PricingProductType]ProductPricing{
	backend.PlanTypeGraduated: {
		model.PricingProductTypeSessions: {
			Included: 500,
			Items: []GraduatedPriceItem{{
				Rate:  20. / 1_000,
				Count: 15_000,
			}, {
				Rate:  15. / 1_000,
				Count: 50_000,
			}, {
				Rate:  12. / 1_000,
				Count: 150_000,
			}, {
				Rate:  6.5 / 1_000,
				Count: 500_000,
			}, {
				Rate:  3.5 / 1_000,
				Count: 1_000_000,
			}, {
				Rate: 2.5 / 1_000,
			}},
		},
		model.PricingProductTypeErrors: {
			Included: 1_000,
			Items: []GraduatedPriceItem{{
				Rate:  2. / 1_000,
				Count: 50_000,
			}, {
				Rate:  0.5 / 1_000,
				Count: 100_000,
			}, {
				Rate:  0.25 / 1_000,
				Count: 200_000,
			}, {
				Rate:  0.2 / 1_000,
				Count: 500_000,
			}, {
				Rate:  0.1 / 1_000,
				Count: 5_000_000,
			}, {
				Rate: 0.05 / 1_000,
			}},
		},
		model.PricingProductTypeLogs: {
			Included: 1_000_000,
			Items: []GraduatedPriceItem{{
				Rate:  2.5 / 1_000_000,
				Count: 1_000_000,
			}, {
				Rate:  2. / 1_000_000,
				Count: 10_000_000,
			}, {
				Rate:  1.5 / 1_000_000,
				Count: 100_000_000,
			}, {
				Rate:  1. / 1_000_000,
				Count: 1_000_000_000,
			}, {
				Rate: 0.5 / 1_000_000,
			}},
		},
		model.PricingProductTypeTraces: {
			Included: 25_000_000,
			Items: []GraduatedPriceItem{{
				Rate:  2.5 / 1_000_000,
				Count: 1_000_000,
			}, {
				Rate:  2. / 1_000_000,
				Count: 10_000_000,
			}, {
				Rate:  1.5 / 1_000_000,
				Count: 100_000_000,
			}, {
				Rate:  1. / 1_000_000,
				Count: 1_000_000_000,
			}, {
				Rate: 0.5 / 1_000_000,
			}},
		},
		model.PricingProductTypeMetrics: {
			Included: 1_000,
			Items: []GraduatedPriceItem{{
				Rate:  2.5 / 1_000,
				Count: 1_000,
			}, {
				Rate:  2. / 1_000,
				Count: 10_000,
			}, {
				Rate:  1.5 / 1_000,
				Count: 100_000,
			}, {
				Rate:  1. / 1_000,
				Count: 1_000_000,
			}, {
				Rate: 0.5 / 1_000,
			}},
		},
	},
	backend.PlanTypeUsageBased: {
		model.PricingProductTypeSessions: {
			Included: 500,
			Items: []GraduatedPriceItem{{
				Rate: 20. / 1_000,
			}},
		},
		model.PricingProductTypeErrors: {
			Included: 1_000,
			Items: []GraduatedPriceItem{{
				Rate: 2. / 1_000,
			}},
		},
		model.PricingProductTypeLogs: {
			Included: 1_000_000,
			Items: []GraduatedPriceItem{{
				Rate: 1.5 / 1_000_000,
			}},
		},
		model.PricingProductTypeTraces: {
			Included: 1_000_000,
			Items: []GraduatedPriceItem{{
				Rate: 1.5 / 1_000_000,
			}},
		},
		model.PricingProductTypeMetrics: {
			Included: 1_000,
			Items: []GraduatedPriceItem{{
				Rate: 1.5 / 1_000,
			}},
		},
	},
	backend.PlanTypeLite: {
		model.PricingProductTypeSessions: {
			Included: 2_000,
			Items: []GraduatedPriceItem{{
				Rate: 5. / 1_000,
			}},
		},
		model.PricingProductTypeErrors: {
			Included: 4_000,
			Items: []GraduatedPriceItem{{
				Rate: 0.2 / 1_000,
			}},
		},
		model.PricingProductTypeLogs: {
			Included: 4_000_000,
			Items: []GraduatedPriceItem{{
				Rate: 1.5 / 1_000_000,
			}},
		},
		model.PricingProductTypeTraces: {
			Included: 4_000_000,
			Items: []GraduatedPriceItem{{
				Rate: 1.5 / 1_000_000,
			}},
		},
		model.PricingProductTypeMetrics: {
			Included: 2_000,
			Items: []GraduatedPriceItem{{
				Rate: 1.5 / 1_000,
			}},
		},
	},
	backend.PlanTypeBasic: {
		model.PricingProductTypeSessions: {
			Included: 10_000,
			Items: []GraduatedPriceItem{{
				Rate: 5. / 1_000,
			}},
		},
		model.PricingProductTypeErrors: {
			Included: 20_000,
			Items: []GraduatedPriceItem{{
				Rate: 0.2 / 1_000,
			}},
		},
		model.PricingProductTypeLogs: {
			Included: 20_000_000,
			Items: []GraduatedPriceItem{{
				Rate: 1.5 / 1_000_000,
			}},
		},
		model.PricingProductTypeTraces: {
			Included: 20_000_000,
			Items: []GraduatedPriceItem{{
				Rate: 1.5 / 1_000_000,
			}},
		},
		model.PricingProductTypeMetrics: {
			Included: 3_000,
			Items: []GraduatedPriceItem{{
				Rate: 1.5 / 1_000,
			}},
		},
	},
	backend.PlanTypeStartup: {
		model.PricingProductTypeSessions: {
			Included: 80_000,
			Items: []GraduatedPriceItem{{
				Rate: 5. / 1_000,
			}},
		},
		model.PricingProductTypeErrors: {
			Included: 160_000,
			Items: []GraduatedPriceItem{{
				Rate: 0.2 / 1_000,
			}},
		},
		model.PricingProductTypeLogs: {
			Included: 160_000_000,
			Items: []GraduatedPriceItem{{
				Rate: 1.5 / 1_000_000,
			}},
		},
		model.PricingProductTypeTraces: {
			Included: 160_000_000,
			Items: []GraduatedPriceItem{{
				Rate: 1.5 / 1_000_000,
			}},
		},
		model.PricingProductTypeMetrics: {
			Included: 6_000,
			Items: []GraduatedPriceItem{{
				Rate: 1.5 / 1_000,
			}},
		},
	},
	backend.PlanTypeEnterprise: {
		model.PricingProductTypeSessions: {
			Included: 300_000,
			Items: []GraduatedPriceItem{{
				Rate: 5. / 1_000,
			}},
		},
		model.PricingProductTypeErrors: {
			Included: 600_000,
			Items: []GraduatedPriceItem{{
				Rate: 0.2 / 1_000,
			}},
		},
		model.PricingProductTypeLogs: {
			Included: 600_000_000,
			Items: []GraduatedPriceItem{{
				Rate: 1.5 / 1_000_000,
			}},
		},
		model.PricingProductTypeTraces: {
			Included: 600_000_000,
			Items: []GraduatedPriceItem{{
				Rate: 1.5 / 1_000_000,
			}},
		},
		model.PricingProductTypeMetrics: {
			Included: 24_000,
			Items: []GraduatedPriceItem{{
				Rate: 1.5 / 1_000,
			}},
		},
	},
	backend.PlanTypeFree: {
		model.PricingProductTypeSessions: {
			Included: 500,
			Items: []GraduatedPriceItem{{
				Rate: 5. / 1_000,
			}},
		},
		model.PricingProductTypeErrors: {
			Included: 1_000,
			Items: []GraduatedPriceItem{{
				Rate: 0.2 / 1_000,
			}},
		},
		model.PricingProductTypeLogs: {
			Included: 1_000_000,
			Items: []GraduatedPriceItem{{
				Rate: 1.5 / 1_000_000,
			}},
		},
		model.PricingProductTypeTraces: {
			Included: 25_000_000,
			Items: []GraduatedPriceItem{{
				Rate: 1.5 / 1_000_000,
			}},
		},
		model.PricingProductTypeMetrics: {
			Included: 1_000,
			Items: []GraduatedPriceItem{{
				Rate: 1.5 / 1_000,
			}},
		},
	},
}

func GetSessions7DayAverage(ctx context.Context, DB *gorm.DB, ccClient *clickhouse.Client, workspace *model.Workspace) (float64, error) {
	var avg float64
	if err := DB.WithContext(ctx).Raw(`
			SELECT COALESCE(AVG(count), 0) as trailingAvg
			FROM daily_session_counts_view
			WHERE project_id in (SELECT id FROM projects WHERE workspace_id=?)
			AND date >= now() - INTERVAL '8 days'
			AND date < now() - INTERVAL '1 day'`, workspace.ID).
		Scan(&avg).Error; err != nil {
		return 0, e.Wrap(err, "error querying for session meter")
	}
	return avg, nil
}

func GetWorkspaceSessionsMeter(ctx context.Context, DB *gorm.DB, ccClient *clickhouse.Client, redisClient *redis.Client, workspace *model.Workspace) (int64, error) {
	meterSpan, _ := util.StartSpanFromContext(ctx, "GetWorkspaceSessionsMeter",
		util.ResourceName("GetWorkspaceSessionsMeter"),
		util.Tag("workspace_id", workspace.ID))
	defer meterSpan.Finish()

	res, err := redis.CachedEval(ctx, redisClient, fmt.Sprintf(`workspace-sessions-meter-%d`, workspace.ID), time.Minute, time.Hour, func() (*int64, error) {
		var meter int64
		if err := DB.WithContext(ctx).Raw(`
		WITH billing_start AS (
			SELECT COALESCE(next_invoice_date - interval '1 month', billing_period_start, date_trunc('month', now(), 'UTC'))
			FROM workspaces
			WHERE id=@workspace_id
		),
		billing_end AS (
			SELECT COALESCE(next_invoice_date, billing_period_end, date_trunc('month', now(), 'UTC') + interval '1 month')
			FROM workspaces
			WHERE id=@workspace_id
		),
		materialized_rows AS (
			SELECT count, date
			FROM daily_session_counts_view
			WHERE project_id in (SELECT id FROM projects WHERE workspace_id=@workspace_id)
			AND date >= (SELECT * FROM billing_start)
			AND date < (SELECT * FROM billing_end)
		),
		start_date as (SELECT COALESCE(MAX(date), (SELECT * from billing_start)) FROM materialized_rows)
		SELECT SUM(count) as currentPeriodSessionCount from (
			SELECT COUNT(*) FROM sessions
			WHERE project_id IN (SELECT id FROM projects WHERE workspace_id=@workspace_id)
			AND created_at >= (SELECT * FROM start_date)
			AND created_at < (SELECT * FROM billing_end)
			AND excluded <> true
			AND within_billing_quota
			AND (active_length >= 1000 OR (active_length is null and length >= 1000))
			AND processed = true
			UNION ALL SELECT COALESCE(SUM(count), 0) FROM materialized_rows
			WHERE date < (SELECT MAX(date) FROM materialized_rows)
		) a`, sql.Named("workspace_id", workspace.ID)).
			Scan(&meter).Error; err != nil {
			return nil, e.Wrap(err, "error querying for session meter")
		}
		return &meter, nil
	})
	return pointy.Int64Value(res, 0), err
}

func GetErrors7DayAverage(ctx context.Context, DB *gorm.DB, ccClient *clickhouse.Client, workspace *model.Workspace) (float64, error) {
	var avg float64
	if err := DB.WithContext(ctx).Raw(`
			SELECT COALESCE(AVG(count), 0) as trailingAvg
			FROM daily_error_counts_view
			WHERE project_id in (SELECT id FROM projects WHERE workspace_id=?)
			AND date >= now() - INTERVAL '8 days'
			AND date < now() - INTERVAL '1 day'`, workspace.ID).
		Scan(&avg).Error; err != nil {
		return 0, e.Wrap(err, "error querying for session meter")
	}
	return avg, nil
}

func GetWorkspaceErrorsMeter(ctx context.Context, DB *gorm.DB, ccClient *clickhouse.Client, redisClient *redis.Client, workspace *model.Workspace) (int64, error) {
	meterSpan, _ := util.StartSpanFromContext(ctx, "GetWorkspaceErrorsMeter",
		util.ResourceName("GetWorkspaceErrorsMeter"),
		util.Tag("workspace_id", workspace.ID))
	defer meterSpan.Finish()

	res, err := redis.CachedEval(ctx, redisClient, fmt.Sprintf(`workspace-errors-meter-%d`, workspace.ID), time.Minute, time.Hour, func() (*int64, error) {
		var meter int64
		if err := DB.WithContext(ctx).Raw(`
		WITH billing_start AS (
			SELECT COALESCE(next_invoice_date - interval '1 month', billing_period_start, date_trunc('month', now(), 'UTC'))
			FROM workspaces
			WHERE id=@workspace_id
		),
		billing_end AS (
			SELECT COALESCE(next_invoice_date, billing_period_end, date_trunc('month', now(), 'UTC') + interval '1 month')
			FROM workspaces
			WHERE id=@workspace_id
		),
		materialized_rows AS (
			SELECT count, date
			FROM daily_error_counts_view
			WHERE project_id in (SELECT id FROM projects WHERE workspace_id=@workspace_id)
			AND date >= (SELECT * FROM billing_start)
			AND date < (SELECT * FROM billing_end)
		),
		start_date as (SELECT COALESCE(MAX(date), (SELECT * from billing_start)) FROM materialized_rows)
		SELECT SUM(count) as currentPeriodErrorCount from (
			SELECT COUNT(*) FROM error_objects
			WHERE project_id IN (SELECT id FROM projects WHERE workspace_id=@workspace_id)
			AND created_at >= (SELECT * FROM start_date)
			AND created_at < (SELECT * FROM billing_end)
			UNION ALL SELECT COALESCE(SUM(count), 0) FROM materialized_rows
			WHERE date < (SELECT MAX(date) FROM materialized_rows)
		) a`, sql.Named("workspace_id", workspace.ID)).
			Scan(&meter).Error; err != nil {
			return nil, e.Wrap(err, "error querying for error meter")
		}
		return &meter, nil
	})
	return pointy.Int64Value(res, 0), err
}

func get7DayAverageImpl(ctx context.Context, DB *gorm.DB, ccClient *clickhouse.Client, workspace *model.Workspace, productType model.PricingProductType) (float64, error) {
	startDate := time.Now().AddDate(0, 0, -8)
	endDate := time.Now().AddDate(0, 0, -1)
	projectIds := lo.Map(workspace.Projects, func(p model.Project, _ int) int {
		return p.ID
	})

	var avgFn func(ctx context.Context, projectIds []int, dateRange backend.DateRangeRequiredInput) (float64, error)
	switch productType {
	case model.PricingProductTypeLogs:
		avgFn = ccClient.ReadLogsDailyAverage
	case model.PricingProductTypeTraces:
		avgFn = ccClient.ReadTracesDailyAverage
	case model.PricingProductTypeMetrics:
		avgFn = ccClient.ReadMetricsDailyAverage
	default:
		return 0, fmt.Errorf("invalid product type %s", productType)
	}

	return avgFn(ctx, projectIds, backend.DateRangeRequiredInput{StartDate: startDate, EndDate: endDate})
}

func getWorkspaceMeterImpl(ctx context.Context, DB *gorm.DB, ccClient *clickhouse.Client, workspace *model.Workspace, productType model.PricingProductType) (int64, error) {
	var startDate time.Time
	if workspace.NextInvoiceDate != nil {
		startDate = workspace.NextInvoiceDate.AddDate(0, -1, 0)
	} else if workspace.BillingPeriodStart != nil {
		startDate = *workspace.BillingPeriodStart
	} else {
		currentYear, currentMonth, _ := time.Now().Date()
		startDate = time.Date(currentYear, currentMonth, 1, 0, 0, 0, 0, time.UTC)
	}

	var endDate time.Time
	if workspace.NextInvoiceDate != nil {
		endDate = *workspace.NextInvoiceDate
	} else if workspace.BillingPeriodEnd != nil {
		endDate = *workspace.BillingPeriodEnd
	} else {
		currentYear, currentMonth, _ := time.Now().Date()
		endDate = time.Date(currentYear, currentMonth, 1, 0, 0, 0, 0, time.UTC).AddDate(0, 1, 0)
	}

	projectIds := lo.Map(workspace.Projects, func(p model.Project, _ int) int {
		return p.ID
	})

	var sumFn func(ctx context.Context, projectIds []int, dateRange backend.DateRangeRequiredInput) (uint64, error)
	switch productType {
	case model.PricingProductTypeLogs:
		sumFn = ccClient.ReadLogsDailySum
	case model.PricingProductTypeTraces:
		sumFn = ccClient.ReadTracesDailySum
	case model.PricingProductTypeMetrics:
		sumFn = ccClient.ReadMetricsDailySum
	default:
		return 0, fmt.Errorf("invalid product type %s", productType)
	}

	count, err := sumFn(ctx, projectIds, backend.DateRangeRequiredInput{StartDate: startDate, EndDate: endDate})
	if err != nil {
		return 0, err
	}

	return int64(count), nil
}

func GetLogs7DayAverage(ctx context.Context, DB *gorm.DB, ccClient *clickhouse.Client, workspace *model.Workspace) (float64, error) {
	return get7DayAverageImpl(ctx, DB, ccClient, workspace, model.PricingProductTypeLogs)
}

func GetWorkspaceLogsMeter(ctx context.Context, DB *gorm.DB, ccClient *clickhouse.Client, redis *redis.Client, workspace *model.Workspace) (int64, error) {
	return getWorkspaceMeterImpl(ctx, DB, ccClient, workspace, model.PricingProductTypeLogs)
}

func GetTraces7DayAverage(ctx context.Context, DB *gorm.DB, ccClient *clickhouse.Client, workspace *model.Workspace) (float64, error) {
	return get7DayAverageImpl(ctx, DB, ccClient, workspace, model.PricingProductTypeTraces)
}

func GetWorkspaceTracesMeter(ctx context.Context, DB *gorm.DB, ccClient *clickhouse.Client, redis *redis.Client, workspace *model.Workspace) (int64, error) {
	return getWorkspaceMeterImpl(ctx, DB, ccClient, workspace, model.PricingProductTypeTraces)
}

func GetWorkspaceMetricsMeter(ctx context.Context, DB *gorm.DB, ccClient *clickhouse.Client, redis *redis.Client, workspace *model.Workspace) (int64, error) {
	return getWorkspaceMeterImpl(ctx, DB, ccClient, workspace, model.PricingProductTypeMetrics)
}

func GetLimitAmount(limitCostCents *int, productType model.PricingProductType, planType backend.PlanType, retentionPeriod backend.RetentionPeriod) *int64 {
	// Self-hosted: no limits
	return nil
}

func ProductToBasePriceCents(productType model.PricingProductType, planType backend.PlanType, meter int64) float64 {
	// Self-hosted: no billing, return zero cost
	return 0
}

func RetentionMultiplier(retentionPeriod backend.RetentionPeriod) float64 {
	switch retentionPeriod {
	case backend.RetentionPeriodSevenDays:
		return 1
	case backend.RetentionPeriodThirtyDays:
		return 1
	case backend.RetentionPeriodThreeMonths:
		return 1
	case backend.RetentionPeriodSixMonths:
		return 1.5
	case backend.RetentionPeriodTwelveMonths:
		return 2
	case backend.RetentionPeriodTwoYears:
		return 2.5
	case backend.RetentionPeriodThreeYears:
		return 3
	default:
		return 1
	}
}

func TypeToMemberLimit(planType backend.PlanType, unlimitedMembers bool) *int64 {
	// Self-hosted: unlimited members
	return nil
}

func IncludedAmount(planType backend.PlanType, productType model.PricingProductType) int64 {
	return ProductPrices[planType][productType].Included
}

// MustUpgradeForClearbit - self-hosted: never require upgrade
func MustUpgradeForClearbit(planTier string) bool {
	return false
}

// Worker is a no-op for self-hosted deployments
type Worker struct{}

func NewWorker(db *gorm.DB, redis *redis.Client, store interface{}, ccClient *clickhouse.Client, pricingClient *Client, awsmpClient interface{}, mailClient interface{}) *Worker {
	return &Worker{}
}

type WorkspaceOverages = map[model.PricingProductType]int64

func (w *Worker) ReportStripeUsageForWorkspace(ctx context.Context, workspaceID int) error {
	// No-op for self-hosted
	return nil
}

func (w *Worker) ReportAllUsage(ctx context.Context) {
	// No-op for self-hosted
	log.WithContext(ctx).Info("self-hosted: skipping usage reporting")
}

func (w *Worker) CalculateOverages(ctx context.Context, workspaceID int) (WorkspaceOverages, error) {
	// Self-hosted: no overages
	return WorkspaceOverages{}, nil
}

// ProductTypeToQuotaConfig is kept for compatibility but not used for billing enforcement in self-hosted mode
type overageConfig struct {
	MaxCostCents          func(*model.Workspace) *int
	Meter                 func(ctx context.Context, DB *gorm.DB, ccClient *clickhouse.Client, redisClient *redis.Client, workspace *model.Workspace) (int64, error)
	RetentionPeriod       func(*model.Workspace) backend.RetentionPeriod
	Included              func(*model.Workspace) int64
	OverageEmail          interface{}
	OverageEmailThreshold int64
}

var ProductTypeToQuotaConfig = map[model.PricingProductType]overageConfig{}
