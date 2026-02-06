using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class ReconciliationService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public ReconciliationService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        // 1. Initialize Reconciliation (Import Statement)
        public async Task<(string error, Guid? reconId)> ImportStatementAsync(
            Guid companyId, Guid bankAccountId, DateOnly statementDate, decimal endingBalance,
            Stream csvStream, string userId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            // Calculate Book Balance at that specific date (Snapshot)
            // Logic: Sum(Debits - Credits) for this account <= Date
            var bookBalance = await ctx.GLTransactions
                .Where(t => t.CompanyId == companyId && t.AccountId == bankAccountId && t.PostingDate <= statementDate)
                .SumAsync(t => t.Debit - t.Credit);

            var header = new BankReconciliation
            {
                CompanyId = companyId,
                BankAccountId = bankAccountId,
                StatementDate = statementDate,
                StatementEndingBalance = endingBalance,
                BookBalanceAtDate = bookBalance,
                PreparedByUserId = userId,
                Status = ReconStatus.Open
            };

            // Parse CSV
            using var reader = new StreamReader(csvStream);
            await reader.ReadLineAsync(); // Skip Header
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                var cols = line.Split(','); // Adjust delimeter based on CSV format
                if (cols.Length < 3) continue;

                // Expected CSV: Date, Description, Reference, Amount
                if (DateOnly.TryParse(cols[0], out var date) && decimal.TryParse(cols[3], out var amount))
                {
                    header.StatementLines.Add(new BankStatementLine
                    {
                        TransactionDate = date,
                        Description = cols[1],
                        Reference = cols[2],
                        Amount = amount,
                        IsCleared = false
                    });
                }
            }

            if (header.StatementLines.Count == 0) return ("CSV is empty or invalid.", null);

            ctx.BankReconciliations.Add(header);
            await ctx.SaveChangesAsync();
            return (string.Empty, header.Id);
        }

        // 2. Fetch Data for Workspace
        public async Task<ReconViewModel> GetReconWorkspaceAsync(Guid reconId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var header = await ctx.BankReconciliations
                .Include(r => r.StatementLines)
                .FirstOrDefaultAsync(r => r.Id == reconId);

            if (header == null) throw new Exception("Reconciliation not found.");

            // Fetch GL Transactions that are NOT reconciled yet
            // Condition: Same Account, Date <= Statement Date, IsReconciled = False
            // OR IsReconciled = True but linked to THIS reconciliation (so we can unmatch them)
            var glTxns = await ctx.GLTransactions
                .Where(t => t.CompanyId == header.CompanyId &&
                            t.AccountId == header.BankAccountId &&
                            t.PostingDate <= header.StatementDate &&
                            (!t.IsReconciled || t.BankReconciliationId == reconId))
                .ToListAsync();

            return new ReconViewModel
            {
                Header = header,
                OutstandingGLTransactions = glTxns.Where(t => !t.IsReconciled || t.BankReconciliationId == null).ToList()
            };
        }

        // 3. Auto-Match Function
        public async Task<int> RunAutoMatchAsync(Guid reconId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var header = await ctx.BankReconciliations
                .Include(r => r.StatementLines)
                .FirstOrDefaultAsync(r => r.Id == reconId);

            if (header == null) return 0;

            // Get Candidates
            var glCandidates = await ctx.GLTransactions
                .Where(t => t.CompanyId == header.CompanyId &&
                            t.AccountId == header.BankAccountId &&
                            !t.IsReconciled &&
                            t.PostingDate <= header.StatementDate)
                .ToListAsync();

            int matches = 0;

            foreach (var bankLine in header.StatementLines.Where(l => !l.IsCleared))
            {
                GLTransaction? match = null;

                // Priority A: Exact Amount + Exact Reference
                if (!string.IsNullOrEmpty(bankLine.Reference))
                {
                    match = glCandidates.FirstOrDefault(g =>
                        (g.Debit - g.Credit) == bankLine.Amount && // Amount match
                        (g.Narration.Contains(bankLine.Reference) || bankLine.Reference.Contains(g.Narration)) // Ref match
                    );
                }

                // Priority B: Exact Amount + Date Window (+/- 3 days)
                if (match == null)
                {
                    var minDate = bankLine.TransactionDate.AddDays(-3);
                    var maxDate = bankLine.TransactionDate.AddDays(3);

                    match = glCandidates.FirstOrDefault(g =>
                        (g.Debit - g.Credit) == bankLine.Amount &&
                        g.PostingDate >= minDate && g.PostingDate <= maxDate
                    );
                }

                if (match != null)
                {
                    // Execute Match
                    bankLine.IsCleared = true;
                    bankLine.MatchedGLTransactionId = match.Id;

                    match.IsReconciled = true;
                    match.BankReconciliationId = header.Id; // Link for audit trail

                    // Remove from candidates so it's not matched twice
                    glCandidates.Remove(match);
                    matches++;
                }
            }

            await ctx.SaveChangesAsync();
            return matches;
        }

        // 4. Manual Match / Unmatch
        public async Task ToggleMatchAsync(Guid bankLineId, Guid glTxnId, bool link)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var bankLine = await ctx.BankStatementLines.FindAsync(bankLineId);
            var glTxn = await ctx.GLTransactions.FindAsync(glTxnId);

            if (bankLine != null && glTxn != null)
            {
                if (link)
                {
                    bankLine.IsCleared = true;
                    bankLine.MatchedGLTransactionId = glTxn.Id;
                    glTxn.IsReconciled = true;
                    glTxn.BankReconciliationId = bankLine.ReconciliationId;
                }
                else
                {
                    bankLine.IsCleared = false;
                    bankLine.MatchedGLTransactionId = null;
                    glTxn.IsReconciled = false;
                    glTxn.BankReconciliationId = null;
                }
                await ctx.SaveChangesAsync();
            }
        }

        // 5. Finalize (The "Close" month action)
        public async Task<string> FinalizeAsync(Guid reconId, string approverId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var header = await ctx.BankReconciliations.FindAsync(reconId);

            if (header == null) return "Recon not found.";

            // RBAC Check: Maker != Checker
            if (header.PreparedByUserId == approverId)
                return "Security Constraint: The approver cannot be the same person who prepared the reconciliation.";

            header.Status = ReconStatus.Reconciled;
            header.ApprovedByUserId = approverId;
            header.ApprovedAt = DateTime.UtcNow;

            await ctx.SaveChangesAsync();
            return "Success";
        }
    }
}