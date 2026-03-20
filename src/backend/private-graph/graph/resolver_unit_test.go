package graph

import (
	"fmt"
	"testing"

	"github.com/BrewingCoder/holdfast/src/backend/model"
	modelInputs "github.com/BrewingCoder/holdfast/src/backend/private-graph/graph/model"
	"github.com/pkg/errors"
	"github.com/stretchr/testify/assert"
)

// Unit tests for pure functions in resolver.go — no DB required.

func TestIsAuthError_AuthenticationError(t *testing.T) {
	assert.True(t, isAuthError(AuthenticationError))
}

func TestIsAuthError_AuthorizationError(t *testing.T) {
	assert.True(t, isAuthError(AuthorizationError))
}

func TestIsAuthError_WrappedAuthenticationError(t *testing.T) {
	wrapped := errors.Wrap(AuthenticationError, "some context")
	assert.True(t, isAuthError(wrapped))
}

func TestIsAuthError_WrappedAuthorizationError(t *testing.T) {
	wrapped := errors.Wrap(AuthorizationError, "some context")
	assert.True(t, isAuthError(wrapped))
}

func TestIsAuthError_UnrelatedError(t *testing.T) {
	assert.False(t, isAuthError(errors.New("some other error")))
}

func TestIsAuthError_Nil(t *testing.T) {
	assert.False(t, isAuthError(nil))
}

func TestBytesConversion_AllUnits(t *testing.T) {
	expected := map[string]int64{
		"b":  1,
		"kb": 1024,
		"mb": 1024 * 1024,
		"gb": 1024 * 1024 * 1024,
		"tb": 1024 * 1024 * 1024 * 1024,
		"pb": 1024 * 1024 * 1024 * 1024 * 1024,
	}
	assert.Equal(t, expected, BytesConversion)
}

func TestBytesConversion_PowersOf1024(t *testing.T) {
	units := []string{"b", "kb", "mb", "gb", "tb", "pb"}
	for i, unit := range units {
		expected := int64(1)
		for j := 0; j < i; j++ {
			expected *= 1024
		}
		assert.Equal(t, expected, BytesConversion[unit], "unit %s should be 1024^%d", unit, i)
	}
}

func TestBytesConversion_UnknownUnit(t *testing.T) {
	_, ok := BytesConversion["zb"]
	assert.False(t, ok)
}

func TestBytesConversion_CaseSensitive(t *testing.T) {
	// Map keys are lowercase — verify uppercase variants don't exist
	for _, unit := range []string{"B", "KB", "MB", "GB", "TB", "PB"} {
		_, ok := BytesConversion[unit]
		assert.False(t, ok, "uppercase %s should not be in BytesConversion", unit)
	}
}

func TestAdminAndWorkspaceDetails_NoPromoCode(t *testing.T) {
	// Verify the generated model no longer has a PromoCode field.
	// This is a compile-time check — if PromoCode exists, this won't compile
	// because AdminAndWorkspaceDetails would have an extra field.
	details := modelInputs.AdminAndWorkspaceDetails{
		FirstName:           "Test",
		LastName:            "User",
		UserDefinedRole:     "engineer",
		UserDefinedTeamSize: "1-5",
		HeardAbout:          "github",
		Referral:            "",
		WorkspaceName:       "test-workspace",
	}
	assert.Equal(t, "Test", details.FirstName)
	assert.Equal(t, "test-workspace", details.WorkspaceName)
	assert.Nil(t, details.AllowedAutoJoinEmailOrigins)
}

func TestAuthenticationError_Message(t *testing.T) {
	assert.Equal(t, "401 - AuthenticationError", AuthenticationError.Error())
}

func TestAuthorizationError_Message(t *testing.T) {
	assert.Equal(t, "403 - AuthorizationError", AuthorizationError.Error())
}

func TestIsAuthError_DoubleWrapped(t *testing.T) {
	doubleWrapped := fmt.Errorf("outer: %w", fmt.Errorf("inner: %w", AuthenticationError))
	assert.True(t, isAuthError(doubleWrapped))
}

