using System;

namespace OdpNetPerformance.Benchmarking
{
    /// <summary>
    /// Represents the outcome of a benchmark execution.
    /// </summary>
    public sealed class BenchmarkResult
    {
        /// <summary>
        /// Gets or sets the benchmark name.
        /// </summary>
        public string TestName { get; set; }

        /// <summary>
        /// Gets or sets the number of iterations executed.
        /// </summary>
        public int Iterations { get; set; }

        /// <summary>
        /// Gets or sets the elapsed execution time.
        /// </summary>
        public TimeSpan Elapsed { get; set; }

        /// <summary>
        /// Gets or sets the calculated operations per second.
        /// </summary>
        public double OperationsPerSecond { get; set; }

        /// <summary>
        /// Gets or sets a details string for the result.
        /// </summary>
        public string Details { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the benchmark succeeded.
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// Gets or sets the error message when execution fails.
        /// </summary>
        public string ErrorMessage { get; set; }
    }
}