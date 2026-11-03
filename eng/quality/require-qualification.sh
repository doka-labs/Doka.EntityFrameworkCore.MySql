#!/usr/bin/env bash

# Requires the exact declared dependency set to have completed successfully.
# WHY: An empty shell loop or a skipped producer must never qualify a candidate.

set -euo pipefail

if [[ "$#" -eq 0 ]]; then
    echo "At least one required qualification job must be declared." >&2
    exit 2
fi

expected="$(printf '%s\n' "$@" | jq -R . | jq -s .)"
if ! jq -s -e --argjson expected "${expected}" '
    length == 1
    and (.[0] | type == "object"
    and ($expected | length) > 0
    and ($expected | all(type == "string" and length > 0))
    and ($expected | length) == ($expected | unique | length)
    and keys == ($expected | sort)
    and all(.[]; type == "object" and .result == "success"))
' <<< "${RESULTS:-}" >/dev/null; then
    echo "Qualification requires exactly the declared jobs, all with result success." >&2
    exit 1
fi

# WHY: Ordinary JSON object parsing keeps only the last duplicate key. Inspect
# the stream too so a later success cannot hide a preceding failed job/result.
if ! jq --stream -s -e --argjson expected "${expected}" '
    ([.[]
      | select((length == 1 and (.[0] | length) == 2)
               or (length == 2 and (.[0] | length) == 1))
      | .[0][0]] | sort) == ($expected | sort)
    and ([.[]
      | select(length == 2 and (.[0] | length) == 2 and .[0][1] == "result")
      | .[0][0]] | sort) == ($expected | sort)
' <<< "${RESULTS:-}" >/dev/null; then
    echo "Qualification results must not repeat or omit a job or result." >&2
    exit 1
fi

jq -r 'to_entries | sort_by(.key)[] | "\(.key): \(.value.result)"' <<< "${RESULTS}"
