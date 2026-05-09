#!/bin/sh -e
# get-project-keys.sh — print all workspace/project/API-key tuples from the dev seed.
# Run after `docker compose up` has seeded the dev instance.
#
# Usage:
#   ./infra/docker/get-project-keys.sh
#
# Login (after seeding, hobby/dev defaults):
#   email:    dev@holdfast.local
#   password: $ADMIN_PASSWORD (default "password" from infra/docker/.env)
#   URL:      http://localhost:3000

docker exec postgres psql -U postgres -d postgres -At -F$'\t' -c "
    SELECT w.name, p.name, p.secret
    FROM projects p
    JOIN workspaces w ON p.workspace_id = w.id
    ORDER BY w.id, p.id;
" | awk -F'\t' 'BEGIN{print "WORKSPACE\tPROJECT\tAPI_KEY"} {print $1"\t"$2"\t"$3}'
