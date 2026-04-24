using System;
using System.Collections.Generic;

namespace OdpNetPerformance.Benchmarking
{
    /// <summary>
    /// Writes benchmark results to the console in a structured format.
    /// </summary>
    public static class ConsoleReporter
    {
        /// <summary>
        /// Writes a line to the console.
        /// </summary>
        public static void WriteHeader(string text)
        {
            const string marker = "MedianElapsedMs=";

            if (string.IsNullOrEmpty(text))
            {
                Console.WriteLine(text);
                return;
            }

            int searchStart = 0;
            int markerIndex = text.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                Console.WriteLine(text);
                return;
            }

            while (markerIndex >= 0)
            {
                Console.Write(text.Substring(searchStart, markerIndex - searchStart));

                int valueStart = markerIndex + marker.Length;
                int valueEnd = text.IndexOf(';', valueStart);
                if (valueEnd < 0)
                {
                    valueEnd = text.Length;
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write(text.Substring(markerIndex, valueEnd - markerIndex));
                Console.ResetColor();

                searchStart = valueEnd;
                markerIndex = text.IndexOf(marker, searchStart, StringComparison.Ordinal);
            }

            if (searchStart < text.Length)
            {
                Console.Write(text.Substring(searchStart));
            }

            Console.WriteLine();
        }

        /// <summary>
        /// Writes a single benchmark result.
        /// </summary>
        public static void WriteResult(BenchmarkResult result)
        {
            Console.Write(
                "Test={0};Iterations={1};",
                result.TestName,
                result.Iterations);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("MedianElapsedMs={0:F2}", result.Elapsed.TotalMilliseconds);
            Console.ResetColor();

            Console.WriteLine(
                ";OpsPerSec={0:F2};Success={1};Details={2};Error={3}",
                result.OperationsPerSecond,
                result.IsSuccess,
                string.IsNullOrWhiteSpace(result.Details) ? "-" : result.Details,
                string.IsNullOrWhiteSpace(result.ErrorMessage) ? "-" : result.ErrorMessage);
        }

        /// <summary>
        /// Writes the supplied benchmark results.
        /// </summary>
        public static void WriteResults(IEnumerable<BenchmarkResult> results)
        {
            foreach (BenchmarkResult result in results)
            {
                WriteResult(result);
            }
        }
    }
}