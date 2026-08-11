"""Keep every copy of a database image pin on the one Dependabot maintains.

Every supported pin appears in the Compose stack and a C# constant. The two
representative performance targets also appear in hosted workflows and the
performance contract. Dependabot edits only the Compose stack, so an accepted
update leaves its applicable copies behind until they are reconciled.

The Compose stack is the source: it is what the update lands in. Every other
copy is held against it, and `--fix` rewrites them.

The check is closed on both sides. It does not ask whether the digest pins it
finds agree, because a reference that lost its digest is not a pin and would
simply not be found. It asks instead which targets a file must carry, and then
rejects anything that is not a full digest pin, any expected target that is
absent, and any reference to an image the Compose stack does not pin.

The accepted baseline is deliberately excluded. It records which image produced
a measurement, so it keeps the pin it was measured with even after the contract
moves on.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
COMPOSE = "docker/compose.yml"

# Compose service -> the target it pins.
SERVICES = {
    "mysql84": "mysql84",
    "mysql97": "mysql97",
    "mariadb1011": "mariadb1011",
    "mariadb114": "mariadb114",
    "mariadb118": "mariadb118",
    "mariadb123": "mariadb123",
}

# The release line each target is allowed to sit on. Specification evidence is
# calibrated to every entry; the representative target entries additionally
# bind the performance contract and baseline. An image from another series
# therefore tests a different contract. Changing a line here is the deliberate
# act that such a support decision requires.
SUPPORTED_LINES = {
    "mysql84": "mysql:8.4",
    "mysql97": "mysql:9.7",
    "mariadb1011": "mariadb:10.11",
    "mariadb114": "mariadb:11.4",
    "mariadb118": "mariadb:11.8",
    "mariadb123": "mariadb:12.3",
}

CSHARP_FILE = "tests/Doka.EntityFrameworkCore.MySql.TestUtilities/TestDatabaseImages.cs"
CSHARP_CONSTANTS = {
    "mysql84": "MySql84",
    "mysql97": "MySql97",
    "mariadb1011": "MariaDb1011",
    "mariadb114": "MariaDb114",
    "mariadb118": "MariaDb118",
    "mariadb123": "MariaDb123",
}

# How many references to each target a copy has to carry. The count is part of
# the contract because a job that loses its image would otherwise stay hidden
# behind its siblings: two of three occurrences still look like a file that
# references the engine. Adding or removing a job changes these numbers, and
# that is meant to be a deliberate edit rather than something a check absorbs.
MIRROR_TARGETS = {
    ".github/workflows/ci.yml": {"mysql84": 3, "mariadb118": 1},
    ".github/workflows/benchmark-scorecard.yml": {"mysql84": 2, "mariadb118": 2},
    "benchmarks/performance-contract.json": {"mysql84": 1, "mariadb118": 1},
    CSHARP_FILE: {
        "mysql84": 1,
        "mysql97": 1,
        "mariadb1011": 1,
        "mariadb114": 1,
        "mariadb118": 1,
        "mariadb123": 1,
    },
}

# Two patterns, deliberately: one to find, one to judge.
#
# Finding runs to the next syntax delimiter rather than to the last character
# that belongs in a well-formed reference. Listing the allowed characters
# instead would end the match right before anything unusual, hand the valid
# prefix to the validator, and let the rest pass unseen -- which is how a
# trailing '/suffix' survived a check that already rejected '-suffix'.
#
# The delimiters are what actually terminates an image reference in the files
# this reads: whitespace in YAML, and the quote or punctuation that closes a
# JSON or C# string.
ANY_REFERENCE = re.compile(
    r"(?<![0-9A-Za-z.-])(?:mysql|mariadb):[0-9][^\s\"',;}\]]*"
)

# Judging is exact and is applied with fullmatch, so a digest that is too long,
# too short, uppercase, or followed by anything at all fails here.
DIGEST_PIN = re.compile(
    r"(?P<name>mysql|mariadb):(?P<version>[0-9][0-9A-Za-z.-]*)"
    r"@sha256:[0-9a-f]{64}"
)

# The name and version of a reference, whether or not a digest follows.
REFERENCE_HEAD = re.compile(r"(?P<name>mysql|mariadb):(?P<version>[0-9][0-9A-Za-z.-]*)")


class PinError(Exception):
    """One image pin could not be read or reconciled."""


def is_digest_pin(reference: str) -> bool:
    """Return whether a reference is exactly one well-formed digest pin."""
    return DIGEST_PIN.fullmatch(reference) is not None


def release_line(reference: str) -> str:
    """Return the release line a reference belongs to, such as 'mariadb:11.8'.

    Multiple lines from each image family are pinned at once, so the image name
    alone cannot say which copy an update belongs to. The line does, and a patch
    update never leaves it. A move to a new line is a decision, not a
    synchronization.
    """
    parsed = REFERENCE_HEAD.match(reference)
    if parsed is None:
        raise PinError(f"'{reference}' does not name a supported engine image.")
    version = parsed.group("version").split(".")

    return f"{parsed.group('name')}:{'.'.join(version[:2])}"


def read_compose_pins(root: Path) -> dict[str, str]:
    """Return the pin each Compose service declares."""
    text = (root / COMPOSE).read_text(encoding="utf-8")
    pins: dict[str, str] = {}
    service: str | None = None

    for line in text.splitlines():
        name = re.match(r"^  (?P<service>[a-z0-9-]+):\s*$", line)
        if name:
            service = name.group("service")
            continue
        image = re.match(r"^\s+image:\s*(?P<image>\S+)\s*$", line)
        if image and service in SERVICES:
            pins[SERVICES[service]] = image.group("image")
            service = None

    missing = sorted(set(SERVICES.values()) - set(pins))
    if missing:
        raise PinError(f"{COMPOSE} declares no image for: {', '.join(missing)}.")

    for target, pin in pins.items():
        if not is_digest_pin(pin):
            raise PinError(
                f"{COMPOSE} pins '{target}' as '{pin}', which carries no digest."
            )

        supported = SUPPORTED_LINES[target]
        if release_line(pin) != supported:
            raise PinError(
                f"{COMPOSE} pins '{target}' on {release_line(pin)}, but the "
                f"provider supports {supported}. Moving a target to another "
                "release line needs its own matrix and baseline work."
            )

    return pins


def references_in(root: Path, relative: str) -> list[str]:
    """Return every engine reference a file makes, pinned or not."""
    path = root / relative
    if not path.is_file():
        raise PinError(f"{relative} is missing.")

    return [match.group(0) for match in ANY_REFERENCE.finditer(path.read_text(encoding="utf-8"))]


def declared_csharp_constants(root: Path) -> dict[str, str]:
    """Return the reference each C# constant declares, if it is declared."""
    text = (root / CSHARP_FILE).read_text(encoding="utf-8")
    declared: dict[str, str] = {}

    for target, constant in CSHARP_CONSTANTS.items():
        found = re.search(rf'{constant}\s*=\s*\n?\s*"(?P<image>[^"]+)"', text)
        if found:
            declared[target] = found.group("image")

    return declared


