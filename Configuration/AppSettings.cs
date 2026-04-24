using System;
using System.Configuration;

namespace OdpNetPerformance.Configuration
{
    /// <summary>
    /// Represents tunable application settings for the benchmark suite.
    /// </summary>
    public sealed class AppSettings
    {
        /// <summary>
        /// Gets the number of warm-up iterations to run before measurements.
        /// </summary>
        public int WarmupIterations { get; private set; }

        /// <summary>
        /// Gets the number of CRUD iterations.
        /// </summary>
        public int CrudIterations { get; private set; }

        /// <summary>
        /// Gets the number of rows used by row-based benchmarks.
        /// </summary>
        public int BenchmarkRowCount { get; private set; }

        /// <summary>
        /// Gets the row count used by the large fetch benchmark scenario.
        /// </summary>
        public int LargeFetchRowCount { get; private set; }

        /// <summary>
        /// Gets the number of fetch-size benchmark iterations.
        /// </summary>
        public int FetchSizeIterations { get; private set; }

        /// <summary>
        /// Gets the fetch-size read mode (FullRow or RowOnly).
        /// </summary>
        public string FetchSizeReadMode { get; private set; }

        /// <summary>
        /// Gets the 256 KB fetch size in bytes.
        /// </summary>
        public int FetchSize256KbBytes { get; private set; }

        /// <summary>
        /// Gets the 1 MB fetch size in bytes.
        /// </summary>
        public int FetchSizeOneMbBytes { get; private set; }

        /// <summary>
        /// Gets the 10 MB fetch size in bytes.
        /// </summary>
        public int FetchSizeTenMbBytes { get; private set; }

        /// <summary>
        /// Gets the startup-calibrated fetch size in bytes.
        /// </summary>
        public int StartupCalibratedFetchSizeBytes { get; private set; }

        /// <summary>
        /// Gets a value indicating whether startup fetch-size calibration has been applied.
        /// </summary>
        public bool IsStartupFetchSizeCalibrated { get; private set; }

        /// <summary>
        /// Gets the number of connection open/close iterations.
        /// </summary>
        public int ConnectionIterations { get; private set; }

        /// <summary>
        /// Gets the number of array-bind iterations.
        /// </summary>
        public int ArrayBindIterations { get; private set; }

        /// <summary>
        /// Gets the array-bind batch size.
        /// </summary>
        public int ArrayBindBatchSize { get; private set; }

        /// <summary>
        /// Gets the number of concurrent workers.
        /// </summary>
        public int ConcurrentWorkers { get; private set; }

        /// <summary>
        /// Gets the number of iterations per concurrent worker.
        /// </summary>
        public int ConcurrentIterationsPerWorker { get; private set; }

        /// <summary>
        /// Gets the number of ConfigureAwait comparison iterations.
        /// </summary>
        public int ConfigureAwaitIterations { get; private set; }

        /// <summary>
        /// Gets the number of timing samples to collect per benchmark.
        /// </summary>
        public int BenchmarkSampleCount { get; private set; }

        /// <summary>
        /// Gets the pause between timing samples in milliseconds.
        /// </summary>
        public int BenchmarkSamplePauseMilliseconds { get; private set; }

        /// <summary>
        /// Gets a value indicating whether Oracle self-tuning is enabled.
        /// </summary>
        public bool EnableSelfTuning { get; private set; }

        /// <summary>
        /// Gets the benchmark connection mode (Warm or Cold).
        /// </summary>
        public string BenchmarkConnectionMode { get; private set; }

        /// <summary>
        /// Gets a value indicating whether deterministic seed rows are inserted.
        /// </summary>
        public bool SeedBenchmarkData { get; private set; }

        /// <summary>
        /// Gets the batch size used for deterministic seed row insertion.
        /// </summary>
        public int SeedBatchSize { get; private set; }

        /// <summary>
        /// Gets the Oracle test table name.
        /// </summary>
        public string TestTableName { get; private set; }

