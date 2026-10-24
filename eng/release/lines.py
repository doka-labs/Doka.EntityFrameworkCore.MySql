"""Pure contracts for release-line authority and frozen branch qualification.

Reading a candidate's copy of this policy does not grant publication rights.
The publication boundary independently observes protected main and validates
the exact admitted candidate against that current authority under its lock.
"""

from __future__ import annotations

import copy
import hashlib
import json
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Any
from xml.etree import ElementTree


POLICY_PATH = Path(__file__).resolve().with_name("lines-policy.json")
SPECIFICATION_BASELINE_PATH = Path(
    "tests/Doka.EntityFrameworkCore.MySql.FunctionalTests/Specification/Contracts/SpecSuiteBaseline.json"
)

REPOSITORY = "doka-labs/Doka.EntityFrameworkCore.MySql"
AUTHORITY_REF = "refs/heads/main"
LEGACY_WORKFLOW_PATH = ".github/workflows/release-candidate.yml"
# WHY: every line uses the existing public entry and publisher environment;
# a framework upgrade does not introduce another credential generation.
LINE_WORKFLOW_PATH = LEGACY_WORKFLOW_PATH
SHA256 = re.compile(r"[0-9a-f]{64}")
REVISION = re.compile(r"[0-9A-Za-z][0-9A-Za-z._-]{0,127}")
VERSION = re.compile(r"(0|[1-9][0-9]*)[.](0|[1-9][0-9]*)[.](0|[1-9][0-9]*)(?:-([0-9a-z.-]+))?")
POLICY_FIELDS = {"schemaVersion", "revision", "repository", "authorityRef", "lines"}
LINE_FIELDS = {"sourceRef", "role", "packageMajor", "channel", "upstreamStage", "state", "workflowPath",
               "publisherEnvironment"}
SNAPSHOT_FIELDS = {"schemaVersion", "repository", "sourceRef", "role", "packageMajor", "channel", "upstreamStage",
                   "stateAtQualification", "workflowPath", "publisherEnvironment", "authorizationRevision",
                   "authorizationDigest"}
TOOLCHAIN_FIELDS = {"approvedDotnetSdk", "targetFramework", "efCoreVersion", "testRunner", "upstreamStage"}


class LinePolicyError(RuntimeError):
    """A line, historical snapshot or branch toolchain cannot prove its identity."""


@dataclass(frozen=True)
class ReleaseVersion:
    """Canonical immutable SemVer identity used by publication ordering."""

    major: int
    minor: int
    patch: int
    prerelease: tuple[str, ...]

    @property
    def is_prerelease(self) -> bool:
        """Return whether this identity precedes its corresponding stable release."""
        return bool(self.prerelease)


def parse_version(value: str) -> ReleaseVersion:
    """Parse one canonical three-part NuGet/SemVer identity without aliases.

    WHY: NuGet normalizes aliases and ignores build metadata for identity;
    accepting those here could bind different tags to the same package.
    """
    match = VERSION.fullmatch(value) if isinstance(value, str) else None
    if match is None:
        raise LinePolicyError("Release version must be a canonical NuGet/SemVer identity.")

    prerelease = tuple(match[4].split(".")) if match[4] else ()
    if any(not item or (item.isdigit() and len(item) > 1 and item[0] == "0") for item in prerelease):
        raise LinePolicyError("Release version has a noncanonical prerelease identifier.")

    return ReleaseVersion(int(match[1]), int(match[2]), int(match[3]), prerelease)


def compare_versions(left: str, right: str) -> int:
    """Compare canonical versions by SemVer precedence, returning -1, 0 or 1."""
    first = parse_version(left)
    second = parse_version(right)
    first_core = (first.major, first.minor, first.patch)
    second_core = (second.major, second.minor, second.patch)
    if first_core != second_core:
        return (first_core > second_core) - (first_core < second_core)

    if not first.prerelease or not second.prerelease:
        return int(not first.prerelease) - int(not second.prerelease)

    for first_part, second_part in zip(first.prerelease, second.prerelease):
        if first_part == second_part:
            continue

        if first_part.isdigit() and second_part.isdigit():
            return (int(first_part) > int(second_part)) - (int(first_part) < int(second_part))

        if first_part.isdigit() != second_part.isdigit():
            return -1 if first_part.isdigit() else 1

        return (first_part > second_part) - (first_part < second_part)

    return (len(first.prerelease) > len(second.prerelease)) - (len(first.prerelease) < len(second.prerelease))


