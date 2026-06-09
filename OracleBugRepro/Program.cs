using System;
using System.Configuration;
using System.Reflection;
using Oracle.ManagedDataAccess.Client;

namespace OracleBugRepro
{
    /// <summary>
    /// Standalone Oracle SR repro for OracleDataReader.GetChars.
    /// Uses only ODP.NET primitives (OracleConnection / OracleCommand / OracleDataReader).
    /// </summary>
    internal static class Program
    {
        private static readonly string TableName =
            "ODPNET_SR_" + Guid.NewGuid().ToString("N").Substring(0, 18).ToUpperInvariant();

        private static int Main()
        {
            try
            {
                EnsureConfigurationConfigured();

                string[] inputValues = BuildDefaultPatternValues(26);

                using (OracleConnection connection = OpenConnection())
                {
                    bool tableCreated = false;
                    bool executionFailed = false;

                    CreateTable(connection);
                    tableCreated = true;

                    try
                    {
                        InsertRows(connection, inputValues);
                        VerifyRows(connection, inputValues);
                    }
                    catch
                    {
                        executionFailed = true;
                        throw;
                    }
                    finally
                    {
                        if (tableCreated)
                        {
                            try
                            {
                                DropTableIfExists(connection);
                            }
                            catch (Exception cleanupException)
                            {
                                Console.Error.WriteLine("Cleanup failed:");
                                Console.Error.WriteLine(cleanupException.ToString());

                                if (!executionFailed)
                                {
                                    throw;
                                }
                            }
                        }
                    }
                }

                Console.WriteLine("Oracle SR repro completed: no mismatches detected.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Oracle SR repro failed.");
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static void EnsureConfigurationConfigured()
        {
            ConnectionStringSettings oracleDb = ConfigurationManager.ConnectionStrings["OracleDb"];
            if (oracleDb == null || string.IsNullOrWhiteSpace(oracleDb.ConnectionString))
            {
                throw new ConfigurationErrorsException("Connection string 'OracleDb' is missing or empty.");
            }
        }

        private static OracleConnection OpenConnection()
        {
            ConnectionStringSettings oracleDb = ConfigurationManager.ConnectionStrings["OracleDb"];
            if (oracleDb == null || string.IsNullOrWhiteSpace(oracleDb.ConnectionString))
            {
                throw new ConfigurationErrorsException("Connection string 'OracleDb' is missing or empty.");
            }

            var connection = new OracleConnection(oracleDb.ConnectionString);
            connection.Open();
            return connection;
        }

        private static void CreateTable(OracleConnection connection)
        {
            using (OracleCommand ddl = connection.CreateCommand())
            {
                ddl.CommandText = $"CREATE TABLE {TableName} (k NUMBER PRIMARY KEY, v VARCHAR2(50))";
                ddl.ExecuteNonQuery();
            }
        }

        private static string[] BuildDefaultPatternValues(int rowCount)
        {
            string[] dayNames = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
            string[] values = new string[rowCount];

            for (int idx = 0; idx < rowCount; idx++)
            {
                values[idx] = dayNames[(idx / 2) % 7];
            }

            return values;
        }

        private static void InsertRows(OracleConnection connection, string[] values)
        {
            for (int idx = 0; idx < values.Length; idx++)
            {
                string value = values[idx];

                using (OracleCommand insert = connection.CreateCommand())
                {
                    insert.CommandText = $"INSERT INTO {TableName} (k, v) VALUES (:k, :v)";
                    insert.Parameters.Add(new OracleParameter("k", idx));
                    insert.Parameters.Add(new OracleParameter("v", value));
                    insert.ExecuteNonQuery();
                }
            }

            using (OracleCommand commit = connection.CreateCommand())
            {
                commit.CommandText = "COMMIT";
                commit.ExecuteNonQuery();
            }
        }

        private static void VerifyRows(OracleConnection connection, string[] expectedValues)
        {
            char[] buffer = new char[50];

            using (OracleCommand select = connection.CreateCommand())
            {
                select.CommandText = $"SELECT k, v FROM {TableName} ORDER BY k";
                select.FetchSize = 40L * 1024 * 1024;

                using (OracleDataReader reader = select.ExecuteReader())
                {
                    int rowIndex = 0;
                    while (reader.Read())
                    {
                        _ = reader.GetDecimal(0);

                        Array.Clear(buffer, 0, buffer.Length);

                        string expectedValue = expectedValues[rowIndex];
                        int expectedLength = expectedValue.Length;
                        long charCount = reader.GetChars(1, 0, buffer, 0, buffer.Length);

                        if (charCount != expectedLength)
                        {
                            throw new InvalidOperationException(
                                charCount == 0
                                    ? $"Row {rowIndex}: GetChars(1) returned 0 while expected {expectedLength}. GetString(1) returned '{reader.GetString(1)}'."
                                    : $"Row {rowIndex}: expected {expectedLength}, actual {charCount}.");
                        }

                        if (charCount > 0)
                        {
                            string actualValue = new string(buffer, 0, (int)charCount);
                            if (!string.Equals(expectedValue, actualValue, StringComparison.Ordinal))
                            {
                                throw new InvalidOperationException(
                                    $"Row {rowIndex}: unexpected buffer content. Expected '{expectedValue}', actual '{actualValue}'.");
                            }
                        }

                        rowIndex++;
                    }

                    if (rowIndex != expectedValues.Length)
                    {
                        throw new InvalidOperationException($"Expected to read {expectedValues.Length} rows, actual {rowIndex}.");
                    }
                }
            }
        }

        private static void DropTableIfExists(OracleConnection connection)
        {
            try
            {
                using (OracleCommand drop = connection.CreateCommand())
                {
                    drop.CommandText = $"DROP TABLE {TableName} PURGE";
                    drop.ExecuteNonQuery();
                }
            }
            catch (OracleException ex) when (ex.Number == 942)
            {
                // ORA-00942: table or view does not exist.
            }
        }

        private static string GetOracleManagedDataAccessVersion()
        {
            AssemblyMetadataAttribute metadata = typeof(Program).Assembly.GetCustomAttribute<AssemblyMetadataAttribute>();
            return metadata == null || string.IsNullOrWhiteSpace(metadata.Value) ? "Unknown" : metadata.Value;
        }
    }
}