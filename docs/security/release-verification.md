# Verifying a Release

Every published provider release is bound through three complementary
mechanisms:

1. a signed annotated Git tag identifies the source commit;
2. SLSA provenance binds release artifacts to the protected GitHub Actions
   workflow and source commit; and
3. NuGet.org repository signatures protect the packages distributed by
   NuGet.org.

These checks prove different properties. Passing one is not evidence that the
others passed.

The commands below describe the current three-package release contract,
including `Doka.Caching.MySql`. For an older release predating that package,
use the verification guide and manifest from its signed tag; do not require
new package subjects that were not part of that historical release.

## Prerequisites

- Git with SSH signature verification support;
- GitHub CLI with `gh attestation verify` support; and
- .NET SDK 10 or another supported SDK for `dotnet nuget verify`;
- `curl`; and
- `jq`.

Use the exact release tag and never substitute a branch name:

```bash
set -euo pipefail

release_tag="v<release-version>"
release_version="${release_tag#v}"
repository="doka-labs/Doka.EntityFrameworkCore.MySql"
release_root="artifacts/${release_tag}"
nuget_root="${release_root}/nuget.org"
```

## Verify the Source Tag

Clone or fetch the repository, then configure Git to use the tracked release
signer registry for this verification:

```bash
git fetch origin "refs/tags/${release_tag}:refs/tags/${release_tag}"
git -c gpg.ssh.allowedSignersFile=.github/allowed_signers \
  verify-tag "${release_tag}"

release_commit="$(git rev-list -n 1 "${release_tag}")"
test -n "${release_commit}"
```

The command must report a valid signature from a principal and key present in
`.github/allowed_signers`. A tag pointing at the expected commit but carrying
an untrusted signature is not an accepted release identity.

## Download the Immutable Release Assets

Download the exact GitHub release into an empty directory:

```bash
mkdir -p "${release_root}"
gh release download "${release_tag}" \
  --repo "${repository}" \
  --dir "${release_root}"
```

The GitHub release must include:

- three primary `.nupkg` packages: the provider, NetTopologySuite extension,
  and `Doka.Caching.MySql`;
- three matching `.snupkg` symbol packages;
- `release-provenance.intoto.jsonl`;
- the release-candidate manifest and detached checksum; and
- the published SBOM and release evidence listed by the release manifest.

Missing or duplicate identity assets are a verification failure. Do not choose
one of two conflicting files by filename or timestamp.

The package files attached to the GitHub release are the exact candidate bytes
attested before publication. They intentionally do not contain the NuGet.org
repository signature that is added to the separately distributed public copy.

## Verify SLSA Provenance

The portable bundle contains the attestations emitted by the exact
`release-candidate.yml` workflow. Verify each downloaded package against that
bundle and pin the repository, signer workflow, workflow commit, source ref,
source commit, and hosted-runner requirement:

```bash
for artifact in "${release_root}/"*.nupkg "${release_root}/"*.snupkg; do
  gh attestation verify "${artifact}" \
    --bundle "${release_root}/release-provenance.intoto.jsonl" \
    --repo "${repository}" \
    --signer-workflow "${repository}/.github/workflows/release-candidate.yml" \
    --signer-digest "${release_commit}" \
    --source-ref "refs/heads/main" \
    --source-digest "${release_commit}" \
    --deny-self-hosted-runners
done
```

`--repo` alone is intentionally insufficient: it would accept any authorized
workflow in the repository. The signer and source constraints bind the
attestation to the release workflow and the signed commit.

The command uses GitHub's trusted-root resolution. For offline verification,
obtain and protect a trusted root separately with
`gh attestation trusted-root`, then pass it through
`--custom-trusted-root`. Keeping only the bundle is not enough to establish an
offline trust root.

## Verify NuGet Repository Signatures

Discover NuGet.org's package-content endpoint from its V3 service index, then
download the three exact public package versions:

