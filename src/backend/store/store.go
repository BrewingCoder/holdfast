package store

import (
	"github.com/BrewingCoder/holdfast/src/backend/clickhouse"
	"github.com/BrewingCoder/holdfast/src/backend/integrations"
	kafka_queue "github.com/BrewingCoder/holdfast/src/backend/kafka-queue"
	"github.com/BrewingCoder/holdfast/src/backend/redis"
	"github.com/BrewingCoder/holdfast/src/backend/storage"

	"gorm.io/gorm"
)

type Store struct {
	DB                 *gorm.DB
	Redis              *redis.Client
	IntegrationsClient *integrations.Client
	StorageClient      storage.Client
	DataSyncQueue      kafka_queue.MessageQueue
	ClickhouseClient   *clickhouse.Client
}

func NewStore(db *gorm.DB, redis *redis.Client, integrationsClient *integrations.Client, storageClient storage.Client, dataSyncQueue kafka_queue.MessageQueue, clickhouseClient *clickhouse.Client) *Store {
	return &Store{
		DB:                 db,
		Redis:              redis,
		IntegrationsClient: integrationsClient,
		StorageClient:      storageClient,
		DataSyncQueue:      dataSyncQueue,
		ClickhouseClient:   clickhouseClient,
	}
}
