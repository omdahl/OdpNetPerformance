using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using OdpNetPerformance.Benchmarking;

namespace OdpNetPerformance.Oracle
{
    /// <summary>
    /// Ensures required Oracle schema objects exist for the benchmark suite.
    /// </summary>
    public sealed class SchemaInitializer
    {
        private readonly BenchmarkContext context;

        /// <summary>
        /// Initializes a new instance of the <see cref="SchemaInitializer"/> class.
        /// </summary>
        public SchemaInitializer(BenchmarkContext context)
        {
            this.context = context;
        }

        /// <summary>
        /// Recreates the benchmark table and optionally seeds deterministic benchmark rows.
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            using (var connection = new OracleConnection(context.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                using (var dropCommand = connection.CreateCommand())
                {
                    dropCommand.BindByName = true;
                    dropCommand.CommandText = BuildDropTableBlock(context.TableName);
                    await dropCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                using (var createCommand = connection.CreateCommand())
                {
                    createCommand.BindByName = true;
                    createCommand.CommandText = BuildCreateTableSql(context.TableName);
                    await createCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                if (context.Settings.SeedBenchmarkData)
                {
                    await SeedBenchmarkRowsAsync(connection, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async Task SeedBenchmarkRowsAsync(OracleConnection connection, CancellationToken cancellationToken)
        {
            int rowCount = System.Math.Max(context.Settings.BenchmarkRowCount, context.Settings.LargeFetchRowCount);
            int batchSize = context.Settings.SeedBatchSize;

            using (var transaction = connection.BeginTransaction())
            {
                int nextId = 1;
                var createdAt = new System.DateTime(2020, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);

                while (nextId <= rowCount)
                {
                    int currentBatchSize = System.Math.Min(batchSize, rowCount - nextId + 1);
                    int[] ids = new int[currentBatchSize];
                    string[] names = new string[currentBatchSize];
                    int[] values = new int[currentBatchSize];
                    System.DateTime[] createdAtValues = new System.DateTime[currentBatchSize];

                    for (int index = 0; index < currentBatchSize; index++)
                    {
                        int id = nextId++;
                        ids[index] = id;
                        names[index] = "seed-" + id.ToString("D8");
                        values[index] = id;
                        createdAtValues[index] = createdAt;
                    }

                    using (var insertCommand = connection.CreateCommand())
                    {
                        insertCommand.BindByName = true;
                        insertCommand.Transaction = transaction;
                        insertCommand.ArrayBindCount = currentBatchSize;
                        insertCommand.CommandText = "INSERT INTO " + context.TableName + " (ID, NAME, VALUE, CREATED_AT) VALUES (:id, :name, :value, :createdAt)";
                        insertCommand.Parameters.Add(new OracleParameter("id", ids));
                        insertCommand.Parameters.Add(new OracleParameter("name", names));
                        insertCommand.Parameters.Add(new OracleParameter("value", values));
                        insertCommand.Parameters.Add(new OracleParameter("createdAt", createdAtValues));
                        await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                }

                transaction.Commit();
            }
        }

        private static string BuildDropTableBlock(string tableName)
        {
            return "BEGIN EXECUTE IMMEDIATE 'DROP TABLE " + tableName + " CASCADE CONSTRAINTS PURGE'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -942 THEN RAISE; END IF; END;";
        }

        private static string BuildCreateTableSql(string tableName)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("CREATE TABLE ");
            builder.Append(tableName);
            builder.Append(" (");
            builder.Append("ID NUMBER(10) NOT NULL, ");
            builder.Append("NAME VARCHAR2(100) NOT NULL, ");
            builder.Append("VALUE NUMBER(10) NOT NULL, ");
            builder.Append("CREATED_AT DATE NOT NULL");

            for (int index = 1; index <= 296; index++)
            {
                builder.Append(", F");
                builder.Append(index.ToString("D3"));
                builder.Append(" VARCHAR2(16) DEFAULT 'WIDEVAL' NOT NULL");
            }

            builder.Append(", CONSTRAINT PK_");
            builder.Append(tableName);
            builder.Append(" PRIMARY KEY (ID))");
            return builder.ToString();
        }
    }
}