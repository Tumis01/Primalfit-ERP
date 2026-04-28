//using Microsoft.EntityFrameworkCore;
//using PrimafitERP.Data;
//using Primafit_ERP.Components.Models;

//namespace Primafit_ERP.Services
//{
//    public class AccountsPayableService : IAccountsPayableService
//    {
//        private readonly IDbContextFactory<AppDbContext> _dbFactory;

//        public AccountsPayableService(IDbContextFactory<AppDbContext> dbFactory)
//        {
//            _dbFactory = dbFactory;
//        }

//        public async Task<List<VendorBill>> GetPendingBillsAsync(Guid companyId)
//        {
//            using var ctx = await _dbFactory.CreateDbContextAsync();
//            return await ctx.VendorBills
//                .Include(b => b.Vendor)
//                .Where(b => b.CompanyDetailsId == companyId && b.Status != BillStatus.Paid)
//                .OrderBy(b => b.DueDate)
//                .ToListAsync();
//        }

//        public async Task<VendorBill?> GetBillByIdAsync(Guid id)
//        {
//            using var ctx = await _dbFactory.CreateDbContextAsync();
//            return await ctx.VendorBills
//                .Include(b => b.Vendor)
//                .Include(b => b.Lines)
//                .FirstOrDefaultAsync(b => b.Id == id);
//        }

//        public async Task<string> SaveBillDraftAsync(VendorBill bill)
//        {
//            using var ctx = await _dbFactory.CreateDbContextAsync();

//            // 1. Check if bill exists in DB
//            var exists = await ctx.VendorBills.AnyAsync(b => b.Id == bill.Id);

//            if (!exists)
//            {
//                // --- NEW BILL ---
//                if (bill.Id == Guid.Empty) bill.Id = Guid.NewGuid();

//                // Link lines
//                foreach (var line in bill.Lines)
//                {
//                    line.VendorBillId = bill.Id;
//                    if (line.Id == Guid.Empty) line.Id = Guid.NewGuid();
//                }

//                bill.TotalAmount = bill.Lines.Sum(l => l.LineTotal);
//                ctx.VendorBills.Add(bill);
//            }
//            else
//            {
//                // --- UPDATE EXISTING ---
//                var existing = await ctx.VendorBills
//                    .Include(b => b.Lines)
//                    .FirstOrDefaultAsync(b => b.Id == bill.Id);

//                if (existing == null) return "Bill not found.";
//                if (existing.Status != BillStatus.Draft) return "Cannot edit a posted bill.";

//                // Update Header
//                existing.VendorId = bill.VendorId;
//                existing.ExternalInvoiceNumber = bill.ExternalInvoiceNumber;
//                existing.BillDate = bill.BillDate;
//                existing.DueDate = bill.DueDate;
//                existing.CurrencyId = bill.CurrencyId;
//                existing.ExchangeRate = bill.ExchangeRate;

//                // Recalculate Total
//                existing.TotalAmount = bill.Lines.Sum(l => l.LineTotal);

//                // Update Lines: Remove Old -> Add New
//                ctx.VendorBillLines.RemoveRange(existing.Lines);

//                foreach (var line in bill.Lines)
//                {
//                    var newLine = new VendorBillLine
//                    {
//                        Id = Guid.NewGuid(),
//                        VendorBillId = existing.Id,
//                        // ItemId = line.ItemId, <--- REMOVED THIS
//                        Description = line.Description,
//                        ExpenseGlAccountId = line.ExpenseGlAccountId,
//                        Quantity = line.Quantity,
//                        UnitCost = line.UnitCost
//                    };
//                    ctx.VendorBillLines.Add(newLine);
//                }
//            }

//            try
//            {
//                await ctx.SaveChangesAsync();
//                return "";
//            }
//            catch (Exception ex)
//            {
//                return $"Error saving bill: {ex.Message}";
//            }
//        }
//        public async Task<string> PostVendorBillAsync(Guid billId, string userId)
//        {
//            using var ctx = await _dbFactory.CreateDbContextAsync();
//            using var transaction = await ctx.Database.BeginTransactionAsync();

//            try
//            {
//                var bill = await ctx.VendorBills
//                    .Include(b => b.Lines)
//                    .Include(b => b.Vendor)
//                    .FirstOrDefaultAsync(b => b.Id == billId);

//                if (bill == null) return "Bill not found.";
//                if (bill.Status != BillStatus.Draft) return "Bill is already processed.";
//                if (!bill.Lines.Any()) return "Bill has no lines.";

//                // 1. Get Accounts
//                var apControlId = await GetSystemAccountId(ctx, bill.CompanyDetailsId, "Accounts Payable");
//                if (apControlId == Guid.Empty) return "System Error: 'Accounts Payable' control account missing.";

