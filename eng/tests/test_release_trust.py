"""Contracts for the trust root a release tag must satisfy.

A tag is the immutable identity a published package is attributed to, and the
release path previously accepted it on the strength of being annotated and
pointing at the planned commit. That leaves the three questions these tests
cover: who signed it, whether its commit ever reached the protected branch, and
whether the branch evidence came from the branch rather than from a pull
request against it.

The local preparation command checks the branch and signer prerequisites before
hosted qualification. The write-capable job repeats the complete tag trust root
after qualification and before any publication mutation.
"""

from __future__ import annotations

import hashlib
import json
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from eng.release import trust


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
POLICY_PATH = REPOSITORY_ROOT / "eng" / "release" / "evidence-policy.json"

SSH_OUTPUT = (
    'Good "git" signature for kdominic@gmx.de with ED25519 key '
    "SHA256:Nkug92pcXECxr/ahDdfnvayqT8E4gWRYHIuUXQzstZI\n"
)
GPG_OUTPUT = (
    "gpg: Signature made Sun Aug 10 02:00:00 2026 CEST\n"
    "gpg: Good signature from \"Release Bot <release@example.test>\" [ultimate]\n"
    "Primary key fingerprint: ABCD 1234 ABCD 1234 ABCD  1234 ABCD 1234 ABCD 1234\n"
)


def git(repo: Path, *arguments: str) -> None:
    """Run one git command in a scratch repository.

    Signing is disabled explicitly: the operator's global configuration signs
    every commit, and a scratch repository has no key material, so the fixture
    would otherwise fail for a reason unrelated to what it tests.
    """
    subprocess.run(
        ("git", "-C", str(repo), "-c", "commit.gpgsign=false",
         "-c", "tag.gpgsign=false", *arguments),
        check=True,
        capture_output=True,
        text=True,
    )


class ApiVerificationTests(unittest.TestCase):
    """Prove the remote verdict is required and not merely consulted."""

    def test_a_valid_verification_is_accepted(self) -> None:
        """Accept the only verdict GitHub reports for a good signature."""
        trust.verify_api_signature({"verified": True, "reason": "valid"})

    def test_an_unverified_signature_is_rejected(self) -> None:
        """Reject a tag the remote could not verify."""
        for reason in ("unsigned", "unknown_key", "bad_email", "expired_key"):
            with self.subTest(reason=reason):
                with self.assertRaises(trust.TrustRootError):
                    trust.verify_api_signature(
                        {"verified": False, "reason": reason}
                    )

    def test_a_verified_flag_without_a_valid_reason_is_rejected(self) -> None:
        """Reject a verdict whose reason is not the accepted one.

        `verified` and `reason` are separate fields; requiring only the flag
        would accept any future reason the API introduces.
        """
        with self.assertRaises(trust.TrustRootError):
            trust.verify_api_signature({"verified": True, "reason": "unknown"})

    def test_a_missing_verification_object_is_rejected(self) -> None:
        """Reject a tag response that carries no verification at all."""
        for value in (None, [], "verified"):
            with self.subTest(value=value):
                with self.assertRaises(trust.TrustRootError):
                    trust.verify_api_signature(value)


