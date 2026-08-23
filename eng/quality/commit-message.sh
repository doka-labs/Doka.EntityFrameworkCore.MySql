#!/usr/bin/env bash

# Validates the deterministic repository commit-message shape without loading
# a second language runtime or package framework.

set -euo pipefail
export LC_ALL=C

message_path="${1:-}"
if [[ -z "${message_path}" || ! -f "${message_path}" ]]; then
    echo "Usage: $(basename "$0") <commit-message-path>" >&2
    exit 2
fi

scissors="------------------------ >8 ------------------------"
lines=()
while IFS= read -r line; do
    lines[${#lines[@]}]="${line}"
done < <(
    awk -v scissors="${scissors}" '
        index($0, scissors) { exit }
        /^[[:space:]]*#/ { next }
        { sub(/\r$/, ""); print }
    ' "${message_path}"
)

while (( ${#lines[@]} > 0 )); do
    last_index=$(( ${#lines[@]} - 1 ))
    if [[ -n "${lines[${last_index}]}" ]]; then
        break
    fi
    unset 'lines[last_index]'
done

errors=()
add_error() {
    errors[${#errors[@]}]="$1"
}

if (( ${#lines[@]} == 0 )); then
    add_error "the commit message is empty"
else
    for index in "${!lines[@]}"; do
        line="${lines[${index}]}"
        line_number=$(( index + 1 ))
        if printf '%s' "${line}" | grep -q '[^ -~]'; then
            add_error "line ${line_number} must contain ASCII characters only"
        fi
        if [[ "${line}" =~ [[:space:]]$ ]]; then
            add_error "line ${line_number} contains trailing whitespace"
        fi
        if (( ${#line} > 72 )); then
            add_error "line ${line_number} exceeds the 72-character limit"
        fi
    done

    subject="${lines[0]}"
    if [[ ! "${subject}" =~ ^(build|chore|ci|docs|feat|fix|perf|refactor|revert|style|test)(\([a-z0-9][a-z0-9.-]*\))?!?:\ [a-z0-9].+$ ]]; then
        add_error "the subject must use '<type>(<scope>): <lower-case summary>' with an approved Conventional Commit type"
    fi
    if [[ "${subject}" == *. ]]; then
        add_error "the subject must not end with a period"
    fi

    if (( ${#lines[@]} == 1 )); then
        add_error "the subject must be followed by a rationale and change bullets"
    elif [[ -n "${lines[1]}" ]]; then
        add_error "the subject must be followed by one blank line"
    else
        for (( index = 2; index + 1 < ${#lines[@]}; index++ )); do
            if [[ -z "${lines[${index}]}" && -z "${lines[$(( index + 1 ))]}" ]]; then
                add_error "body sections must be separated by exactly one blank line"
                break
            fi
        done

        rationale_end=-1
        for (( index = 2; index < ${#lines[@]}; index++ )); do
            if [[ -z "${lines[${index}]}" ]]; then
                rationale_end=${index}
                break
            fi
        done

        if (( rationale_end < 0 )); then
            add_error "the rationale bullet and change bullets must be separated by one blank line"
        else
            if (( rationale_end == 2 )) || [[ "${lines[2]}" != '- '* || "${lines[2]}" == '- ' ]]; then
                add_error "the rationale section must start with one non-empty bullet"
            fi
            for (( index = 3; index < rationale_end; index++ )); do
                if [[ "${lines[${index}]}" == '- '* ]]; then
                    add_error "the rationale section must contain exactly one bullet"
                elif [[ "${lines[${index}]}" != '  '* ]]; then
                    add_error "wrapped rationale lines must start with two spaces"
                fi
            done

            change_start=$(( rationale_end + 1 ))
            change_end=${#lines[@]}
            for (( index = change_start; index < ${#lines[@]}; index++ )); do
                if [[ -z "${lines[${index}]}" ]]; then
                    change_end=${index}
                    break
                fi
            done

            bullet_count=0
            for (( index = change_start; index < change_end; index++ )); do
                if [[ "${lines[${index}]}" == '- '* ]]; then
                    bullet_count=$(( bullet_count + 1 ))
                    if [[ "${lines[${index}]}" == '- ' ]]; then
                        add_error "change bullets must not be empty"
                    fi
                elif [[ "${lines[${index}]}" != '  '* ]]; then
                    add_error "wrapped change lines must start with two spaces"
                fi
            done
            if (( bullet_count == 0 )); then
                add_error "the change section must contain at least one bullet"
            fi

            for (( index = change_end + 1; index < ${#lines[@]}; index++ )); do
                if [[ -n "${lines[${index}]}" && ! "${lines[${index}]}" =~ ^[A-Za-z][A-Za-z0-9-]*:\ .+ ]]; then
                    add_error "content after the change bullets must contain Git trailers only"
                    break
                fi
            done
        fi
    fi
fi

if (( ${#errors[@]} == 0 )); then
    exit 0
fi

echo "Commit message rejected:" >&2
for error in "${errors[@]}"; do
    echo "- ${error}" >&2
done

cat >&2 <<'EOF'

Expected shape:

  fix(provider): summarize the change

  - Explain why the change is required.

  - Describe the implemented change.
  - Describe its verification.
EOF
exit 1
