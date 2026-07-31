#!/usr/bin/env bash

# Fails fast when the repository's required .NET SDK major is unavailable, so
# later build output cannot be mistaken for a provider regression.

set -euo pipefail

required_major="10"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "dotnet is required but was not found on PATH."
    exit 1
fi

sdk_version="$(dotnet --version)"
sdk_major="${sdk_version%%.*}"

if [[ "${sdk_major}" != "${required_major}" ]]; then
    echo "This repository requires the .NET 10 SDK."
    echo "Current SDK: ${sdk_version}"
    exit 1
fi

echo "Using .NET SDK ${sdk_version}"
