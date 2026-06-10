using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;

namespace OdpNumberRepro
{
    internal static class Program
    {
        private const string DefaultTableName = "ODP_NUM_REPRO";
        private const ConsoleColor HighlightColor = ConsoleColor.Cyan;
        private static readonly Regex TableNamePattern = new Regex("^[A-Z][A-Z0-9_]{0,29}$", RegexOptions.Compiled);

        private static int Main(string[] args)
        {
            var options = Options.Parse(args);
            var connectionString = GetRequiredConnectionString();

            TryEnableAppDomainMonitoring();

            PrintHeader(options);

            try
            {
                using (var connection = new OracleConnection(connectionString))
                {
                    connection.Open();

                    if (options.Prepare)
                    {
                        Console.Write("Preparing dataset in table '");
                        Console.Write(options.TableName);
                        Console.Write("' with ");
                        WriteHighlightedValue(options.SetupRows);
                        Console.WriteLine(" rows per dataset...");
                        PrepareDataset(connection, options);
                        Console.WriteLine("Dataset preparation done.");
                    }

                    if (options.Matrix)
                    {
                        var all = RunMatrix(connection, options);
                        PrintSummaryTable(all);
                        var csvPath = WriteScenarioCsv(all, options.OutputDirectory);
                        Console.WriteLine();
                        Console.WriteLine("Matrix summary CSV: {0}", csvPath);
                        return 0;
                    }

                    var single = RunScenario(connection, options, options.Dataset, options.Accessor);
                    PrintSummaryTable(new List<ScenarioSummary> { single });
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("ERROR");
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static List<ScenarioSummary> RunMatrix(OracleConnection connection, Options options)
        {
            var datasets = new[] { "DUP", "HC" };
            var accessors = new[]
            {
                AccessorKind.GetDecimal,
                AccessorKind.GetOracleDecimal,
                AccessorKind.GetValueDecimal,
                AccessorKind.GetDouble
            };

            var all = new List<ScenarioSummary>();

            foreach (var dataset in datasets)
            {
                foreach (var accessor in accessors)
                {
                    Console.WriteLine();
                    Console.WriteLine("=== Scenario: Dataset={0}, Accessor={1} ===", dataset, accessor);
                    var summary = RunScenario(connection, options, dataset, accessor);
                    all.Add(summary);
                }
            }

            return all;
        }

        private static ScenarioSummary RunScenario(OracleConnection connection, Options options, string dataset, AccessorKind accessor)
        {
            var measuredRuns = new List<RunResult>();
            var totalRuns = options.Warmup + options.Iterations;

            for (var i = 1; i <= totalRuns; i++)
            {
                var isWarmup = i <= options.Warmup;

                if (!isWarmup)
                {
                    ForceFullGc();
                }

                var result = ExecuteSingleRun(connection, options, dataset, accessor, i, isWarmup);

                if (result.RowsRead != options.Rows && !(options.AllowFewerRows && result.RowsRead < options.Rows))
                {
                    throw new InvalidOperationException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Expected to read {0} rows but read {1} rows for dataset '{2}' and accessor '{3}'.",
                            options.Rows,
                            result.RowsRead,
                            dataset,
                            accessor));
                }

                WriteRunLine(
                    isWarmup ? "WARMUP" : "RUN   ",
                    i,
                    result.RowsRead,
                    result.ElapsedMs,
                    result.CpuMs,
                    result.AppDomainAllocatedBytes,
                    result.HeapDeltaBytes,
                    result.WorkingSetDeltaBytes,
                    result.PrivateMemoryDeltaBytes,
                    result.Gen0Collections,
                    result.Gen1Collections,
                    result.Gen2Collections,
                    result.Checksum);

                if (!isWarmup)
                {
                    measuredRuns.Add(result);
                }
            }

            return BuildSummary(options, dataset, accessor, measuredRuns);
        }

        private static RunResult ExecuteSingleRun(
            OracleConnection connection,
            Options options,
            string dataset,
            AccessorKind accessor,
            int runNumber,
            bool isWarmup)
        {
            var before = MetricsSnapshot.Capture();
            var sw = Stopwatch.StartNew();

            var rows = 0;
            var decimalChecksum = 0m;
            var doubleChecksum = 0d;

            using (var cmd = connection.CreateCommand())
            {
                cmd.BindByName = true;
                cmd.CommandText = string.Format(
                    CultureInfo.InvariantCulture,
                    "select VAL from (select VAL from {0} where DATASET = :p_dataset order by ID) where rownum <= :p_rows",
                    options.TableName);

                cmd.Parameters.Add("p_dataset", OracleDbType.Varchar2, dataset, System.Data.ParameterDirection.Input);
                cmd.Parameters.Add("p_rows", OracleDbType.Int32, options.Rows, System.Data.ParameterDirection.Input);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        rows++;
                        ConsumeValue(reader, accessor, ref decimalChecksum, ref doubleChecksum);
                    }
                }
            }

