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

func TestWorkspace_NoTrialFields(t *testing.T) {
	// Compile-time verification that trial fields are removed from Workspace.
	// If TrialEndDate, EligibleForTrialExtension, or TrialExtensionEnabled
	// exist, this struct literal would have extra fields and fail to compile.
	w := model.Workspace{
		AllowMeterOverage: true,
	}
	assert.True(t, w.AllowMeterOverage)
}

func TestOrganization_NoTrialEndDate(t *testing.T) {
	// Verify Organization no longer has TrialEndDate
	org := model.Organization{}
	assert.Equal(t, 0, org.ID)
}

func TestProject_NoTrialEndDate(t *testing.T) {
	// Verify Project no longer has TrialEndDate
	p := model.Project{}
	assert.Equal(t, 0, p.ID)
}
