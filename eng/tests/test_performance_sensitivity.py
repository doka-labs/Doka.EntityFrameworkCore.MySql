#!/usr/bin/env python3
"""Verify the pre-registered power claim with the production decision path."""

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from eng.performance.contract import InvalidEvidenceError, sha256
from eng.performance.sensitivity import (
    evaluate_registered_sensitivity,
    validate_registered_characterization,
)


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
CONTRACT_PATH = REPOSITORY_ROOT / "benchmarks" / "performance-contract.json"


class PairedSensitivityTests(unittest.TestCase):
    """Keep the fixed population tied to a reproducible detection guarantee."""

    @classmethod
    def setUpClass(cls) -> None:
        """Evaluate the expensive deterministic assurance once for this class."""
        cls.contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        cls.assurance = evaluate_registered_sensitivity(cls.contract)

    def test_registered_population_meets_the_minimum_power(self) -> None:
        """Require the confidence-bounded power, not its point estimate."""
        self.assertGreaterEqual(
            self.assurance["powerLowerBound"],
            self.assurance["minimumPower"],
        )
        self.assertEqual(
            self.contract["pairedPolicy"]["blocks"]["completeBlocks"],
            self.assurance["blocks"],
        )
        self.assertEqual(180, self.assurance["detections"])
        self.assertAlmostEqual(
            0.8595933737545114,
            self.assurance["powerLowerBound"],
        )

    def test_detectable_ratio_follows_the_required_primary_budget(self) -> None:
        """Express the registered required effect as a reviewable ratio."""
        sensitivity = self.contract["pairedPolicy"]["sensitivity"]
        primary_metric = self.contract["pairedPolicy"]["primaryFamily"]["metric"]
        expected = {
            primary_metric: self.contract["pairedPolicy"]["practicalBudgets"][
                primary_metric
            ]
            * sensitivity["minimumDetectableBudgetMultiple"]
        }

        self.assertEqual(expected, self.assurance["minimumDetectableRatios"])
        self.assertEqual(
            len(self.contract["requiredTargets"]), self.assurance["familySize"]
        )

    def test_characterization_recomputes_the_registered_upper_bound(self) -> None:
        """Bind the planning dispersion to immutable hosted measurements."""
        characterization = validate_registered_characterization(
            self.contract, REPOSITORY_ROOT
        )

        self.assertEqual(
            self.contract["pairedPolicy"]["sensitivity"][
                "maximumLogRatioStandardDeviation"
            ],
            characterization["confidenceBound"][
                "upperLogRatioStandardDeviation"
            ],
        )

    def test_characterization_digest_is_load_bearing(self) -> None:
        """Reject a planning data set whose reviewed bytes changed."""
        broken = json.loads(json.dumps(self.contract))
        broken["pairedPolicy"]["sensitivity"]["characterization"]["sha256"] = (
            "0" * 64
        )

        with self.assertRaises(InvalidEvidenceError):
            validate_registered_characterization(broken, REPOSITORY_ROOT)

    def test_characterization_source_identity_is_load_bearing(self) -> None:
        """Refuse a planning file that cannot name its hosted workflow run."""
        characterization = validate_registered_characterization(
            self.contract, REPOSITORY_ROOT
        )
        broken = json.loads(json.dumps(characterization))
        broken["sources"][0]["runId"] = "github-unknown"

        with self.assertRaisesRegex(InvalidEvidenceError, "runId"):
            # Exercise the identity validation without weakening the canonical
            # digest check: the temporary file is deliberately rebound in a
            # private contract copy, exactly as a reviewed replacement would be.
            with tempfile.NamedTemporaryFile(
                dir=REPOSITORY_ROOT / "benchmarks" / "characterization",
                suffix=".json",
            ) as temporary:
                path = Path(temporary.name)
                path.write_text(json.dumps(broken), encoding="utf-8")
                rebound = json.loads(json.dumps(self.contract))
                rebound["pairedPolicy"]["sensitivity"]["characterization"] = {
                    "path": path.relative_to(REPOSITORY_ROOT).as_posix(),
                    "sha256": sha256(path),
                }
                validate_registered_characterization(rebound, REPOSITORY_ROOT)

    def test_characterization_bound_contract_is_load_bearing(self) -> None:
        """Reject another method or confidence level under canonical data."""
        characterization = validate_registered_characterization(
            self.contract, REPOSITORY_ROOT
        )
        for field, value in (
            ("method", "unregistered-bound"),
            ("confidenceLevel", 0.95),
        ):
            with self.subTest(field=field):
                broken = json.loads(json.dumps(characterization))
                broken["confidenceBound"][field] = value
                with tempfile.NamedTemporaryFile(
                    dir=REPOSITORY_ROOT / "benchmarks" / "characterization",
                    suffix=".json",
                ) as temporary:
                    path = Path(temporary.name)
                    path.write_text(json.dumps(broken), encoding="utf-8")
                    rebound = json.loads(json.dumps(self.contract))
                    rebound["pairedPolicy"]["sensitivity"][
                        "characterization"
                    ] = {
                        "path": path.relative_to(REPOSITORY_ROOT).as_posix(),
                        "sha256": sha256(path),
                    }
                    with self.assertRaisesRegex(
                        InvalidEvidenceError,
                        "NIST 99 percent",
                    ):
                        validate_registered_characterization(
                            rebound, REPOSITORY_ROOT
                        )


if __name__ == "__main__":
    unittest.main()
