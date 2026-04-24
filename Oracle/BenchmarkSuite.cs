using System.Collections.Generic;
using OdpNetPerformance.Benchmarking;

namespace OdpNetPerformance.Oracle
{
    /// <summary>
    /// Creates the benchmark suite for Oracle performance testing.
    /// </summary>
    public static class BenchmarkSuite
    {
        /// <summary>
        /// Creates all benchmark tests.
        /// </summary>
        public static IReadOnlyCollection<IPerformanceTest> Create(BenchmarkContext context)
        {
            return new IPerformanceTest[]
            {
                new FetchSizePerformanceTest(context),
            };
        }
    }
}

