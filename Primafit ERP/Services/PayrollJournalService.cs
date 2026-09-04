using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class PayrollJournalService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly GLOperationsService _glOps;

        public PayrollJournalService(IDbContextFactory<AppDbContext> dbFactory, GLOperationsService glOps)
        {
            _dbFactory = dbFactory;
            _glOps = glOps;
        }

        // 1. ACCRUAL: Recognize the expense and liabilities
        public async Task<string> ApproveAndPostPayrollAsync(Guid runId, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var tx = await ctx.Database.BeginTransactionAsync();

            try
            {
                var run = await ctx.PayrollRuns.Include(r => r.PayrollItems).FirstOrDefaultAsync(r => r.Id == runId);
                if (run == null) return "Payroll run not found.";
                if (run.Status != PayrollRunStatus.Draft)
                {
                    var existingBatch = run.GLBatchId.HasValue
                        ? await ctx.GLBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == run.GLBatchId.Value && b.CompanyId == run.CompanyId)
                        : null;
                    if (run.Status != PayrollRunStatus.Approved || existingBatch?.Status != BatchStatus.Rejected)
                        return existingBatch?.Status == BatchStatus.Posted
                            ? "This payroll has already been committed to the General Ledger."
                            : "Only Draft payrolls or rejected payroll batches can be submitted.";
                }

                var settings = await ctx.PayrollSettings.FirstOrDefaultAsync(s => s.CompanyId == run.CompanyId);
                if (settings == null) return "STOP: Missing GL Configuration. Please map Payroll GL Accounts in settings.";

                var glLines = new List<GLJournalLine>();

                // --- 1. CALCULATE EXACT CREDITS FROM LINE ITEMS ---
                decimal totalPaye = run.PayrollItems.Sum(i => i.PAYETax);
                decimal totalPension = run.PayrollItems.Sum(i => i.EmployeePension + i.EmployerPension);
                decimal totalOtherDeductions = run.PayrollItems.Sum(i => i.OtherDeductions);
                decimal totalNetPay = run.PayrollItems.Sum(i => i.NetPay);

                // --- 2. CALCULATE EXACT DEBIT ---
                decimal totalExpense = totalPaye + totalPension + totalOtherDeductions + totalNetPay;

                // Dr: Salaries Expense
                if (totalExpense > 0)
                    glLines.Add(new GLJournalLine { SegCoaId = settings.SalariesExpenseAccountId, Debit = totalExpense, Credit = 0, Reference = $"Payroll Expense: {run.Period}" });

                // Cr: PAYE Payable (Liability)
                if (totalPaye > 0)
                    glLines.Add(new GLJournalLine { SegCoaId = settings.PAYEPayableAccountId, Debit = 0, Credit = totalPaye, Reference = $"PAYE Liability: {run.Period}" });

                // Cr: Pension Payable (Liability)
                if (totalPension > 0)
                    glLines.Add(new GLJournalLine { SegCoaId = settings.PensionPayableAccountId, Debit = 0, Credit = totalPension, Reference = $"Pension Liability: {run.Period}" });

                // Cr: Other Deductions
                if (totalOtherDeductions > 0)
                    glLines.Add(new GLJournalLine { SegCoaId = settings.SalariesExpenseAccountId, Debit = 0, Credit = totalOtherDeductions, Reference = $"Other Deductions Withheld: {run.Period}" });

                // Cr: Salaries Payable (Net Pay Liability)
                if (totalNetPay > 0)
                    glLines.Add(new GLJournalLine { SegCoaId = settings.SalariesPayableAccountId, Debit = 0, Credit = totalNetPay, Reference = $"Net Pay Liability: {run.Period}" });

                // Post to GL Engine
                var (err, batchId) = await _glOps.CreateJournalEntryAsync(run.CompanyId, DateOnly.FromDateTime(run.RunDate), "Payroll Accrual", $"PR-{run.Period}", glLines, userId, existingBatchId: run.GLBatchId);
                if (!string.IsNullOrEmpty(err)) throw new Exception(err);

                if (batchId.HasValue)
                {
                    var postErr = await _glOps.PostBatchAsync(run.CompanyId, batchId.Value, userId);
                    if (!string.IsNullOrEmpty(postErr)) throw new Exception($"GL Post Error: {postErr}");
                    run.GLBatchId = batchId;
                }

                run.Status = PayrollRunStatus.Approved;
                await ctx.SaveChangesAsync();
                await tx.CommitAsync();

                return string.Empty;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return $"Accounting Error: {ex.Message}";
            }
        }

        // 2. DISBURSEMENT: Pay the employees
        public async Task<string> DisburseSalariesAsync(Guid runId, Guid bankAccountId, DateTime paymentDate, string reference, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var tx = await ctx.Database.BeginTransactionAsync();

            try
            {
                var run = await ctx.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId);
                if (run == null) return "Payroll run not found.";
                if (run.Status != PayrollRunStatus.Approved && run.Status != PayrollRunStatus.Paid)
                    return "Payroll must be Approved before disbursement.";
                if (run.DisbursementGLBatchId.HasValue)
                {
                    var existingBatch = await ctx.GLBatches.AsNoTracking()
                        .FirstOrDefaultAsync(b => b.Id == run.DisbursementGLBatchId.Value && b.CompanyId == run.CompanyId);
                    if (existingBatch?.Status != BatchStatus.Rejected)
                        return existingBatch?.Status == BatchStatus.Posted
                            ? "Salaries for this period have already been committed to the General Ledger."
                            : "Salary disbursement is already awaiting review.";
                }

                var settings = await ctx.PayrollSettings.FirstOrDefaultAsync(s => s.CompanyId == run.CompanyId);

                var glLines = new List<GLJournalLine>
                {
                    // Dr: Salaries Payable (Clearing the liability)
                    new() { SegCoaId = settings!.SalariesPayableAccountId, Debit = run.TotalNetPay, Credit = 0, Reference = $"Salary Payout: {run.Period}" },
                    // Cr: Bank Account (Cash outflow)
                    new() { SegCoaId = bankAccountId, Debit = 0, Credit = run.TotalNetPay, Reference = $"Salary Payout: {run.Period} - {reference}" }
                };

                var (err, batchId) = await _glOps.CreateJournalEntryAsync(run.CompanyId, DateOnly.FromDateTime(paymentDate), "Salary Disbursement", reference, glLines, userId, existingBatchId: run.DisbursementGLBatchId);
                if (!string.IsNullOrEmpty(err)) throw new Exception(err);

                if (batchId.HasValue)
                {
                    var postErr = await _glOps.PostBatchAsync(run.CompanyId, batchId.Value, userId);
                    if (!string.IsNullOrEmpty(postErr)) throw new Exception($"GL Post Error: {postErr}");

                    run.DisbursementGLBatchId = batchId;
                    run.Status = PayrollRunStatus.Paid; // Mark as Paid
                }

                await ctx.SaveChangesAsync();
                await tx.CommitAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return $"Disbursement Error: {ex.Message}";
            }
        }

        // 3. REMITTANCE: Pay Taxes/Pension (Added userId to signature)
        public async Task<string> RemitStatutoryAsync(Guid runId, Guid bankAccountId, DateTime paymentDate, string reference, bool isPaye, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var tx = await ctx.Database.BeginTransactionAsync();

            try
            {
                var run = await ctx.PayrollRuns.Include(r => r.PayrollItems).FirstOrDefaultAsync(r => r.Id == runId);
                if (run == null) return "Payroll run not found.";
                if (run.Status == PayrollRunStatus.Draft) return "Payroll must be Approved before remitting taxes.";

                var settings = await ctx.PayrollSettings.FirstOrDefaultAsync(s => s.CompanyId == run.CompanyId);

                decimal amount = 0;
                Guid liabilityAccountId = Guid.Empty;
                string typeName = isPaye ? "PAYE" : "Pension";

                if (isPaye)
                {
                    if (run.IsPayeRemitted)
                    {
                        var existingBatch = run.PayeRemittanceGLBatchId.HasValue
                            ? await ctx.GLBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == run.PayeRemittanceGLBatchId.Value && b.CompanyId == run.CompanyId)
                            : null;
                        if (existingBatch?.Status != BatchStatus.Rejected)
                            return existingBatch?.Status == BatchStatus.Posted ? "PAYE has already been committed to the General Ledger." : "PAYE remittance is already awaiting review.";
                    }
                    amount = run.PayrollItems.Sum(i => i.PAYETax);
                    liabilityAccountId = settings!.PAYEPayableAccountId;
                }
                else
                {
                    if (run.IsPensionRemitted)
                    {
                        var existingBatch = run.PensionRemittanceGLBatchId.HasValue
                            ? await ctx.GLBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == run.PensionRemittanceGLBatchId.Value && b.CompanyId == run.CompanyId)
                            : null;
                        if (existingBatch?.Status != BatchStatus.Rejected)
                            return existingBatch?.Status == BatchStatus.Posted ? "Pension has already been committed to the General Ledger." : "Pension remittance is already awaiting review.";
                    }
                    amount = run.PayrollItems.Sum(i => i.EmployeePension + i.EmployerPension);
                    liabilityAccountId = settings!.PensionPayableAccountId;
                }

                if (amount <= 0) return $"No {typeName} liability to remit.";

                var glLines = new List<GLJournalLine>
                {
                    // Dr: Statutory Payable (Clearing liability)
                    new() { SegCoaId = liabilityAccountId, Debit = amount, Credit = 0, Reference = $"{typeName} Remittance: {run.Period}" },
                    // Cr: Bank Account (Cash outflow)
                    new() { SegCoaId = bankAccountId, Debit = 0, Credit = amount, Reference = $"{typeName} Remittance: {run.Period} - {reference}" }
                };

                var existingRemittanceBatchId = isPaye ? run.PayeRemittanceGLBatchId : run.PensionRemittanceGLBatchId;
                var (err, batchId) = await _glOps.CreateJournalEntryAsync(run.CompanyId, DateOnly.FromDateTime(paymentDate), "Statutory Remittance", reference, glLines, userId, existingBatchId: existingRemittanceBatchId);
                if (!string.IsNullOrEmpty(err)) throw new Exception(err);

                if (batchId.HasValue)
                {
                    var postErr = await _glOps.PostBatchAsync(run.CompanyId, batchId.Value, userId);
                    if (!string.IsNullOrEmpty(postErr)) throw new Exception($"GL Post Error: {postErr}");

                    if (isPaye) { run.IsPayeRemitted = true; run.PayeRemittanceGLBatchId = batchId; }
                    else { run.IsPensionRemitted = true; run.PensionRemittanceGLBatchId = batchId; }
                }

                await ctx.SaveChangesAsync();
                await tx.CommitAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return $"Remittance Error: {ex.Message}";
            }
        }
    }
}
