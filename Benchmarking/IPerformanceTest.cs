using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OdpNetPerformance.Benchmarking
{
    /// <summary>
    /// Defines a benchmark test that can produce one or more results.
    /// </summary>
    public interface IPerformanceTest
    {
        /// <summary>
        /// Gets the friendly benchmark name.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Executes the benchmark and returns the produced results.
        /// </summary>
        Task<IReadOnlyCollection<BenchmarkResult>> RunAsync(CancellationToken cancellationToken);
    }
}