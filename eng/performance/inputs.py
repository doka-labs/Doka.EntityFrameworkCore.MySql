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

RELEASE_REUSE_INPUT_FILES = MEASUREMENT_INPUT_FILES | {
    # The scorecard expands the matrix, the target workflow produces its
    # artifacts, and the sensitivity module validates their statistical claim.
    # Release reuse also binds the endpoint estimator and bounded attempt
    # selector because current policy consumes the statistics they persisted.
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


def invalidates_release_reuse(path: str) -> bool:
    """Return whether ``path`` invalidates release-time evidence reuse.

    Release reuse additionally binds the hosted workflow and admission policy.
    Root build files are matched by shape so a newly introduced solution-wide
    configuration file cannot escape the source-binding contract.
    """
    if path in ACCEPTED_EVIDENCE_FILES:
        return False
    if path in RELEASE_REUSE_INPUT_FILES:
        return True
    if path.startswith(MEASUREMENT_INPUT_PREFIXES):
        return True

    return "/" not in path and (
        path.startswith("Directory.Build.") or path.endswith((".sln", ".slnx"))
    )
