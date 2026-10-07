#!/bin/bash
# DEMO ONLY - wait for the existing SQL Server, then apply EF migrations to POSTER.
set -euo pipefail

CONN="${ConnectionStrings__PosterPrintRequest:-}"
if [[ -z "${CONN}" ]]; then
  echo "ERROR: ConnectionStrings__PosterPrintRequest is not set."
  exit 1
fi

database="$(printf '%s' "${CONN}" | sed -n 's/.*[Dd]atabase=\([^;]*\).*/\1/p')"
if [[ "${database}" != "POSTER" ]]; then
  echo "ERROR: Demo migrate database must be POSTER."
  exit 1
fi

if echo "${CONN}" | grep -Eqi 'Server=(localhost|127\.0\.0\.1|\(localdb\))'; then
  echo "ERROR: Demo migrate must use Docker service host sqlserver, not localhost."
  exit 1
fi

MAX_ATTEMPTS="${MIGRATE_MAX_ATTEMPTS:-30}"
SLEEP_SECS="${MIGRATE_SLEEP_SECS:-2}"
SQL_HOST="${DEMO_SQL_HOST:-sqlserver}"
SQL_PORT="${DEMO_SQL_PORT:-1433}"

echo "Waiting for TCP ${SQL_HOST}:${SQL_PORT}..."
for attempt in $(seq 1 "${MAX_ATTEMPTS}"); do
  if (echo >"/dev/tcp/${SQL_HOST}/${SQL_PORT}") >/dev/null 2>&1; then
    echo "TCP port open (attempt ${attempt})."
    break
  fi
  if [[ "${attempt}" -eq "${MAX_ATTEMPTS}" ]]; then
    echo "ERROR: Timed out waiting for ${SQL_HOST}:${SQL_PORT}."
    exit 1
  fi
  sleep "${SLEEP_SECS}"
done

echo "Allowing SQL Server login readiness..."
sleep 5

echo "Applying EF migrations to POSTER..."
for attempt in $(seq 1 "${MAX_ATTEMPTS}"); do
  echo "dotnet ef database update attempt ${attempt}/${MAX_ATTEMPTS}..."
  if dotnet ef database update \
    --project src/PosterPrintRequest.Infrastructure/PosterPrintRequest.Infrastructure.csproj \
    --startup-project src/PosterPrintRequest.Web/PosterPrintRequest.Web.csproj \
    --connection "${CONN}"; then
    echo "Migrations applied successfully."
    exit 0
  fi
  if [[ "${attempt}" -eq "${MAX_ATTEMPTS}" ]]; then
    echo "ERROR: EF migrate failed after ${MAX_ATTEMPTS} attempts."
    exit 1
  fi
  sleep "${SLEEP_SECS}"
done
