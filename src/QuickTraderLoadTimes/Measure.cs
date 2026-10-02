using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace QuickTraderLoadTimes
{
    // No game or Unity types in this file: the test project compiles it directly.

    /// <summary>A bag of millisecond samples, summarised once at the end of a trader open.</summary>
    public sealed class Series
    {
        private readonly List<double> _values = new List<double>();

        public int Count => _values.Count;

        public double Sum
        {
            get
            {
                double sum = 0;
                foreach (double v in _values) sum += v;
                return sum;
            }
        }

        public double Max => _values.Count == 0 ? 0 : _values.Max();

        public double Mean => _values.Count == 0 ? 0 : Sum / _values.Count;

        public void Add(double value) => _values.Add(value);

        /// <summary>Nearest-rank percentile: always one of the samples, 0 when empty.</summary>
        public double Percentile(double p)
        {
            if (_values.Count == 0) return 0;
            List<double> sorted = new List<double>(_values);
            sorted.Sort();
            int rank = (int)Math.Ceiling(p / 100.0 * sorted.Count);
            rank = Math.Max(1, Math.Min(sorted.Count, rank));
            return sorted[rank - 1];
        }

        public int CountOver(double threshold)
        {
            int n = 0;
            foreach (double v in _values)
            {
                if (v > threshold) n++;
            }
            return n;
        }

        /// <summary>"n=12 sum 40.1 avg 3.3 p50 2.9 p95 9.0 max 11.2", or "n=0".</summary>
        public string Describe()
        {
            if (_values.Count == 0) return "n=0";
            return "n=" + Count
                + " sum " + Fmt.Ms(Sum)
                + " avg " + Fmt.Ms(Mean)
                + " p50 " + Fmt.Ms(Percentile(50))
                + " p95 " + Fmt.Ms(Percentile(95))
                + " max " + Fmt.Ms(Max);
        }
    }

    public static class Fmt
    {
        /// <summary>One decimal, invariant culture, so the CSV reads the same on any locale.</summary>
        public static string Ms(double ms) => ms.ToString("0.0", CultureInfo.InvariantCulture);

        /// <summary>A time that may never have happened: empty in the CSV, "never" in the log.</summary>
        public static string MaybeMs(double? ms, string missing = "") => ms.HasValue ? Ms(ms.Value) : missing;

        public static string Mb(long bytes) => (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture);

        public static string SignedMb(long bytes)
        {
            string s = Mb(Math.Abs(bytes));
            return (bytes < 0 ? "-" : "+") + s;
        }
    }

    public static class Csv
    {
        public static string Escape(string field)
        {
            if (field == null) return "";
            bool quote = field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            if (!quote) return field;
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }

        public static string Row(IEnumerable<string> fields)
        {
            StringBuilder sb = new StringBuilder();
            bool first = true;
            foreach (string f in fields)
            {
                if (!first) sb.Append(',');
                sb.Append(Escape(f));
                first = false;
            }
            return sb.ToString();
        }
    }
}
