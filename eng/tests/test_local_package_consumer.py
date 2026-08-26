"""Behavioral contracts for the isolated local-package consumer gate."""

from __future__ import annotations

import json
import os
import stat
import subprocess
import tempfile
import textwrap
import unittest
from pathlib import Path


class LocalPackageConsumerTests(unittest.TestCase):
    """Prove package-only restore identity without contacting NuGet.org."""

    _VERSION = "10.0.0-test.1"
    _PROVIDER_ID = "Doka.EntityFrameworkCore.MySql"
    _SPATIAL_ID = "Doka.EntityFrameworkCore.MySql.NetTopologySuite"
    _CACHE_ID = "Doka.Caching.MySql"

    def setUp(self) -> None:
        """Create candidate packages and a deterministic dotnet test double."""
        self.repository_root = Path(__file__).resolve().parents[2]
        self.temporary_directory = tempfile.TemporaryDirectory(
            prefix="doka-local-package-consumer-"
        )
        self.root = Path(self.temporary_directory.name)
        self.packages = self.root / "candidate-packages"
        self.evidence = self.root / "evidence"
        self.bin_directory = self.root / "bin"
        self.packages.mkdir()
        self.bin_directory.mkdir()

        (self.packages / f"{self._PROVIDER_ID}.{self._VERSION}.nupkg").write_bytes(
            b"provider-candidate-bytes\n"
        )
        (self.packages / f"{self._SPATIAL_ID}.{self._VERSION}.nupkg").write_bytes(
            b"spatial-candidate-bytes\n"
        )
        (self.packages / f"{self._CACHE_ID}.{self._VERSION}.nupkg").write_bytes(b"cache-candidate-bytes\n")
        self._write_fake_dotnet()

    def tearDown(self) -> None:
        """Dispose the isolated fixture."""
        self.temporary_directory.cleanup()

    def test_accepts_exact_candidate_package_bytes(self) -> None:
        """Accept a package-only graph restored from the candidate files."""
        result = self._run_gate(tamper=False)

        self.assertEqual(0, result.returncode, result.stderr)
        evidence = json.loads(
            (self.evidence / "local-package-consumer.json").read_text(
                encoding="ascii"
            )
        )
        self.assertEqual(3, evidence["schemaVersion"])
        self.assertEqual("pass", evidence["qualification"])
        self.assertEqual(
            "provider-migration-operation-conformance",
            evidence["qualificationSurface"],
        )
        self.assertEqual(
            {
                "baselineRendering": "pass",
                "commandBoundaries": "pass",
                "contextLifetime": "pass",
                "duplicateHandlerIdFailure": "pass",
                "duplicateOperationOwnershipFailure": "pass",
                "exactTypeDispatch": "pass",
                "registrationOrderIndependence": "pass",
                "unknownOperationFailure": "pass",
            },
            evidence["migrationOperationHandlerConformance"],
        )
        self.assertEqual(0, evidence["projectReferences"])
        self.assertEqual(self._VERSION, evidence["releaseVersion"])
        self.assertEqual(3, len(evidence["packages"]))
        self.assertEqual("pass", evidence["cacheRegistration"])
        self.assertEqual(0, evidence["cacheEfCoreDependencies"])

    def test_rejects_a_cache_consumer_with_an_ef_core_dependency(self) -> None:
        """The standalone package must not be validated inside an EF graph."""
        result = self._run_gate(tamper=False, inject_ef=True)
        self.assertNotEqual(0, result.returncode)
        self.assertFalse((self.evidence / "local-package-consumer.json").exists())

    def test_rejects_changed_cache_package_bytes(self) -> None:
        """A successful provider restore cannot hide stale cache bytes."""
        result = self._run_gate(tamper=False, tamper_cache=True)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("did not restore the exact candidate package bytes", result.stderr)
        self.assertFalse((self.evidence / "local-package-consumer.json").exists())

    def test_rejects_restored_package_bytes_that_do_not_match_candidate(self) -> None:
        """Reject a remote or stale package masquerading as the candidate version."""
        result = self._run_gate(tamper=True)

        self.assertNotEqual(0, result.returncode)
        self.assertIn(
            "did not restore the exact candidate package bytes",
            result.stderr,
        )
        self.assertFalse((self.evidence / "local-package-consumer.json").exists())

    def _run_gate(
        self, *, tamper: bool, inject_ef: bool = False, tamper_cache: bool = False
    ) -> subprocess.CompletedProcess[str]:
        environment = os.environ.copy()
        environment.update(
            {
                "DOKA_TEST_PACKAGES_DIR": str(self.packages),
                "DOKA_TEST_TAMPER": "1" if tamper else "0",
                "DOKA_TEST_INJECT_EF": "1" if inject_ef else "0",
                "DOKA_TEST_TAMPER_CACHE": "1" if tamper_cache else "0",
                "DOKA_TEST_VERSION": self._VERSION,
                "PATH": f"{self.bin_directory}{os.pathsep}{environment['PATH']}",
            }
        )

        return subprocess.run(
            [
                "bash",
                str(
                    self.repository_root
                    / "eng"
                    / "testing"
                    / "test-local-package-consumer.sh"
                ),
                self._VERSION,
                str(self.packages),
                str(self.evidence),
            ],
            cwd=self.repository_root,
            env=environment,
            check=False,
            capture_output=True,
            text=True,
        )

    def _write_fake_dotnet(self) -> None:
        sdk_version = json.loads(
            (self.repository_root / "global.json").read_text(encoding="ascii")
        )["sdk"]["version"]
        fake_dotnet = self.bin_directory / "dotnet"
        fake_dotnet.write_text(
            textwrap.dedent(
                f"""\
                #!/usr/bin/env bash
                set -euo pipefail

                if [[ "${{1:-}}" == "--version" ]]; then
                    printf '%s\\n' '{sdk_version}'
                    exit 0
                fi

                if [[ "${{1:-}}" == "build" ]]; then
                    exit 0
                fi

                if [[ "${{1:-}}" == "run" ]]; then
                    if [[ " $* " != *" --migration-handler-only "* && " $* " != *" --registration-only "* ]]; then
                        printf 'The package consumer did not request the handler dispatch.\n' >&2
                        exit 65
                    fi

                    exit 0
                fi

                if [[ "${{1:-}}" != "restore" ]]; then
                    printf 'Unsupported fake dotnet command: %s\\n' "${{1:-}}" >&2
                    exit 64
                fi

                consumer_root="$(dirname "$2")"
                if [[ "$2" == *"CacheConsumer.csproj" ]]; then
                    cache_id='doka.caching.mysql'
                    cache_source="${{DOKA_TEST_PACKAGES_DIR}}/Doka.Caching.MySql.${{DOKA_TEST_VERSION}}.nupkg"
                    cache_target="${{NUGET_PACKAGES}}/${{cache_id}}/${{DOKA_TEST_VERSION}}/${{cache_id}}.${{DOKA_TEST_VERSION}}.nupkg"
                    mkdir -p "${{consumer_root}}/obj" "$(dirname "${{cache_target}}")"
                    cp "${{cache_source}}" "${{cache_target}}"
                    if [[ "${{DOKA_TEST_TAMPER_CACHE}}" == "1" ]]; then
                        printf 'different-cache-source\\n' >> "${{cache_target}}"
                    fi
                    extra_library=""
                    if [[ "${{DOKA_TEST_INJECT_EF}}" == "1" ]]; then
                        extra_library=',"Microsoft.EntityFrameworkCore/10.0.11":{{"type":"package"}}'
                    fi
                    printf '{{"libraries":{{"Doka.Caching.MySql/%s":{{"type":"package"}}%s}}}}\\n' \\
                        "${{DOKA_TEST_VERSION}}" "${{extra_library}}" > "${{consumer_root}}/obj/project.assets.json"
                    exit 0
                fi
                provider_id='doka.entityframeworkcore.mysql'
                spatial_id='doka.entityframeworkcore.mysql.nettopologysuite'
                provider_source="${{DOKA_TEST_PACKAGES_DIR}}/Doka.EntityFrameworkCore.MySql.${{DOKA_TEST_VERSION}}.nupkg"
                spatial_source="${{DOKA_TEST_PACKAGES_DIR}}/Doka.EntityFrameworkCore.MySql.NetTopologySuite.${{DOKA_TEST_VERSION}}.nupkg"
                provider_target="${{NUGET_PACKAGES}}/${{provider_id}}/${{DOKA_TEST_VERSION}}/${{provider_id}}.${{DOKA_TEST_VERSION}}.nupkg"
                spatial_target="${{NUGET_PACKAGES}}/${{spatial_id}}/${{DOKA_TEST_VERSION}}/${{spatial_id}}.${{DOKA_TEST_VERSION}}.nupkg"

                mkdir -p "${{consumer_root}}/obj" "$(dirname "${{provider_target}}")" "$(dirname "${{spatial_target}}")"
                cp "${{provider_source}}" "${{provider_target}}"
                cp "${{spatial_source}}" "${{spatial_target}}"

                if [[ "${{DOKA_TEST_TAMPER}}" == "1" ]]; then
                    printf 'different-source\\n' >> "${{provider_target}}"
                fi

                printf '{{"libraries":{{"Doka.EntityFrameworkCore.MySql/%s":{{"type":"package"}},"Doka.EntityFrameworkCore.MySql.NetTopologySuite/%s":{{"type":"package"}}}}}}\\n' \\
                    "${{DOKA_TEST_VERSION}}" \\
                    "${{DOKA_TEST_VERSION}}" \\
                    > "${{consumer_root}}/obj/project.assets.json"
                """
            ),
            encoding="ascii",
        )
        fake_dotnet.chmod(fake_dotnet.stat().st_mode | stat.S_IXUSR)


if __name__ == "__main__":
    unittest.main()
