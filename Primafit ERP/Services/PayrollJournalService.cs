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
                var run = await ctx.PayrollRuns.FromSqlInterpolated($"SELECT * FROM [PayrollRuns] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {runId}").Include(r => r.PayrollItems).FirstOrDefaultAsync();
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

                foreach (var item in run.PayrollItems)
                {
                    void Leg(Guid account, decimal debit, decimal credit, string reference)
                    {
                        if (debit > 0 || credit > 0) glLines.Add(new GLJournalLine { SegCoaId = account, Debit = debit, Credit = credit,
                            Reference = reference, DimensionSetId = item.DimensionSetId, IsDimensionDetail = true });
                    }
                    var pension = item.EmployeePension + item.EmployerPension;
                    Leg(settings.SalariesExpenseAccountId, item.PAYETax + pension + item.OtherDeductions + item.NetPay, 0, $"Payroll Expense: {run.Period}");
                    Leg(settings.PAYEPayableAccountId, 0, item.PAYETax, $"PAYE Liability: {run.Period}");
                    Leg(settings.PensionPayableAccountId, 0, pension, $"Pension Liability: {run.Period}");
                    Leg(settings.SalariesExpenseAccountId, 0, item.OtherDeductions, $"Other Deductions Withheld: {run.Period}");
                    Leg(settings.SalariesPayableAccountId, 0, item.NetPay, $"Net Pay Liability: {run.Period}");
                }

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
                var run = await ctx.PayrollRuns.FromSqlInterpolated($"SELECT * FROM [PayrollRuns] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {runId}").Include(r => r.PayrollItems).FirstOrDefaultAsync();
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

                DimensionService.AllocateSummaryLines(glLines, run.PayrollItems.Select(x => (x.DimensionSetId, x.NetPay)));
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
                var run = await ctx.PayrollRuns.FromSqlInterpolated($"SELECT * FROM [PayrollRuns] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {runId}").Include(r => r.PayrollItems).FirstOrDefaultAsync();
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

                DimensionService.AllocateSummaryLines(glLines, run.PayrollItems.Select(x => (x.DimensionSetId, isPaye ? x.PAYETax : x.EmployeePension + x.EmployerPension)));
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
