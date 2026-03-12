public interface IStatementParser
{
    bool CanParse(string fileName, string? contentType);
    Task<List<ParsedStatementRow>> ParseAsync(Stream stream);
}

public sealed class ParsedStatementRow
{
    public Guid Id { get; set; }
    public DateOnly Date { get; set; }
    public string? Reference { get; set; }
    public string? Description { get; set; }
    public decimal Amount { get; set; }
}