            sw.Stop();
            var after = MetricsSnapshot.Capture();

            return new RunResult
            {
                RunNumber = runNumber,
                IsWarmup = isWarmup,
                Dataset = dataset,
                Accessor = accessor,
                RowsRead = rows,
                ElapsedMs = sw.Elapsed.TotalMilliseconds,
                CpuMs = (after.ProcessCpu - before.ProcessCpu).TotalMilliseconds,
                AppDomainAllocatedBytes = SubtractNullable(after.AppDomainTotalAllocatedBytes, before.AppDomainTotalAllocatedBytes),
                HeapDeltaBytes = after.ManagedHeapBytes - before.ManagedHeapBytes,
                WorkingSetDeltaBytes = after.WorkingSetBytes - before.WorkingSetBytes,
                PrivateMemoryDeltaBytes = after.PrivateMemoryBytes - before.PrivateMemoryBytes,
                Gen0Collections = after.Gen0Collections - before.Gen0Collections,
                Gen1Collections = after.Gen1Collections - before.Gen1Collections,
                Gen2Collections = after.Gen2Collections - before.Gen2Collections,
                Checksum = accessor == AccessorKind.GetDouble
                    ? doubleChecksum.ToString("R", CultureInfo.InvariantCulture)
                    : decimalChecksum.ToString(CultureInfo.InvariantCulture)
            };
        }

        private static void ConsumeValue(OracleDataReader reader, AccessorKind accessor, ref decimal decimalChecksum, ref double doubleChecksum)
        {
            switch (accessor)
            {
                case AccessorKind.GetDecimal:
                    decimalChecksum += reader.GetDecimal(0);
                    break;

                case AccessorKind.GetDouble:
                    doubleChecksum += reader.GetDouble(0);
                    break;

                case AccessorKind.GetOracleDecimal:
                    var oracleDecimal = reader.GetOracleDecimal(0);
                    if (!oracleDecimal.IsNull)
                    {
                        decimalChecksum += oracleDecimal.Value;
                    }

                    break;

                case AccessorKind.GetValueDecimal:
                    var value = reader.GetValue(0);
                    if (value != DBNull.Value)
                    {
                        decimalChecksum += Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    }

                    break;

                default:
                    throw new ArgumentOutOfRangeException("accessor", accessor, "Unknown accessor");
            }
        }

