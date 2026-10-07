#!/bin/sh
# DEMO ONLY - validate the Poster database and storage root, then start the web process as app.
set -eu

CONN="${ConnectionStrings__PosterPrintRequest:-}"
ROOT="${SharedStorage__RootPath:-}"

if [ -z "${CONN}" ]; then
  echo "ERROR: ConnectionStrings__PosterPrintRequest is required."
  exit 1
fi

if [ -z "${ROOT}" ]; then
  echo "ERROR: SharedStorage__RootPath is required."
  exit 1
fi

database=$(printf '%s' "${CONN}" | sed -n 's/.*[Dd]atabase=\([^;]*\).*/\1/p')
if [ "${database}" != "POSTER" ]; then
  echo "ERROR: Demo web database must be POSTER."
  exit 1
fi

if printf '%s' "${CONN}" | grep -Eqi 'Server=(localhost|127\.0\.0\.1|\(localdb\))'; then
  echo "ERROR: Demo web must use Docker host sqlserver, not a local SQL instance."
  exit 1
fi

if [ "${ROOT}" != "/var/poster-print-request" ]; then
  echo "ERROR: Demo storage root must be /var/poster-print-request."
  exit 1
fi

mkdir -p "${ROOT}"
chown app:app "${ROOT}"

export ASPNETCORE_URLS="http://+:${PORT:-8080}"

exec runuser -u app -- dotnet PosterPrintRequest.Web.dll
