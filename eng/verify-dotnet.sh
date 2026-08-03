#!/usr/bin/env bash

# Fails fast when the selected SDK differs from the immutable repository
# contract. Release evidence and local builds therefore execute the same
# compiler and CLI implementation rather than merely sharing a major version.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
global_json="${repo_root}/global.json"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "dotnet is required but was not found on PATH."
    exit 1
fi

required_version="$(
    sed -n \
        's/^[[:space:]]*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*$/\1/p' \
        "${global_json}"
)"
roll_forward="$(
    sed -n \
        's/^[[:space:]]*"rollForward"[[:space:]]*:[[:space:]]*"\([^"]*\)".*$/\1/p' \
        "${global_json}"
)"
allow_prerelease="$(
    sed -n \
        's/^[[:space:]]*"allowPrerelease"[[:space:]]*:[[:space:]]*\([^,[:space:]]*\).*$/\1/p' \
        "${global_json}"
)"

if [[ ! "${required_version}" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "global.json must declare one exact stable .NET SDK version." >&2
    exit 1
fi

if [[ "${roll_forward}" != "disable" ]]; then
    echo "global.json must disable .NET SDK roll-forward." >&2
    exit 1
fi

if [[ "${allow_prerelease}" != "false" ]]; then
    echo "global.json must reject prerelease .NET SDK selection." >&2
    exit 1
fi

sdk_version="$(cd "${repo_root}" && dotnet --version)"

if [[ "${sdk_version}" != "${required_version}" ]]; then
    echo "This repository requires the exact .NET SDK ${required_version}." >&2
    echo "Current SDK: ${sdk_version}" >&2
    exit 1
fi

echo "Using .NET SDK ${sdk_version}"
