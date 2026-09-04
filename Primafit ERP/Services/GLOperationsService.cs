using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Primafit_ERP.Services
{
    public class GLOperationsService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public GLOperationsService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        // =========================================================
        // Helpers
        // =========================================================
        private static bool IsBalanced(IEnumerable<GLJournalLine> lines)
            => Math.Abs(lines.Sum(x => x.Debit) - lines.Sum(x => x.Credit)) <= 0.0001m;

        private static string? ValidateJournalLines(IEnumerable<GLJournalLine> lines)
        {
            var materialLines = lines.Where(l => l.SegCoaId != Guid.Empty && (l.Debit != 0 || l.Credit != 0)).ToList();
            if (!materialLines.Any()) return "No valid journal lines were provided.";
            if (materialLines.Any(l => l.Debit < 0 || l.Credit < 0)) return "Debit and credit amounts cannot be negative.";
            if (materialLines.Any(l => l.Debit > 0 && l.Credit > 0)) return "A journal line cannot contain both a debit and a credit.";
            if (!IsBalanced(materialLines)) return "Journal is not balanced (Debits must equal Credits).";
            return null;
        }

        private static void AddAudit(AppDbContext ctx, Guid companyId, string userId, string action, string entityType, Guid entityId, string details)
        {
            ctx.AuditLogs.Add(new AuditLog
            {
                CompanyId = companyId,
                UserId = string.IsNullOrWhiteSpace(userId) ? "system" : userId,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Details = details,
                CreatedAt = DateTime.UtcNow
            });
        }

        private async Task<AccountingPeriod> ResolvePeriodOrThrow(AppDbContext ctx, Guid companyId, DateOnly txnDate)
        {
            var period = await ctx.AccountingPeriods
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.CompanyId == companyId
                                       && !p.IsClosed
                                       && p.StartDate <= txnDate
                                       && p.EndDate >= txnDate);

            if (period != null) return period;

            var openPeriods = await ctx.AccountingPeriods
                .AsNoTracking()
                .Where(p => p.CompanyId == companyId && !p.IsClosed)
                .OrderBy(p => p.StartDate)
                .Select(p => $"{p.StartDate:MMM dd, yyyy} to {p.EndDate:MMM dd, yyyy}")
                .ToListAsync();

            if (!openPeriods.Any())
            {
                throw new Exception("Cannot process transaction: There are no open accounting periods available for this company. Please open a new period in the Ledger settings.");
            }

            var availableRanges = string.Join(" | ", openPeriods);

            throw new Exception($"Cannot process transaction: The date {txnDate:MMM dd, yyyy} is closed or invalid. Please select a date within the following open periods: {availableRanges}");
        }

        private async Task<string?> ValidateSegmentedAccountsAsync(
            AppDbContext ctx,
            Guid companyId,
            List<GLJournalLine> lines,
            bool requireAllowJournal)
        {
            var ids = lines.Select(x => x.SegCoaId).Where(x => x != Guid.Empty).Distinct().ToList();
            if (ids.Count == 0) return "No valid accounts selected.";

            var accounts = await ctx.SegChartOfAccounts
                .AsNoTracking()
                .Where(a => a.CompanyId == companyId && ids.Contains(a.Id))
                .Select(a => new { a.Id, a.AllowJournal })
                .ToListAsync();

            if (accounts.Count != ids.Count)
                return "One or more selected accounts do not exist in the Segmented COA for this company.";

            if (requireAllowJournal)
            {
                var blocked = accounts.Where(a => !a.AllowJournal).Select(a => a.Id).ToList();
                if (blocked.Any())
                    return "One or more selected accounts are not allowed for Journal posting (AllowJournal = No).";
            }

            return null;
        }

        // =========================================================
        // 1) Create Standard Journal Entry (Wrapper)
        // =========================================================
        public async Task<(string error, Guid? batchId)> CreateJournalEntryAsync(
    Guid companyId,
    DateOnly txnDate,
    string batchName,
    string? description,
    List<GLJournalLine> lines,
    string userId,
    bool requireAllowJournal = false,
    Guid? existingBatchId = null) // Automated sub-ledgers remain exempt from AllowJournal control accounts
        {
            string journalNumber = $"JV-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";

            if (existingBatchId.HasValue && existingBatchId.Value != Guid.Empty)
            {
                return await StageSubledgerBatchAsync(
                    companyId,
                    txnDate,
                    batchName,
                    description,
                    journalNumber,
                    lines,
                    userId,
                    existingBatchId);
            }

            // Pass the bypass configuration down into the batch engine context
            var result = await CreateDraftBatchAsync(
                companyId,
                txnDate,
                batchName,
                description,
                journalNumber,
                description,
                lines,
                BatchType.Standard,
                userId,
                requireAllowJournal); // FIXED: Forwarding the configuration flag

            if (!string.IsNullOrWhiteSpace(result.error) || !result.batchId.HasValue)
                return result;

            var releaseErr = await ReleaseBatchAsync(companyId, result.batchId.Value, userId);
            if (!string.IsNullOrWhiteSpace(releaseErr))
                return (releaseErr, result.batchId);

            return result;
        }

        // =========================================================
        // 2) CORE: Create Draft Batch (The Engine)
        // =========================================================
        public async Task<(string error, Guid? batchId)> CreateDraftBatchAsync(
            Guid companyId, DateOnly txnDate, string batchName, string? description,
            string journalNumber, string? narration, List<GLJournalLine> lines,
            BatchType type, string userId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var cleanLines = lines
                .Where(l => l.SegCoaId != Guid.Empty && (l.Debit > 0 || l.Credit > 0))
                .ToList();

            var lineError = ValidateJournalLines(cleanLines);
            if (!string.IsNullOrWhiteSpace(lineError)) return (lineError!, null);

            AccountingPeriod period;
            try { period = await ResolvePeriodOrThrow(ctx, companyId, txnDate); }
            catch (Exception ex) { return (ex.Message, null); }

            // FIX: Only enforce Allow Journal rules for Standard Manual Journals
            bool requireAllowJournal = type == BatchType.Standard;

            var acctErr = await ValidateSegmentedAccountsAsync(ctx, companyId, cleanLines, requireAllowJournal);
            if (!string.IsNullOrWhiteSpace(acctErr)) return (acctErr!, null);

            var batch = new GLBatch
            {
                CompanyId = companyId,
                AccountingPeriodId = period.Id,
                BatchName = batchName,
                Description = description,
                Type = type,
                Status = BatchStatus.Draft,
                CreatedByUserId = userId,
                ClearAfterPost = false
            };

            batch.Journals.Add(new GLJournalHeader
            {
                CompanyId = companyId,
                AccountingPeriodId = period.Id,
                JournalNumber = journalNumber,
                Narration = narration,
                TransactionDate = txnDate,
                Lines = cleanLines
            });

            ctx.GLBatches.Add(batch);
            AddAudit(ctx, companyId, userId, "BatchCreated", nameof(GLBatch), batch.Id,
                $"Created {type} batch '{batchName}' with journal '{journalNumber}' for review.");
            await ctx.SaveChangesAsync();

            return (string.Empty, batch.Id);
        }
        public async Task<(string error, Guid? batchId)> CreateDraftBatchAsync(
    Guid companyId,
    DateOnly txnDate,
    string batchName,
    string? description,
    string journalNumber,
    string? narration,
    List<GLJournalLine> lines,
    BatchType type,
    string userId,
    bool? requireAllowJournalOverride = null)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var cleanLines = lines
                .Where(l => l.SegCoaId != Guid.Empty && (l.Debit > 0 || l.Credit > 0))
                .ToList();

            var lineError = ValidateJournalLines(cleanLines);
            if (!string.IsNullOrWhiteSpace(lineError)) return (lineError!, null);

            AccountingPeriod period;
            try { period = await ResolvePeriodOrThrow(ctx, companyId, txnDate); }
            catch (Exception ex) { return (ex.Message, null); }

            // FIXED: Evaluate if an explicit sub-ledger bypass override flag is passed down
            bool requireAllowJournal = requireAllowJournalOverride ?? (type == BatchType.Standard);

            var acctErr = await ValidateSegmentedAccountsAsync(ctx, companyId, cleanLines, requireAllowJournal);
            if (!string.IsNullOrWhiteSpace(acctErr)) return (acctErr!, null);

            var batch = new GLBatch
            {
                CompanyId = companyId,
                AccountingPeriodId = period.Id,
                BatchName = batchName,
                Description = description,
                Type = type,
                Status = BatchStatus.Draft,
                CreatedByUserId = userId,
                ClearAfterPost = false
            };

            batch.Journals.Add(new GLJournalHeader
            {
                CompanyId = companyId,
                AccountingPeriodId = period.Id,
                JournalNumber = journalNumber,
                Narration = narration,
                TransactionDate = txnDate,
                Lines = cleanLines
            });

            ctx.GLBatches.Add(batch);
            AddAudit(ctx, companyId, userId, "BatchCreated", nameof(GLBatch), batch.Id,
                $"Created {type} batch '{batchName}' with journal '{journalNumber}' for review.");
            await ctx.SaveChangesAsync();

            return (string.Empty, batch.Id);
        }
        // =========================================================
        // 3) Opening Balances (Migration)
        // =========================================================
        public async Task<string> CreateOpeningBalanceMigrationAsync(Guid companyId, DateOnly migrationDate, string batchName, List<GLJournalLine> inputLines, string userId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var cleanLines = inputLines.Where(l => l.SegCoaId != Guid.Empty && (l.Debit > 0 || l.Credit > 0)).ToList();
            if (cleanLines.Count == 0) return "Enter at least one valid line.";

            AccountingPeriod period;
            try { period = await ResolvePeriodOrThrow(ctx, companyId, migrationDate); }
            catch (Exception ex) { return ex.Message; }

            // FIX: Allow Migration batches to post to Control Accounts (AR, AP, Inventory)
            var acctErr = await ValidateSegmentedAccountsAsync(ctx, companyId, cleanLines, requireAllowJournal: false);
            if (!string.IsNullOrWhiteSpace(acctErr)) return acctErr!;

            var batch = new GLBatch
            {
                CompanyId = companyId,
                AccountingPeriodId = period.Id,
                BatchName = batchName,
                Description = "System Migration",
                Type = BatchType.Migration,
                Status = BatchStatus.Draft,
                CreatedByUserId = userId
            };

            batch.Journals.Add(new GLJournalHeader
            {
                CompanyId = companyId,
                AccountingPeriodId = period.Id,
                JournalNumber = "OPENING-BAL",
                TransactionDate = migrationDate,
                Lines = cleanLines
            });

            ctx.GLBatches.Add(batch);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // =========================================================
        // 4) Release & Reject
        // =========================================================
        public async Task<string> ReleaseBatchAsync(Guid companyId, Guid batchId, string userId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var batch = await ctx.GLBatches.Include(b => b.Journals).ThenInclude(j => j.Lines).FirstOrDefaultAsync(b => b.Id == batchId && b.CompanyId == companyId);
            if (batch == null) return "Batch not found.";
            if (batch.Status != BatchStatus.Draft) return "Only Draft batches can be released.";

            foreach (var j in batch.Journals)
            {
                var lineError = ValidateJournalLines(j.Lines);
                if (!string.IsNullOrWhiteSpace(lineError))
                    return $"Journal '{j.JournalNumber}' failed validation: {lineError}";
            }

            batch.Status = BatchStatus.Ready;
            batch.ReleasedByUserId = userId; // Assigned to actual user
            batch.ReleasedAt = DateTime.UtcNow;
            AddAudit(ctx, companyId, userId, "BatchSubmittedForReview", nameof(GLBatch), batch.Id,
                $"Batch '{batch.BatchName}' passed validation and was submitted for review.");
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> RejectBatchAsync(Guid companyId, Guid batchId, string reason, string userId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var batch = await ctx.GLBatches.FirstOrDefaultAsync(b => b.Id == batchId && b.CompanyId == companyId);
            if (batch == null) return "Batch not found.";
            if (string.IsNullOrWhiteSpace(reason)) return "A rejection reason is required.";
            if (batch.Status != BatchStatus.Ready && batch.Status != BatchStatus.Draft)
                return $"Only Draft or Ready batches can be rejected. Current status: {batch.Status}.";

            batch.Status = BatchStatus.Rejected;
            batch.IsLocked = false;
            batch.RejectedByUserId = userId; // Assigned to actual user
            batch.RejectedAt = DateTime.UtcNow;
            batch.RejectionReason = reason.Trim();
            AddAudit(ctx, companyId, userId, "BatchRejected", nameof(GLBatch), batch.Id,
                $"Batch '{batch.BatchName}' was rejected. Reason: {batch.RejectionReason}");

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        // Compatibility guard for legacy transaction services. They may still
        // call this method after constructing their journal, but no GL rows are
        // created until a reviewer explicitly approves the batch.
        public async Task<string> PostBatchAsync(Guid companyId, Guid batchId, string userId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var batch = await ctx.GLBatches.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == batchId && b.CompanyId == companyId);
            if (batch == null) return "Batch not found.";
            if (batch.Status == BatchStatus.Ready) return string.Empty;
            return $"Batch is not available for posting. Current Status: {batch.Status}";
        }

        // =========================================================
        // 5) Approve and Post Batch (Atomic, Segmented)
        // =========================================================
        public async Task<string> ApproveAndPostBatchAsync(Guid companyId, Guid batchId, string userId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            await using var tx = await ctx.Database.BeginTransactionAsync();

            try
            {
                var batch = await ctx.GLBatches
                    .Include(b => b.Journals)
                    .ThenInclude(j => j.Lines)
                    .FirstOrDefaultAsync(b => b.Id == batchId && b.CompanyId == companyId);

                if (batch == null)
                    return "Batch not found.";

                if (batch.Status != BatchStatus.Ready)
                    return $"Batch cannot be posted. Current Status: {batch.Status}";

                var targetPeriod = await ctx.AccountingPeriods
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == batch.AccountingPeriodId);

                if (targetPeriod == null)
                    return "Fatal Error: The accounting period linked to this batch no longer exists.";

                if (targetPeriod.IsClosed)
                    return $"STOP: Cannot post. The accounting period ({targetPeriod.StartDate:yyyy-MM-dd} to {targetPeriod.EndDate:yyyy-MM-dd}) is currently CLOSED.";

                bool hasPostedAnyJournal = false;

                foreach (var journal in batch.Journals)
                {
                    // Extract unposted lines for posting
                    var pendingLines = journal.Lines.Where(l => !l.IsPosted).ToList();

                    if (!pendingLines.Any())
                        continue;

                    var lineError = ValidateJournalLines(pendingLines);
                    if (!string.IsNullOrWhiteSpace(lineError))
                        return $"Journal '{journal.JournalNumber}' failed validation: {lineError}";

                    var acctErr = await ValidateSegmentedAccountsAsync(ctx, companyId, pendingLines, requireAllowJournal: false);
                    if (!string.IsNullOrWhiteSpace(acctErr))
                        return acctErr!;

                    foreach (var line in pendingLines)
                    {
                        ctx.GLTransactions.Add(new GLTransaction
                        {
                            CompanyId = companyId,
                            AccountingPeriodId = batch.AccountingPeriodId,
                            PostingDate = journal.TransactionDate,
                            BatchId = batch.Id,
                            JournalId = journal.Id,
                            SegCoaId = line.SegCoaId,
                            Debit = line.Debit,
                            Credit = line.Credit,
                            Narration = line.Reference ?? journal.Narration,
                            CreatedAt = DateTime.UtcNow
                        });

                        if (batch.ClearAfterPost)
                        {
                            ctx.Set<GLJournalLine>().Remove(line);
                        }
                                        else
                        {
                            line.IsPosted = true;
                        }
                    }

                    journal.Status = JournalStatus.Posted;
        hasPostedAnyJournal = true;
                }

                if (!hasPostedAnyJournal)
            return "No pending lines found to post in this batch.";

        // All batches must lock and transition to Posted once committed to the ledger
                batch.Status = BatchStatus.Posted;
                batch.IsLocked = true;
                batch.PostedByUserId = userId;
                batch.PostedAt = DateTime.UtcNow;
                AddAudit(ctx, companyId, userId, "BatchPosted", nameof(GLBatch), batch.Id,
                    $"Batch '{batch.BatchName}' was approved and committed to the General Ledger.");

        await ctx.SaveChangesAsync();
        await tx.CommitAsync();

        return string.Empty;
            }
            catch (Exception ex)
            {
            await tx.RollbackAsync();
            return $"Posting failed: {ex.Message}";
            }
        }

        public async Task<List<GLBatch>> GetActiveJournalBatchesAsync(Guid companyId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.GLBatches
                .Include(b => b.Journals)
                .ThenInclude(j => j.Lines)
                .Where(b => b.CompanyId == companyId)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();
        }
        // =========================================================
        // 6) INTERACTIVE UI LIFECYCLE
        // =========================================================

        public async Task<GLBatch?> GetBatchByIdAsync(Guid batchId, Guid? companyId = null)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.GLBatches
                .Include(b => b.Journals)
                .ThenInclude(j => j.Lines)
                .FirstOrDefaultAsync(b => b.Id == batchId && (!companyId.HasValue || b.CompanyId == companyId.Value));
        }

        public async Task<GLBatch> CreateDraftBatchAsync(
            Guid companyId, DateOnly date, string description, string userId, bool clearAfterPost)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var period = await ResolvePeriodOrThrow(ctx, companyId, date);

            var batchName = $"JV-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";

            var batch = new GLBatch
            {
                CompanyId = companyId,
                AccountingPeriodId = period.Id,
                BatchName = batchName,
                Description = description,
                Type = BatchType.Standard,
                Status = BatchStatus.Draft,
                CreatedByUserId = userId,
                ClearAfterPost = clearAfterPost
            };

            batch.Journals.Add(new GLJournalHeader
            {
                CompanyId = companyId,
                AccountingPeriodId = period.Id,
                JournalNumber = batchName,
                Narration = description,
                TransactionDate = date,
                Status = JournalStatus.Draft
            });

            ctx.GLBatches.Add(batch);
            await ctx.SaveChangesAsync();
            return batch;
        }

        // Notice the query no longer explicitly ignores Anonymous, 
        // as we are now assuming all Standard batches originate from a real user ID.
        

        public async Task<string> DeleteDraftBatchAsync(Guid batchId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var batch = await ctx.GLBatches.Include(b => b.Journals).ThenInclude(j => j.Lines).FirstOrDefaultAsync(b => b.Id == batchId);

            if (batch == null) return "Batch not found.";
            if (batch.Status != BatchStatus.Draft) return "Only Draft batches can be deleted.";
            if (batch.IsLocked) return "Unlock the batch before deleting it.";

            ctx.GLBatches.Remove(batch);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> SubmitForApprovalAsync(Guid batchId, string userId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var batch = await ctx.GLBatches
                .Include(b => b.Journals)
                .ThenInclude(j => j.Lines)
                .FirstOrDefaultAsync(b => b.Id == batchId);

            if (batch == null) return "Batch not found.";
            if (batch.Status == BatchStatus.Posted) return "Cannot modify a batch that has already been posted.";
            if (batch.Status == BatchStatus.Ready) return "Batch is already locked and awaiting review.";

            var allLines = batch.Journals.SelectMany(j => j.Lines).Where(l => !l.IsPosted).ToList();
            if (!allLines.Any()) return "Batch has no lines. Cannot submit an empty batch.";

            foreach (var j in batch.Journals)
            {
                var lineError = ValidateJournalLines(j.Lines.Where(l => !l.IsPosted));
                if (!string.IsNullOrWhiteSpace(lineError))
                    return $"Journal '{j.JournalNumber}' failed validation: {lineError}";
            }

            // Move to Ready status so it queues in /gl/batches
            batch.Status = BatchStatus.Ready;
            batch.IsLocked = true;
            batch.ReleasedByUserId = userId;
            batch.ReleasedAt = DateTime.UtcNow;
            batch.RejectionReason = null;
            batch.RejectedByUserId = null;
            batch.RejectedAt = null;

            AddAudit(ctx, batch.CompanyId, userId, "BatchSubmittedForReview", nameof(GLBatch), batch.Id,
                $"Batch '{batch.BatchName}' passed validation and was submitted for review.");

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> RevertToDraftAsync(Guid batchId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var batch = await ctx.GLBatches.FirstOrDefaultAsync(b => b.Id == batchId);

            if (batch == null) return "Batch not found.";
            if (batch.Status == BatchStatus.Posted) return "Cannot unlock or edit a batch that has already been posted to the General Ledger. Use Reuse Batch to begin a new posting cycle.";

            batch.Status = BatchStatus.Draft;
            batch.IsLocked = false;
            batch.ReleasedByUserId = null;
            batch.ReleasedAt = null;

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> LockBatchAsync(Guid batchId, string userId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var batch = await ctx.GLBatches.FirstOrDefaultAsync(b => b.Id == batchId);
            if (batch == null) return "Batch not found.";
            if (batch.Status != BatchStatus.Draft) return "Only Draft batches can be locked.";
            if (batch.IsLocked) return "Batch is already locked.";

            batch.IsLocked = true;
            AddAudit(ctx, batch.CompanyId, userId, "BatchLocked", nameof(GLBatch), batch.Id,
                $"Draft batch '{batch.BatchName}' was locked; it remains Draft until submitted for review.");
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> ReuseBatchAsync(Guid batchId, string userId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var batch = await ctx.GLBatches.Include(b => b.Journals).ThenInclude(j => j.Lines)
                .FirstOrDefaultAsync(b => b.Id == batchId);
            if (batch == null) return "Batch not found.";
            if (batch.Status != BatchStatus.Posted) return "Only posted batches can be reused.";

            batch.Status = BatchStatus.Draft;
            batch.IsLocked = false;
            batch.ReleasedByUserId = null;
            batch.ReleasedAt = null;
            batch.RejectedByUserId = null;
            batch.RejectedAt = null;
            batch.RejectionReason = null;
            foreach (var journal in batch.Journals) journal.Status = JournalStatus.Draft;

            AddAudit(ctx, batch.CompanyId, userId, "BatchReused", nameof(GLBatch), batch.Id,
                $"Posted batch '{batch.BatchName}' was reopened for additional journal entries.");
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> AddJournalLineAsync(Guid batchId, GLJournalLine line)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var batch = await ctx.GLBatches.Include(b => b.Journals).FirstOrDefaultAsync(b => b.Id == batchId);

            if (batch == null) return "Batch not found.";
            if (batch.Status != BatchStatus.Draft || batch.IsLocked) return "Batch is locked.";

            var header = batch.Journals.FirstOrDefault();
            if (header == null) return "Journal Header is missing.";

            // FIX: Only enforce if this is a Standard Batch
            bool requireAllowJournal = batch.Type == BatchType.Standard;
            var acctErr = await ValidateSegmentedAccountsAsync(ctx, batch.CompanyId, new List<GLJournalLine> { line }, requireAllowJournal);

            if (!string.IsNullOrEmpty(acctErr)) return acctErr;

            line.HeaderId = header.Id;
            ctx.Set<GLJournalLine>().Add(line);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }
        public async Task<(string error, Guid? batchId)> StageSubledgerBatchAsync(
    Guid companyId,
    DateOnly txnDate,
    string batchName,
    string? description,
    string sourceReference,
    List<GLJournalLine> lines,
    string userId,
            Guid? existingBatchId = null,
            bool? clearAfterPost = null)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var cleanLines = lines
                .Where(l => l.SegCoaId != Guid.Empty && (l.Debit > 0 || l.Credit > 0))
                .ToList();

            var lineError = ValidateJournalLines(cleanLines);
            if (!string.IsNullOrWhiteSpace(lineError))
                return ($"Cannot stage batch: {lineError}", null);

            AccountingPeriod period;
            try
            {
                period = await ResolvePeriodOrThrow(ctx, companyId, txnDate);
            }
            catch (Exception ex)
            {
                return (ex.Message, null);
            }

            var acctErr = await ValidateSegmentedAccountsAsync(ctx, companyId, cleanLines, requireAllowJournal: false);
            if (!string.IsNullOrWhiteSpace(acctErr))
                return (acctErr!, null);

            GLBatch? batch = null;

            // 1. First priority: Locate by explicit existing Batch ID
            if (existingBatchId.HasValue && existingBatchId.Value != Guid.Empty)
            {
                batch = await ctx.GLBatches
                    .Include(b => b.Journals)
                    .ThenInclude(j => j.Lines)
                    .FirstOrDefaultAsync(b => b.Id == existingBatchId.Value && b.CompanyId == companyId);

                if (batch != null && batch.Status == BatchStatus.Posted)
                    return ("This transaction's financial batch has already been committed to the General Ledger and cannot be modified.", null);
            }

            // 2. Defensive Fallback: If ID wasn't passed, check if an unposted batch with the same name already exists
            if (batch == null)
            {
                batch = await ctx.GLBatches
                    .Include(b => b.Journals)
                    .ThenInclude(j => j.Lines)
                    .FirstOrDefaultAsync(b => b.CompanyId == companyId
                                           && b.BatchName == batchName
                                           && b.Status != BatchStatus.Posted);
            }

            if (batch == null)
            {
                // Brand new batch
                batch = new GLBatch
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    AccountingPeriodId = period.Id,
                    BatchName = batchName,
                    Description = description,
                    Type = BatchType.Standard,
                    Status = BatchStatus.Ready,
                    CreatedByUserId = userId,
                    ClearAfterPost = clearAfterPost ?? false
                };

                var header = new GLJournalHeader
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    AccountingPeriodId = period.Id,
                    JournalNumber = sourceReference,
                    Narration = description,
                    TransactionDate = txnDate,
                    Status = JournalStatus.Draft,
                    BatchId = batch.Id,
                    Lines = cleanLines
                };

                batch.Journals.Add(header);
                ctx.GLBatches.Add(batch);
            }
            else
            {
                // Update existing batch in-place (Prevents duplicates & clears rejections)
                batch.AccountingPeriodId = period.Id;
                batch.BatchName = batchName;
                batch.Description = description;
                batch.Status = BatchStatus.Ready;
                batch.IsLocked = true;
                if (clearAfterPost.HasValue) batch.ClearAfterPost = clearAfterPost.Value;
                batch.RejectionReason = null;
                batch.RejectedAt = null;
                batch.RejectedByUserId = null;

                var header = batch.Journals.FirstOrDefault();
                if (header != null)
                {
                    header.AccountingPeriodId = period.Id;
                    header.JournalNumber = sourceReference;
                    header.TransactionDate = txnDate;
                    header.Narration = description;

                    ctx.Set<GLJournalLine>().RemoveRange(header.Lines);
                    foreach (var line in cleanLines)
                    {
                        line.HeaderId = header.Id;
                        ctx.Set<GLJournalLine>().Add(line);
                    }
                }
            }

            AddAudit(ctx, companyId, userId, "BatchStaged", nameof(GLBatch), batch.Id,
                $"Transaction batch '{batch.BatchName}' was staged for review with source reference '{sourceReference}'.");

            await ctx.SaveChangesAsync();
            return (string.Empty, batch.Id);
        }
        public async Task<string> UpdateJournalLineAsync(GLJournalLine line)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var existing = await ctx.Set<GLJournalLine>().Include(l => l.Header).ThenInclude(h => h.Batch).FirstOrDefaultAsync(l => l.Id == line.Id);

            if (existing == null) return "Line not found.";
            if (existing.Header?.Batch?.Status != BatchStatus.Draft || existing.Header.Batch.IsLocked) return "Batch is locked.";
            if (existing.IsPosted) return "Posted journal lines cannot be edited.";

            // FIX: Only enforce if this is a Standard Batch
            bool requireAllowJournal = existing.Header.Batch.Type == BatchType.Standard;
            var acctErr = await ValidateSegmentedAccountsAsync(ctx, existing.Header.CompanyId, new List<GLJournalLine> { line }, requireAllowJournal);

            if (!string.IsNullOrEmpty(acctErr)) return acctErr;

            existing.SegCoaId = line.SegCoaId;
            existing.Reference = line.Reference;
            existing.Debit = line.Debit;
            existing.Credit = line.Credit;

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<string> RemoveJournalLineAsync(Guid lineId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var existing = await ctx.Set<GLJournalLine>().Include(l => l.Header).ThenInclude(h => h.Batch).FirstOrDefaultAsync(l => l.Id == lineId);

            if (existing == null) return "Line not found.";
            if (existing.Header?.Batch?.Status != BatchStatus.Draft || existing.Header.Batch.IsLocked) return "Batch is locked.";
            if (existing.IsPosted) return "Posted journal lines cannot be removed.";

            ctx.Set<GLJournalLine>().Remove(existing);
            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<List<LedgerReportRow>> GetLedgerReportAsync(Guid companyId, DateOnly startDate, DateOnly endDate)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            var query =
                from t in ctx.GLTransactions.AsNoTracking()
                join a in ctx.SegChartOfAccounts.AsNoTracking() on t.SegCoaId equals a.Id
                join j in ctx.GLJournalHeaders.AsNoTracking() on t.JournalId equals j.Id
                where t.CompanyId == companyId
                      && t.PostingDate >= startDate
                      && t.PostingDate <= endDate
                orderby a.AccountCode, t.PostingDate
                select new LedgerReportRow
                {
                    SegCoaId = t.SegCoaId,
                    AccountCode = a.AccountCode,
                    AccountName = a.Description,
                    PostingDate = t.PostingDate,
                    JournalNumber = j.JournalNumber,
                    Narration = t.Narration,
                    Debit = t.Debit,
                    Credit = t.Credit
                };

            return await query.ToListAsync();
        }
    }
}