```bash
mkdir -p "${nuget_root}"

package_base="$(
  curl -fsSL https://api.nuget.org/v3/index.json |
    jq -er '
      [.resources[] | select(."@type" == "PackageBaseAddress/3.0.0") | ."@id"]
      | if length == 1 then .[0] else error("ambiguous package base") end
    '
)"

for package_id in \
  Doka.EntityFrameworkCore.MySql \
  Doka.EntityFrameworkCore.MySql.NetTopologySuite \
  Doka.Caching.MySql; do
  lower_id="$(printf '%s' "${package_id}" | tr '[:upper:]' '[:lower:]')"
  package_file="${lower_id}.${release_version}.nupkg"

  curl -fsSL \
    "${package_base%/}/${lower_id}/${release_version}/${package_file}" \
    -o "${nuget_root}/${package_file}"
done
```

Release tags use the repository's canonical lowercase NuGet-version subset, so
`release_version` is already the normalized lowercase value required by the
package-content API.

Run signature verification against those NuGet.org copies, not against the
unsigned candidate assets from GitHub:

```bash
dotnet nuget verify "${nuget_root}/"*.nupkg --all
```

The command must identify a valid NuGet repository signature for each package.
NuGet signatures use the platform or SDK certificate trust store; a missing or
untrusted signing or timestamp chain is a failure to investigate, not a reason
to disable verification.

Repository signing changes the raw package archive. The release workflow binds
the public package back to the candidate by comparing its canonical content
without the repository signature, then retains the signed public bytes and
signature verification as completion evidence.

The `.snupkg` files are covered by SLSA provenance and exact release-asset
readback. NuGet symbol-server indexing is asynchronous and is separately
verified by the publication workflow.

The standalone cache's assembly and Portable PDB are separate symbol subjects,
not part of the provider's symbol identity. A successful provider or spatial
readback does not satisfy a missing cache package, signature, or PDB.

## Interpreting the Result

A release is verified only when all of these statements hold:

- the tag signature is valid under the repository's signer registry;
- the tag resolves to the same commit enforced by every package attestation;
- every package and symbol file matches the SLSA subject digest;
- the signer workflow is the protected release-candidate workflow on `main`;
- the attestation was produced on a GitHub-hosted runner; and
- every primary package carries a valid NuGet repository signature.

Checksums detect accidental or malicious byte changes but do not identify who
authorized the bytes. Signatures and provenance provide that identity layer.

## Primary Sources

- Git, [git-verify-tag](https://git-scm.com/docs/git-verify-tag), retrieved
  2026-08-21. `git verify-tag` validates the cryptographic signature embedded
  in a tag object.
- Git, [gpg.ssh.allowedSignersFile](https://git-scm.com/docs/git-config#Documentation/git-config.txt-gpgsshallowedSignersFile),
  retrieved 2026-08-21. SSH signature trust is established through an allowed
  signers file.
- GitHub CLI, [gh attestation verify](https://cli.github.com/manual/gh_attestation_verify),
  retrieved 2026-08-21. The command documents local bundles, signer workflow,
  source identity, and hosted-runner restrictions.
- GitHub CLI, [gh attestation trusted-root](https://cli.github.com/manual/gh_attestation_trusted-root),
  retrieved 2026-08-21. Offline verification requires an independently
  supplied trusted root in addition to the bundle.
- Microsoft, [dotnet nuget verify](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-nuget-verify),
  retrieved 2026-08-21. The command verifies signed NuGet packages.
- Microsoft, [NuGet V3 service index](https://learn.microsoft.com/en-us/nuget/api/service-index),
  retrieved 2026-08-21. Package-source capabilities and endpoints are
  discovered through the service index.
- Microsoft, [NuGet package content](https://learn.microsoft.com/en-us/nuget/api/package-base-address-resource),
  retrieved 2026-08-21. Exact package bytes use the discovered
  `PackageBaseAddress/3.0.0` resource and normalized lowercase IDs and versions.
- OpenSSF Best Practices, [Silver signed-release criterion](https://www.bestpractices.dev/en/criteria/1),
  retrieved 2026-08-21.
