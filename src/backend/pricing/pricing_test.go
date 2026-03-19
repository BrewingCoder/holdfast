package pricing

import (
	"fmt"
	"testing"

	"github.com/BrewingCoder/holdfast/src/backend/model"
	backend "github.com/BrewingCoder/holdfast/src/backend/private-graph/graph/model"
	"github.com/stretchr/testify/assert"
)

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

func TestIncludedAmount_GraduatedPlan(t *testing.T) {
	// Verify IncludedAmount returns expected values for the Graduated plan
	// These are now the sole source of truth — no Monthly*Limit overrides
	expected := map[model.PricingProductType]int64{
		model.PricingProductTypeSessions: 500,
		model.PricingProductTypeErrors:   1_000,
	}
	for productType, expectedAmount := range expected {
		result := IncludedAmount(backend.PlanTypeGraduated, productType)
		assert.Equal(t, expectedAmount, result, "product=%s should have included=%d", productType, expectedAmount)
	}
}

func TestIncludedAmount_AllProductTypes(t *testing.T) {
	// Every plan+product combo should return a non-negative included amount
	for planType := range ProductPrices {
		for productType := range ProductPrices[planType] {
			result := IncludedAmount(planType, productType)
			assert.GreaterOrEqual(t, result, int64(0), "plan=%s product=%s should be >= 0", planType, productType)
		}
	}
}