class LocalVerificationParsingTests(unittest.TestCase):
    """Prove git's own verdict is read rather than assumed."""

    def test_the_real_repository_tag_parses(self) -> None:
        """Parse the verdict git produces for the tag this repository carries.

        Parsing a fixture would only prove the fixture matches the parser. This
        case reads whatever the installed git actually emits.
        """
        result = subprocess.run(
            ("git", "-C", str(REPOSITORY_ROOT), "verify-tag", "--raw",
             "v10.0.0-rc.7"),
            capture_output=True,
            text=True,
            check=False,
        )
        if result.returncode != 0:
            self.skipTest("The release tag is not verifiable in this checkout.")

        parsed = trust.parse_local_verification(
            f"{result.stderr}\n{result.stdout}"
        )

        self.assertTrue(parsed["fingerprint"].startswith("SHA256:"))
        self.assertTrue(parsed["principal"])

    def test_an_ssh_verdict_is_parsed(self) -> None:
        """Extract principal, key type, and fingerprint from the SSH backend."""
        parsed = trust.parse_local_verification(SSH_OUTPUT)

        self.assertEqual("kdominic@gmx.de", parsed["principal"])
        self.assertEqual("ed25519", parsed["keyType"])
        self.assertTrue(parsed["fingerprint"].startswith("SHA256:"))

    def test_an_openpgp_verdict_is_parsed(self) -> None:
        """Support the other backend git may be configured with."""
        parsed = trust.parse_local_verification(GPG_OUTPUT)

        self.assertEqual("release@example.test", parsed["principal"])
        self.assertEqual("openpgp", parsed["keyType"])
        self.assertEqual("ABCD1234ABCD1234ABCD1234ABCD1234ABCD1234",
                         parsed["fingerprint"])

    def test_anything_but_a_good_signature_is_rejected(self) -> None:
        """Reject output that does not state a good signature."""
        for output in ("", "error: no signature found",
                       'Bad "git" signature for kdominic@gmx.de'):
            with self.subTest(output=output[:24]):
                with self.assertRaises(trust.TrustRootError):
                    trust.parse_local_verification(output)


class TrustedSignerTests(unittest.TestCase):
    """Prove identity and key must match the same registered entry."""

    def setUp(self) -> None:
        """Load the shipped signer policy."""
        policy = json.loads(POLICY_PATH.read_text(encoding="utf-8"))
        self.signers = policy["trustedTagSigners"]["signers"]
        self.observed = trust.parse_local_verification(SSH_OUTPUT)

    def test_the_shipped_signer_matches(self) -> None:
        """Accept the signer this repository registers."""
        matched = trust.match_trusted_signer(self.observed, self.signers)

        self.assertIn("kdominic@gmx.de", matched["principals"])

    def test_an_unregistered_principal_is_rejected(self) -> None:
        """Reject a signature from someone the policy never registered."""
        observed = dict(self.observed, principal="stranger@example.test")

        with self.assertRaises(trust.TrustRootError):
            trust.match_trusted_signer(observed, self.signers)

    def test_a_registered_principal_with_a_foreign_key_is_rejected(self) -> None:
        """Reject a known identity presenting an unregistered key.

        Accepting the principal alone would let a compromised or replaced key
        sign releases under a name the policy trusts.
        """
        observed = dict(self.observed, fingerprint="SHA256:" + "A" * 43)

        with self.assertRaises(trust.TrustRootError):
            trust.match_trusted_signer(observed, self.signers)

    def test_a_foreign_key_type_is_rejected(self) -> None:
        """Reject a key algorithm the policy did not register."""
        observed = dict(self.observed, keyType="rsa")

        with self.assertRaises(trust.TrustRootError):
            trust.match_trusted_signer(observed, self.signers)

    def test_an_empty_signer_policy_rejects_everything(self) -> None:
        """Refuse to trust any signer when none is registered."""
        with self.assertRaises(trust.TrustRootError):
            trust.match_trusted_signer(self.observed, [])


class ReachabilityTests(unittest.TestCase):
    """Prove a tag must name a commit that reached the protected branch."""

    def setUp(self) -> None:
        """Build a scratch repository with a branch and a detached commit."""
        self._directory = tempfile.TemporaryDirectory(prefix="doka-trust-")
        self.addCleanup(self._directory.cleanup)
        self.repo = Path(self._directory.name)
        git(self.repo, "init", "--initial-branch=main", "--quiet")
        git(self.repo, "config", "user.name", "Test")
        git(self.repo, "config", "user.email", "test@example.test")
        (self.repo / "file.txt").write_text("one", encoding="utf-8")
        git(self.repo, "add", "file.txt")
        git(self.repo, "commit", "--quiet", "-m", "first")
        self.on_branch = subprocess.run(
            ("git", "-C", str(self.repo), "rev-parse", "HEAD"),
            capture_output=True, text=True, check=True,
        ).stdout.strip()

        git(self.repo, "checkout", "--quiet", "-b", "side")
        (self.repo / "file.txt").write_text("two", encoding="utf-8")
        git(self.repo, "commit", "--quiet", "-am", "side")
        self.off_branch = subprocess.run(
            ("git", "-C", str(self.repo), "rev-parse", "HEAD"),
            capture_output=True, text=True, check=True,
        ).stdout.strip()
        git(self.repo, "checkout", "--quiet", "main")

    def test_a_commit_on_the_branch_is_accepted(self) -> None:
        """Accept a tag whose commit is reachable from the protected branch."""
        trust.verify_commit_is_on_protected_branch(
            self.repo, self.on_branch, "refs/heads/main"
        )

    def test_a_commit_outside_the_branch_is_rejected(self) -> None:
        """Reject a tag created on a commit the branch never carried.

        Without this check a tag could name reviewed-looking work that never
        passed through branch protection.
        """
        with self.assertRaises(trust.TrustRootError):
            trust.verify_commit_is_on_protected_branch(
                self.repo, self.off_branch, "refs/heads/main"
            )