//                // 2. Find Period
//                var postDate = DateOnly.FromDateTime(bill.BillDate);
//                var period = await ctx.AccountingPeriods
//                     .FirstOrDefaultAsync(p => p.CompanyId == bill.CompanyDetailsId
//                                            && p.StartDate <= postDate
//                                            && p.EndDate >= postDate);

//                if (period == null || period.IsClosed) return "Accounting period is closed or missing.";

//                // 3. Create Batch & Journal
//                var batch = new GLBatch
//                {
//                    CompanyId = bill.CompanyDetailsId,
//                    AccountingPeriodId = period.Id,
//                    BatchName = $"BILL-{bill.Vendor?.Name}-{DateTime.Now:yyyyMMdd}",
//                    Status = BatchStatus.Posted,
//                    CreatedByUserId = userId,
//                    PostedByUserId = userId,
//                    PostedAt = DateTime.UtcNow
//                };
//                ctx.GLBatches.Add(batch);

//                var journal = new GLJournalHeader
//                {
//                    CompanyId = bill.CompanyDetailsId,
//                    AccountingPeriodId = period.Id,
//                    BatchId = batch.Id,
//                    JournalNumber = $"APB-{bill.Id.ToString().Substring(0, 8).ToUpper()}",
//                    TransactionDate = postDate,
//                    Narration = $"Vendor Bill: {bill.ExternalInvoiceNumber}",
//                    Status = JournalStatus.Posted
//                };
//                ctx.GLJournalHeaders.Add(journal);
//                await ctx.SaveChangesAsync();

//                // 4. Generate Lines (Explicit Execution)
//                var glLines = new List<GLTransaction>();
//                decimal totalLiability = 0;

//                // A. DEBITS (Expenses/Assets)
//                foreach (var line in bill.Lines)
//                {
//                    if (line.ExpenseGlAccountId == Guid.Empty) return "Line item missing Expense Account.";

//                    decimal lineBaseAmount = line.LineTotal * bill.ExchangeRate;
//                    totalLiability += lineBaseAmount;

//                    glLines.Add(new GLTransaction
//                    {
//                        CompanyId = bill.CompanyDetailsId,
//                        AccountingPeriodId = period.Id,
//                        BatchId = batch.Id,
//                        JournalId = journal.Id,
//                        AccountId = line.ExpenseGlAccountId,
//                        PostingDate = postDate,
//                        Debit = lineBaseAmount,
//                        Credit = 0,
//                        Narration = line.Description
//                    });
//                }

//                // B. CREDIT (Accounts Payable Liability)
//                glLines.Add(new GLTransaction
//                {
//                    CompanyId = bill.CompanyDetailsId,
//                    AccountingPeriodId = period.Id,
//                    BatchId = batch.Id,
//                    JournalId = journal.Id,
//                    AccountId = apControlId,
//                    PostingDate = postDate,
//                    Debit = 0,
//                    Credit = totalLiability,
//                    Narration = $"Liability for Inv {bill.ExternalInvoiceNumber}"
//                });

//                ctx.GLTransactions.AddRange(glLines);

//                // 5. Update Status
//                bill.Status = BillStatus.Approved; // Ready for payment
//                await ctx.SaveChangesAsync();
//                await transaction.CommitAsync();

//                return "";
//            }
//            catch (Exception ex)
//            {
//                await transaction.RollbackAsync();
//                return $"Posting failed: {ex.Message}";
//            }
//        }

//        // --- EVENT 2: PAY BILL (Discharge Liability) ---
//        public async Task<string> PayVendorBillAsync(Guid billId, Guid bankAccountId, decimal amountPaid, DateTime paymentDate, string userId)
//        {
//            using var ctx = await _dbFactory.CreateDbContextAsync();
//            using var transaction = await ctx.Database.BeginTransactionAsync();

//            try
//            {
//                var bill = await ctx.VendorBills
//                    .Include(b => b.Vendor)
//                    .FirstOrDefaultAsync(b => b.Id == billId);

//                if (bill == null) return "Bill not found.";
//                if (bill.Status == BillStatus.Paid) return "Bill already paid.";

//                // Note: In a real app, you'd check if Partial Payment is allowed. 
//                // For this logic, we assume full payment or manual calculation.

//                // 1. Get Accounts
//                var apControlId = await GetSystemAccountId(ctx, bill.CompanyDetailsId, "Accounts Payable");
//                var fxGainLossId = await GetSystemAccountId(ctx, bill.CompanyDetailsId, "Exchange Gain/Loss");