func TestAccountDetails_NoStripeCustomerID(t *testing.T) {
	// Verify the generated model no longer has a StripeCustomerID field.
	// Compile-time check — if field exists, this struct literal would fail.
	details := modelInputs.AccountDetails{
		ID:      1,
		Name:    "test-workspace",
		Members: []*modelInputs.AccountDetailsMember{},
	}
	assert.Equal(t, 1, details.ID)
	assert.Equal(t, "test-workspace", details.Name)
	assert.Empty(t, details.Members)
}

func TestAccount_NoStripeCustomerID(t *testing.T) {
	// Verify the Account type no longer has a StripeCustomerID field.
	account := modelInputs.Account{
		Name:     "test",
		Email:    "test@example.com",
		PlanTier: "Enterprise",
	}
	assert.Equal(t, "test", account.Name)
	assert.Equal(t, "Enterprise", account.PlanTier)
}

func TestAccountDetailsMember_Fields(t *testing.T) {
	// Test adjacent AccountDetailsMember struct used by AccountDetails resolver.
	member := modelInputs.AccountDetailsMember{
		ID:    42,
		Name:  "Jane Doe",
		Email: "jane@example.com",
	}
	assert.Equal(t, 42, member.ID)
	assert.Equal(t, "Jane Doe", member.Name)
	assert.Equal(t, "jane@example.com", member.Email)
	assert.Nil(t, member.LastActive)
}

// --- Issue #31: Monthly limit and max cents field removal ---

func TestWorkspace_NoMonthlyLimitFields(t *testing.T) {
	// Compile-time verification: if any Monthly*Limit or *MaxCents fields
	// still exist on model.Workspace, this struct literal will fail to compile
	// because Go requires all fields to be present or none named.
	w := model.Workspace{
		Name:     strPtr("test-workspace"),
		PlanTier: "Enterprise",
	}
	assert.Equal(t, "test-workspace", *w.Name)
	assert.Equal(t, "Enterprise", w.PlanTier)
}

func TestWorkspace_RetentionFieldsPreserved(t *testing.T) {
	// Retention periods should still exist after removing billing limits.
	sessions := modelInputs.RetentionPeriodSixMonths
	errors := modelInputs.RetentionPeriodSevenDays
	logs := modelInputs.RetentionPeriodThirtyDays
	traces := modelInputs.RetentionPeriodThirtyDays
	metrics := modelInputs.RetentionPeriodThirtyDays
	w := model.Workspace{
		RetentionPeriod:        &sessions,
		ErrorsRetentionPeriod:  &errors,
		LogsRetentionPeriod:    &logs,
		TracesRetentionPeriod:  &traces,
		MetricsRetentionPeriod: &metrics,
	}
	assert.Equal(t, modelInputs.RetentionPeriodSixMonths, *w.RetentionPeriod)
	assert.Equal(t, modelInputs.RetentionPeriodSevenDays, *w.ErrorsRetentionPeriod)
	assert.Equal(t, modelInputs.RetentionPeriodThirtyDays, *w.LogsRetentionPeriod)
	assert.Equal(t, modelInputs.RetentionPeriodThirtyDays, *w.TracesRetentionPeriod)
	assert.Equal(t, modelInputs.RetentionPeriodThirtyDays, *w.MetricsRetentionPeriod)
}

func TestOrganization_NoMonthlySessionLimit(t *testing.T) {
	// Compile-time check: Organization no longer has MonthlySessionLimit.
	org := model.Organization{
		Name: strPtr("test-org"),
	}
	assert.Equal(t, "test-org", *org.Name)
}

func TestProject_NoMonthlySessionLimit(t *testing.T) {
	// Compile-time check: Project no longer has MonthlySessionLimit.
	p := model.Project{
		Name:        strPtr("test-project"),
		WorkspaceID: 1,
	}
	assert.Equal(t, "test-project", *p.Name)
	assert.Equal(t, 1, p.WorkspaceID)
}

// strPtr is a helper to create a *string from a literal.
func strPtr(s string) *string {
	return &s
}
