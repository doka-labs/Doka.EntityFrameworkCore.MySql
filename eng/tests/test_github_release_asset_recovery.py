"""Recover documented empty upload residue without replacing published content."""

from __future__ import annotations

import argparse
import copy
import os
import unittest
from pathlib import Path
from unittest import mock

from eng.release import github as release
from eng.release import lines
from eng.tests import test_github_release as fixtures


class GitHubReleaseAssetRecoveryTests(unittest.TestCase):
    """Exercise real reconciliation using current-schema on-disk candidate evidence."""

    def setUp(self) -> None:
        """Reuse the full candidate fixture and substitute only external transport."""
        self.fixture = fixtures.GitHubReleaseTests()
        # WHY: Manual fixture composition must also run its registered cleanups;
        # tearDown alone leaves the fixture's isolated process environment active.
        self.addCleanup(self.fixture.doCleanups)
        self.fixture.setUp()
        self.addCleanup(self.fixture.tearDown)
        self.plan = self.fixture._enable_release_line_receipts()
        patcher = mock.patch.dict(os.environ, {
            "DOKA_PUBLICATION_LOCK_GROUP": "doka-publication", "DOKA_PUBLICATION_LOCK_HELD": "1",
        })

        patcher.start()
        self.addCleanup(patcher.stop)

    def _draft(self) -> fixtures.FakeReleaseClient:
        """Seed one exact planned empty starter in an otherwise matching draft."""
        client = fixtures.FakeReleaseClient()
        client.create_draft(self.plan)
        client.add_asset(self.plan["assets"][0], b"")
        client.release["assets"][0]["state"] = "starter"

        return client

    def test_empty_starter_recovers_during_staging_and_finalization(self) -> None:
        """Both write paths replace only documented residue and retain matching uploads."""
        for operation in ("stage", "finalize"):
            with self.subTest(operation=operation):
                # Arrange
                client = self._draft()
                starter_id = client.release["assets"][0]["id"]
                matching = self.plan["assets"][1]
                payload = Path(matching["path"]).read_bytes()
                client.add_asset(matching, payload)
                matching_before = copy.deepcopy(client.release["assets"][1])
                plan_before = copy.deepcopy(self.plan)

                # Act
                if operation == "stage":
                    receipt = release.stage_release(self.plan, client)
                else:
                    receipt = release.finalize_release(
                        self.plan, client, authority=self.plan["publicationAuthority"]
                    )

                # Assert
                self.assertEqual([(self.plan["repository"], starter_id)], client.delete_calls)
                self.assertNotIn(matching["name"], client.upload_calls[0])
                self.assertIn(matching_before, client.release["assets"])
                self.assertEqual(payload, client.payloads[matching_before["id"]])
                self.assertEqual(plan_before, self.plan)
                self.assertEqual(operation == "finalize", bool(client.publish_calls))
                self.assertEqual(
                    "draft-staged-and-verified" if operation == "stage" else "published-and-verified",
                    receipt["status"],
                )

    def test_invalid_starter_record_never_mutates_the_draft(self) -> None:
        """Only a planned empty starter with a positive integer ID is recoverable."""
        variants = (
            {"name": "unplanned.bin"}, {"state": "unknown"},
            {"size": 1}, {"size": -1}, {"size": False}, {"size": 0.0}, {"size": "0"},
            {"id": 0}, {"id": -1}, {"id": True}, {"id": "100"}, {"id": None},
        )

        for changed in variants:
            with self.subTest(changed=changed):
                # Arrange
                client = self._draft()
                client.release["assets"][0].update(changed)
                before = copy.deepcopy(client.release)

                # Act
                error = fixtures._record_exception(lambda: release.stage_release(self.plan, client))

                # Assert
                self.assertIsInstance(error, release.GitHubReleaseError)
                self.assertEqual(before, client.release)
                self.assertEqual([], client.delete_calls)
                self.assertEqual([], client.upload_calls)
                self.assertEqual(0, client.publish_calls)

    def test_complete_inventory_is_validated_before_any_starter_is_deleted(self) -> None:
        """A later duplicate, malformed or unplanned asset blocks every cleanup."""
        for variant in ("duplicate-name", "duplicate-id", "malformed", "unplanned"):
            with self.subTest(variant=variant):
                # Arrange
                client = self._draft()
                second = copy.deepcopy(client.release["assets"][0])
                if variant == "duplicate-name":
                    second["id"] += 1
                elif variant == "duplicate-id":
                    second["name"] = self.plan["assets"][1]["name"]
                elif variant == "malformed":
                    second = None
                else:
                    second["id"] += 1
                    second["name"] = "unplanned.bin"

                client.release["assets"].append(second)
                before = copy.deepcopy(client.release)

                # Act
                error = fixtures._record_exception(lambda: release.stage_release(self.plan, client))

                # Assert
                self.assertIsInstance(error, release.GitHubReleaseError)
                self.assertEqual(before, client.release)
                self.assertEqual([], client.delete_calls)
                self.assertEqual([], client.upload_calls)

    def test_all_uploaded_payloads_are_verified_before_cleanup(self) -> None:
        """A starter appearing before conflicting uploaded bytes must not be removed."""
        for conflict in ("size", "digest"):
            with self.subTest(conflict=conflict):
                # Arrange
                client = self._draft()
                asset = self.plan["assets"][1]
                original = Path(asset["path"]).read_bytes()
                conflicting = b"" if conflict == "size" else bytes([original[0] ^ 1]) + original[1:]
                client.add_asset(asset, conflicting)
                before = copy.deepcopy(client.release)

                # Act
                error = fixtures._record_exception(lambda: release.stage_release(self.plan, client))

                # Assert
                self.assertIsInstance(error, release.GitHubReleaseError)
                self.assertIn("conflict", str(error))
                self.assertEqual(before, client.release)
                self.assertEqual([], client.delete_calls)
                self.assertEqual([], client.upload_calls)

    def test_starter_cleanup_requires_an_explicitly_mutable_draft(self) -> None:
        """Published, immutable and indeterminate releases cannot authorize deletion."""
        variants = (
            {"draft": False, "immutable": True}, {"draft": False, "immutable": False},
            {"draft": True, "immutable": True}, {"draft": True, "immutable": None},
        )

        for changed in variants:
            with self.subTest(changed=changed):
                # Arrange
                client = self._draft()
                client.release.update(changed)
                before = copy.deepcopy(client.release)

                # Act
                error = fixtures._record_exception(lambda: release.stage_release(self.plan, client))

                # Assert
                self.assertIsInstance(error, release.GitHubReleaseError)
                self.assertEqual(before, client.release)
                self.assertEqual([], client.delete_calls)
                self.assertEqual([], client.upload_calls)

    def test_contradictory_immutable_draft_cannot_upload_even_without_a_starter(self) -> None:
        """The draft flag alone never authorizes writes to an immutable release."""
        # Arrange
        client = fixtures.FakeReleaseClient()
        client.create_draft(self.plan)
        client.release["immutable"] = True

        # Act
        error = fixtures._record_exception(lambda: release.stage_release(self.plan, client))

        # Assert
        self.assertIsInstance(error, release.GitHubReleaseError)
        self.assertEqual([], client.upload_calls)
        self.assertEqual([], client.delete_calls)

    def test_conflicting_release_identity_prevents_cleanup(self) -> None:
        """Matching filenames cannot excuse a different tag, title, notes or repository."""
        for field in ("tag_name", "name", "body", "repository"):
            with self.subTest(field=field):
                # Arrange
                client = self._draft()
                plan = copy.deepcopy(self.plan)
                if field == "repository":
                    plan[field] = "foreign/repository"
                else:
                    client.release[field] = "conflicting identity"

                before = copy.deepcopy(client.release)

                # Act
                error = fixtures._record_exception(lambda: release.stage_release(plan, client))

                # Assert
                self.assertIsInstance(error, release.GitHubReleaseError)
                self.assertEqual(before, client.release)
                self.assertEqual([], client.delete_calls)
                self.assertEqual([], client.upload_calls)

    def test_tag_movement_before_cleanup_prevents_deletion(self) -> None:
        """Uploaded readback must not leave cleanup trusting an earlier tag observation."""
        # Arrange
        client = self._draft()
        client.tag_error = release.GitHubReleaseError("tag moved before cleanup")
        client.tag_error_on_verification = 2

        # Act
        error = fixtures._record_exception(lambda: release.stage_release(self.plan, client))

        # Assert
        self.assertIsInstance(error, release.GitHubReleaseError)
        self.assertIn("tag moved before cleanup", str(error))
        self.assertEqual([], client.delete_calls)
        self.assertEqual([], client.upload_calls)

    def test_missing_publication_lock_prevents_cleanup(self) -> None:
        """Cleanup remains subject to the existing shared publication exclusion."""
        # Arrange
        client = self._draft()

        # Act
        with mock.patch.dict(os.environ, {}, clear=True):
            error = fixtures._record_exception(lambda: release.stage_release(self.plan, client))

        # Assert
        self.assertIsInstance(error, release.GitHubReleaseError)
        self.assertIn("publisher lock", str(error))
        self.assertEqual([], client.delete_calls)

    def test_missing_or_revoked_current_authority_prevents_finalization_cleanup(self) -> None:
        """An old admitted plan never grants cleanup against today's missing or inactive authority."""
        for revoked in (False, True):
            with self.subTest(revoked=revoked):
                # Arrange
                client = self._draft()
                authority = None
                if revoked:
                    authority = copy.deepcopy(self.plan["publicationAuthority"])
                    authority["policy"]["lines"][0]["state"] = "inactive"
                    authority["authorizationDigest"] = lines.policy_digest(authority["policy"])

                # Act
                error = fixtures._record_exception(
                    lambda: release.finalize_release(self.plan, client, authority=authority)
                )

                # Assert
                self.assertIsInstance(error, release.GitHubReleaseError)
                self.assertEqual([], client.delete_calls)
                self.assertEqual([], client.upload_calls)

    def test_stage_command_requires_current_authority_before_cleanup(self) -> None:
        """The CLI staging boundary rejects missing current proof before constructing a write client."""
        # Arrange
        plan_path = self.fixture.publication / "staged-plan.json"
        release.write_json(plan_path, self.plan)
        args = argparse.Namespace(
            plan=plan_path, repo=self.fixture.root, admission=None, authority=None,
            output=self.fixture.publication / "stage-result.json",
        )

        # Act
        with mock.patch.object(release, "GitHubCliClient") as client:
            error = fixtures._record_exception(lambda: release.stage_command(args))

        # Assert
        self.assertIsInstance(error, release.GitHubReleaseError)
        self.assertIn("Current publication authority", str(error))
        client.assert_not_called()
        self.assertFalse(args.output.exists())

    def test_ordinary_retry_recovers_delete_failure_before_or_after_remote_commit(self) -> None:
        """An ambiguous response neither replaces identity nor requires manual asset operations."""
        for committed in (False, True):
            with self.subTest(committed=committed):
                # Arrange
                client = self._draft()
                starter_id = client.release["assets"][0]["id"]
                delete = client.delete_asset
                plan_before = copy.deepcopy(self.plan)

                def interrupt(repository: str, asset_id: int) -> None:
                    """Model transport failure on either side of the remote commit."""
                    if committed:
                        delete(repository, asset_id)

                    raise release.GitHubReleaseError("delete response unavailable")

                # Act
                with mock.patch.object(client, "delete_asset", side_effect=interrupt):
                    error = fixtures._record_exception(lambda: release.stage_release(self.plan, client))

                first_ids = {asset["id"] for asset in client.release["assets"]}
                receipt = release.stage_release(self.plan, client)

                # Assert
                self.assertIsInstance(error, release.GitHubReleaseError)
                self.assertIn("delete response unavailable", str(error))
                self.assertEqual(committed, starter_id not in first_ids)
                self.assertEqual([(self.plan["repository"], starter_id)], client.delete_calls)
                self.assertEqual(1, len(client.upload_calls))
                self.assertEqual("draft-staged-and-verified", receipt["status"])
                self.assertEqual(plan_before, self.plan)
                self.assertEqual(0, client.publish_calls)

    def test_strict_readback_still_rejects_a_starter(self) -> None:
        """Only draft reconciliation may consume residue; completed readback stays strict."""
        # Arrange
        client = self._draft()

        # Act
        error = fixtures._record_exception(lambda: release.verify_release_assets(client.release, self.plan, client))

        # Assert
        self.assertIsInstance(error, release.GitHubReleaseError)
        self.assertEqual([], client.delete_calls)

    def test_delete_client_uses_only_the_exact_asset_endpoint(self) -> None:
        """Use targeted REST deletion rather than broad overwrite or release deletion."""
        # Arrange
        client = release.GitHubCliClient()

        # Act
        with mock.patch.object(client, "_run_text", return_value="") as transport:
            result = client.delete_asset(self.plan["repository"], 100)

        # Assert
        self.assertIsNone(result)
        transport.assert_called_once_with(
            "api", "--method", "DELETE", "-H", "Accept: application/vnd.github+json",
            "-H", f"X-GitHub-Api-Version: {release.GITHUB_API_VERSION}",
            f"/repos/{self.plan['repository']}/releases/assets/100",
        )

    def test_delete_client_rejects_invalid_ids_before_transport(self) -> None:
        """Never interpolate an invalid deletion identity into an authenticated request."""
        for asset_id in (True, 0, -1, "100", None):
            with self.subTest(asset_id=asset_id):
                # Arrange
                client = release.GitHubCliClient()

                # Act
                with mock.patch.object(client, "_run_text") as transport:
                    error = fixtures._record_exception(lambda: client.delete_asset(self.plan["repository"], asset_id))

                # Assert
                self.assertIsInstance(error, release.GitHubReleaseError)
                transport.assert_not_called()

    def test_delete_client_propagates_transport_failure(self) -> None:
        """A missing delete response cannot be silently reported as successful cleanup."""
        # Arrange
        client = release.GitHubCliClient()
        failure = release.GitHubReleaseError("delete transport failed")

        # Act
        with mock.patch.object(client, "_run_text", side_effect=failure):
            error = fixtures._record_exception(lambda: client.delete_asset(self.plan["repository"], 100))

        # Assert
        self.assertIs(failure, error)


class FixtureIsolationTests(unittest.TestCase):
    """Keep composed release fixtures from changing later tests' process environment."""

    def test_nested_release_fixture_restores_the_complete_ambient_environment(self) -> None:
        """A complete test lifecycle must restore PATH and every other original variable."""
        # Arrange
        before = dict(os.environ)
        case = GitHubReleaseAssetRecoveryTests("test_strict_readback_still_rejects_a_starter")
        result = unittest.TestResult()

        # Act
        case.run(result)
        after = dict(os.environ)

        # Assert
        self.assertTrue(result.wasSuccessful(), result.errors + result.failures)
        self.assertEqual(before, after)


if __name__ == "__main__":
    unittest.main()
