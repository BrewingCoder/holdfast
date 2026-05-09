#!/usr/bin/env bash
# HOL-41: post-soak verification.
#
# Runs a per-scenario check against the analytics store and prints a
# colored summary. Returns 0 if every scenario produced data, 1 otherwise.
#
# Usage:
#   ./verify.sh                          # CH backend (default)
#   STORAGE_ANALYTICS=Postgres ./verify.sh
#
# Env:
#   STORAGE_ANALYTICS   ClickHouse | Postgres   (default: ClickHouse)
#   SOAK_SERVICE_NAME   service.name to filter (default: holdfast-soak)
#   SOAK_PROJECT_ID     project_id to filter (default: 2)
#   MIN_PER_SCENARIO    minimum row count per scenario to consider green
#                       (default: 1; raise this for longer runs)

set -uo pipefail

BACKEND=${STORAGE_ANALYTICS:-ClickHouse}
SVC=${SOAK_SERVICE_NAME:-holdfast-soak}
PID=${SOAK_PROJECT_ID:-2}
MIN=${MIN_PER_SCENARIO:-1}

# Color helpers (no-op when stdout isn't a tty)
if [ -t 1 ]; then
    GREEN=$'\033[32m'; RED=$'\033[31m'; YELLOW=$'\033[33m'; RESET=$'\033[0m'
else
    GREEN=''; RED=''; YELLOW=''; RESET=''
fi

failed=0
total=0
report() {
    local name=$1 actual=$2 min=$3
    # Defensive: empty / non-numeric responses (missing table, query error,
    # backend down) all collapse to 0 so the integer comparison below is safe.
    if ! [[ "$actual" =~ ^[0-9]+$ ]]; then
        actual=0
    fi
    total=$((total + 1))
    if [ "$actual" -ge "$min" ]; then
        printf '  %sok%s   %-12s %s rows\n' "$GREEN" "$RESET" "$name" "$actual"
    else
        printf '  %sfail%s %-12s %s rows (expected >= %s)\n' "$RED" "$RESET" "$name" "$actual" "$min"
        failed=$((failed + 1))
    fi
}

ch_query() {
    docker exec clickhouse clickhouse-client --query "$1" 2>/dev/null | tr -d '\r\n '
}

pg_query() {
    docker exec postgres psql -U postgres -d postgres -tAc "$1" 2>/dev/null | tr -d '\r\n '
}

case "$BACKEND" in
    ClickHouse|clickhouse)
        echo "Verifying ClickHouse backend (service.name=$SVC, project_id=$PID, min=$MIN)"
        echo

        # Each scenario reports a row count. Names mirror the scenario module names.
        report logs    "$(ch_query "SELECT count() FROM logs WHERE ServiceName = '$SVC' AND LogAttributes['log.scenario'] = 'logs'")"     "$MIN"
        report traces  "$(ch_query "SELECT count() FROM traces WHERE ServiceName = '$SVC' AND TraceAttributes['trace.scenario'] = 'traces'")" "$MIN"
        report errors  "$(ch_query "SELECT count() FROM logs WHERE ServiceName = '$SVC' AND LogAttributes['log.scenario'] = 'errors'")"   "$MIN"
        report sessions "$(ch_query "SELECT count() FROM traces WHERE ServiceName = '$SVC' AND TraceAttributes['session.scenario'] = 'sessions'")" "$MIN"
        report events  "$(ch_query "SELECT count() FROM logs WHERE ServiceName = '$SVC' AND LogAttributes['log.scenario'] = 'events'")"  "$MIN"

        # Metrics_sum stores Sum + Gauge in the OTeL-shaped schema (HOL-42);
        # data-point attributes live in the Attributes Map column.
        report metrics "$(ch_query "SELECT count() FROM metrics_sum WHERE Attributes['metric.scenario'] = 'metrics'")" "$MIN"

        echo
        echo "Distinct error groups (target: ~20 stable groups from errors.mjs):"
        groups=$(ch_query "SELECT uniq(LogAttributes['error.group_key']) FROM logs WHERE ServiceName = '$SVC' AND LogAttributes['log.scenario'] = 'errors'")
        echo "  $groups distinct group_keys"
        ;;

    Postgres|postgres|pg)
        echo "Verifying Postgres backend (service.name=$SVC, project_id=$PID, min=$MIN)"
        echo

        report logs    "$(pg_query "SELECT count(*) FROM analytics.logs WHERE service_name = '$SVC' AND log_attributes ->> 'log.scenario' = 'logs'")"     "$MIN"
        report traces  "$(pg_query "SELECT count(*) FROM analytics.traces WHERE service_name = '$SVC' AND trace_attributes ->> 'trace.scenario' = 'traces'")" "$MIN"
        report errors  "$(pg_query "SELECT count(*) FROM analytics.logs WHERE service_name = '$SVC' AND log_attributes ->> 'log.scenario' = 'errors'")"   "$MIN"
        report sessions "$(pg_query "SELECT count(*) FROM analytics.traces WHERE service_name = '$SVC' AND trace_attributes ->> 'session.scenario' = 'sessions'")" "$MIN"
        report events  "$(pg_query "SELECT count(*) FROM analytics.logs WHERE service_name = '$SVC' AND log_attributes ->> 'log.scenario' = 'events'")"  "$MIN"
        report metrics "$(pg_query "SELECT count(*) FROM analytics.metrics WHERE tags ->> 'metric.scenario' = 'metrics'")" "$MIN"

        echo
        echo "Distinct error groups (target: ~20 stable groups from errors.mjs):"
        groups=$(pg_query "SELECT count(DISTINCT log_attributes ->> 'error.group_key') FROM analytics.logs WHERE service_name = '$SVC' AND log_attributes ->> 'log.scenario' = 'errors'")
        echo "  $groups distinct group_keys"
        ;;

    *)
        printf '%sUnknown STORAGE_ANALYTICS=%s%s — must be ClickHouse or Postgres\n' "$RED" "$BACKEND" "$RESET" >&2
        exit 2
        ;;
esac

echo
if [ "$failed" -eq 0 ]; then
    printf '%s%d/%d scenarios green%s\n' "$GREEN" "$total" "$total" "$RESET"
    exit 0
else
    printf '%s%d/%d scenarios failed%s\n' "$RED" "$failed" "$total" "$RESET"
    printf '\n%sTroubleshooting:%s see tests/soak/README.md "Troubleshooting" section.\n' "$YELLOW" "$RESET"
    exit 1
fi