        private static ScenarioSummary BuildSummary(Options options, string dataset, AccessorKind accessor, List<RunResult> measuredRuns)
        {
            if (measuredRuns.Count == 0)
            {
                throw new InvalidOperationException("No measured runs available.");
            }

            return new ScenarioSummary
            {
                Dataset = dataset,
                Accessor = accessor,
                Rows = options.Rows,
                WarmupRuns = options.Warmup,
                MeasuredRuns = measuredRuns.Count,
                AvgElapsedMs = measuredRuns.Average(x => x.ElapsedMs),
                StdElapsedMs = StdDev(measuredRuns.Select(x => x.ElapsedMs).ToList()),
                AvgCpuMs = measuredRuns.Average(x => x.CpuMs),
                AvgAppDomainAllocatedBytes = AverageNullableLong(measuredRuns.Select(x => x.AppDomainAllocatedBytes).ToList()),
                AvgHeapDeltaBytes = measuredRuns.Average(x => x.HeapDeltaBytes),
                AvgWorkingSetDeltaBytes = measuredRuns.Average(x => x.WorkingSetDeltaBytes),
                AvgPrivateMemoryDeltaBytes = measuredRuns.Average(x => x.PrivateMemoryDeltaBytes),
                AvgGen0Collections = measuredRuns.Average(x => x.Gen0Collections),
                AvgGen1Collections = measuredRuns.Average(x => x.Gen1Collections),
                AvgGen2Collections = measuredRuns.Average(x => x.Gen2Collections),
                FirstChecksum = measuredRuns[0].Checksum,
                RowsRead = measuredRuns[0].RowsRead
            };
        }

        private static void PrintSummaryTable(List<ScenarioSummary> summaries)
        {
            if (summaries == null || summaries.Count == 0)
            {
                return;
            }

            Console.WriteLine();
            Console.WriteLine("Summary table");
            Console.Write("Rows=");
            WriteHighlightedValue(summaries[0].Rows);
            Console.Write(", Warmup=");
            WriteHighlightedValue(summaries[0].WarmupRuns);
            Console.Write(", Measured=");
            WriteHighlightedValue(summaries[0].MeasuredRuns);
            Console.WriteLine();

            var datasets = summaries.Select(summary => summary.Dataset).Distinct().ToList();

            foreach (var dataset in datasets)
            {
                Console.WriteLine();
                Console.WriteLine("{0} dataset:", dataset);
                WriteSummaryTableHeader();

                foreach (var summary in summaries.Where(candidate => candidate.Dataset == dataset))
                {
                    WriteSummaryTableRow(summary);
                }
            }
        }

        private static void WriteSummaryTableHeader()
        {
            Console.WriteLine("Accessor             Avg elapsed     Avg CPU         Avg allocated bytes     Avg Gen0/1/2");
            Console.WriteLine("-------------------  --------------  --------------  ----------------------  ---------------");
        }

        private static void WriteSummaryTableRow(ScenarioSummary summary)
        {
            WriteLeftCell(summary.Accessor.ToString(), 19, false);
            Console.Write("  ");
            WriteRightCell(summary.AvgElapsedMs.ToString("F2", CultureInfo.InvariantCulture) + " ms", 14, true);
            Console.Write("  ");
            WriteRightCell(summary.AvgCpuMs.ToString("F2", CultureInfo.InvariantCulture) + " ms", 14, true);
            Console.Write("  ");
            WriteRightCell(FormatNullableLong(summary.AvgAppDomainAllocatedBytes), 22, true);
            Console.Write("  ");
            WriteRightCell(FormatGenCollections(summary), 15, true);
            Console.WriteLine();
        }

        private static void WriteLeftCell(string value, int width, bool highlight)
        {
            WriteCell((value ?? string.Empty).PadRight(width), highlight);
        }

        private static void WriteRightCell(string value, int width, bool highlight)
        {
            WriteCell((value ?? string.Empty).PadLeft(width), highlight);
        }

        private static void WriteCell(string value, bool highlight)
        {
            if (highlight)
            {
                WriteHighlightedValue(value);
                return;
            }

            Console.Write(value);
        }