def _object(value: Any, fields: set[str], label: str) -> dict:
    """Reject unknown properties rather than silently declaring unconsumed knobs."""
    if not isinstance(value, dict) or set(value) != fields:
        raise LinePolicyError(f"{label} must contain exactly these fields: {', '.join(sorted(fields))}.")

    return value


def _schema(value: Any, label: str) -> None:
    """Keep JSON bool separate from the integer schema version."""
    if type(value) is not int or value != 1:
        raise LinePolicyError(f"{label} requires schemaVersion 1.")


def _choice(value: Any, allowed: tuple[str, ...], label: str) -> str:
    """Return a strict string choice without implicit coercion."""
    if not isinstance(value, str) or value not in allowed:
        raise LinePolicyError(f"{label} must be one of: {', '.join(allowed)}.")

    return value


def _revision(value: Any) -> None:
    """Require one printable ASCII revision token, not an inferred Git identity."""
    if not isinstance(value, str) or REVISION.fullmatch(value) is None:
        raise LinePolicyError("Release-line revision must be a canonical ASCII token.")


def _digest(value: Any, label: str) -> None:
    """Validate a content identity before it is used as a finite permission."""
    if not isinstance(value, str) or SHA256.fullmatch(value) is None:
        raise LinePolicyError(f"{label} must be an exact lowercase SHA-256 digest.")


def _line_identity(value: dict) -> None:
    """Validate approved role/ref, package-major and workflow/environment pairs."""
    role = _choice(value["role"], ("main", "maintenance", "preview"), "Release-line role")
    major = value["packageMajor"]
    if type(major) is not int or major < 1:
        raise LinePolicyError("Release-line packageMajor must be a positive integer.")

    expected_ref = AUTHORITY_REF if role == "main" else f"refs/heads/{role}/{major}.x"
    if value["sourceRef"] != expected_ref:
        raise LinePolicyError(f"Release-line sourceRef must be the exact approved identity {expected_ref}.")

    stage = _choice(value["upstreamStage"], ("ga", "preview", "rc"), "Release-line upstream stage")
    if role != "preview" and stage != "ga":
        raise LinePolicyError("Stable main and maintenance lines require GA upstream dependencies.")

    workflow = value["workflowPath"]
    environment = value["publisherEnvironment"]
    if workflow != LINE_WORKFLOW_PATH:
        raise LinePolicyError("Release-line workflow must use the exact registered public entry.")

    if environment != "nuget":
        raise LinePolicyError("Every release line must use the existing nuget publisher environment.")


def validate_policy(value: Any) -> dict:
    """Validate current authorization data without performing hosted reads/writes.

    Publication independently checks the existing environment and current
    branch grants. This contract neither changes nor restricts admin bypass.
    """
    policy = _object(value, POLICY_FIELDS, "Release-line policy")
    _schema(policy["schemaVersion"], "Release-line policy")
    _revision(policy["revision"])
    if policy["repository"] != REPOSITORY or policy["authorityRef"] != AUTHORITY_REF:
        raise LinePolicyError("Release-line policy must identify this repository and protected main authority.")

    entries = policy["lines"]
    if not isinstance(entries, list) or not entries:
        raise LinePolicyError("Release-line policy must contain a nonempty lines array.")

    refs: set[str] = set()
    owners: set[int] = set()
    main_count = 0
    for entry in entries:
        line = _object(entry, LINE_FIELDS, "Release line")
        _line_identity(line)
        source_ref = line["sourceRef"]
        if source_ref in refs:
            raise LinePolicyError(f"Release-line policy contains a duplicate sourceRef: {source_ref}.")

        refs.add(source_ref)
        main_count += int(line["role"] == "main")
        channel = _choice(line["channel"], ("stable-and-prerelease", "prerelease"), "Release-line channel")
        if (line["role"] == "preview") != (channel == "prerelease"):
            raise LinePolicyError("Only preview lines use the prerelease-only package channel.")

        state = _choice(line["state"], ("inactive", "prepared", "active"), "Release-line state")
        if state != "active":
            continue

        major = line["packageMajor"]
        if major in owners:
            raise LinePolicyError(f"Only one active publication route may own package major {major}.")

        owners.add(major)

    if main_count != 1:
        raise LinePolicyError("Release-line policy must retain exactly one main identity.")

    return copy.deepcopy(policy)


