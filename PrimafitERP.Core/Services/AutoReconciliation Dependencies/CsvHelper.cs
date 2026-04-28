using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

public sealed class CsvStatementParser : IStatementParser
{
    public bool CanParse(string fileName, string? contentType)
        => fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

    public async Task<List<ParsedStatementRow>> ParseAsync(Stream stream)
    {
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null,
            DetectColumnCountChanges = false
        });

        var rows = new List<ParsedStatementRow>();

        if (!await csv.ReadAsync()) return rows;
        csv.ReadHeader();

        while (await csv.ReadAsync())
        {
            var dateText = GetAny(csv, "Date", "TxnDate", "TransactionDate");
            var amountText = GetAny(csv, "Amount", "Debit", "Credit", "Net", "Value");
            var reference = GetAny(csv, "Reference", "Ref");
            var desc = GetAny(csv, "Description", "Narration", "Details");

            if (!TryDate(dateText, out var date)) continue;
            if (!TryAmount(amountText, out var amount)) continue;

            rows.Add(new ParsedStatementRow
            {
                Date = date,
                Reference = reference,
                Description = string.IsNullOrWhiteSpace(desc) ? reference : desc,
                Amount = amount
            });
        }

        return rows;
    }

    private static string? GetAny(CsvReader csv, params string[] names)
    {
        foreach (var n in names)
        {
            if (csv.TryGetField(n, out string? v) && !string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }
        return null;
    }

    private static bool TryDate(string? s, out DateOnly d)
    {
        d = default;
        if (string.IsNullOrWhiteSpace(s)) return false;

        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ||
            DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out dt))
        {
            d = DateOnly.FromDateTime(dt);
            return true;
        }

        return false;
    }

    private static bool TryAmount(string? s, out decimal amt)
    {
        amt = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;

        var cleaned = s.Trim().Replace(",", "").Replace(" ", "");

        var negParen = cleaned.StartsWith("(") && cleaned.EndsWith(")");
        if (negParen) cleaned = cleaned.Trim('(', ')');

        if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out amt) ||
            decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.CurrentCulture, out amt))
        {
            if (negParen) amt *= -1;
            return true;
        }

        return false;
    }
}