COMMIT = "a" * 40


def qualification(**overrides: object) -> dict[str, object]:
    """Return a complete, accepted qualification receipt."""
    receipt = {
        "name": "repository-qualification",
        "id": 5001,
        "checkSuiteId": 7001,
        "conclusion": "success",
        "workflowPath": ".github/workflows/ci.yml",
        "workflowRunId": 9001,
        "runAttempt": 1,
        "event": "push",
        "headBranch": "main",
        "commit": COMMIT,
        "workflowConclusion": "success",
    }
    receipt.update(overrides)

    return receipt


def qualification_manifest(
    receipt: dict[str, object],
    *,
    repository: str = "doka-labs/Doka.EntityFrameworkCore.MySql",
    commit: str = COMMIT,
    tree_id: str = "b" * 40,
    release_tag: str = "v10.0.0-rc.1",
) -> tuple[dict[str, object], dict[str, object]]:
    """Return a complete manifest whose protected entry binds ``receipt``."""
    policy = json.loads(POLICY_PATH.read_text(encoding="utf-8"))
    digest = trust.release_qualification.policy_digest(policy)
    entries: list[dict[str, object]] = []
    for index, gate in enumerate(policy["gates"], start=1):
        entry: dict[str, object] = {"gate": gate["id"], "kind": gate["kind"]}
        for field in gate["boundIdentities"]:
            values: dict[str, object] = {
                "commit": commit,
                "treeId": tree_id,
                "workflowPath": gate["producerWorkflow"],
                "workflowRunId": (
                    receipt["workflowRunId"]
                    if gate["kind"] == "protected-check"
                    else 1000 + index
                ),
                "runAttempt": (
                    receipt["runAttempt"]
                    if gate["kind"] == "protected-check"
                    else 1
                ),
                "event": receipt.get("event", "push"),
                "conclusion": receipt.get("conclusion", "success"),
                "apiResourceId": receipt.get("id", 5001),
                "responseDigest": hashlib.sha256(
                    json.dumps(
                        receipt,
                        sort_keys=True,
                        separators=(",", ":"),
                    ).encode("utf-8")
                ).hexdigest(),
                "sourceHash": "c" * 64,
                "dependencySnapshotDigest": "d" * 64,
                "artifactId": 2000 + index,
                "artifactDigest": "e" * 64,
            }
            entry[field] = values[field]
        entries.append(entry)

    return (
        {
            "schemaVersion": 2,
            "kind": "release-qualification-manifest",
            "policyVersion": policy["policyVersion"],
            "policyDigest": digest,
            "selectionRuleVersion": policy["selectionRule"]["version"],
            "repository": repository,
            "commit": commit,
            "treeId": tree_id,
            "expectedReleaseTag": release_tag,
            "releaseVersion": release_tag.removeprefix("v"),
            "assemblingRunAttempt": 1,
            "requiredProtectedChecks": policy["requiredProtectedChecks"],
            "gates": entries,
        },
        policy,
    )


