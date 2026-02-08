using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;
using System.Globalization;

namespace Primafit_ERP.Services
{
    public class ReconciliationService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly StatementImportService _statementImport;

        public ReconciliationService(
        IDbContextFactory<AppDbContext> dbFactory,
        StatementImportService statementImport)
        {
            _dbFactory = dbFactory;
            _statementImport = statementImport; 
        }

        // VIEW EXPECTS: (string err, Guid id)
        public async Task<(string err, Guid id)> StartReconciliationAsync(
             Guid companyId,
             Guid bankAccountId,
             DateOnly statementDate,
             decimal endingBalance,
             ReconType type,
             IBrowserFile? uploadedFile,
             string userId)
        {
            try
            {
                if (companyId == Guid.Empty) return ("Select a company.", Guid.Empty);
                if (bankAccountId == Guid.Empty) return ("Select a bank account.", Guid.Empty);

                if (type == ReconType.Automatic && uploadedFile == null)
                    return ("Upload a bank statement file.", Guid.Empty);

                await using var db = await _dbFactory.CreateDbContextAsync();

                // If a draft exists, UPDATE it to the latest ending balance (prevents old target balance reappearing)
                var existingDraft = await db.BankReconciliations
                    .Include(r => r.StatementLines)
                    .FirstOrDefaultAsync(r =>
                        r.CompanyId == companyId &&
                        r.BankAccountId == bankAccountId &&
                        r.StatementDate == statementDate &&
                        r.Status == ReconStatus.Draft);

                if (existingDraft != null)
                {
                    existingDraft.StatementEndingBalance = endingBalance;
                    existingDraft.Type = type;
                    existingDraft.PreparedByUserId = userId;

                    if (type == ReconType.Automatic && uploadedFile != null)
                    {
                        // Clear old lines and re-import
                        if (existingDraft.StatementLines.Count > 0)
                            db.BankStatementLines.RemoveRange(existingDraft.StatementLines);

                        await using var stream = uploadedFile.OpenReadStream(10 * 1024 * 1024);

                        var rows = await _statementImport.ParseAsync(uploadedFile.Name, uploadedFile.ContentType, stream);
                        if (rows.Count == 0)
                            return ("No valid rows detected. Ensure the file includes Date and Amount columns.", Guid.Empty);

                        foreach (var r in rows)
                        {
                            db.BankStatementLines.Add(new BankStatementLine
                            {
                                ReconciliationId = existingDraft.Id,
                                Date = r.Date,
                                Reference = r.Reference,
                                Description = r.Description ?? "Imported",
                                Amount = r.Amount,
                                IsMatched = false,
                                MatchedGLTransactionId = null
                            });
                        }
                    }

                    await db.SaveChangesAsync();
                    return ("", existingDraft.Id);
                }

                // Create new reconciliation
                var recon = new BankReconciliation
                {
                    CompanyId = companyId,
                    BankAccountId = bankAccountId,
                    StatementDate = statementDate,
                    StatementEndingBalance = endingBalance,
                    Type = type,
                    Status = ReconStatus.Draft,
                    PreparedByUserId = userId,
                    CreatedAt = DateTime.UtcNow
                };

                if (type == ReconType.Automatic)
                {
                    Console.WriteLine($"UPLOAD => Name: {uploadedFile!.Name} | Type: {uploadedFile.ContentType} | Size: {uploadedFile.Size}");

                    await using var stream = uploadedFile.OpenReadStream(10 * 1024 * 1024);

                    var rows = await _statementImport.ParseAsync(uploadedFile.Name, uploadedFile.ContentType, stream);
                    if (rows.Count == 0)
                        return ("No valid rows detected. Ensure the file includes Date and Amount columns.", Guid.Empty);

                    foreach (var r in rows)
                    {
                        recon.StatementLines.Add(new BankStatementLine
                        {
                            ReconciliationId = recon.Id,
                            Date = r.Date,
                            Reference = r.Reference,
                            Description = r.Description ?? "Imported",
                            Amount = r.Amount,
                            IsMatched = false,
                            MatchedGLTransactionId = null
                        });
                    }
                }

                db.BankReconciliations.Add(recon);
                await db.SaveChangesAsync();
                return ("", recon.Id);
            }
            catch (Exception ex)
            {
                return ($"Failed to start reconciliation: {ex.Message}", Guid.Empty);
            }
        }


