# Performance Budget Operations

Status: historical baseline automation is retired. The filename remains stable
for published links.

## Accept an Engine Image Update

Update the Compose image, `benchmarks/performance-contract.json`, the test
image mirror, and any workflow mirror in one reviewed change. Then run:

```bash
./eng/quality-gates.sh
DOKA_BENCHMARK_TARGET=<target> \
DOKA_BENCHMARK_PROFILE=smoke \
DOKA_BENCHMARK_PORT=0 \
./eng/benchmark.sh --up-run-down
```

The compiled repository contract rejects mismatched image pins. It has no fix
mode because image changes must stay visible in review.

## Seed an Accepted Baseline

There is no accepted historical baseline and no seeding command. The current
gate compares raw BenchmarkDotNet measurements with explicit checked-in family
budgets and same-run controls.

To change a budget:

1. Capture a fresh complete benchmark artifact for every affected target.
2. Explain whether the change is a provider regression, an intentional
   behavior change, or a corrected bound.
3. Modify `benchmarks/performance-contract.json` in the reviewed change.
4. Rerun the affected profiles.

No workflow writes or promotes the contract.

## Hosted Runner Baseline

Hosted runners do not own a baseline. The monthly `benchmark` workflow and a
manual dispatch run the current tree and upload raw artifacts. A workflow job
may be rerun after invalid infrastructure evidence, but the repository does
not select among attempts or convert a rerun into an accepted baseline.

## Budget Review Rules

- Do not raise a budget to convert invalid evidence into a pass.
- Keep workload IDs and family ownership explicit.
- Treat exit `78` as an infrastructure or evidence defect and exit `1` as a
  measured budget violation.
- Review the raw BenchmarkDotNet samples, allocation statistics, target image,
  and soak metrics with every budget change.

## Primary Sources

- [BenchmarkDotNet documentation](https://benchmarkdotnet.org/articles/overview.html)
- [GitHub Actions rerunning workflows and jobs](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/re-run-workflows-and-jobs)
