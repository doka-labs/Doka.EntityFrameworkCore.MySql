"""Classify repository changes that can invalidate performance evidence.

The hosted benchmark resolver and release-evidence verifier ask related but
different questions. Keeping both classifications here makes that distinction
explicit and prevents their path inventories from drifting independently.
"""

from __future__ import annotations

ACCEPTED_EVIDENCE_FILES = frozenset(
    {
        "benchmarks/baselines/doka-benchmark-baseline.json",
    }
)

NO_MEASUREMENT = "none"
SMOKE_MEASUREMENT = "smoke"
SCORECARD_MEASUREMENT = "scorecard"

MEASUREMENT_INPUT_FILES = frozenset(
    {
        ".config/dotnet-tools.json",
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
        "NuGet.config",
        "eng/benchmark.sh",
        "eng/common/__init__.py",
        "eng/common/deadline.py",
        "eng/common/verify-dotnet.sh",
        "eng/performance/__init__.py",
        "eng/performance/benchmark.sh",
        "eng/performance/benchmarkdotnet.py",
        "eng/performance/baseline.py",
        "eng/performance/confirmation.py",
        "eng/performance/contract.py",
        "eng/performance/environment.py",
        "eng/performance/evaluation.py",
        "eng/performance/host.py",
        "eng/performance/host-preflight.sh",
        "eng/performance/paired-benchmark.sh",
        "eng/performance/reports.py",
        "eng/performance/soak.py",
        "eng/performance/statistics.py",
        "global.json",
    }
)

MEASUREMENT_INPUT_PREFIXES = (
    "benchmarks/",
    "docker/",
    "src/",
)

SCORECARD_INPUT_FILES = MEASUREMENT_INPUT_FILES | {
    # The scorecard expands the matrix, the target workflow produces its
    # artifacts, and the sensitivity module validates their statistical claim.
    # It also binds the endpoint estimator and bounded attempt selector because
    # current policy consumes the statistics they persist.
    # Smoke orchestration is intentionally absent because it has no release
    # authority and cannot change an accepted scorecard's meaning.
    ".github/workflows/benchmark-scorecard.yml",
    ".github/workflows/benchmark-target.yml",
    "eng/performance/check-benchmark-ratios.sh",
    "eng/performance/attempts.py",
    "eng/performance/cli.py",
    "eng/performance/paired.py",
    "eng/performance/sensitivity.py",
}

SMOKE_INPUT_FILES = frozenset(
    {
        ".github/workflows/benchmark-smoke.yml",
    }
)

SCORECARD_INPUT_PREFIXES = (
    "benchmarks/",
    "docker/",
)

SCORECARD_SOURCE_SUFFIXES = (
    ".csproj",
    "/packages.lock.json",
)

SMOKE_INPUT_PREFIXES = (
    "src/",
)


def affects_measurement(path: str) -> bool:
    """Return whether ``path`` can change a measured provider workload.

    Accepted evidence is output, not input. Workflow orchestration and the
    resolver itself likewise remain outside this predicate because they alter
    when a measurement runs rather than what the measurement represents.
    """
    if path in ACCEPTED_EVIDENCE_FILES:
        return False

    return path in MEASUREMENT_INPUT_FILES or path.startswith(
        MEASUREMENT_INPUT_PREFIXES,
    )


def measurement_tier(path: str) -> str:
    """Return the least expensive measurement that safely covers ``path``.

    Provider source receives the complete six-target smoke because an ordinary
    bug fix can break a target-specific benchmark path without changing the
    measurement contract. Benchmark, evaluator, dependency, SDK, and database
    inputs receive the complete scorecard. Accepted evidence is output and
    never allocates a new measurement by itself.

    ``Directory.Packages.props`` is conservatively scorecard-relevant here.
    The Git-aware workflow resolver may lower a proven test-, analyzer-, or
    example-only package update after comparing both revisions structurally.
    """
    if path in ACCEPTED_EVIDENCE_FILES:
        return NO_MEASUREMENT
    if path.startswith("src/") and path.endswith(SCORECARD_SOURCE_SUFFIXES):
        return SCORECARD_MEASUREMENT
    if path in SCORECARD_INPUT_FILES or path.startswith(
        SCORECARD_INPUT_PREFIXES,
    ):
        return SCORECARD_MEASUREMENT
    if path in SMOKE_INPUT_FILES or path.startswith(SMOKE_INPUT_PREFIXES):
        return SMOKE_MEASUREMENT

    return NO_MEASUREMENT
