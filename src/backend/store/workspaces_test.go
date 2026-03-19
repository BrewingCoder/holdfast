package store

import (
	"context"
	"testing"

	"github.com/BrewingCoder/holdfast/src/backend/model"
	"github.com/stretchr/testify/assert"
)

func TestGetWorkspace(t *testing.T) {
	ctx := context.Background()

	defer teardown(t)

	_, err := store.GetWorkspace(ctx, 1)
	assert.Error(t, err)

	workspace := model.Workspace{}
	store.DB.Create(&workspace)

	foundWorkspace, err := store.GetWorkspace(ctx, workspace.ID)
	assert.NoError(t, err)
	assert.Equal(t, workspace.ID, foundWorkspace.ID)
}
