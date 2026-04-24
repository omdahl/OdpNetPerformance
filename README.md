# OdpNetPerformance Minimal Repro

This folder contains a reduced repro for the ODP.NET 23.26 fetch-size performance issue observed during upgrade validation from ODP.NET 19.x.

## Scope

- .NET Framework 4.7.2 console application
- Oracle.ManagedDataAccess package reference controlled by the project file
- Reduced benchmark suite containing only the `FetchSize` scenario
- Automatic schema creation and benchmark data seeding

## What It Runs

The repro initializes a benchmark table and then executes the `FetchSize` benchmark with these settings from `App.config`:

- Row counts: `2500` and `25000`
- Fetch sizes: `256 KB`, `1 MB`, `10 MB`
- Read mode: `FullRow`
- Connection mode: `Warm`
- Measured iterations: `4`
- Timing samples: `2`

## Files Included

- `OdpNetPerformance.csproj`: SDK-style .NET Framework project
- `App.config`: Oracle connection string and benchmark settings
- `Program.cs`: entrypoint, startup calibration, benchmark execution
- `Benchmarking/`: shared timing, reporting, and benchmark result types
- `Configuration/AppSettings.cs`: config loader
- `Oracle/SchemaInitializer.cs`: recreates and seeds the benchmark table
- `Oracle/OracleBenchmarkBase.cs`: shared Oracle benchmark helpers
- `Oracle/FetchSizePerformanceTest.cs`: fetch-size repro scenario
- `Oracle/BenchmarkSuite.cs`: reduced suite with only the fetch-size benchmark enabled

## Prerequisites

- Windows
- .NET SDK capable of building `net472`
- Network access to the target Oracle database
- Valid Oracle credentials in `App.config`

## Configuration

Update the `OracleDb` connection string in `App.config` before running.

The benchmark table name is controlled by the `TestTableName` app setting. The program drops and recreates that table during initialization, so use a dedicated schema or a safe test table name.

## Build

From this folder:

```powershell
dotnet build /p:Configuration=Release /p:OracleManagedDataAccessVersion=19.25.0
# compare these versions
dotnet build /p:Configuration=Release /p:OracleManagedDataAccessVersion=23.26.0
```

## Run

After a successful build:

```powershell
.\bin\Release\net472\OdpNetPerformance.exe
```

## Expected Console Shape

The output includes:

- ODP.NET version
- startup fetch-size calibration results
- `Running=FetchSize`
- benchmark result lines such as `Test=FetchSize.2500Rows.1MB`
- final status line

## Notes

- This reduced repro intentionally excludes the broader benchmark scenarios from the main repository.
- The current goal is to reproduce the fetch-size-related read performance difference between ODP.NET 19.25 and ODP.NET 23.26 with the smallest practical source set.