        /// <summary>
        /// Loads application settings from App.config.
        /// </summary>
        public static AppSettings Load()
        {
            int oneMbFetchSizeBytes = GetRequiredInt("FetchSizeOneMbBytes");

            return new AppSettings
            {
                WarmupIterations = GetRequiredInt("WarmupIterations"),
                CrudIterations = GetRequiredInt("CrudIterations"),
                BenchmarkRowCount = GetRequiredInt("BenchmarkRowCount"),
                LargeFetchRowCount = GetOptionalInt("LargeFetchRowCount", 25000, 1),
                FetchSizeIterations = GetRequiredInt("FetchSizeIterations"),
                FetchSizeReadMode = GetOptionalFetchSizeReadMode("FetchSizeReadMode", "FullRow"),
                FetchSize256KbBytes = GetRequiredInt("FetchSize256KbBytes"),
                FetchSizeOneMbBytes = oneMbFetchSizeBytes,
                FetchSizeTenMbBytes = GetRequiredInt("FetchSizeTenMbBytes"),
                StartupCalibratedFetchSizeBytes = oneMbFetchSizeBytes,
                IsStartupFetchSizeCalibrated = false,
                ConnectionIterations = GetRequiredInt("ConnectionIterations"),
                ArrayBindIterations = GetRequiredInt("ArrayBindIterations"),
                ArrayBindBatchSize = GetRequiredInt("ArrayBindBatchSize"),
                ConcurrentWorkers = GetRequiredInt("ConcurrentWorkers"),
                ConcurrentIterationsPerWorker = GetRequiredInt("ConcurrentIterationsPerWorker"),
                ConfigureAwaitIterations = GetRequiredInt("ConfigureAwaitIterations"),
                BenchmarkSampleCount = GetOptionalInt("BenchmarkSampleCount", 3, 1),
                BenchmarkSamplePauseMilliseconds = GetOptionalInt("BenchmarkSamplePauseMilliseconds", 200, 0),
                EnableSelfTuning = GetOptionalBool("EnableSelfTuning", true),
                BenchmarkConnectionMode = GetOptionalConnectionMode("BenchmarkConnectionMode", "Warm"),
                SeedBenchmarkData = GetOptionalBool("SeedBenchmarkData", true),
                SeedBatchSize = GetOptionalInt("SeedBatchSize", 2000, 1),
                TestTableName = GetRequiredString("TestTableName")
            };
        }

        /// <summary>
        /// Stores the startup-calibrated fetch size for optional benchmark scenarios.
        /// </summary>
        /// <param name="fetchSizeBytes">The calibrated fetch size in bytes.</param>
        public void ApplyStartupFetchSizeCalibration(int fetchSizeBytes)
        {
            if (fetchSizeBytes <= 0)
            {
                throw new ArgumentOutOfRangeException("fetchSizeBytes", "Calibrated fetch size must be greater than zero.");
            }

            StartupCalibratedFetchSizeBytes = fetchSizeBytes;
            IsStartupFetchSizeCalibrated = true;
        }

        private static int GetRequiredInt(string key)
        {
            string value = ConfigurationManager.AppSettings[key];
            int parsed;
            if (string.IsNullOrWhiteSpace(value) || !int.TryParse(value, out parsed) || parsed <= 0)
            {
                throw new ConfigurationErrorsException("AppSetting '" + key + "' is missing or invalid.");
            }

            return parsed;
        }

        private static string GetRequiredString(string key)
        {
            string value = ConfigurationManager.AppSettings[key];
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ConfigurationErrorsException("AppSetting '" + key + "' is missing or empty.");
            }

            return value.Trim();
        }

        private static int GetOptionalInt(string key, int defaultValue, int minValue)
        {
            string value = ConfigurationManager.AppSettings[key];
            int parsed;
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            if (!int.TryParse(value, out parsed) || parsed < minValue)
            {
                throw new ConfigurationErrorsException("AppSetting '" + key + "' is missing or invalid.");
            }

            return parsed;
        }

        private static bool GetOptionalBool(string key, bool defaultValue)
        {
            string value = ConfigurationManager.AppSettings[key];
            bool parsed;
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            if (!bool.TryParse(value, out parsed))
            {
                throw new ConfigurationErrorsException("AppSetting '" + key + "' is missing or invalid.");
            }

            return parsed;
        }

        private static string GetOptionalConnectionMode(string key, string defaultValue)
        {
            string value = ConfigurationManager.AppSettings[key];
            string mode = string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();

            if (!string.Equals(mode, "Warm", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(mode, "Cold", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConfigurationErrorsException("AppSetting '" + key + "' must be either 'Warm' or 'Cold'.");
            }

            return string.Equals(mode, "Cold", StringComparison.OrdinalIgnoreCase) ? "Cold" : "Warm";
        }

        private static string GetOptionalFetchSizeReadMode(string key, string defaultValue)
        {
            string value = ConfigurationManager.AppSettings[key];
            string mode = string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();

            if (!string.Equals(mode, "FullRow", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(mode, "RowOnly", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConfigurationErrorsException("AppSetting '" + key + "' must be either 'FullRow' or 'RowOnly'.");
            }

            return string.Equals(mode, "RowOnly", StringComparison.OrdinalIgnoreCase) ? "RowOnly" : "FullRow";
        }
    }
}