        // VIEW EXPECTS: ReconViewModel?
        public async Task<ReconViewModel?> GetReconDataAsync(Guid companyId, Guid reconId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var recon = await db.BankReconciliations
                .Include(r => r.StatementLines)
                .FirstOrDefaultAsync(r => r.Id == reconId && r.CompanyId == companyId);

            if (recon == null) return null;

            // Candidate GL Txns:
            // show unreconciled OR reconciled to THIS recon
            var txns = await db.GLTransactions
                .Where(t =>
                    t.CompanyId == companyId &&
                    t.AccountId == recon.BankAccountId &&
                    (t.BankReconciliationId == null || t.BankReconciliationId == reconId) &&
                    t.PostingDate <= recon.StatementDate)
                .OrderBy(t => t.PostingDate)
                .ToListAsync();

            return new ReconViewModel
            {
                Reconciliation = recon,
                CandidateGLTransactions = txns
            };
        }

        // VIEW EXPECTS: ToggleCleared(txnId, reconId, isChecked)
        public async Task ToggleTransactionClearedAsync(Guid txnId, Guid reconId, bool isChecked)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var txn = await db.GLTransactions.FirstOrDefaultAsync(t => t.Id == txnId);
            if (txn == null) return;

            if (isChecked)
            {
                txn.BankReconciliationId = reconId;
            }
            else
            {
                if (txn.BankReconciliationId == reconId)
                    txn.BankReconciliationId = null;
            }

