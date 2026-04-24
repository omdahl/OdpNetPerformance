using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using OdpNetPerformance.Benchmarking;
using OdpNetPerformance.Configuration;
using OdpNetPerformance.Oracle;

namespace OdpNetPerformance
{
    internal static class Program
    {
        private const int QuaterMegabyteInBytes = 256 * 1024;
        private const int OneMegabyteInBytes = 1024 * 1024;
        private const int FiveMegabytesInBytes = 5 * 1024 * 1024;
        private const int TenMegabytesInBytes = 10 * 1024 * 1024;
        private const int CalibrationRowCount = 2500;
        private const int CalibrationWarmupIterations = 1;
        private const int CalibrationMeasuredIterations = 2;
        private const int CalibrationSampleCount = 3;

        private static int Main(string[] args)
        {
            try
            {
                return RunAsync(args).GetAwaiter().GetResult();
            }
            catch (ConfigurationErrorsException ex)
            {
                Console.Error.WriteLine("Configuration error: " + ex.Message);
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Fatal error: " + ex.Message);
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static async Task<int> RunAsync(string[] args)
        {
            string connectionString = GetRequiredConnectionString();
            AppSettings settings = AppSettings.Load();
            OracleConfiguration.SelfTuning = settings.EnableSelfTuning;

            BenchmarkTimer.SampleCount = settings.BenchmarkSampleCount;
            BenchmarkTimer.PauseBetweenSamplesMilliseconds = settings.BenchmarkSamplePauseMilliseconds;
            BenchmarkTimer.BeforeSampleAsync =
                string.Equals(settings.BenchmarkConnectionMode, "Cold", StringComparison.OrdinalIgnoreCase)
                    ? (Func<int, CancellationToken, Task>)ClearPoolsBeforeSampleAsync
                    : null;

            var context = new BenchmarkContext(connectionString, settings);
            context.ResultReported = ConsoleReporter.WriteResult;
            var initializer = new SchemaInitializer(context);
            var cancellationToken = CancellationToken.None;

            ConsoleReporter.WriteHeader("ODP.NET Performance Benchmark");
            ConsoleReporter.WriteHeader("--------------------------------");
            ConsoleReporter.WriteHeader("ODP.NET Version=" + GetOracleManagedDataAccessVersion());
            ConsoleReporter.WriteHeader("DataSource=" + GetOracleDataSource(connectionString));
            ConsoleReporter.WriteHeader("SelfTuningEnabled=" + settings.EnableSelfTuning);
            ConsoleReporter.WriteHeader("ConnectionMode=" + settings.BenchmarkConnectionMode + ";TimingSamples=" + settings.BenchmarkSampleCount + ";SamplePauseMs=" + settings.BenchmarkSamplePauseMilliseconds);
            ConsoleReporter.WriteHeader("SeedBenchmarkData=" + settings.SeedBenchmarkData + ";SeedBatchSize=" + settings.SeedBatchSize);

            await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
            OracleConnection.ClearAllPools();

            await ApplyStartupFetchSizeCalibrationAsync(context, cancellationToken).ConfigureAwait(false);
            OracleConnection.ClearAllPools();

            IReadOnlyCollection<IPerformanceTest> tests = BenchmarkSuite.Create(context);
            bool allSucceeded = true;

            foreach (IPerformanceTest test in tests)
            {
                ConsoleReporter.WriteHeader("Running=" + test.Name);
                IReadOnlyCollection<BenchmarkResult> results = await test.RunAsync(cancellationToken).ConfigureAwait(false);

                foreach (BenchmarkResult result in results)
                {
                    if (!result.IsSuccess)
                    {
                        allSucceeded = false;
                    }
                }
            }

            ConsoleReporter.WriteHeader(allSucceeded ? "Status=Success" : "Status=Failure");
            return allSucceeded ? 0 : 1;
        }

        private static Task ClearPoolsBeforeSampleAsync(int sampleIndex, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OracleConnection.ClearAllPools();
            return Task.FromResult(0);
        }

        private static async Task ApplyStartupFetchSizeCalibrationAsync(BenchmarkContext context, CancellationToken cancellationToken)
        {
            ConsoleReporter.WriteHeader("Running=Startup.FetchSizeCalibration");

            int[] candidates = new[]
            {
                QuaterMegabyteInBytes,
                OneMegabyteInBytes,
                FiveMegabytesInBytes,
                TenMegabytesInBytes
            };

            var uniqueCandidates = new List<int>(3);
            foreach (int candidate in candidates)
            {
                if (!uniqueCandidates.Contains(candidate))
                {
                    uniqueCandidates.Add(candidate);
                }
            }

            var measurements = new List<FetchSizeCalibrationMeasurement>(uniqueCandidates.Count);

            try
            {
                bool readAllColumns = string.Equals(context.Settings.FetchSizeReadMode, "FullRow", StringComparison.OrdinalIgnoreCase);
                int rowCount = Math.Max(1, Math.Min(context.Settings.BenchmarkRowCount, CalibrationRowCount));

                using (var connection = new OracleConnection(context.ConnectionString))
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                    using (var command = connection.CreateCommand())
                    {
                        command.BindByName = true;
                        command.CommandText = "SELECT * FROM " + context.TableName + " WHERE ID <= :maxId ORDER BY ID";
                        command.Parameters.Add(new OracleParameter("maxId", rowCount));

                        foreach (int candidate in uniqueCandidates)
                        {
                            command.FetchSize = candidate;
                            var elapsedSamples = new List<double>(CalibrationSampleCount);

                            for (int sampleIndex = 0; sampleIndex < CalibrationSampleCount; sampleIndex++)
                            {
                                for (int warmupIndex = 0; warmupIndex < CalibrationWarmupIterations; warmupIndex++)
                                {
                                    await ConsumeReaderAsync(command, readAllColumns, cancellationToken).ConfigureAwait(false);
                                }

                                Stopwatch stopwatch = Stopwatch.StartNew();
                                for (int measuredIndex = 0; measuredIndex < CalibrationMeasuredIterations; measuredIndex++)
                                {
                                    await ConsumeReaderAsync(command, readAllColumns, cancellationToken).ConfigureAwait(false);
                                }

                                stopwatch.Stop();
                                elapsedSamples.Add(stopwatch.Elapsed.TotalMilliseconds);
                            }

                            double medianElapsedMs = GetMedianMilliseconds(elapsedSamples);
                            double operationsPerSecond = CalibrationMeasuredIterations / Math.Max(0.001d, medianElapsedMs / 1000d);

                            measurements.Add(new FetchSizeCalibrationMeasurement(candidate, medianElapsedMs, operationsPerSecond));
                        }
                    }
                }

                foreach (FetchSizeCalibrationMeasurement measurement in measurements)
                {
                    ConsoleReporter.WriteHeader(
                        "CalibrationCandidate=" + ToFetchSizeLabel(measurement.FetchSizeBytes) +
                        ";Bytes=" + measurement.FetchSizeBytes.ToString(CultureInfo.InvariantCulture) +
                        ";MedianElapsedMs=" + measurement.MedianElapsedMilliseconds.ToString("F2", CultureInfo.InvariantCulture) +
                        ";OpsPerSec=" + measurement.OperationsPerSecond.ToString("F2", CultureInfo.InvariantCulture));
                }

                FetchSizeCalibrationMeasurement bestMeasurement = measurements[0];
                for (int index = 1; index < measurements.Count; index++)
                {
                    FetchSizeCalibrationMeasurement current = measurements[index];
                    if (current.MedianElapsedMilliseconds < bestMeasurement.MedianElapsedMilliseconds)
                    {
                        bestMeasurement = current;
                    }
                }

                context.Settings.ApplyStartupFetchSizeCalibration(bestMeasurement.FetchSizeBytes);

                ConsoleReporter.WriteHeader(
                    "CalibrationSelected=" + ToFetchSizeLabel(bestMeasurement.FetchSizeBytes) +
                    ";Bytes=" + bestMeasurement.FetchSizeBytes.ToString(CultureInfo.InvariantCulture) +
                    ";MedianElapsedMs=" + bestMeasurement.MedianElapsedMilliseconds.ToString("F2", CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                ConsoleReporter.WriteHeader(
                    "CalibrationSkipped=True;Reason=" + ex.GetType().Name + ": " + ex.Message +
                    ";Using=" + ToFetchSizeLabel(context.Settings.FetchSizeOneMbBytes) +
                    ";Bytes=" + context.Settings.FetchSizeOneMbBytes.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static async Task ConsumeReaderAsync(OracleCommand command, bool readAllColumns, CancellationToken cancellationToken)
        {
            using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!readAllColumns)
                    {
                        continue;
                    }

                    int fieldCount = reader.FieldCount;
                    for (int columnIndex = 0; columnIndex < fieldCount; columnIndex++)
                    {
                        reader.GetValue(columnIndex);
                    }
                }
            }
        }

        private static double GetMedianMilliseconds(IList<double> values)
        {
            if (values == null || values.Count == 0)
            {
                return 0d;
            }

            double[] ordered = new double[values.Count];
            values.CopyTo(ordered, 0);
            Array.Sort(ordered);

            int middleIndex = ordered.Length / 2;
            if (ordered.Length % 2 == 1)
            {
                return ordered[middleIndex];
            }

            return (ordered[middleIndex - 1] + ordered[middleIndex]) / 2d;
        }

        private static string ToFetchSizeLabel(int fetchSizeBytes)
        {
            if (fetchSizeBytes % OneMegabyteInBytes == 0)
            {
                return (fetchSizeBytes / OneMegabyteInBytes).ToString(CultureInfo.InvariantCulture) + "MB";
            }

            if (fetchSizeBytes % 1024 == 0)
            {
                return (fetchSizeBytes / 1024).ToString(CultureInfo.InvariantCulture) + "KB";
            }

            return fetchSizeBytes.ToString(CultureInfo.InvariantCulture) + "B";
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

        private static string GetOracleManagedDataAccessVersion()
        {
            Version version = typeof(OracleConnection).Assembly.GetName().Version;
            return version == null ? "Unknown" : version.ToString();
        }

        private static string GetOracleDataSource(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return "Unknown";
            }

            try
            {
                var builder = new OracleConnectionStringBuilder(connectionString);
                if (string.IsNullOrWhiteSpace(builder.DataSource))
                {
                    return "Unknown";
                }

                return builder.DataSource;
            }
            catch (ArgumentException)
            {
                return "Unknown";
            }
        }

        private sealed class FetchSizeCalibrationMeasurement
        {
            public FetchSizeCalibrationMeasurement(int fetchSizeBytes, double medianElapsedMilliseconds, double operationsPerSecond)
            {
                FetchSizeBytes = fetchSizeBytes;
                MedianElapsedMilliseconds = medianElapsedMilliseconds;
                OperationsPerSecond = operationsPerSecond;
            }

            public int FetchSizeBytes { get; private set; }

            public double MedianElapsedMilliseconds { get; private set; }

            public double OperationsPerSecond { get; private set; }
        }
    }
}