        private static string FormatGenCollections(ScenarioSummary summary)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:F2}/{1:F2}/{2:F2}",
                summary.AvgGen0Collections,
                summary.AvgGen1Collections,
                summary.AvgGen2Collections);
        }

        private static string WriteScenarioCsv(List<ScenarioSummary> all, string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                outputDirectory = Environment.CurrentDirectory;
            }

            Directory.CreateDirectory(outputDirectory);
            var path = Path.Combine(outputDirectory, "odp-number-matrix-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv");

            using (var writer = new StreamWriter(path))
            {
                writer.WriteLine("Dataset,Accessor,Rows,WarmupRuns,MeasuredRuns,AvgElapsedMs,StdElapsedMs,AvgCpuMs,AvgAppDomainAllocatedBytes,AvgHeapDeltaBytes,AvgWorkingSetDeltaBytes,AvgPrivateMemoryDeltaBytes,AvgGen0,AvgGen1,AvgGen2,RowsCheck,Checksum");

                foreach (var s in all)
                {
                    writer.WriteLine(string.Join(",",
                        EscapeCsv(s.Dataset),
                        EscapeCsv(s.Accessor.ToString()),
                        s.Rows.ToString(CultureInfo.InvariantCulture),
                        s.WarmupRuns.ToString(CultureInfo.InvariantCulture),
                        s.MeasuredRuns.ToString(CultureInfo.InvariantCulture),
                        s.AvgElapsedMs.ToString("F2", CultureInfo.InvariantCulture),
                        s.StdElapsedMs.ToString("F2", CultureInfo.InvariantCulture),
                        s.AvgCpuMs.ToString("F2", CultureInfo.InvariantCulture),
                        EscapeCsv(FormatNullableLong(s.AvgAppDomainAllocatedBytes)),
                        s.AvgHeapDeltaBytes.ToString("F0", CultureInfo.InvariantCulture),
                        s.AvgWorkingSetDeltaBytes.ToString("F0", CultureInfo.InvariantCulture),
                        s.AvgPrivateMemoryDeltaBytes.ToString("F0", CultureInfo.InvariantCulture),
                        s.AvgGen0Collections.ToString("F2", CultureInfo.InvariantCulture),
                        s.AvgGen1Collections.ToString("F2", CultureInfo.InvariantCulture),
                        s.AvgGen2Collections.ToString("F2", CultureInfo.InvariantCulture),
                        s.RowsRead.ToString(CultureInfo.InvariantCulture),
                        EscapeCsv(s.FirstChecksum)));
                }
            }

            return path;
        }

        private static string EscapeCsv(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0)
            {
                return '"' + value.Replace("\"", "\"\"") + '"';
            }

            return value;
        }

        private static string FormatNullableLong(long? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "N/A";
        }

        private static long? AverageNullableLong(List<long?> values)
        {
            var nonNull = values.Where(x => x.HasValue).Select(x => (double)x.Value).ToList();
            if (nonNull.Count == 0)
            {
                return null;
            }

            return (long)Math.Round(nonNull.Average());
        }

        private static double StdDev(List<double> values)
        {
            if (values.Count <= 1)
            {
                return 0;
            }

            var avg = values.Average();
            var variance = values.Sum(v => (v - avg) * (v - avg)) / (values.Count - 1);
            return Math.Sqrt(variance);
        }

        private static long? SubtractNullable(long? a, long? b)
        {
            if (!a.HasValue || !b.HasValue)
            {
                return null;
            }

            return a.Value - b.Value;
        }

        private static void TryEnableAppDomainMonitoring()
        {
            try
            {
                AppDomain.MonitoringIsEnabled = true;
            }
            catch
            {
                // Optional metric only.
            }
        }

        private static void ForceFullGc()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static void PrintHeader(Options options)
        {
            Console.WriteLine("=== ODP.NET NUMBER read repro (.NET Framework 4.7.2 / C# 7.3) ===");
            Console.Write("Table       : ");
            Console.WriteLine(options.TableName);
            Console.Write("Prepare     : ");
            Console.WriteLine(options.Prepare);
            Console.Write("Matrix      : ");
            Console.WriteLine(options.Matrix);
            Console.Write("Dataset     : ");
            Console.WriteLine(options.Dataset);
            Console.Write("Accessor    : ");
            Console.WriteLine(options.Accessor);
            Console.Write("Rows        : ");
            WriteHighlightedValue(options.Rows);
            Console.WriteLine();
            Console.Write("Warmup      : ");
            WriteHighlightedValue(options.Warmup);
            Console.WriteLine();
            Console.Write("Iterations  : ");
            WriteHighlightedValue(options.Iterations);
            Console.WriteLine();
            Console.WriteLine();
        }

        private static void WriteRunLine(
            string runLabel,
            int runNumber,
            int rowsRead,
            double elapsedMs,
            double cpuMs,
            long? appDomainAllocatedBytes,
            long heapDeltaBytes,
            long workingSetDeltaBytes,
            long privateMemoryDeltaBytes,
            int gen0Collections,
            int gen1Collections,
            int gen2Collections,
            string checksum)
        {
            Console.Write(runLabel);
            Console.Write(" #");
            WriteHighlightedValue(runNumber);
            Console.Write(" | Rows=");
            WriteHighlightedValue(rowsRead);
            Console.Write(" | ElapsedMs=");
            WriteHighlightedValue(elapsedMs.ToString("F2", CultureInfo.InvariantCulture));
            Console.Write(" | CpuMs=");
            WriteHighlightedValue(cpuMs.ToString("F2", CultureInfo.InvariantCulture));
            Console.Write(" | AppDomainAlloc=");
            WriteHighlightedValue(FormatNullableLong(appDomainAllocatedBytes));
            Console.Write(" | HeapDelta=");
            WriteHighlightedValue(heapDeltaBytes);
            Console.Write(" | WSDelta=");
            WriteHighlightedValue(workingSetDeltaBytes);
            Console.Write(" | PrivateDelta=");
            WriteHighlightedValue(privateMemoryDeltaBytes);
            Console.Write(" | Gen0=");
            WriteHighlightedValue(gen0Collections);
            Console.Write(" Gen1=");
            WriteHighlightedValue(gen1Collections);
            Console.Write(" Gen2=");
            WriteHighlightedValue(gen2Collections);
            Console.Write(" | Checksum=");
            WriteHighlightedValue(checksum);
            Console.WriteLine();
        }

        private static void WriteHighlightedValue(object value)
        {
            if (ShouldUseConsoleHighlighting())
            {
                var previousColor = Console.ForegroundColor;
                Console.ForegroundColor = HighlightColor;
                Console.Write(value);
                Console.ForegroundColor = previousColor;
                return;
            }

            Console.Write(value);
        }

        private static bool ShouldUseConsoleHighlighting()
        {
            return !Console.IsOutputRedirected && !Console.IsErrorRedirected;
        }

        private static string GetRequiredConnectionString()
        {
            ConnectionStringSettings connectionString = ConfigurationManager.ConnectionStrings["OracleDb"];
            if (connectionString == null || string.IsNullOrWhiteSpace(connectionString.ConnectionString))
            {
                throw new ConfigurationErrorsException("Connection string 'OracleDb' is missing or empty.");
            }

            return connectionString.ConnectionString;
        }

        private static void PrepareDataset(OracleConnection connection, Options options)
        {
            var indexName = BuildIndexName(options.TableName);

            ExecuteIgnoreTableMissing(connection, "drop table " + options.TableName + " purge");

            ExecuteNonQuery(connection,
                "create table " + options.TableName + " (" +
                "DATASET varchar2(10) not null, " +
                "ID number not null, " +
                "VAL number(30,10) not null)");

            ExecuteNonQuery(connection,
                "insert /*+ append */ into " + options.TableName + " (DATASET, ID, VAL) " +
                "select 'DUP', level, mod(level, 10) + 0.1234567890 from dual connect by level <= :p_rows",
                new OracleParameter("p_rows", OracleDbType.Int32, options.SetupRows, System.Data.ParameterDirection.Input));

            ExecuteNonQuery(connection,
                "insert /*+ append */ into " + options.TableName + " (DATASET, ID, VAL) " +
                "select 'HC', level, level + (level / 1000000000) from dual connect by level <= :p_rows",
                new OracleParameter("p_rows", OracleDbType.Int32, options.SetupRows, System.Data.ParameterDirection.Input));

            ExecuteNonQuery(connection, "commit");
            ExecuteNonQuery(connection, "create index " + indexName + " on " + options.TableName + "(DATASET, ID)");
            ExecuteNonQuery(
                connection,
                "begin dbms_stats.gather_table_stats(user, :p_table_name); end;",
                new OracleParameter("p_table_name", OracleDbType.Varchar2, options.TableName, System.Data.ParameterDirection.Input));
        }

        private static string BuildIndexName(string tableName)
        {
            var prefix = "IX_" + tableName;

            if (prefix.Length > 28)
            {
                prefix = prefix.Substring(0, 28);
            }

            return prefix + "_1";
        }

        private static void ExecuteIgnoreTableMissing(OracleConnection connection, string sql)
        {
            try
            {
                ExecuteNonQuery(connection, sql);
            }
            catch (OracleException ex)
            {
                // ORA-00942 table or view does not exist
                if (ex.Number != 942)
                {
                    throw;
                }
            }
        }

        private static void ExecuteNonQuery(OracleConnection connection, string sql, params OracleParameter[] parameters)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.BindByName = true;
                cmd.CommandText = sql;

                if (parameters != null)
                {
                    foreach (var p in parameters)
                    {
                        cmd.Parameters.Add(p);
                    }
                }

                cmd.ExecuteNonQuery();
            }
        }

        private sealed class MetricsSnapshot
        {
            public long ManagedHeapBytes { get; private set; }
            public long WorkingSetBytes { get; private set; }
            public long PrivateMemoryBytes { get; private set; }
            public TimeSpan ProcessCpu { get; private set; }
            public int Gen0Collections { get; private set; }
            public int Gen1Collections { get; private set; }
            public int Gen2Collections { get; private set; }
            public long? AppDomainTotalAllocatedBytes { get; private set; }

            public static MetricsSnapshot Capture()
            {
                using (var process = Process.GetCurrentProcess())
                {
                    var snapshot = new MetricsSnapshot
                    {
                        ManagedHeapBytes = GC.GetTotalMemory(false),
                        WorkingSetBytes = process.WorkingSet64,
                        PrivateMemoryBytes = process.PrivateMemorySize64,
                        ProcessCpu = process.TotalProcessorTime,
                        Gen0Collections = GC.CollectionCount(0),
                        Gen1Collections = GC.CollectionCount(1),
                        Gen2Collections = GC.CollectionCount(2),
                        AppDomainTotalAllocatedBytes = TryReadAppDomainAllocatedBytes()
                    };

                    return snapshot;
                }
            }

            private static long? TryReadAppDomainAllocatedBytes()
            {
                try
                {
                    return AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
                }
                catch
                {
                    return null;
                }
            }
        }

        private sealed class RunResult
        {
            public int RunNumber { get; set; }
            public bool IsWarmup { get; set; }
            public string Dataset { get; set; }
            public AccessorKind Accessor { get; set; }
            public int RowsRead { get; set; }
            public double ElapsedMs { get; set; }
            public double CpuMs { get; set; }
            public long? AppDomainAllocatedBytes { get; set; }
            public long HeapDeltaBytes { get; set; }
            public long WorkingSetDeltaBytes { get; set; }
            public long PrivateMemoryDeltaBytes { get; set; }
            public int Gen0Collections { get; set; }
            public int Gen1Collections { get; set; }
            public int Gen2Collections { get; set; }
            public string Checksum { get; set; }
        }

        private sealed class ScenarioSummary
        {
            public string Dataset { get; set; }
            public AccessorKind Accessor { get; set; }
            public int Rows { get; set; }
            public int WarmupRuns { get; set; }
            public int MeasuredRuns { get; set; }
            public int RowsRead { get; set; }
            public string FirstChecksum { get; set; }
            public double AvgElapsedMs { get; set; }
            public double StdElapsedMs { get; set; }
            public double AvgCpuMs { get; set; }
            public long? AvgAppDomainAllocatedBytes { get; set; }
            public double AvgHeapDeltaBytes { get; set; }
            public double AvgWorkingSetDeltaBytes { get; set; }
            public double AvgPrivateMemoryDeltaBytes { get; set; }
            public double AvgGen0Collections { get; set; }
            public double AvgGen1Collections { get; set; }
            public double AvgGen2Collections { get; set; }
        }

        private enum AccessorKind
        {
            GetDecimal,
            GetDouble,
            GetOracleDecimal,
            GetValueDecimal
        }

        private sealed class Options
        {
            public string TableName { get; set; }
            public bool Prepare { get; set; }
            public int SetupRows { get; set; }
            public bool Matrix { get; set; }
            public string Dataset { get; set; }
            public AccessorKind Accessor { get; set; }
            public int Rows { get; set; }
            public int Warmup { get; set; }
            public int Iterations { get; set; }
            public bool AllowFewerRows { get; set; }
            public string OutputDirectory { get; set; }

            public static Options Parse(string[] args)
            {
                var options = new Options
                {
                    TableName = DefaultTableName,
                    Prepare = true,
                    SetupRows = 500000,
                    Matrix = true,
                    Dataset = "DUP",
                    Accessor = AccessorKind.GetDecimal,
                    Rows = 5000,
                    Warmup = 1,
                    Iterations = 2,
                    AllowFewerRows = false,
                    OutputDirectory = Environment.CurrentDirectory
                };

                foreach (var arg in args)
                {
                    if (!arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var parts = arg.Substring(2).Split(new[] { '=' }, 2);
                    var key = parts[0].Trim().ToLowerInvariant();
                    var value = parts.Length == 2 ? parts[1].Trim() : "true";

                    switch (key)
                    {
                        case "table":
                            options.TableName = NormalizeAndValidateTableName(value);
                            break;
                        case "prepare":
                            options.Prepare = ParseBoolean(value);
                            break;
                        case "setup-rows":
                            options.SetupRows = int.Parse(value, CultureInfo.InvariantCulture);
                            break;
                        case "matrix":
                            options.Matrix = ParseBoolean(value);
                            break;
                        case "dataset":
                            options.Dataset = value.ToUpperInvariant();
                            break;
                        case "accessor":
                            options.Accessor = ParseAccessor(value);
                            break;
                        case "rows":
                            options.Rows = int.Parse(value, CultureInfo.InvariantCulture);
                            break;
                        case "warmup":
                            options.Warmup = int.Parse(value, CultureInfo.InvariantCulture);
                            break;
                        case "iterations":
                            options.Iterations = int.Parse(value, CultureInfo.InvariantCulture);
                            break;
                        case "allow-fewer-rows":
                            options.AllowFewerRows = ParseBoolean(value);
                            break;
                        case "outdir":
                            options.OutputDirectory = value;
                            break;
                    }
                }

                return options;
            }

            private static bool ParseBoolean(string value)
            {
                return value.Equals("1", StringComparison.OrdinalIgnoreCase)
                       || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                       || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                       || value.Equals("y", StringComparison.OrdinalIgnoreCase);
            }

            private static AccessorKind ParseAccessor(string value)
            {
                if (value.Equals("GetDecimal", StringComparison.OrdinalIgnoreCase))
                {
                    return AccessorKind.GetDecimal;
                }

                if (value.Equals("GetDouble", StringComparison.OrdinalIgnoreCase))
                {
                    return AccessorKind.GetDouble;
                }

                if (value.Equals("GetOracleDecimal", StringComparison.OrdinalIgnoreCase))
                {
                    return AccessorKind.GetOracleDecimal;
                }

                if (value.Equals("GetValueDecimal", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("GetValue_Decimal", StringComparison.OrdinalIgnoreCase))
                {
                    return AccessorKind.GetValueDecimal;
                }

                throw new ArgumentOutOfRangeException("value", value,
                    "Accessor must be one of: GetDecimal, GetDouble, GetOracleDecimal, GetValueDecimal");
            }

            private static string NormalizeAndValidateTableName(string value)
            {
                var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();

                if (!TableNamePattern.IsMatch(normalized))
                {
                    throw new ArgumentOutOfRangeException(
                        "value",
                        value,
                        "Table name must match Oracle simple identifier pattern: ^[A-Z][A-Z0-9_]{0,29}$");
                }

                return normalized;
            }
        }
    }
}