def declared_contract_targets(root: Path) -> dict[str, str]:
    """Return the image each required performance target declares."""
    contract = json.loads(
        (root / "benchmarks/performance-contract.json").read_text(encoding="utf-8")
    )

    return {
        target: definition["serverImage"]
        for target, definition in contract.get("requiredTargets", {}).items()
        if "serverImage" in definition
    }


def collect_drift(root: Path) -> list[str]:
    """Return one message per way a copy departs from the Compose stack."""
    expected = read_compose_pins(root)
    by_line = {release_line(pin): pin for pin in expected.values()}
    drift: list[str] = []

    for relative, required_counts in MIRROR_TARGETS.items():
        found = references_in(root, relative)
        expected_counts = {
            release_line(expected[target]): count
            for target, count in required_counts.items()
        }
        seen_counts: dict[str, int] = {}

        for reference in found:
            if not is_digest_pin(reference):
                drift.append(f"{relative} references {reference} without a digest.")
                continue

            line = release_line(reference)
            if line not in by_line:
                drift.append(
                    f"{relative} references {reference}, which {COMPOSE} does not pin."
                )
                continue

            seen_counts[line] = seen_counts.get(line, 0) + 1
            if reference != by_line[line]:
                drift.append(
                    f"{relative} references {reference}, but {COMPOSE} pins "
                    f"{by_line[line]}."
                )

        for line, count in sorted(expected_counts.items()):
            seen = seen_counts.get(line, 0)
            if seen != count:
                drift.append(
                    f"{relative} references {line} {seen} time(s), expected {count}."
                )

    # The two structured copies name their targets, so they get a second check
    # the text scan cannot perform. Counting references proves how many pins a
    # file holds, not which target each one belongs to: swapping the MySQL and
    # MariaDB pins between two constants leaves every count intact while every
    # consumer then reaches the wrong engine.
    declared_constants = declared_csharp_constants(root)
    for target, constant in sorted(CSHARP_CONSTANTS.items()):
        if target not in declared_constants:
            drift.append(f"{CSHARP_FILE} declares no {constant} constant.")
        elif declared_constants[target] != expected[target]:
            drift.append(
                f"{CSHARP_FILE} declares {constant} as {declared_constants[target]}, "
                f"but {COMPOSE} pins {expected[target]} for '{target}'."
            )

    contract_relative = "benchmarks/performance-contract.json"
    contract_targets = declared_contract_targets(root)
    for target in sorted(MIRROR_TARGETS[contract_relative]):
        if target not in contract_targets:
            drift.append(
                f"{contract_relative} declares no server image for required "
                f"target '{target}'."
            )
        elif contract_targets[target] != expected[target]:
            drift.append(
                f"{contract_relative} declares '{target}' as "
                f"{contract_targets[target]}, but {COMPOSE} pins {expected[target]}."
            )

    for target in sorted(set(contract_targets) - set(expected)):
        drift.append(
            f"{contract_relative} requires target '{target}', which {COMPOSE} "
            "does not declare."
        )

    return drift