def _unique_object(pairs: list[tuple[str, Any]]) -> dict:
    """Reject duplicate JSON names before normal dictionary parsing loses them."""
    result = {}
    for key, value in pairs:
        if key in result:
            raise LinePolicyError(f"Release-line JSON contains a duplicate property: {key}.")

        result[key] = value

    return result


def _read_json(path: Path, label: str) -> Any:
    """Read a regular JSON file with deterministic object-key semantics."""
    if path.is_symlink() or not path.is_file():
        raise LinePolicyError(f"{label} must be a regular file: {path}.")

    try:
        return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=_unique_object)
    except (OSError, UnicodeError, json.JSONDecodeError) as exception:
        raise LinePolicyError(f"Unable to read {label}: {path}.") from exception


def load_policy(path: Path | None = None) -> dict:
    """Load strict reviewed policy data; this operation itself grants no rights."""
    return validate_policy(_read_json(path or POLICY_PATH, "release-line policy"))


def parse_policy(value: str | bytes) -> dict:
    """Validate downloaded policy bytes with the same strict key semantics."""
    try:
        parsed = json.loads(value, object_pairs_hook=_unique_object)
    except (ValueError, UnicodeError, TypeError) as exception:
        raise LinePolicyError("Release-line policy is not valid JSON.") from exception

    return validate_policy(parsed)


def policy_digest(policy: dict) -> str:
    """Bind the complete canonical reviewed policy independently of JSON layout."""
    validated = validate_policy(policy)
    encoded = json.dumps(validated, sort_keys=True, separators=(",", ":"), ensure_ascii=True).encode("ascii")

    return hashlib.sha256(encoded).hexdigest()


def _version_for_line(line: dict, version: str) -> str:
    """Validate candidate major and derive its actual package channel."""
    parsed = parse_version(version)
    if parsed.major != line["packageMajor"]:
        raise LinePolicyError("Candidate package major does not match its exact authorized release line.")

    channel = "prerelease" if parsed.is_prerelease else "stable"
    if line["role"] == "preview" and channel != "prerelease":
        raise LinePolicyError("A preview source may publish prerelease packages only, even after upstream GA.")

    return channel


def select_line(policy: dict, source_ref: str, version: str | None = None, *, purpose: str = "qualification") -> dict:
    """Select one exact permitted line, without admitting a publication attempt."""
    validated = validate_policy(policy)
    allowed = {"qualification": ("prepared", "active"), "publication": ("active",),
               "inspect": ("inactive", "prepared", "active")}
    if not isinstance(purpose, str) or purpose not in allowed:
        raise LinePolicyError("Release-line selection purpose must be qualification, publication or inspect.")

    selected = next((entry for entry in validated["lines"] if entry["sourceRef"] == source_ref), None)
    if selected is None or selected["state"] not in allowed[purpose]:
        raise LinePolicyError(f"The exact source ref is not eligible for {purpose}: {source_ref}.")

    if version is not None:
        _version_for_line(selected, version)

    return selected


