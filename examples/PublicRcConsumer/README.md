# Exact Public Platform RC Consumer

This source-only fixture is materialized outside the repository by:

```text
python3 -m eng.release.consumer --help
```

Use the complete [operator procedure](../../docs/operations/release-lines.md#exact-public-rc-consumer)
after an independently approved Doka RC is published. It requires that exact
Doka version, SDK RC, EF Core RC, target framework and trusted public NuGet
readback receipt/checksum. There is deliberately no concrete new RC version,
ProjectReference, local feed or SafeMigrations dependency.

The command uses the already installed SDK; it installs nothing. The generated
project uses exact `[version]` references, isolated NuGet.org restore and
repository-signature validation, checks the resolved graph and actual package
identities, builds, then executes provider model/SQL, spatial and cache
consumption assertions. It opens no database. The candidate's full live/runtime
qualification remains a separate requirement.

Open the generated `PublicRcConsumer.csproj` in Rider to inspect or reproduce
the consumer. `public-rc-consumer.json` is emitted only after all checks finish.
The optional `preview/11.x` route is currently inactive: offline contract tests
are not evidence of an actual public Doka 11 RC restore or publication.
