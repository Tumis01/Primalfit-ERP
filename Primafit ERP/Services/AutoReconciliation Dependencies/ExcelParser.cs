using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.IO;

public sealed class ExcelStatementParser : IStatementParser
{
    public bool CanParse(string fileName, string? contentType)
        => fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

    public async Task<List<ParsedStatementRow>> ParseAsync(Stream stream)
    {
        // EPPlus license must be set at startup (Program.cs).
        // Do NOT set ExcelPackage.LicenseContext here (obsolete in EPPlus 8+).

        using var package = new ExcelPackage();
        await package.LoadAsync(stream);

        var ws = package.Workbook.Worksheets.FirstOrDefault();
        if (ws == null || ws.Dimension == null) return new();

        // Assume header row = 1
        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int c = 1; c <= ws.Dimension.End.Column; c++)
        {
            var h = ws.Cells[1, c].Text?.Trim();
            if (!string.IsNullOrWhiteSpace(h))
                headerMap[h] = c;
        }

        // ✅ FIXED: Returns -1 if none of the names exist
        int Col(params string[] names)
            => names.Select(n => headerMap.TryGetValue(n, out var idx) ? idx : -1)
                    .Where(x => x != -1)
                    .DefaultIfEmpty(-1)
                    .First();

        var dateCol = Col("Date", "TxnDate", "TransactionDate");
        var amtCol = Col("Amount", "Net", "Value");
        var refCol = Col("Reference", "Ref");
        var descCol = Col("Description", "Narration", "Details");

        // ✅ Now this works correctly
        if (dateCol == -1 || amtCol == -1) return new();

        var rows = new List<ParsedStatementRow>();

        for (int r = 2; r <= ws.Dimension.End.Row; r++)
        {
            var dateText = ws.Cells[r, dateCol].Text?.Trim();
            var amtText = ws.Cells[r, amtCol].Text?.Trim();

            if (string.IsNullOrWhiteSpace(dateText) || string.IsNullOrWhiteSpace(amtText))
                continue;

            var reference = refCol != -1 ? ws.Cells[r, refCol].Text?.Trim() : null;
            var desc = descCol != -1 ? ws.Cells[r, descCol].Text?.Trim() : null;

            if (!DateTime.TryParse(dateText, out var dt))
                continue;

            var cleaned = amtText.Replace(",", "").Replace(" ", "");
            var negParen = cleaned.StartsWith("(") && cleaned.EndsWith(")");
            if (negParen) cleaned = cleaned.Trim('(', ')');

            if (!decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var amt) &&
                !decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.CurrentCulture, out amt))
                continue;

            if (negParen) amt *= -1;

            rows.Add(new ParsedStatementRow
            {
                Date = DateOnly.FromDateTime(dt),
                Reference = reference,
                Description = string.IsNullOrWhiteSpace(desc) ? reference : desc,
                Amount = amt
            });
        }

        return rows;
    }
}