def freeze_line(policy: dict, source_ref: str, version: str, *, workflow_path: str | None = None) -> dict:
    """Freeze reviewed qualification identity without claiming future permission."""
    selected = select_line(policy, source_ref, version)
    if workflow_path is not None and workflow_path != selected["workflowPath"]:
        raise LinePolicyError("Candidate signer workflow does not match the selected line workflow.")

    snapshot = {key: selected[key] for key in ("sourceRef", "role", "packageMajor", "upstreamStage",
                                             "workflowPath", "publisherEnvironment")}
    snapshot.update(schemaVersion=1, repository=policy["repository"], channel=_version_for_line(selected, version),
                    stateAtQualification=selected["state"], authorizationRevision=policy["revision"],
                    authorizationDigest=policy_digest(policy))

    return validate_snapshot(snapshot, source_ref=source_ref, version=version, workflow_path=workflow_path)


def validate_snapshot(
    snapshot: Any,
    *,
    source_ref: str | None = None,
    version: str | None = None,
    workflow_path: str | None = None,
    repository: str | None = None,
) -> dict:
    """Validate frozen historical identity independently of current lifecycle.

    WHY: deactivation revokes future writes, not provenance of already qualified
    or published bytes. A caller seeking publication must separately validate
    protected-main current authority and its exact admitted candidate.
    """
    value = _object(snapshot, SNAPSHOT_FIELDS, "Frozen release-line snapshot")
    _schema(value["schemaVersion"], "Frozen release-line snapshot")
    _line_identity(value)
    if value["repository"] != REPOSITORY:
        raise LinePolicyError("Frozen release-line snapshot identifies a different repository.")

    _choice(value["channel"], ("stable", "prerelease"), "Frozen package channel")
    _choice(value["stateAtQualification"], ("prepared", "active"), "Frozen qualification state")
    _revision(value["authorizationRevision"])
    _digest(value["authorizationDigest"], "Frozen authorization digest")
    if value["role"] == "preview" and value["channel"] != "prerelease":
        raise LinePolicyError("A frozen preview line must have a prerelease package channel.")

    if version is not None and _version_for_line(value, version) != value["channel"]:
        raise LinePolicyError("Frozen release-line channel does not match the candidate version.")

    expected = {"sourceRef": source_ref, "workflowPath": workflow_path, "repository": repository}
    if any(item is not None and value[key] != item for key, item in expected.items()):
        raise LinePolicyError("Frozen release-line snapshot does not match the expected release identity.")

    return copy.deepcopy(value)


def validate_legacy_identity(source_ref: str, version: str, workflow_path: str | None = None) -> None:
    """Read historical main-only schemas without granting a new publication.

    Old schemas predate the line-major contract; retain those exact identities
    rather than relabeling historical releases using today's authorization.
    """
    parse_version(version)
    if source_ref != AUTHORITY_REF or workflow_path not in (None, LEGACY_WORKFLOW_PATH):
        raise LinePolicyError(
            "Historical legacy release evidence must retain its original main-only workflow identity."
        )


def _xml_property(path: Path, name: str) -> str:
    """Require a single unambiguous evaluated-independent MSBuild property."""
    if path.is_symlink() or not path.is_file():
        raise LinePolicyError(f"Branch toolchain contract must be a regular file: {path}.")

    try:
        root = ElementTree.parse(path).getroot()
    except (OSError, ElementTree.ParseError) as exception:
        raise LinePolicyError(f"Unable to parse branch toolchain contract: {path}.") from exception

    properties = list(root.iter(name))
    if len(properties) != 1 or not properties[0].text or not properties[0].text.strip():
        raise LinePolicyError(f"{path.name} must define one unambiguous {name} property.")

    parents = {child: parent for parent in root.iter() for child in parent}
    ancestor = parents.get(properties[0])
    while ancestor is not None:
        if ancestor.get("Condition"):
            raise LinePolicyError(f"{path.name} must define one unambiguous {name} property.")

        ancestor = parents.get(ancestor)

    condition = properties[0].get("Condition")
    default_ef_condition = name == "DokaEfCoreVersion" and condition == "'$(DokaEfCoreVersion)' == ''"
    if condition and not default_ef_condition:
        raise LinePolicyError(f"{path.name} must define one unambiguous {name} property.")

    if name == "TargetFramework" and list(root.iter("TargetFrameworks")):
        raise LinePolicyError(f"{path.name} must define one unambiguous {name} property.")

    return properties[0].text.strip()