class BranchEvidenceOriginTests(unittest.TestCase):
    """Prove branch evidence must come from the branch.

    The event alone decides nothing: a workflow dispatched on main also reports
    a branch, and a rerun of a different workflow can carry the same check
    name. Every identity the receipt claims is therefore bound.
    """

    def check(self, **overrides: object) -> None:
        """Run the origin check against the shipped bindings."""
        trust.verify_branch_evidence_origin(
            qualification(**overrides),
            commit=COMMIT,
            expected_branch="main",
            expected_workflow=".github/workflows/ci.yml",
        )

    def test_a_push_receipt_is_accepted(self) -> None:
        """Accept qualification produced by a push on the protected branch."""
        self.check()

    def test_a_pull_request_receipt_is_rejected(self) -> None:
        """Reject qualification that only ever ran against the branch."""
        for event in ("pull_request", "pull_request_target", "workflow_dispatch"):
            with self.subTest(event=event):
                with self.assertRaises(trust.TrustRootError):
                    self.check(event=event)

    def test_a_receipt_from_another_branch_is_rejected(self) -> None:
        """Reject a push on a branch whose protection produced no evidence."""
        with self.assertRaises(trust.TrustRootError):
            self.check(headBranch="release/staging")

    def test_a_receipt_for_another_commit_is_rejected(self) -> None:
        """Reject evidence that describes a different commit."""
        with self.assertRaises(trust.TrustRootError):
            self.check(commit="b" * 40)

    def test_a_receipt_from_another_workflow_is_rejected(self) -> None:
        """Reject a same-named check produced by a workflow file we do not trust."""
        with self.assertRaises(trust.TrustRootError):
            self.check(workflowPath=".github/workflows/anything-else.yml")

    def test_an_unsuccessful_receipt_is_rejected(self) -> None:
        """Reject a check that concluded as anything but success."""
        for conclusion in ("failure", "cancelled", "skipped", "neutral", None):
            with self.subTest(conclusion=conclusion):
                with self.assertRaises(trust.TrustRootError):
                    self.check(conclusion=conclusion)

    def test_a_receipt_without_a_run_identity_is_rejected(self) -> None:
        """Reject a receipt whose run identity could not be resolved."""
        for field in ("workflowRunId", "runAttempt"):
            with self.subTest(field=field):
                with self.assertRaises(trust.TrustRootError):
                    self.check(**{field: None})


