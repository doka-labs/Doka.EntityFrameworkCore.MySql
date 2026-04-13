# Getting Started

Minimal setup for `Doka.EntityFrameworkCore.MySql`.

## Prerequisites

- .NET 10 SDK
- MySQL 8.4 (use `docker compose -f ../../docker/compose.yml up -d mysql84`)

## Run

```bash
dotnet run --project examples/GettingStarted/GettingStarted.csproj
```

## What it demonstrates

1. `UseMySql()` with connection string and `MySqlServerVersion`
2. `EnsureCreated()` to create the database schema
3. Insert and query a simple entity
