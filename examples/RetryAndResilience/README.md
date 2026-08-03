# Retry and Resilience

Demonstrates explicit transient-failure retry configuration with bounded retry
count and maximum delay.

```bash
dotnet run --project examples/RetryAndResilience/RetryAndResilience.csproj
```

The sample configures the policy only. Fault injection, commit-unknown
behavior, timeout handling, and retry diagnostics are covered by the provider's
unit and integration contracts rather than simulated with an unreliable live
failure in documentation code.
