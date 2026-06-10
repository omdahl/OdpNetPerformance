# OdpNet Repro Suite

This folder contains three ODP.NET repro projects in a single solution:

- `OdpNetPerformance`: the reduced fetch-size performance benchmark
- `OracleBugRepro`: the Oracle `GetChars` repro that uses the shared `OracleDb` connection string
- `OdpNumberRepro`: the Oracle `NUMBER` read allocation/GC repro for support

## Scope

- .NET Framework 4.7.2 console applications
- Oracle.ManagedDataAccess package reference controlled by each project file
- `OdpNetPerformance` keeps the reduced benchmark suite containing only the `FetchSize` scenario
- `OracleBugRepro` exercises `OracleDataReader.GetChars`
- `OdpNumberRepro` compares current `NUMBER` reader accessor paths and reports allocation/GC pressure
- All repro projects use an `OracleDb` connection string from their `App.config`

## What It Runs

`OdpNetPerformance` initializes a benchmark table and then executes the `FetchSize` benchmark with these settings from `App.config`:

- Row counts: `2500` and `25000`
- Fetch sizes: `256 KB`, `1 MB`, `10 MB`
- Read mode: `FullRow`
- Connection mode: `Warm`
- Measured iterations: `4`
- Timing samples: `2`

`OracleBugRepro` creates a throwaway test table, inserts a repeating text pattern, and then verifies `GetChars` against `GetString` using the same `OracleDb` connection string.

`OdpNumberRepro` prepares numeric-heavy test data and compares `GetDecimal`, `GetOracleDecimal`, `GetValueDecimal`, and `GetDouble` when reading Oracle `NUMBER` values at scale.

## Files Included

- `OdpNet.sln`: solution containing all repro projects
- `OdpNetPerformance.csproj`: SDK-style .NET Framework benchmark project
- `OracleBugRepro/OracleBugRepro.csproj`: SDK-style .NET Framework repro project
- `OdpNumberRepro/OdpNumberRepro.csproj`: SDK-style .NET Framework `NUMBER` read repro project
- `App.config`: Oracle connection string and benchmark settings for `OdpNetPerformance`
- `Benchmarking/`: shared timing, reporting, and benchmark result types
- `Configuration/AppSettings.cs`: config loader
- `Oracle/SchemaInitializer.cs`: recreates and seeds the benchmark table
- `Oracle/OracleBenchmarkBase.cs`: shared Oracle benchmark helpers
- `Oracle/FetchSizePerformanceTest.cs`: fetch-size repro scenario
- `Oracle/BenchmarkSuite.cs`: reduced suite with only the fetch-size benchmark enabled
- `OracleBugRepro/App.config`: Oracle connection string used by the `GetChars` repro
- `OracleBugRepro/Program.cs`: standalone `GetChars` repro entry point
- `OdpNumberRepro/App.config`: Oracle connection string used by the `NUMBER` read repro
- `OdpNumberRepro/Program.cs`: standalone `NUMBER` read repro entry point
- `OdpNumberRepro/README.md`: support-oriented `NUMBER` read repro instructions

## Prerequisites

- Windows
- Visual Studio 2026 Developer PowerShell, or equivalent build environment
- .NET SDK capable of building `net472`
- Network access to the target Oracle database
- Valid Oracle credentials in `App.config`

## Configuration

Update the `OracleDb` connection string in `App.config` before running.

The benchmark table name is controlled by the `TestTableName` app setting. The program drops and recreates that table during initialization, so use a dedicated schema or a safe test table name.

## Build

From this folder:

```powershell
dotnet build .\OdpNet.sln -c Release
dotnet build .\OdpNet.sln -c Release /p:OracleManagedDataAccessVersion=19.25.0
# compare against the newer version
dotnet build .\OdpNet.sln -c Release /p:OracleManagedDataAccessVersion=23.26.0
```

## Run

After a successful build:

```powershell
.\bin\Release\net472\OdpNetPerformance.exe
.\OracleBugRepro\bin\Release\net472\OracleBugRepro.exe
.\OdpNumberRepro\bin\Release\net472\OdpNumberRepro.exe
```

## Expected Console Shape

The output includes:

- ODP.NET version
- startup fetch-size calibration results
- `Running=FetchSize`
- benchmark result lines such as `Test=FetchSize.2500Rows.1MB`
- final status line
- `OracleBugRepro` reports the `GetChars` mismatch or a clean completion message
- `OdpNumberRepro` reports per-run `NUMBER` read measurements and a compact accessor summary table

## Notes

- This reduced repro intentionally excludes the broader benchmark scenarios from the main repository.
- The current goal is to keep the benchmark repro, the `GetChars` repro, and the `NUMBER` read repro side by side so all Oracle behaviors can be tested from the same solution.