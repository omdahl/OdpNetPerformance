using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using OdpNetPerformance.Benchmarking;

namespace OdpNetPerformance.Oracle
{
    /// <summary>
    /// Provides shared Oracle benchmark helpers.
    /// </summary>
    public abstract class OracleBenchmarkBase : IPerformanceTest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OracleBenchmarkBase"/> class.
        /// </summary>
        protected OracleBenchmarkBase(BenchmarkContext context)
        {
            Context = context;
        }

        /// <inheritdoc/>
        public abstract string Name { get; }

        /// <summary>
        /// Gets the benchmark context.
        /// </summary>
        protected BenchmarkContext Context { get; private set; }

        /// <inheritdoc/>
        public async Task<IReadOnlyCollection<BenchmarkResult>> RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await ExecuteCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OracleException ex)
            {
                BenchmarkResult failure = CreateFailureResult(Name, ex.ToString());
                ReportResult(failure);
                return new[] { failure };
            }
            catch (Exception ex)
            {
                BenchmarkResult failure = CreateFailureResult(Name, ex.ToString());
                ReportResult(failure);
                return new[] { failure };
            }
        }

        /// <summary>
        /// Executes the benchmark implementation.
        /// </summary>
        protected abstract Task<IReadOnlyCollection<BenchmarkResult>> ExecuteCoreAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Creates an Oracle connection for benchmark use.
        /// </summary>
        protected OracleConnection CreateConnection()
        {
            return new OracleConnection(Context.ConnectionString);
        }

        /// <summary>
        /// Gets the validated Oracle table name.
        /// </summary>
        protected string TableName
        {
            get { return Context.TableName; }
        }

        /// <summary>
        /// Gets the allowlisted SELECT command text used by the fetch-size benchmark.
        /// </summary>
        protected string FetchSizeSelectCommandText
        {
            get { return "SELECT * FROM " + TableName + " WHERE ID <= :maxId ORDER BY ID"; }
        }

        /// <summary>
        /// Reports a benchmark result to the configured progress sink.
        /// </summary>
        protected void ReportResult(BenchmarkResult result)
        {
            if (Context.ResultReported != null)
            {
                Context.ResultReported(result);
            }
        }

        private static BenchmarkResult CreateFailureResult(string testName, string error)
        {
            return new BenchmarkResult
            {
                TestName = testName,
                Iterations = 0,
                Elapsed = TimeSpan.Zero,
                OperationsPerSecond = 0d,
                Details = "-",
                IsSuccess = false,
                ErrorMessage = error
            };
        }
    }
}