            await db.SaveChangesAsync();
        }

        // VIEW EXPECTS: AutoMatchAsync(companyId, reconId, tolerance)
        public async Task<int> AutoMatchAsync(Guid companyId, Guid reconId, decimal tolerance)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var recon = await db.BankReconciliations
                .Include(r => r.StatementLines)
                .FirstOrDefaultAsync(r => r.Id == reconId && r.CompanyId == companyId);

            if (recon == null) return 0;

            // make reconciliation flexible for any account type
            var graceDays = 10;        // allow postings slightly after statement date
            var dateWindowDays = 10;   // allowed diff between statement date and GL posting date

            // Get GL candidates on the SELECTED account (asset/bank/expense etc.)
            var gl = await db.GLTransactions
                .Where(t =>
                    t.CompanyId == companyId &&
                    t.AccountId == recon.BankAccountId &&
                    t.BankReconciliationId == null &&
                    t.PostingDate <= recon.StatementDate.AddDays(graceDays))
                .OrderBy(t => t.PostingDate)
                .ToListAsync();

            int matched = 0;

            foreach (var line in recon.StatementLines.Where(l => !l.IsMatched))
            {
                var stAmt = line.Amount;

                var match = gl.FirstOrDefault(t =>
                {
                    var glNet = (t.Debit - t.Credit);

                    var amountMatches =
                        Math.Abs(glNet - stAmt) <= tolerance
                        || Math.Abs(glNet + stAmt) <= tolerance
                        || Math.Abs(Math.Abs(glNet) - Math.Abs(stAmt)) <= tolerance;

                    var dateMatches =
                        Math.Abs(t.PostingDate.DayNumber - line.Date.DayNumber) <= dateWindowDays;

                    return amountMatches && dateMatches;
                });

                if (match == null) continue;

                match.BankReconciliationId = reconId;

                line.IsMatched = true;
                line.MatchedGLTransactionId = match.Id;

                gl.Remove(match);
                matched++;
            }

            await db.SaveChangesAsync();
            return matched;
        }

        // VIEW EXPECTS: string err
        public async Task<string> MatchManuallyAsync(Guid companyId, Guid statementLineId, Guid txnId)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();

                var line = await db.BankStatementLines.FirstOrDefaultAsync(l => l.Id == statementLineId);
                if (line == null) return "Statement line not found.";

                // Ensure the reconciliation belongs to the company (security check)
                var recon = await db.BankReconciliations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Id == line.ReconciliationId && r.CompanyId == companyId);

                if (recon == null) return "Reconciliation not found for this company.";

                var txn = await db.GLTransactions.FirstOrDefaultAsync(t => t.Id == txnId && t.CompanyId == companyId);
                if (txn == null) return "GL transaction not found.";

                if (line.IsMatched || line.MatchedGLTransactionId != null)
                    return "This statement line is already matched.";

                if (txn.BankReconciliationId != null)
                    return "This GL transaction is already reconciled.";

                txn.BankReconciliationId = line.ReconciliationId;

                line.IsMatched = true;
                line.MatchedGLTransactionId = txn.Id;

                await db.SaveChangesAsync();
                return "";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // VIEW EXPECTS: string (Success or message)
        public async Task<string> FinalizeReconciliationAsync(Guid companyId, Guid reconId)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();

                var recon = await db.BankReconciliations
                    .FirstOrDefaultAsync(r => r.Id == reconId && r.CompanyId == companyId);

                if (recon == null) return "Reconciliation not found.";

                recon.Status = ReconStatus.Finalized;
                recon.ApprovedAt = DateTime.UtcNow;

                await db.SaveChangesAsync();
                return "Success";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // ---------------- CSV Parsing ----------------
        // Expected (simple):
        // Date, Reference, Amount, Description(optional)
        private static async Task<List<CsvRow>> ParseCsvAsync(Stream stream)
        {
            using var reader = new StreamReader(stream);

            var rows = new List<CsvRow>();
            var all = await reader.ReadToEndAsync();

            var lines = all.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return rows;

            // auto-skip header if it looks like a header
            int start = 0;
            var first = lines[0].ToLowerInvariant();
            if (first.Contains("date") && first.Contains("amount"))
                start = 1;

            for (int i = start; i < lines.Length; i++)
            {
                var parts = lines[i].Split(',');
                if (parts.Length < 3) continue;

                if (!TryParseDateOnly(parts[0].Trim(), out var d)) continue;
                if (!TryParseDecimal(parts[2].Trim(), out var amt)) continue;

                rows.Add(new CsvRow
                {
                    Date = d,
                    Reference = parts[1].Trim(),
                    Amount = amt,
                    Description = parts.Length >= 4 ? parts[3].Trim() : "Imported"
                });
            }

            return rows;
        }

        private static bool TryParseDateOnly(string text, out DateOnly date)
        {
            var formats = new[]
            {
                "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "MM/dd/yyyy", "M/d/yyyy",
                "dd-MM-yyyy", "d-M-yyyy"
            };

            foreach (var f in formats)
            {
                if (DateTime.TryParseExact(text, f, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                {
                    date = DateOnly.FromDateTime(dt);
                    return true;
                }
            }

            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var any))
            {
                date = DateOnly.FromDateTime(any);
                return true;
            }

            date = default;
            return false;
        }

        private static bool TryParseDecimal(string text, out decimal value)
        {
            text = text.Trim();

            bool negParen = text.StartsWith("(") && text.EndsWith(")");
            if (negParen) text = text.Trim('(', ')');

            text = text.Replace(" ", "");

            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            {
                if (negParen) value *= -1;
                return true;
            }

            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value))
            {
                if (negParen) value *= -1;
                return true;
            }

            return false;
        }

        private sealed class CsvRow
        {
            public DateOnly Date { get; set; }
            public string? Reference { get; set; }
            public decimal Amount { get; set; }
            public string? Description { get; set; }
        }
    }
}