def _stage(version: ReleaseVersion) -> str:
    """Classify only the reviewed upstream GA, preview and RC identities."""
    if not version.prerelease:
        return "ga"

    if version.prerelease[0] not in ("preview", "rc"):
        raise LinePolicyError("Upstream dependency version must use a reviewed preview or rc identity.")

    return version.prerelease[0]


def _ef_bounds(value: str, major: int, stage: str) -> tuple[str, str | None]:
    """Validate exact preview pins or a bounded same-major stable EF contract."""
    if not isinstance(value, str):
        raise LinePolicyError("EF Core policy must be an exact version or a bounded GA interval.")

    interval = re.fullmatch(r"\[([^,\[\]]+), *([^,\[\]]+)\)", value)
    if interval:
        if stage != "ga":
            raise LinePolicyError("Platform prerelease EF Core dependencies require an exact reviewed pin.")

        lower, upper = interval.groups()
        minimum, maximum = parse_version(lower), parse_version(upper)
        upper_boundary = (maximum.major, maximum.minor, maximum.patch) == (major + 1, 0, 0)
        if (minimum.major != major or minimum.is_prerelease or maximum.is_prerelease
                or (maximum.major != major and not upper_boundary) or compare_versions(lower, upper) >= 0):
            raise LinePolicyError("Stable EF Core bounds must remain inside the authorized package major.")

        return lower, upper

    exact = parse_version(value)
    if exact.major != major or _stage(exact) != stage:
        raise LinePolicyError("EF Core exact pin does not match the selected package major/upstream stage.")

    return value, None


def validate_toolchain(toolchain: Any, snapshot: dict, *, resolved_packages: dict[str, str] | None = None) -> dict:
    """Verify portable branch toolchain identity without consulting a checkout.

    Source-only roll-forward and prerelease selection controls are checked by
    inspect_toolchain; a portable verifier also compares the observed SDK with
    approvedDotnetSdk before accepting the surrounding evidence envelope.
    """
    frozen = validate_snapshot(snapshot)
    value = _object(toolchain, TOOLCHAIN_FIELDS, "Frozen branch toolchain")
    sdk = parse_version(value["approvedDotnetSdk"])
    major = frozen["packageMajor"]
    stage = frozen["upstreamStage"]
    if sdk.major != major or _stage(sdk) != stage or value["upstreamStage"] != stage:
        raise LinePolicyError("SDK identity must match the selected line package major/upstream stage.")

    if value["targetFramework"] != f"net{major}.0":
        raise LinePolicyError("Target framework must match the selected line package major.")

    _choice(value["testRunner"], ("VSTest", "Microsoft.Testing.Platform"), "Branch test runner")
    lower, upper = _ef_bounds(value["efCoreVersion"], major, stage)
    if resolved_packages is not None:
        if not isinstance(resolved_packages, dict):
            raise LinePolicyError("Resolved packages must map exact package names to exact versions.")

        required = {"Microsoft.EntityFrameworkCore.Design", "Microsoft.EntityFrameworkCore.Relational"}
        if not required.issubset(resolved_packages):
            raise LinePolicyError("Resolved packages must include EF Core Design and Relational identities.")

        ef_versions: set[str] = set()
        for package, version in resolved_packages.items():
            if not isinstance(package, str) or not isinstance(version, str):
                raise LinePolicyError("Resolved packages must map exact package names to exact versions.")

            if not package.startswith("Microsoft.EntityFrameworkCore"):
                continue

            parsed = parse_version(version)
            ef_versions.add(version)
            if parsed.major != major or _stage(parsed) != stage:
                raise LinePolicyError("Resolved EF Core package does not match the selected line major/upstream stage.")

            if upper is None and version != lower:
                raise LinePolicyError("Resolved EF Core package does not match its reviewed exact pin.")

            if upper is not None and (compare_versions(version, lower) < 0 or compare_versions(version, upper) >= 0):
                raise LinePolicyError("Resolved EF Core package is outside the reviewed stable interval.")

        # WHY: individually valid bounds do not make a mixed EF assembly graph
        # safe; the provider's patch contract qualifies one coherent identity.
        if len(ef_versions) != 1:
            raise LinePolicyError("Resolved EF Core packages must share one exact coherent version identity.")

    return copy.deepcopy(value)


