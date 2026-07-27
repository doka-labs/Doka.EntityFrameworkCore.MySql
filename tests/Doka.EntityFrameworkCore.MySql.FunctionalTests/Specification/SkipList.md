# Specification Skip Contract

ADR D-021 replaces the former prose-only skip list with the machine-readable
`SpecDispositions.json` ledger and executable source annotations.

The active contract is:

- provider gaps: zero permitted;
- engine limitations: visible xUnit skips with a stable disposition ID, official vendor
  primary source, retrieval date, reproducible probe, workaround assessment, and
  re-evaluation trigger;
- framework limitations: visible xUnit skips linked to an official `dotnet/efcore` issue,
  reproduced on every supported target before provider-owned SQL generation, and carrying a
  retrieval date plus re-evaluation trigger;
- not applicable: only structurally absent upstream premises, also linked by stable ID;
- silent passes: never permitted.

The pre-D-021 investigation log is retained in `SpecTriageHistory.md` for audit history. It is
not current release evidence.