//                // 2. Find Period for PAYMENT DATE
//                var payDateOnly = DateOnly.FromDateTime(paymentDate);
//                var period = await ctx.AccountingPeriods
//                     .FirstOrDefaultAsync(p => p.CompanyId == bill.CompanyDetailsId
//                                            && p.StartDate <= payDateOnly
//                                            && p.EndDate >= payDateOnly);

//                if (period == null || period.IsClosed) return "Payment date falls in a closed period.";

//                // 3. Create Journal for Payment
//                var batch = new GLBatch
//                {
//                    CompanyId = bill.CompanyDetailsId,
//                    AccountingPeriodId = period.Id,
//                    BatchName = $"PAY-{bill.Vendor?.Name}-{DateTime.Now:yyyyMMdd}",
//                    Status = BatchStatus.Posted,
//                    CreatedByUserId = userId,
//                    PostedByUserId = userId,
//                    PostedAt = DateTime.UtcNow
//                };
//                ctx.GLBatches.Add(batch);

//                var journal = new GLJournalHeader
//                {
//                    CompanyId = bill.CompanyDetailsId,
//                    AccountingPeriodId = period.Id,
//                    BatchId = batch.Id,
//                    JournalNumber = $"APP-{Guid.NewGuid().ToString().Substring(0, 8)}",
//                    TransactionDate = payDateOnly,
//                    Narration = $"Payment for Bill {bill.ExternalInvoiceNumber}",
//                    Status = JournalStatus.Posted
//                };
//                ctx.GLJournalHeaders.Add(journal);
//                await ctx.SaveChangesAsync();

//                // 4. Generate GL Lines
//                var glLines = new List<GLTransaction>();

//                // A. DEBIT AP (Reduce Liability)
//                // IMPORTANT: We must debit the AP account using the ORIGINAL Bill Exchange Rate
//                // to completely zero out the liability created in Step 1.
//                decimal liabilityReliefBase = amountPaid * bill.ExchangeRate;

//                glLines.Add(new GLTransaction
//                {
//                    CompanyId = bill.CompanyDetailsId,
//                    AccountingPeriodId = period.Id,
//                    BatchId = batch.Id,
//                    JournalId = journal.Id,
//                    AccountId = apControlId,
//                    PostingDate = payDateOnly,
//                    Debit = liabilityReliefBase,
//                    Credit = 0,
//                    Narration = $"Clear Liability Inv {bill.ExternalInvoiceNumber}"
//                });

                
//                decimal paymentRate = bill.ExchangeRate; // Replace with actual Spot Rate if available
//                decimal cashOutflowBase = amountPaid * paymentRate;

//                glLines.Add(new GLTransaction
//                {
//                    CompanyId = bill.CompanyDetailsId,
//                    AccountingPeriodId = period.Id,
//                    BatchId = batch.Id,
//                    JournalId = journal.Id,
//                    AccountId = bankAccountId,
//                    PostingDate = payDateOnly,
//                    Debit = 0,
//                    Credit = cashOutflowBase,
//                    Narration = "Bank Transfer"
//                });

//                // C. FX DIFFERENCE
//                decimal diff = liabilityReliefBase - cashOutflowBase;
//                if (diff != 0 && fxGainLossId != Guid.Empty)
//                {
//                    bool isGain = diff > 0; // Liability was 100, we paid 90 -> Gain
//                    glLines.Add(new GLTransaction
//                    {
//                        CompanyId = bill.CompanyDetailsId,
//                        AccountingPeriodId = period.Id,
//                        BatchId = batch.Id,
//                        JournalId = journal.Id,
//                        AccountId = fxGainLossId,
//                        PostingDate = payDateOnly,
//                        Debit = isGain ? 0 : Math.Abs(diff),
//                        Credit = isGain ? Math.Abs(diff) : 0,
//                        Narration = "Realized Exchange Variance"
//                    });
//                }

//                ctx.GLTransactions.AddRange(glLines);

//                // 5. Close Bill
//                bill.Status = BillStatus.Paid;
//                await ctx.SaveChangesAsync();
//                await transaction.CommitAsync();

//                return "";
//            }
//            catch (Exception ex)
//            {
//                await transaction.RollbackAsync();
//                return $"Payment failed: {ex.Message}";
//            }
//        }

//        private async Task<Guid> GetSystemAccountId(AppDbContext ctx, Guid companyId, string partialName)
//        {
//            var acc = await ctx.GLChartOfAccounts
//                .Where(a => a.CompanyId == companyId && a.AccountName.Contains(partialName))
//                .FirstOrDefaultAsync();
//            return acc?.Id ?? Guid.Empty;
//        }
//    }
//}