class QualificationResolutionTests(unittest.TestCase):
    """Prove the receipt is resolved from the workflow run, not guessed.

    The previous shape derived the event from whether the check suite carried
    a branch name, which reports `push` for every workflow that runs on a
    branch, including a manual dispatch. The workflow run is the only resource
    that states the triggering event.
    """

    CHECK_RUNS = {
        "check_runs": [
            {
                "name": "repository-qualification",
                "id": 5001,
                "conclusion": "success",
                "check_suite": {"id": 7001},
            },
            {"name": "something-else", "id": 5002, "check_suite": {"id": 7001}},
        ]
    }

    WORKFLOW_RUNS = {
        "workflow_runs": [
            {
                "id": 9001,
                "run_attempt": 2,
                "event": "push",
                "head_branch": "main",
                "head_sha": COMMIT,
                "path": ".github/workflows/ci.yml",
                "conclusion": "success",
            }
        ]
    }

    def test_the_named_check_run_is_selected(self) -> None:
        """Pick the check run by name rather than by position."""
        selected = trust.select_check_run(
            self.CHECK_RUNS, "repository-qualification", COMMIT
        )

        self.assertEqual(5001, selected["id"])

    def test_an_absent_check_run_is_rejected(self) -> None:
        """Reject a commit that never produced the required check."""
        with self.assertRaises(trust.TrustRootError):
            trust.select_check_run({"check_runs": []}, "repository-qualification", COMMIT)

    def test_the_same_check_in_two_suites_is_ambiguous(self) -> None:
        """Refuse to let API ordering decide which evidence counts."""
        payload = {
            "check_runs": [
                {"name": "repository-qualification", "id": 1, "check_suite": {"id": 1}},
                {"name": "repository-qualification", "id": 2, "check_suite": {"id": 2}},
            ]
        }

        with self.assertRaises(trust.TrustRootError):
            trust.select_check_run(payload, "repository-qualification", COMMIT)

    def test_a_suite_without_a_workflow_run_is_rejected(self) -> None:
        """Reject a check suite no workflow run can be resolved for."""
        with self.assertRaises(trust.TrustRootError):
            trust.select_workflow_run(
                {"workflow_runs": []}, check_suite_id=7001, commit=COMMIT
            )

    def test_the_receipt_carries_every_bound_identity(self) -> None:
        """Prove the resolved receipt states what the origin check needs."""
        receipt = trust.qualification_receipt(
            trust.select_check_run(
                self.CHECK_RUNS, "repository-qualification", COMMIT
            ),
            trust.select_workflow_run(
                self.WORKFLOW_RUNS, check_suite_id=7001, commit=COMMIT
            ),
        )

        self.assertEqual(".github/workflows/ci.yml", receipt["workflowPath"])
        self.assertEqual(9001, receipt["workflowRunId"])
        self.assertEqual(2, receipt["runAttempt"])
        self.assertEqual("push", receipt["event"])
        self.assertEqual("main", receipt["headBranch"])
        self.assertEqual(COMMIT, receipt["commit"])

    def test_an_incomplete_workflow_run_is_rejected(self) -> None:
        """Reject a workflow run that cannot answer what the receipt claims."""
        for field in ("id", "run_attempt", "event", "head_branch", "head_sha",
                      "path", "conclusion"):
            with self.subTest(field=field):
                run = dict(self.WORKFLOW_RUNS["workflow_runs"][0])
                run.pop(field)
                with self.assertRaises(trust.TrustRootError):
                    trust.qualification_receipt({"name": "x"}, run)

    def test_a_dispatch_on_main_is_not_branch_evidence(self) -> None:
        """Close the case the previous derivation silently accepted.

        A manual dispatch on main carries a branch name, which the old code
        read as proof of a push. The workflow run states the event, and this is
        the case that proves the difference is now decided correctly.
        """
        run = dict(self.WORKFLOW_RUNS["workflow_runs"][0])
        run["event"] = "workflow_dispatch"
        receipt = trust.qualification_receipt(
            trust.select_check_run(
                self.CHECK_RUNS, "repository-qualification", COMMIT
            ),
            run,
        )

        with self.assertRaises(trust.TrustRootError):
            trust.verify_branch_evidence_origin(
                receipt,
                commit=COMMIT,
                expected_branch="main",
                expected_workflow=".github/workflows/ci.yml",
            )


