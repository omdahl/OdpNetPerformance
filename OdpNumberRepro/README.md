# ODP.NET NUMBER Read Repro (Console EXE)

Minimal **.NET Framework 4.7.2 / C# 7.3** console program for vendor support.

It helps reproduce and quantify potential allocation/GC pressure when reading NUMBER columns at scale.

## Objective

This sample exists to support the following SR text:

> We are seeing high allocation/GC pressure in ODP.NET Managed Driver when reading NUMBER columns at scale (50k+ rows, numeric-heavy result sets), causing throughput impact in hot read paths.
>
> We have a tactical internal optimization with ~15% elapsed-time improvement, but it relies on private/internal driver members and is not a supportable long-term solution.
>
> Request: please provide a supported low-allocation NUMBER-read API on the provider reader (e.g., OracleDataReader), preferably:
> 1) long GetNumberBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
> 2) int GetNumberByteCount(int ordinal)
> 3) bool TryGetNumberBytes(int ordinal, Span<byte> destination, out int bytesWritten)
> Optional: TryGetInt32/TryGetInt64/TryGetDecimal/TryGetDouble no-exception fast paths.

The repro compares the current NUMBER accessor choices so Oracle can see the allocation, GC, and throughput impact of the existing API surface versus a supported low-allocation path.

This sample does not call `GetNumberBytes(...)` directly because that is the requested API, not an API that exists in the current driver. The existing accessors in the sample are the baseline used to demonstrate the problem and quantify the benefit of a supported buffer-based read path.

This follows established ADO.NET patterns such as `SqlDataReader.GetBytes`, `SqlDataReader.GetFieldValue<T>`, and `SqlDataReader.GetSqlDecimal`.

This is not a SQL tuning repro, a schema design repro, or a query plan investigation. The focus is the client-side Oracle data access behavior when reading NUMBER values.

> ⚠️ `--prepare=true` drops and recreates the target table with `PURGE`. Run only in a disposable schema / dedicated test user.

## What it does

- Automatic data setup by default (`--prepare=true`) in table `ODP_NUM_REPRO` (configurable)
- Runs benchmark scenarios on two datasets:
  - `DUP` (duplicate-heavy)
  - `HC` (high-cardinality)
- Compares accessor paths:
  - `GetDecimal`
  - `GetOracleDecimal`
  - `GetValueDecimal`
  - `GetDouble`
- Prints detailed per-run metrics and a compact summary table:
  - elapsed ms
  - CPU ms
  - AppDomain allocated bytes (if available)
  - Gen0/Gen1/Gen2 collection deltas
  - checksum (correctness guard)

## Build

### Visual Studio 2026 Developer PowerShell (Windows)

```powershell
dotnet restore .\OdpNumberRepro.csproj
dotnet build .\OdpNumberRepro.csproj -c Release
```

Output EXE:

```text
.\bin\Release\net472\OdpNumberRepro.exe
```

> If needed, change `OracleManagedDataAccessVersion` in the `.csproj` to match the exact driver version under investigation.

## SR-oriented run examples

These examples mirror the support request: read 50k+ NUMBER rows from numeric-heavy result sets, compare the current reader accessors, and observe elapsed time, CPU time, allocation, and GC pressure.

### 1) Reproduce the support case with the full matrix

```powershell
.\bin\Release\net472\OdpNumberRepro.exe \
  --connection="User Id=<user>;Password=<pwd>;Data Source=<tns>" \
  --prepare=true \
  --setup-rows=100000 \
  --matrix=true \
  --rows=50000 \
  --warmup=1 \
  --iterations=2 \
  --outdir=".\out"
```

### 2) Isolate a single accessor on the same workload

```powershell
.\bin\Release\net472\OdpNumberRepro.exe \
  --connection="User Id=<user>;Password=<pwd>;Data Source=<tns>" \
  --matrix=false \
  --dataset=DUP \
  --accessor=GetDecimal \
  --rows=50000 \
  --warmup=1 \
  --iterations=2
```

If Oracle wants the most direct support repro, start with the single-scenario example and compare `GetDecimal`, `GetOracleDecimal`, `GetValueDecimal`, and `GetDouble` on both `DUP` and `HC`.

### 3) Memory-focused comparison for the SR

