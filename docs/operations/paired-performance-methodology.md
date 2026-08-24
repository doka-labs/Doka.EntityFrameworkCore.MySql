# Paired Performance Methodology

Status: superseded by the direct BenchmarkDotNet budget gate.

This page preserves the published documentation anchors while making the
current contract explicit. Paired reference/candidate execution, attempt
selection, dispersion confirmation, and workflow promotion no longer exist in
the repository.

## What a Paired Run Measures

There is no current paired run. Provider workloads are measured once by
BenchmarkDotNet for the selected target and profile. The performance gate uses
absolute family budgets and same-run BenchmarkDotNet controls, avoiding a
second provider checkout and a host-matching state machine.

## Registered Sensitivity

The retired design registered cross-run sensitivity and dispersion evidence.
The current design does not claim that a hosted runner can provide a stable
historical ratio. It therefore makes no paired sensitivity claim.

Changes that need a finer comparison should be investigated with a focused
BenchmarkDotNet experiment and reviewed raw data. Such an experiment does not
become automatic CI policy unless a separate, evidenced design decision adds
it.

## What the Contract Controls

The current contract controls:

- required database targets and exact server images;
- host CPU admission before measurement;
- workload identity and operation batches;
- absolute latency, allocation, and Gen2 limits per family;
- same-run BenchmarkDotNet allocation and ratio controls; and
- sustained resource and throughput budgets.

It does not control GitHub attempts, retries, PR state, baseline promotion, or
release qualification.

## Primary Sources

- [BenchmarkDotNet documentation](https://benchmarkdotnet.org/articles/overview.html)
- [BenchmarkDotNet accuracy and precision](https://benchmarkdotnet.org/articles/guides/accuracy-and-precision.html)
