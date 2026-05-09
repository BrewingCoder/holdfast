#!/bin/sh -e
# smoke-test-ingest.sh — verify the SDK ingest pipeline end-to-end.
#
# Sends one InitializeSession + one pushPayload(errors=[…]) against the local
# public GraphQL endpoint, then polls ClickHouse for the resulting error_objects
# row. Exits 0 on success, non-zero on failure.
#
# HOL-6: gives a fresh-checkout dev a single-command smoke test.

PROJECT_ID="${PROJECT_ID:-2}"
PUBLIC_URL="${PUBLIC_URL:-http://localhost:8082/public}"
SECURE_ID="smoke-test-$(date +%s)"
TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-30}"

echo "[1/3] initializeSession (session_secure_id=$SECURE_ID, project=$PROJECT_ID) →"
init_resp=$(curl -sS -X POST "$PUBLIC_URL" -H 'Content-Type: application/json' -d "{
    \"query\": \"mutation init { initializeSession(session_secure_id: \\\"$SECURE_ID\\\", organization_verbose_id: \\\"$PROJECT_ID\\\", clientVersion: \\\"smoke\\\", firstloadVersion: \\\"smoke\\\", clientConfig: \\\"{}\\\", environment: \\\"smoke-test\\\", fingerprint: \\\"$SECURE_ID\\\", serviceName: \\\"smoke-test\\\", client_id: \\\"smoke\\\", enable_strict_privacy: false, enable_recording_network_contents: false) { secure_id project_id } }\"
}")
echo "      $init_resp"
case "$init_resp" in
    *initializeSession*) ;;
    *) echo "      ✗ initializeSession failed"; exit 1 ;;
esac

echo "[2/3] pushPayload with 1 synthetic error →"
push_resp=$(curl -sS -X POST "$PUBLIC_URL" -H 'Content-Type: application/json' -d "{
    \"query\": \"mutation push { pushPayload(session_secure_id: \\\"$SECURE_ID\\\", payload_id: \\\"1\\\", events: { events: [] }, messages: \\\"[]\\\", resources: \\\"[]\\\", errors: [{ event: \\\"smoke test error from $SECURE_ID\\\", type: \\\"TypeError\\\", url: \\\"http://smoke-test\\\", source: \\\"smoke.js\\\", lineNumber: 1, columnNumber: 1, stackTrace: [], timestamp: \\\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\\\" }]) }\"
}")
echo "      $push_resp"
case "$push_resp" in
    *pushPayload*) ;;
    *) echo "      ✗ pushPayload failed"; exit 1 ;;
esac

echo "[3/3] waiting for ClickHouse error_objects (timeout ${TIMEOUT_SECONDS}s)…"
deadline=$(($(date +%s) + TIMEOUT_SECONDS))
while [ "$(date +%s)" -lt "$deadline" ]; do
    count=$(docker exec clickhouse clickhouse-client -q "
        SELECT count()
        FROM error_objects
        WHERE ProjectID = $PROJECT_ID
          AND Timestamp >= now() - INTERVAL 5 MINUTE" 2>/dev/null || echo 0)
    if [ "$count" != "0" ] && [ -n "$count" ]; then
        echo "      $count row(s) found ✓"
        echo ""
        echo "Smoke test passed. The ingest pipeline is working end-to-end."
        exit 0
    fi
    sleep 2
done

echo "      ✗ no error_objects rows after ${TIMEOUT_SECONDS}s — pipeline is broken somewhere"
echo ""
echo "Diagnostics:"
echo "  docker compose -f infra/docker/compose.yml -f infra/docker/compose.hobby-dotnet.yml logs backend | grep -E 'fail|error' | tail -20"
echo "  docker exec kafka kafka-get-offsets --bootstrap-server kafka:9092 --topic frontend-errors"
echo "  docker exec postgres psql -U postgres -d postgres -c 'SELECT count(*) FROM error_groups WHERE project_id = $PROJECT_ID'"
exit 1