For the low-allocation API request, the most important existing-reader baseline is `GetDecimal` versus `GetOracleDecimal` on the same row count and dataset:

```powershell
.\bin\Release\net472\OdpNumberRepro.exe \
  --connection="User Id=<user>;Password=<pwd>;Data Source=<tns>" \
  --prepare=false \
  --matrix=false \
  --dataset=DUP \
  --accessor=GetDecimal \
  --rows=50000 \
  --warmup=1 \
  --iterations=2

.\bin\Release\net472\OdpNumberRepro.exe \
  --connection="User Id=<user>;Password=<pwd>;Data Source=<tns>" \
  --prepare=false \
  --matrix=false \
  --dataset=DUP \
  --accessor=GetOracleDecimal \
  --rows=50000 \
  --warmup=1 \
  --iterations=2
```

Compare these summary fields first:

- `AvgAppDomainAllocatedBytes`
- `AvgGen0/1/2`
- `AvgElapsedMs`
- `AvgCpuMs`

If checksums and row counts match but both accessors still allocate heavily, the request is not to choose between `GetDecimal` and `GetOracleDecimal`. The request is for a supported buffer-based NUMBER read path that avoids per-row allocation and conversion overhead in hot read paths.

## Example conclusion table

Example output from one local full-matrix run with `--rows=50000`, `--warmup=1`, and `--iterations=2`.

`DUP` dataset:

| Accessor | Avg elapsed | Avg CPU | Avg allocated bytes | Avg Gen0/1/2 |
|---|---:|---:|---:|---:|
| `GetDecimal` | 13467.73 ms | 13007.81 ms | 1165471444 | 276.00/4.50/1.00 |
| `GetOracleDecimal` | 23721.42 ms | 22914.06 ms | 1926795960 | 457.00/3.00/1.00 |
| `GetValueDecimal` | 15722.68 ms | 15125.00 ms | 1322359956 | 313.00/4.00/1.00 |
| `GetDouble` | 12025.08 ms | 11648.44 ms | 1006802972 | 238.00/3.50/1.00 |

`HC` dataset:

| Accessor | Avg elapsed | Avg CPU | Avg allocated bytes | Avg Gen0/1/2 |
|---|---:|---:|---:|---:|
| `GetDecimal` | 13728.63 ms | 13054.69 ms | 1178318104 | 279.00/6.00/2.00 |
| `GetOracleDecimal` | 23359.26 ms | 22585.94 ms | 1940223448 | 460.00/5.00/2.00 |
| `GetValueDecimal` | 15626.32 ms | 15117.19 ms | 1335222752 | 316.00/5.00/2.00 |
| `GetDouble` | 12151.76 ms | 11640.63 ms | 1019618580 | 241.00/7.00/2.00 |

Conclusion from this run:

- `GetOracleDecimal` has the highest elapsed time, CPU time, allocated bytes, and Gen0 collections in both datasets.
- `GetDecimal` is better than `GetOracleDecimal`, but still allocates more than 1.1 GB to read 50k rows in this run.
- `GetDouble` is fastest and allocates least here, but it is not a general replacement for exact `NUMBER`/decimal semantics.
- The support request is therefore for a new supported low-allocation NUMBER read API, not just guidance to pick the least costly existing accessor.

## Command-line parameters

- `--connection=<...>` **required**
- `--table=ODP_NUM_REPRO` (default)
  - table name must match `^[A-Z][A-Z0-9_]{0,29}$`
- `--prepare=true|false` (default `true`)
- `--setup-rows=<int>` (default `500000`)
- `--matrix=true|false` (default `true`)
- `--dataset=DUP|HC` (used when matrix=false)
- `--accessor=GetDecimal|GetOracleDecimal|GetValueDecimal|GetDouble` (used when matrix=false)
- `--rows=<int>` (default `5000`)
- `--warmup=<int>` (default `1`)
- `--iterations=<int>` (default `2`)
- `--allow-fewer-rows=true|false` (default `false`)
- `--outdir=<path>` (default current directory)

## Notes

- This repro is intentionally simple and self-contained for support workflows.
- Metrics are process/app-domain level signals to highlight trend differences between accessor paths.
- For deep allocation attribution, pair this run with a memory profiler trace.
- Connection strings passed on the command line may be visible in shell history and process listings. Prefer a dedicated disposable test user.
