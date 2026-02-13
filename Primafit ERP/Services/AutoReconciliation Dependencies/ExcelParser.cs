using OfficeOpenXml;
using System.Globalization;

public sealed class ExcelStatementParser : IStatementParser
{
    public bool CanParse(string fileName, string? contentType)
        => fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

    public async Task<List<ParsedStatementRow>> ParseAsync(Stream stream)
    {
        

        using var package = new ExcelPackage();
        await package.LoadAsync(stream);

        var ws = package.Workbook.Worksheets.FirstOrDefault();
        if (ws == null || ws.Dimension == null) return new();

        int startRow = ws.Dimension.Start.Row;
        int endRow = ws.Dimension.End.Row;
        int startCol = ws.Dimension.Start.Column;
        int endCol = ws.Dimension.End.Column;

        // 1) Read header row (assume first row is header)
        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int c = startCol; c <= endCol; c++)
        {
            var header = ws.Cells[startRow, c].Text?.Trim();
            if (!string.IsNullOrWhiteSpace(header) && !headerMap.ContainsKey(header))
                headerMap[header] = c;
        }

        // 2) Helper to find any header name
        int? FindCol(params string[] names)
        {
            foreach (var n in names)
                if (headerMap.TryGetValue(n, out var col))
                    return col;
            return null;
        }

        var colDate = FindCol("Date", "TxnDate", "TransactionDate", "ValueDate", "PostingDate");
        var colAmount = FindCol("Amount", "Net", "Value", "Debit/Credit", "Withdrawal", "Deposit", "Credit", "Debit");
        var colRef = FindCol("Reference", "Ref", "Transaction Ref", "TransactionRef");
        var colDesc = FindCol("Description", "Narration", "Details", "Remark", "Remarks");

        // If essential columns are missing, bail with empty list (service will return "No valid transactions found")
        if (colDate == null || colAmount == null)
            return new();

        var rows = new List<ParsedStatementRow>();

        // 3) Parse data rows
        for (int r = startRow + 1; r <= endRow; r++)
        {
            var dateCell = ws.Cells[r, colDate.Value].Value;
            DateOnly date;


            var amountText = ws.Cells[r, colAmount.Value].Text?.Trim();

            var reference = colRef != null ? ws.Cells[r, colRef.Value].Text?.Trim() : null;
            var desc = colDesc != null ? ws.Cells[r, colDesc.Value].Text?.Trim() : null;

            if (dateCell is DateTime dt)
            {
                date = DateOnly.FromDateTime(dt);
            }
            else if (dateCell is double oa) // Excel serial date
            {
                date = DateOnly.FromDateTime(DateTime.FromOADate(oa));
            }
            else
            {
                var dateText = ws.Cells[r, colDate.Value].Text?.Trim();
                if (!TryDate(dateText, out date)) continue;
            }
            if (!TryAmount(amountText, out var amount)) continue;

            // If no Description column, fall back to Reference (matches your test file)
            var description = !string.IsNullOrWhiteSpace(desc) ? desc
                            : !string.IsNullOrWhiteSpace(reference) ? reference
                            : "Imported";

            rows.Add(new ParsedStatementRow
            {
                Date = date,
                Reference = reference,
                Description = description,
                Amount = amount
            });
        }

        return rows;
    }

    private static bool TryDate(string? s, out DateOnly d)
    {
        d = default;
        if (string.IsNullOrWhiteSpace(s)) return false;

        // EPPlus sometimes gives numeric dates as text depending on formatting, so try robust parsing
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
