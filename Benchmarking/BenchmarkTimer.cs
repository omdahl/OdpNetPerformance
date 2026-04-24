using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OdpNetPerformance.Benchmarking
{
    /// <summary>
    /// Provides timing helpers for benchmark operations.
    /// </summary>
    public static class BenchmarkTimer
    {
        private static int sampleCount = 1;
        private static int pauseBetweenSamplesMilliseconds;

        /// <summary>
        /// Gets or sets the number of measured timing samples per benchmark.
        /// </summary>
        public static int SampleCount
        {
            get { return sampleCount; }
            set { sampleCount = Math.Max(1, value); }
        }

        /// <summary>
        /// Gets or sets the pause between timing samples in milliseconds.
        /// </summary>
        public static int PauseBetweenSamplesMilliseconds
        {
            get { return pauseBetweenSamplesMilliseconds; }
            set { pauseBetweenSamplesMilliseconds = Math.Max(0, value); }
        }

        /// <summary>
        /// Gets or sets an optional callback executed before each timing sample.
        /// </summary>
        public static Func<int, CancellationToken, Task> BeforeSampleAsync { get; set; }

        /// <summary>
        /// Runs the specified warm-up and measured actions and returns a benchmark result.
        /// </summary>
        public static async Task<BenchmarkResult> MeasureAsync(
            string testName,
            int warmupIterations,
            int measuredIterations,
            Func<int, CancellationToken, Task> operation,
            string details,
            CancellationToken cancellationToken)
        {
            int warmupCount = Math.Max(0, warmupIterations);
            int measuredCount = Math.Max(1, measuredIterations);
            int measuredSampleCount = Math.Max(1, SampleCount);
            int samplePauseMilliseconds = Math.Max(0, PauseBetweenSamplesMilliseconds);

            var elapsedSamples = new List<TimeSpan>(measuredSampleCount);

            for (int sampleIndex = 0; sampleIndex < measuredSampleCount; sampleIndex++)
            {
                Func<int, CancellationToken, Task> beforeSample = BeforeSampleAsync;
                if (beforeSample != null)
                {
                    await beforeSample(sampleIndex, cancellationToken).ConfigureAwait(false);
                }

                for (int index = 0; index < warmupCount; index++)
                {
                    await operation(index, cancellationToken).ConfigureAwait(false);
                }

                Stopwatch stopwatch = Stopwatch.StartNew();
                for (int index = 0; index < measuredCount; index++)
                {
                    await operation(index, cancellationToken).ConfigureAwait(false);
                }

                stopwatch.Stop();
                elapsedSamples.Add(stopwatch.Elapsed);

                if (samplePauseMilliseconds > 0 && sampleIndex < measuredSampleCount - 1)
                {
                    await Task.Delay(samplePauseMilliseconds, cancellationToken).ConfigureAwait(false);
                }
            }

            TimeSpan medianElapsed = GetMedianElapsed(elapsedSamples);
            string detailWithSampleStats = AppendSampleStats(details, elapsedSamples);

            return new BenchmarkResult
            {
                TestName = testName,
                Iterations = measuredCount,
                Elapsed = medianElapsed,
                OperationsPerSecond = measuredCount / Math.Max(0.001d, medianElapsed.TotalSeconds),
                Details = detailWithSampleStats,
                IsSuccess = true
            };
        }

        private static TimeSpan GetMedianElapsed(IList<TimeSpan> elapsedSamples)
        {
            TimeSpan[] ordered = elapsedSamples.OrderBy(sample => sample).ToArray();
            int medianIndex = ordered.Length / 2;

            if (ordered.Length % 2 == 1)
            {
                return ordered[medianIndex];
            }

            long medianTicks = (ordered[medianIndex - 1].Ticks + ordered[medianIndex].Ticks) / 2;
            return TimeSpan.FromTicks(medianTicks);
        }

        private static string AppendSampleStats(string details, IList<TimeSpan> elapsedSamples)
        {
            if (elapsedSamples == null || elapsedSamples.Count <= 1)
            {
                return details;
            }

            double minMs = elapsedSamples.Min(sample => sample.TotalMilliseconds);
            double maxMs = elapsedSamples.Max(sample => sample.TotalMilliseconds);
            double medianMs = GetMedianElapsed(elapsedSamples).TotalMilliseconds;
            string stats =
                "Samples=" + elapsedSamples.Count +
                " MinMs=" + minMs.ToString("F2", CultureInfo.InvariantCulture) +
                " MedianMs=" + medianMs.ToString("F2", CultureInfo.InvariantCulture) +
                " MaxMs=" + maxMs.ToString("F2", CultureInfo.InvariantCulture) + ".";

            if (string.IsNullOrWhiteSpace(details))
            {
                return stats;
            }

            return details.TrimEnd() + " " + stats;
        }
    }
}