using OdpNetPerformance.Configuration;

namespace OdpNetPerformance.Benchmarking
{
    /// <summary>
    /// Provides shared benchmark configuration and connection information.
    /// </summary>
    public sealed class BenchmarkContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BenchmarkContext"/> class.
        /// </summary>
        public BenchmarkContext(string connectionString, AppSettings settings)
        {
            ConnectionString = connectionString;
            Settings = settings;
            TableName = ValidateIdentifier(settings.TestTableName);
        }

        /// <summary>
        /// Gets the Oracle connection string.
        /// </summary>
        public string ConnectionString { get; private set; }

        /// <summary>
        /// Gets the benchmark settings.
        /// </summary>
        public AppSettings Settings { get; private set; }

        /// <summary>
        /// Gets the validated Oracle table name.
        /// </summary>
        public string TableName { get; private set; }

        /// <summary>
        /// Gets or sets the callback invoked when an individual benchmark result is produced.
        /// </summary>
        public System.Action<BenchmarkResult> ResultReported { get; set; }

        private static string ValidateIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                throw new System.Configuration.ConfigurationErrorsException("TestTableName is missing or empty.");
            }

            string trimmed = identifier.Trim().ToUpperInvariant();
            if (trimmed.Length > 27)
            {
                throw new System.Configuration.ConfigurationErrorsException("TestTableName exceeds the safe identifier length for generated Oracle objects.");
            }

            if (!char.IsLetter(trimmed[0]))
            {
                throw new System.Configuration.ConfigurationErrorsException("TestTableName must start with a letter.");
            }

            for (int index = 0; index < trimmed.Length; index++)
            {
                char current = trimmed[index];
                if (!(char.IsLetterOrDigit(current) || current == '_'))
                {
                    throw new System.Configuration.ConfigurationErrorsException("TestTableName contains invalid characters.");
                }
            }

            return trimmed;
        }
    }
}