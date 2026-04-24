using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using OdpNetPerformance.Benchmarking;

namespace OdpNetPerformance.Oracle
{
    /// <summary>
    /// Measures OracleCommand fetch-size behavior while scanning the full benchmark row set.
    /// </summary>
    public sealed class FetchSizePerformanceTest : OracleBenchmarkBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FetchSizePerformanceTest"/> class.
        /// </summary>
        public FetchSizePerformanceTest(BenchmarkContext context)
            : base(context)
        {
        }

        /// <inheritdoc/>
        public override string Name
        {
            get { return "FetchSize"; }
        }

        /// <inheritdoc/>
        protected override async Task<IReadOnlyCollection<BenchmarkResult>> ExecuteCoreAsync(CancellationToken cancellationToken)
        {
            var results = new List<BenchmarkResult>();
            int[] rowCounts = new[] { Context.Settings.BenchmarkRowCount, Context.Settings.LargeFetchRowCount };
            var fetchSizes = new[]
            {
                new FetchSizeVariant("256KB", Context.Settings.FetchSize256KbBytes),
                new FetchSizeVariant("1MB", Context.Settings.FetchSizeOneMbBytes),
                new FetchSizeVariant("10MB", Context.Settings.FetchSizeTenMbBytes)
            };

            foreach (int rowCount in rowCounts)
            {
                foreach (FetchSizeVariant fetchSize in fetchSizes)
                {
                    BenchmarkResult result = await MeasureAsync(
                        BuildTestName(rowCount, fetchSize.Label),
                        Context.Settings.WarmupIterations,
                        Context.Settings.FetchSizeIterations,
                        fetchSize.Bytes,
                        rowCount,
                        rowCount == Context.Settings.LargeFetchRowCount,
                        cancellationToken).ConfigureAwait(false);

                    results.Add(result);
                    ReportResult(result);
                }
            }

            return results;
        }

        private IReadOnlyCollection<FetchSizeVariant> BuildFetchSizeVariants()
        {
            var variants = new List<FetchSizeVariant>
            {
                new FetchSizeVariant("256KB", Context.Settings.FetchSize256KbBytes),
                new FetchSizeVariant("1MB", Context.Settings.FetchSizeOneMbBytes),
                new FetchSizeVariant("10MB", Context.Settings.FetchSizeTenMbBytes)
            };

            if (Context.Settings.IsStartupFetchSizeCalibrated)
            {
                variants.Add(new FetchSizeVariant("AutoSelected", Context.Settings.StartupCalibratedFetchSizeBytes));
            }

            return variants;
        }

        private sealed class FetchSizeVariant
        {
            public FetchSizeVariant(string label, int bytes)
            {
                Label = label;
                Bytes = bytes;
            }

            public string Label { get; private set; }

            public int Bytes { get; private set; }
        }

        private static string BuildTestName(int rowCount, string fetchSizeLabel)
        {
            return "FetchSize." + rowCount + "Rows." + fetchSizeLabel;
        }

        private async Task<BenchmarkResult> MeasureAsync(
            string testName,
            int warmupIterations,
            int measuredIterations,
            int fetchSizeBytes,
            int rowCount,
            bool forceFullRowRead,
            CancellationToken cancellationToken)
        {
            bool readAllColumns = forceFullRowRead || string.Equals(Context.Settings.FetchSizeReadMode, "FullRow", StringComparison.OrdinalIgnoreCase);
            string actualReadMode = readAllColumns ? "FullRow" : "RowOnly";
            OracleConnection sampleConnection = null;
            OracleCommand sampleCommand = null;
            Func<int, CancellationToken, Task> previousBeforeSample = BenchmarkTimer.BeforeSampleAsync;

            BenchmarkTimer.BeforeSampleAsync = async delegate(int sampleIndex, CancellationToken token)
            {
                if (previousBeforeSample != null)
                {
                    await previousBeforeSample(sampleIndex, token).ConfigureAwait(false);
                }

                DisposeSampleResources(sampleCommand, sampleConnection);
                sampleConnection = CreateConnection();
                await sampleConnection.OpenAsync(token).ConfigureAwait(false);
                sampleCommand = sampleConnection.CreateCommand();
                sampleCommand.BindByName = true;
                sampleCommand.FetchSize = fetchSizeBytes;
                sampleCommand.CommandText = FetchSizeSelectCommandText;
                sampleCommand.Parameters.Add(new OracleParameter("maxId", rowCount));
            };

            try
            {
                BenchmarkResult result = await BenchmarkTimer.MeasureAsync(
                    testName,
                    warmupIterations,
                    measuredIterations,
                    async delegate(int index, CancellationToken token)
                    {
                        using (var reader = await sampleCommand.ExecuteReaderAsync(token).ConfigureAwait(false))
                        {
                            while (await reader.ReadAsync(token).ConfigureAwait(false))
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
                    },
                    "Scans " + rowCount + " rows with FetchSize=" + fetchSizeBytes + " bytes using ReadMode=" + actualReadMode + ".",
                    cancellationToken).ConfigureAwait(false);

                int totalRowsProcessed = rowCount * measuredIterations;
                result.Details = result.Details + " RowsProcessed=" + totalRowsProcessed + ".";
                return result;
            }
            finally
            {
                BenchmarkTimer.BeforeSampleAsync = previousBeforeSample;
                DisposeSampleResources(sampleCommand, sampleConnection);
            }
        }

        private static void DisposeSampleResources(OracleCommand command, OracleConnection connection)
        {
            if (command != null)
            {
                command.Dispose();
            }

            if (connection != null)
            {
                connection.Dispose();
            }
        }
    }
}