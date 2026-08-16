#!/usr/bin/env bash
set -euo pipefail

required=(
  ASPNETCORE_ENVIRONMENT
  ASPNETCORE_URLS
  ConnectionStrings__Postgres
  Jwt__SigningKey
  Jwt__Issuer
  Jwt__Audience
  AllowedHosts
  Cors__AllowedOrigins__0
)

missing=()
for name in "${required[@]}"; do
  if [[ -z "${!name:-}" ]]; then
    missing+=("${name}")
  fi
done

if (( ${#missing[@]} > 0 )); then
  echo "Missing required environment variables: ${missing[*]}" >&2
  exit 1
fi

if (( ${#Jwt__SigningKey} < 32 )); then
  echo "Jwt__SigningKey must be at least 32 bytes." >&2
  exit 1
fi

if [[ "${ASPNETCORE_ENVIRONMENT}" == "Production" && ( "${AllowedHosts}" == "*" || -z "${AllowedHosts}" ) ]]; then
  echo "AllowedHosts must be explicit in Production." >&2
  exit 1
fi

echo "Deployment environment validation passed for ${ASPNETCORE_ENVIRONMENT}."
