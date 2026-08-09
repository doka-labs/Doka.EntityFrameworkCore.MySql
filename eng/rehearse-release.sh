#!/usr/bin/env bash

# Keep the reviewed operator entry point stable while the implementation
# remains colocated with the release evidence domain.

set -euo pipefail

eng_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

exec "${eng_root}/release/rehearse-release.sh" "$@"
