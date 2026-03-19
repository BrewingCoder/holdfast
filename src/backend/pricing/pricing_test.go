package pricing

import (
	"fmt"
	"testing"

	"github.com/BrewingCoder/holdfast/src/backend/model"
	backend "github.com/BrewingCoder/holdfast/src/backend/private-graph/graph/model"
	"github.com/stretchr/testify/assert"
)

func TestRetentionMultiplier_AllPeriods(t *testing.T) {
	tests := map[backend.RetentionPeriod]float64{
		backend.RetentionPeriodSevenDays:     1,
		backend.RetentionPeriodThirtyDays:    1,
		backend.RetentionPeriodThreeMonths:   1,
		backend.RetentionPeriodSixMonths:     1.5,
		backend.RetentionPeriodTwelveMonths:  2,
		backend.RetentionPeriodTwoYears:      2.5,
		backend.RetentionPeriodThreeYears:    3,
	}
	for period, expected := range tests {
		t.Run(string(period), func(t *testing.T) {
			assert.Equal(t, expected, RetentionMultiplier(period))
		})
	}
}

func TestRetentionMultiplier_Unknown(t *testing.T) {
	assert.Equal(t, float64(1), RetentionMultiplier("unknown_period"))
}

func TestProductToBasePriceCents_AlwaysZero(t *testing.T) {
	// Self-hosted: no billing, all costs are zero
	for _, planType := range []backend.PlanType{backend.PlanTypeFree, backend.PlanTypeBasic, backend.PlanTypeEnterprise, backend.PlanTypeUsageBased, backend.PlanTypeGraduated} {
		for _, productType := range []model.PricingProductType{model.PricingProductTypeSessions, model.PricingProductTypeErrors, model.PricingProductTypeLogs, model.PricingProductTypeTraces} {
			result := ProductToBasePriceCents(productType, planType, 1000)
			assert.Equal(t, float64(0), result, "plan=%s product=%s should be zero cost", planType, productType)
		}
	}
}

func TestTypToMemberLimit_AlwaysNil(t *testing.T) {
	// Self-hosted: unlimited members for all plan types
	for _, planType := range []backend.PlanType{backend.PlanTypeFree, backend.PlanTypeBasic, backend.PlanTypeEnterprise} {
		assert.Nil(t, TypeToMemberLimit(planType, false), "plan=%s unlimited=false should be nil", planType)
		assert.Nil(t, TypeToMemberLimit(planType, true), "plan=%s unlimited=true should be nil", planType)
	}
}

func TestMustUpgradeForClearbit_AlwaysFalse(t *testing.T) {
	// Self-hosted: never require upgrade
	for _, tier := range []string{"Free", "Basic", "Enterprise", "Graduated", "", "unknown"} {
		assert.False(t, MustUpgradeForClearbit(tier), "tier=%s should not require upgrade", tier)
	}
}

func TestWorker_NoOps(t *testing.T) {
	w := NewWorker(nil, nil, nil, nil, nil, nil, nil)
	assert.NotNil(t, w)

	overages, err := w.CalculateOverages(nil, 1)
	assert.NoError(t, err)
	assert.Empty(t, overages)

	assert.NoError(t, w.ReportStripeUsageForWorkspace(nil, 1))
}

func TestGetLimitAmount(t *testing.T) {
	type Testcase struct {
		planType        backend.PlanType
		productType     model.PricingProductType
		retentionPeriod backend.RetentionPeriod
	}
	tests := map[string]Testcase{
		"test legacy plan": {
			planType:        backend.PlanTypeBasic,
			productType:     model.PricingProductTypeSessions,
			retentionPeriod: backend.RetentionPeriodTwoYears,
		},
		"test legacy plan logs": {
			planType:        backend.PlanTypeBasic,
			productType:     model.PricingProductTypeLogs,
			retentionPeriod: backend.RetentionPeriodThreeMonths,
		},
		"test usage-based plan": {
			planType:        backend.PlanTypeUsageBased,
			productType:     model.PricingProductTypeSessions,
			retentionPeriod: backend.RetentionPeriodThreeMonths,
		},
		"test usage-based plan logs": {
			planType:        backend.PlanTypeUsageBased,
			productType:     model.PricingProductTypeLogs,
			retentionPeriod: backend.RetentionPeriodThreeMonths,
		},
		"test graduated plan": {
			planType:        backend.PlanTypeGraduated,
			productType:     model.PricingProductTypeSessions,
			retentionPeriod: backend.RetentionPeriodThreeMonths,
		},
		"test graduated logs": {
			planType:        backend.PlanTypeGraduated,
			productType:     model.PricingProductTypeLogs,
			retentionPeriod: backend.RetentionPeriodThreeMonths,
		},
	}
	for name, tc := range tests {
		for _, limitCostCents := range []int{int(1.23 * 100), int(1234.56 * 100)} {
			t.Run(fmt.Sprintf("%s-%d", name, limitCostCents), func(t *testing.T) {
				count := GetLimitAmount(&limitCostCents, tc.productType, tc.planType, tc.retentionPeriod)
				// Self-hosted: GetLimitAmount returns nil meaning unlimited — no billing caps enforced.
				assert.Nil(t, count, "self-hosted deployments have no limit: GetLimitAmount must return nil")
			})
		}
	}
}
