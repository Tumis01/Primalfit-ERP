using System.Globalization;

namespace Primafit_ERP.Components.Models.Reporting;

public enum ReportColumnKind { Text, Number, Integer, Percentage, Date, Boolean }

public sealed record ReportColumn(ReportColumnKind Kind, int DecimalPlaces = 4, bool IncludeInExports = true);

public static class ReportNumbers
{
    public static StandardReportData ForExport(StandardReportData data)
    {
        var indexes = Enumerable.Range(0, data.Headers.Count).Where(i => Column(data, i).IncludeInExports).ToList();
        if (indexes.Count == data.Headers.Count) return data;
        return new StandardReportData { ReportName = data.ReportName, CompanyName = data.CompanyName, Currency = data.Currency, ReportingPeriod = data.ReportingPeriod, GeneratedBy = data.GeneratedBy, DateGenerated = data.DateGenerated, Headers = indexes.Select(i => data.Headers[i]).ToList(), Columns = indexes.Select(i => Column(data, i)).ToList(), Rows = data.Rows.Select(r => indexes.Select(i => i < r.Count ? r[i] : "").ToList()).ToList() };
    }
    public static ReportColumn Column(StandardReportData data, int index)
    {
        if (index < data.Columns.Count) return data.Columns[index];
        var header = data.Headers[index];
        if (new[] { "%", "Percent" }.Any(x => header.Contains(x, StringComparison.OrdinalIgnoreCase))) return new(ReportColumnKind.Percentage, 2);
        if (header.Contains("(Days)", StringComparison.OrdinalIgnoreCase)) return new(ReportColumnKind.Integer, 0);
        if (new[] { "Days", "No. of", "Count", "Months", "Order" }.Any(x => header.Equals(x, StringComparison.OrdinalIgnoreCase) || header.StartsWith(x + " ", StringComparison.OrdinalIgnoreCase))
            && !header.Contains("Amount") && !header.Contains("Value") && !header.Contains("Discount") && !header.Contains("Tax")) return new(ReportColumnKind.Integer, 0);
        return new(new[] { "Amount", "Balance", "Value", "Debit", "Credit", "Total", "Qty", "Quantity", "Price", "Cost", "Budget", "Forecast", "Actuals", "Encumbered", "Committed", "Variance", "Remaining", "Money In", "Money Out", "Depr." }
            .Any(x => header.Contains(x, StringComparison.OrdinalIgnoreCase)) ? ReportColumnKind.Number : ReportColumnKind.Text);
    }

    public static bool TryParse(string text, out decimal value)
    {
        var cleaned = text.Trim().TrimEnd('%').Trim();
        const NumberStyles styles = NumberStyles.Number | NumberStyles.AllowParentheses | NumberStyles.AllowLeadingSign;
        return decimal.TryParse(cleaned, styles, CultureInfo.CurrentCulture, out value)
            || decimal.TryParse(cleaned, styles, CultureInfo.InvariantCulture, out value);
    }
}
