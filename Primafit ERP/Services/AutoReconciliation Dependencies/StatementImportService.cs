using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

public sealed class StatementImportService
{
    private readonly IEnumerable<IStatementParser> _parsers;

    public StatementImportService(IEnumerable<IStatementParser> parsers)
    {
        _parsers = parsers;
    }

    public async Task<List<ParsedStatementRow>> ParseAsync(
        string fileName,
        string? contentType,
        Stream stream)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidOperationException("File name is missing.");

        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        // EXTENSION-FIRST routing (prevents CSV from ever touching EPPlus)
        IStatementParser? parser = ext switch
        {
            ".csv" => _parsers.FirstOrDefault(p => p is CsvStatementParser),
            ".xlsx" => _parsers.FirstOrDefault(p => p is ExcelStatementParser),
            _ => null
        };

        if (parser == null)
            throw new InvalidOperationException($"Unsupported file type '{ext}'. Please upload CSV or XLSX.");

        Console.WriteLine($"[StatementImport] File: {fileName} | ContentType: {contentType} | Parser: {parser.GetType().Name}");

        // Ensure we start reading from beginning
        if (stream.CanSeek) stream.Position = 0;

        return await parser.ParseAsync(stream);
    }
}