class FrozenQualificationTests(unittest.TestCase):
    """Prove publication revalidates the check attempt selected by the candidate."""

    def setUp(self) -> None:
        """Build one exact protected-check selection and its complete manifest."""
        self.repository = "doka-labs/Doka.EntityFrameworkCore.MySql"
        self.tree = "b" * 40
        self.tag = "v10.0.0-rc.1"
        self.receipt = qualification()
        self.manifest, self.policy = qualification_manifest(
            self.receipt,
            repository=self.repository,
            tree_id=self.tree,
            release_tag=self.tag,
        )

    def verify(self, receipt: dict[str, object] | None = None) -> None:
        """Verify one receipt against the frozen selection."""
        trust.verify_frozen_qualification_receipt(
            receipt or self.receipt,
            manifest=self.manifest,
            policy=self.policy,
            repository=self.repository,
            commit=COMMIT,
            tree_id=self.tree,
            expected_release_tag=self.tag,
        )

    def test_the_frozen_receipt_is_accepted(self) -> None:
        """Accept the exact check, run, attempt, origin, and response digest."""
        self.verify()

    def test_a_different_attempt_or_response_is_rejected(self) -> None:
        """Prevent a later rerun from replacing evidence after assembly."""
        for field, value in (("runAttempt", 2), ("workflowRunId", 9002)):
            with self.subTest(field=field):
                changed = dict(self.receipt)
                changed[field] = value
                with self.assertRaisesRegex(
                    trust.TrustRootError,
                    "frozen manifest",
                ):
                    self.verify(changed)

        changed = dict(self.receipt)
        changed["workflowConclusion"] = "failure"
        with self.assertRaisesRegex(trust.TrustRootError, "responseDigest"):
            self.verify(changed)

    def test_a_manifest_from_another_policy_is_rejected(self) -> None:
        """Keep the frozen gate selection under the policy that assembled it."""
        self.manifest["policyDigest"] = "0" * 64

        with self.assertRaisesRegex(trust.TrustRootError, "different evidence policy"):
            self.verify()

    def test_the_exact_attempt_endpoint_is_used(self) -> None:
        """Read an earlier rerun attempt without consulting current API ordering."""
        check_run = {
            "name": "repository-qualification",
            "id": 5001,
            "conclusion": "success",
            "check_suite": {"id": 7001},
            "details_url": (
                f"https://github.com/{self.repository}/actions/runs/9001/job/5001"
            ),
        }
        workflow_run = {
            "id": 9001,
            "run_attempt": 1,
            "event": "push",
            "head_branch": "main",
            "head_sha": COMMIT,
            "path": ".github/workflows/ci.yml",
            "conclusion": "success",
            "check_suite_id": 7001,
        }
        requested: list[str] = []

        def api(path: str) -> dict[str, object]:
            requested.append(path)
            return check_run if "/check-runs/" in path else workflow_run

        with mock.patch.object(trust, "_gh_json", side_effect=api):
            observed = trust.fetch_frozen_qualification_receipt(
                self.repository,
                manifest=self.manifest,
                policy=self.policy,
                commit=COMMIT,
                tree_id=self.tree,
                expected_release_tag=self.tag,
            )

        self.assertEqual(self.receipt, observed)
        self.assertEqual(
            [
                f"/repos/{self.repository}/check-runs/5001",
                f"/repos/{self.repository}/actions/runs/9001/attempts/1",
            ],
            requested,
        )

    def test_an_unrelated_workflow_attempt_is_rejected(self) -> None:
        """Reject IDs that exist independently but do not share a check suite."""
        payloads = [
            {
                "name": "repository-qualification",
                "id": 5001,
                "conclusion": "success",
                "check_suite": {"id": 7001},
                "details_url": (
                    f"https://github.com/{self.repository}/actions/runs/9001/job/5001"
                ),
            },
            {
                "id": 9001,
                "run_attempt": 1,
                "event": "push",
                "head_branch": "main",
                "head_sha": COMMIT,
                "path": ".github/workflows/ci.yml",
                "conclusion": "success",
                "check_suite_id": 7002,
            },
        ]
        with (
            mock.patch.object(trust, "_gh_json", side_effect=payloads),
            self.assertRaisesRegex(trust.TrustRootError, "same check suite"),
        ):
            trust.fetch_frozen_qualification_receipt(
                self.repository,
                manifest=self.manifest,
                policy=self.policy,
                commit=COMMIT,
                tree_id=self.tree,
                expected_release_tag=self.tag,
            )


