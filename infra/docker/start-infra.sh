#!/bin/bash -e

source env.sh

# startup the infra

SERVICES="clickhouse postgres"

docker compose pull $SERVICES
docker compose up --detach --wait --remove-orphans $SERVICES

if [[ "$*" != *"--go-docker"* ]]; then
  pushd ../../src/backend
  # migrate postgres schema
  go run ./migrations/main.go > /tmp/highlightSetup.log 2>&1
  if grep -e 'OPENSEARCH_ERROR' /tmp/highlightSetup.log; then
    echo 'Failed to migrate HoldFast infrastructure.'
    grep -e 'OPENSEARCH_ERROR' /tmp/highlightSetup.log
    echo 'Full output.'
    cat /tmp/highlightSetup.log
    exit 1
  fi
  popd
fi
echo 'HoldFast infrastructure started'