def _validate_ga_patch_baseline(repo: Path, toolchain: dict) -> None:
    """Require the GA checkout's reviewed same-minor EF patch policy.

    This source preflight does not replace the exact inventory/discovery checks
    or the independently validated live release-evidence matrix.
    """
    baseline = _read_json(repo / SPECIFICATION_BASELINE_PATH, "GA specification patch baseline")
    if not isinstance(baseline, dict):
        raise LinePolicyError("GA specification patch baseline must be an object.")

    _schema(baseline.get("schemaVersion"), "GA specification patch baseline")
    versions = baseline.get("efCoreVersions")
    if not isinstance(versions, list) or not versions:
        raise LinePolicyError("GA specification patch baseline must declare a nonempty efCoreVersions array.")

    lower, upper = _ef_bounds(toolchain["efCoreVersion"], parse_version(toolchain["approvedDotnetSdk"]).major, "ga")
    minimum = parse_version(lower)
    admitted: set[str] = set()
    for version in versions:
        parsed = parse_version(version)
        if parsed.is_prerelease or (parsed.major, parsed.minor) != (minimum.major, minimum.minor):
            raise LinePolicyError("GA specification patch baseline must use the selected stable EF major/minor.")

        if compare_versions(version, lower) < 0 or (upper is None and version != lower):
            raise LinePolicyError("GA specification patch baseline is outside the selected EF policy.")

        if upper is not None and compare_versions(version, upper) >= 0:
            raise LinePolicyError("GA specification patch baseline is outside the selected EF policy.")

        if version in admitted:
            raise LinePolicyError("GA specification patch baseline contains duplicate EF versions.")

        admitted.add(version)

    if lower not in admitted:
        raise LinePolicyError("GA specification patch baseline must include the selected EF minimum.")


def inspect_toolchain(repo: Path, snapshot: dict, *, resolved_packages: dict[str, str] | None = None) -> dict:
    """Read branch-owned toolchain identity and require its own GA patch policy."""
    frozen = validate_snapshot(snapshot)
    global_json = _read_json(repo / "global.json", "branch SDK contract")
    if not isinstance(global_json, dict) or not isinstance(global_json.get("sdk"), dict):
        raise LinePolicyError("global.json must declare one explicit SDK contract.")

    sdk = global_json["sdk"]
    if sdk.get("rollForward") != "disable":
        raise LinePolicyError("The branch SDK contract must disable roll-forward.")

    expected_prerelease = frozen["upstreamStage"] != "ga"
    if sdk.get("allowPrerelease") is not expected_prerelease:
        raise LinePolicyError("The SDK allowPrerelease contract must match the selected upstream stage.")

    test = global_json.get("test", {})
    if not isinstance(test, dict):
        raise LinePolicyError("global.json test runner configuration must be an object.")

    toolchain = {"approvedDotnetSdk": sdk.get("version"),
                 "targetFramework": _xml_property(repo / "Directory.Build.props", "TargetFramework"),
                 "efCoreVersion": _xml_property(repo / "Directory.Packages.props", "DokaEfCoreVersion"),
                 "testRunner": test.get("runner", "VSTest"), "upstreamStage": frozen["upstreamStage"]}

    validated = validate_toolchain(toolchain, frozen, resolved_packages=resolved_packages)
    if frozen["upstreamStage"] == "ga":
        # WHY: a future GA line must bring its own reviewed specification patch
        # baseline, not inherit the current EF10 policy or an earlier RC graph.
        _validate_ga_patch_baseline(repo, validated)

    return validated
