#!/bin/bash -e

source env.sh
./start-infra.sh

./run-frontend.sh &
./run-backend.sh &
echo 'waiting for HoldFast app to come online'
yarn dlx wait-on -l -s 2 "${REACT_APP_FRONTEND_URI}"/index.html "${BACKEND_HEALTH_URI}"

echo "HoldFast started on ${REACT_APP_FRONTEND_URI}"
wait