class LocalSigningKeyTests(unittest.TestCase):
    """Prove the pre-tag check binds the key, not merely its presence.

    Reporting that some signing key is configured answered a weaker question
    than the one that decides the tag: whether the key that would sign is one
    the policy trusts. A wrong key produced a tag the trust root rejects after
    the tag exists, which is when it cannot be taken back.
    """

    def setUp(self) -> None:
        """Build a repository configured to sign with a generated key."""
        self.policy = json.loads(POLICY_PATH.read_text(encoding="utf-8"))
        self.directory = tempfile.TemporaryDirectory()
        self.repo = Path(self.directory.name) / "repository"
        self.repo.mkdir()
        self.key = Path(self.directory.name) / "signer"
        subprocess.run(
            ["ssh-keygen", "-t", "ed25519", "-N", "", "-C", "signer@example.test",
             "-f", str(self.key)],
            check=True,
            capture_output=True,
        )
        subprocess.run(
            ["git", "-C", str(self.repo), "init", "--initial-branch=main"],
            check=True, capture_output=True,
        )
        subprocess.run(
            ["git", "-C", str(self.repo), "config", "user.signingkey",
             str(self.key.with_suffix(".pub"))],
            check=True, capture_output=True,
        )
        self.fingerprint = subprocess.run(
            ["ssh-keygen", "-lf", str(self.key.with_suffix(".pub"))],
            check=True, capture_output=True, text=True,
        ).stdout.split()[1]

    def tearDown(self) -> None:
        """Release the fixture."""
        self.directory.cleanup()

    def test_the_configured_key_resolves_to_its_fingerprint(self) -> None:
        """Read the key that would sign rather than the fact that one is set."""
        observed = trust.local_signing_fingerprint(self.repo)

        self.assertEqual(self.fingerprint, observed["fingerprint"])
        self.assertEqual("ssh-ed25519", observed["keyType"])

    def test_a_registered_key_matches(self) -> None:
        """Accept a key the policy registers."""
        signers = [
            {
                "principals": ["signer@example.test"],
                "keyType": "ssh-ed25519",
                "fingerprint": self.fingerprint,
            }
        ]

        self.assertEqual(
            signers[0],
            trust.match_registered_key(
                trust.local_signing_fingerprint(self.repo), signers
            ),
        )

    def test_an_unregistered_key_is_refused(self) -> None:
        """Refuse a key that is configured but not trusted.

        This is the case the previous check accepted: the repository had a
        signing key, so it reported success, and the tag it produced was
        rejected by the trust root.
        """
        with self.assertRaises(trust.TrustRootError):
            trust.match_registered_key(
                trust.local_signing_fingerprint(self.repo),
                self.policy["trustedTagSigners"]["signers"],
            )

    def test_a_missing_key_is_refused(self) -> None:
        """Refuse a repository that could not sign at all."""
        subprocess.run(
            ["git", "-C", str(self.repo), "config", "--unset", "user.signingkey"],
            check=True, capture_output=True,
        )

        with self.assertRaises(trust.TrustRootError):
            trust.local_signing_fingerprint(self.repo)

    def test_an_unreadable_key_is_refused(self) -> None:
        """Refuse a configured path that carries no key."""
        subprocess.run(
            ["git", "-C", str(self.repo), "config", "user.signingkey",
             str(Path(self.directory.name) / "absent.pub")],
            check=True, capture_output=True,
        )

        with self.assertRaises(trust.TrustRootError):
            trust.local_signing_fingerprint(self.repo)


class TrustRootCompletionTests(unittest.TestCase):
    """Prove the orchestration reaches and enforces the last check.

    Every other case in this module proves an early failure. Without one case
    that runs the whole sequence, the bindings the last check applies would be
    unexercised and could be wrong in either direction.
    """

    def setUp(self) -> None:
        """Build a repository with a signed tag on a protected branch."""
        self.policy = json.loads(POLICY_PATH.read_text(encoding="utf-8"))
        self.directory = tempfile.TemporaryDirectory()
        self.repo = Path(self.directory.name) / "repository"
        self.repo.mkdir()
        self.key = Path(self.directory.name) / "signer"
        subprocess.run(
            ["ssh-keygen", "-t", "ed25519", "-N", "", "-C", "signer@example.test",
             "-f", str(self.key)],
            check=True,
            capture_output=True,
        )
        self.allowed = Path(self.directory.name) / "allowed_signers"
        self.allowed.write_text(
            f"signer@example.test {self.key.with_suffix('.pub').read_text().strip()}\n",
            encoding="utf-8",
        )
        fingerprint = subprocess.run(
            ["ssh-keygen", "-lf", str(self.key.with_suffix(".pub"))],
            check=True,
            capture_output=True,
            text=True,
        ).stdout.split()[1]
        self.policy["trustedTagSigners"]["signers"] = [
            {
                "principals": ["signer@example.test"],
                "keyType": "ssh-ed25519",
                "fingerprint": fingerprint,
            }
        ]

        def git(*arguments: str) -> None:
            subprocess.run(
                ["git", "-C", str(self.repo), *arguments], check=True, capture_output=True
            )

        git("init", "--initial-branch=main")
        git("config", "user.email", "signer@example.test")
        git("config", "user.name", "Signer")
        git("config", "gpg.format", "ssh")
        git("config", "user.signingkey", str(self.key.with_suffix(".pub")))
        git("config", "gpg.ssh.allowedSignersFile", str(self.allowed))
        (self.repo / "README.md").write_text("probe\n", encoding="utf-8")
        git("add", "README.md")
        # Signing commits is the operator's global default here; it is disabled
        # so the fixture proves the tag signature rather than a commit one.
        git("-c", "commit.gpgsign=false", "commit", "-m", "probe")
        git("tag", "-s", "-m", "probe", "v9.9.9")
        git("update-ref", "refs/remotes/origin/main", "HEAD")
        self.commit = subprocess.run(
            ["git", "-C", str(self.repo), "rev-parse", "HEAD"],
            check=True,
            capture_output=True,
            text=True,
        ).stdout.strip()

    def tearDown(self) -> None:
        """Release the fixture repository."""
        self.directory.cleanup()

    def establish(self, **overrides: object) -> dict[str, object]:
        """Run the whole trust root against the fixture."""
        return trust.verify_trust_root(
            repo=self.repo,
            tag="v9.9.9",
            commit=self.commit,
            api_verification={"verified": True, "reason": "valid"},
            policy=self.policy,
            qualification_receipt=qualification(commit=self.commit, **overrides),
            allowed_signers=self.allowed,
        )

    def test_a_complete_trust_root_is_established(self) -> None:
        """Reach the last check and record what it verified."""
        evidence = self.establish()

        self.assertEqual("v9.9.9", evidence["tag"])
        self.assertEqual(self.commit, evidence["commit"])
        self.assertEqual(".github/workflows/ci.yml",
                         evidence["qualification"]["workflowPath"])

    def test_the_last_check_still_rejects_a_dispatch(self) -> None:
        """Prove reaching the last check does not mean passing it."""
        with self.assertRaises(trust.TrustRootError):
            self.establish(event="workflow_dispatch")

    def test_the_workflow_binding_comes_from_the_shipped_policy(self) -> None:
        """Bind the accepted workflow to the policy, not to a literal here."""
        gate = [
            entry
            for entry in self.policy["gates"]
            if entry["id"] in self.policy["requiredProtectedChecks"]
        ][0]

        with self.assertRaises(trust.TrustRootError):
            self.establish(workflowPath=gate["producerWorkflow"] + ".other")


