package store

import (
	"context"
	"errors"
	"fmt"
	"gorm.io/gorm"
	"time"

	"github.com/BrewingCoder/holdfast/src/backend/model"
	"github.com/BrewingCoder/holdfast/src/backend/redis"
)

func (store *Store) GetProjectAssetTransform(ctx context.Context, projectID int, scheme string) (*model.ProjectAssetTransform, error) {
	return redis.CachedEval(ctx, store.Redis, fmt.Sprintf("project-asset-transform-%d-%s", projectID, scheme), 250*time.Millisecond, time.Minute, func() (*model.ProjectAssetTransform, error) {
		var config model.ProjectAssetTransform
		if err := store.DB.
			WithContext(ctx).
			Model(&config).
			Where(&model.ProjectAssetTransform{ProjectID: projectID, SourceScheme: scheme}).
			Take(&config).Error; err != nil {
			if errors.Is(err, gorm.ErrRecordNotFound) {
				return nil, nil
			}
			return nil, err
		}
		return &config, nil
	}, redis.WithStoreNil(true))
}
