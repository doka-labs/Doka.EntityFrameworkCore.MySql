#!/usr/bin/env bash

set -euo pipefail

eng_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

exec "${eng_root}/testing/test-migration-deployment.sh" "$@"
