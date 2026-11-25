using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenRCT.Core.Export
{
    public sealed class CsvColumn<T>
    {
        public string Header { get; }
        public Func<T, object?> Selector { get; }

        public CsvColumn(string header, Func<T, object?> selector)
        {
            Header = header;
            Selector = selector;
        }
    }

    /// <summary>
    /// RFC 4180-compliant CSV exporter with UTF-8 BOM for clean Microsoft Excel compatibility.
    /// </summary>
    public static class CsvExporter
    {
        public static string Serialize<T>(IEnumerable<T> rows, IReadOnlyList<CsvColumn<T>> columns)
        {
            if (columns == null) throw new ArgumentNullException(nameof(columns));

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", columns.Select(c => EscapeField(c.Header))));

            foreach (var row in rows ?? Enumerable.Empty<T>())
            {
                var fields = columns.Select(c => FormatValue(c.Selector(row)));
                sb.AppendLine(string.Join(",", fields));
            }

            return sb.ToString();
        }

        public static void WriteToFile<T>(string filePath, IEnumerable<T> rows, IReadOnlyList<CsvColumn<T>> columns)
        {
            var csv = Serialize(rows, columns);
            var utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            File.WriteAllText(filePath, csv, utf8WithBom);
        }

        public static string EscapeField(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var s = value!;
            bool mustQuote = s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            if (!mustQuote) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        private static string FormatValue(object? val)
        {
            if (val == null) return string.Empty;
            if (val is DateTime dt)
            {
                return EscapeField(dt.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            return EscapeField(val.ToString());
        }
    }
}