class TrustRootOrderTests(unittest.TestCase):
    """Prove the cheapest check decides first."""

    def setUp(self) -> None:
        """Load the shipped policy for the orchestration cases."""
        self.policy = json.loads(POLICY_PATH.read_text(encoding="utf-8"))

    def test_an_unverified_signature_fails_before_local_work(self) -> None:
        """Reject on the remote verdict without touching the repository.

        The repository path here does not exist. If the orchestration reached
        local verification or reachability, it would fail differently.
        """
        with self.assertRaises(trust.TrustRootError) as captured:
            trust.verify_trust_root(
                repo=Path("/nonexistent/repository"),
                tag="v0.0.0",
                commit="0" * 40,
                api_verification={"verified": False, "reason": "unsigned"},
                policy=self.policy,
                qualification_receipt={"event": "push"},
            )

        self.assertIn("unverified", str(captured.exception))

    def test_a_policy_without_signers_is_rejected(self) -> None:
        """Refuse to establish a trust root the policy never described."""
        policy = json.loads(json.dumps(self.policy))
        del policy["trustedTagSigners"]

        with self.assertRaises(trust.TrustRootError):
            trust.verify_trust_root(
                repo=REPOSITORY_ROOT,
                tag="v10.0.0-rc.7",
                commit="0" * 40,
                api_verification={"verified": True, "reason": "valid"},
                policy=policy,
                qualification_receipt={"event": "push"},
            )

    def test_a_missing_allowed_signers_file_is_rejected(self) -> None:
        """Reject local verification that cannot run for lack of key material.

        Silently skipping local verification would reduce the trust root to
        whatever the remote reports.
        """
        policy = json.loads(json.dumps(self.policy))
        policy["trustedTagSigners"]["allowedSignersFile"] = ".github/absent"

        with self.assertRaises(trust.TrustRootError) as captured:
            trust.verify_trust_root(
                repo=REPOSITORY_ROOT,
                tag="v10.0.0-rc.7",
                commit="0" * 40,
                api_verification={"verified": True, "reason": "valid"},
                policy=policy,
                qualification_receipt={"event": "push"},
            )

        self.assertIn("Allowed-signers", str(captured.exception))


if __name__ == "__main__":
    unittest.main()