def rewrite(root: Path) -> list[str]:
    """Rewrite every copy to the Compose pins and return what changed.

    Only references that already name a pinned release line are rewritten. A
    missing constant or a dropped target is a structural change that this
    cannot invent, so `--fix` repairs what drifted and the check still reports
    what is absent.
    """
    by_line = {release_line(pin): pin for pin in read_compose_pins(root).values()}
    changed: list[str] = []

    for relative in MIRROR_TARGETS:
        path = root / relative
        original = path.read_text(encoding="utf-8")

        def replace(match: re.Match[str]) -> str:
            try:
                return by_line.get(release_line(match.group(0)), match.group(0))
            except PinError:
                return match.group(0)

        updated = ANY_REFERENCE.sub(replace, original)
        if updated != original:
            path.write_text(updated, encoding="utf-8")
            changed.append(relative)

    return changed


def main(argv: list[str] | None = None) -> int:
    """Report or repair drift between the Compose stack and its copies."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--fix",
        action="store_true",
        help="Rewrite every copy to the pins the Compose stack declares.",
    )
    parser.add_argument("--repo", default=str(REPOSITORY_ROOT))
    arguments = parser.parse_args(argv)
    root = Path(arguments.repo).resolve()

    try:
        if arguments.fix:
            for relative in rewrite(root):
                print(f"Updated {relative}.")
            remaining = collect_drift(root)
            if remaining:
                print(
                    "Some differences need a decision rather than a rewrite:",
                    file=sys.stderr,
                )
                for message in remaining:
                    print(f"  {message}", file=sys.stderr)
                return 1
            print("Database image pins agree with the Compose stack.")
            return 0

        drift = collect_drift(root)
    except (PinError, OSError, json.JSONDecodeError, KeyError) as error:
        print(f"Image pin check failed: {error}", file=sys.stderr)
        return 1

    if drift:
        print("Database image pins disagree with the Compose stack:", file=sys.stderr)
        for message in drift:
            print(f"  {message}", file=sys.stderr)
        print(
            "\nDependabot updates the Compose stack only. Run "
            "'python3 eng/quality/check-image-pins.py --fix' to carry an update "
            "into every copy.",
            file=sys.stderr,
        )
        return 1

    print("Database image pins agree with the Compose stack.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
