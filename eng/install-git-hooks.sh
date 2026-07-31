#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
hooks_path=".githooks"

if ! git -C "${repo_root}" rev-parse --git-dir > /dev/null 2>&1; then
    echo "The repository Git directory could not be resolved." >&2
    exit 1
fi

for hook in pre-commit pre-push; do
    if [[ ! -x "${repo_root}/${hooks_path}/${hook}" ]]; then
        echo "Hook '${hooks_path}/${hook}' must exist and be executable." >&2
        exit 1
    fi
done

configured_hooks_path="$(git -C "${repo_root}" config --local --get core.hooksPath || true)"

if [[ -n "${configured_hooks_path}" && "${configured_hooks_path}" != "${hooks_path}" ]]; then
    echo "Repository-local core.hooksPath is already '${configured_hooks_path}'." >&2
    echo "Refusing to replace contributor-owned hooks; integrate .githooks manually." >&2
    exit 1
fi

git -C "${repo_root}" config --local core.hooksPath "${hooks_path}"

configured_hooks_path="$(git -C "${repo_root}" config --local --get core.hooksPath)"
if [[ "${configured_hooks_path}" != "${hooks_path}" ]]; then
    echo "Unable to verify the repository-local Git hook configuration." >&2
    exit 1
fi

echo "Repository Git hooks enabled through core.hooksPath=${hooks_path}."
