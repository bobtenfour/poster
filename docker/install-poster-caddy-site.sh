#!/bin/bash
# Append the Poster site block to the existing Caddyfile. Does not edit other site blocks.
set -euo pipefail

if [[ "$(id -u)" -ne 0 ]]; then
  echo "ERROR: run as root."
  exit 1
fi

CADDYFILE="${CADDYFILE:-/etc/caddy/Caddyfile}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
FRAGMENT="${ROOT}/docker/caddy/poster.blueignix.caddy"

[[ -f "${CADDYFILE}" ]] || { echo "ERROR: missing Caddyfile: ${CADDYFILE}"; exit 1; }
[[ -f "${FRAGMENT}" ]] || { echo "ERROR: missing site fragment: ${FRAGMENT}"; exit 1; }
grep -q 'dinventory.blueignix.com' "${CADDYFILE}" || { echo "ERROR: active Caddyfile is missing dinventory.blueignix.com."; exit 1; }
grep -q 'kinventory.blueignix.com' "${CADDYFILE}" || { echo "ERROR: active Caddyfile is missing kinventory.blueignix.com."; exit 1; }
grep -q 'poster.blueignix.com {' "${FRAGMENT}" || { echo "ERROR: fragment is missing the poster.blueignix.com site."; exit 1; }
grep -q 'reverse_proxy 127.0.0.1:8083' "${FRAGMENT}" || { echo "ERROR: fragment must proxy to 127.0.0.1:8083."; exit 1; }

if grep -q 'poster.blueignix.com' "${CADDYFILE}"; then
  echo "poster.blueignix.com is already present. Active Caddyfile left unchanged."
  exit 0
fi

backup_dir="/var/backups/caddy"
mkdir -p "${backup_dir}"
backup="${backup_dir}/Caddyfile.$(date -u +%Y%m%dT%H%M%SZ).bak"
cp -a "${CADDYFILE}" "${backup}"

tmp="$(mktemp)"
awk '{ print }' "${CADDYFILE}" > "${tmp}"
printf '\n' >> "${tmp}"
cat "${FRAGMENT}" >> "${tmp}"

if ! caddy validate --config "${tmp}" --adapter caddyfile; then
  echo "ERROR: caddy validate failed. Active Caddyfile left unchanged (backup at ${backup})."
  rm -f "${tmp}"
  exit 1
fi

install -m 0644 -o root -g root "${tmp}" "${CADDYFILE}"
rm -f "${tmp}"
systemctl reload caddy
echo "poster.blueignix.com installed and Caddy reloaded. Backup: ${backup}"
