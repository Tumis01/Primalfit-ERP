using Microsoft.EntityFrameworkCore;
using PrimafitERP.Data;
using Primafit_ERP.Components.Models;

namespace Primafit_ERP.Services;

public enum ReceiptRegisterState { Draft, InReview, GlDraft, Rejected, Posted, Recorded, NeedsReview }

public sealed record GoodsReceiptRegisterSnapshot(List<GoodsReceipt> Receipts, List<GoodsReceiptDraft> Drafts,
    Dictionary<Guid, ReceiptRegisterState> States, Dictionary<Guid, string> ConfirmedBy);

public static class GoodsReceiptRegisterService
{
    public static async Task<GoodsReceiptRegisterSnapshot> LoadAsync(AppDbContext db, Guid companyId)
    {
        var receipts = await db.GoodsReceipts.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.CompanyId == companyId).OrderByDescending(x => x.DateReceived).ToListAsync();
        var drafts = await db.GoodsReceiptDrafts.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.CompanyId == companyId).OrderByDescending(x => x.DateReceived).ToListAsync();
        var batches = await db.GLBatches.AsNoTracking().Where(x => x.CompanyId == companyId).ToListAsync();
        var batchById = batches.ToDictionary(x => x.Id);
        var numbers = receipts.Select(x => Key(x.GrnNumber)).Concat(drafts.Select(x => Key(x.GrnNumber)))
            .Where(x => x.Length > 0).Distinct().ToList();
        var numberSet = numbers.ToHashSet(StringComparer.Ordinal);
        var descriptions = numbers.Select(x => "GRN " + x).ToList();
        var accruals = numbers.Select(x => "GR/IR ACCRUAL FOR " + x).ToList();
        var journals = await db.GLJournalHeaders.AsNoTracking().Where(x => x.CompanyId == companyId
            && (numbers.Contains(x.JournalNumber.Trim().ToUpper())
                || (x.Narration != null && descriptions.Contains(x.Narration.Trim().ToUpper()))))
            .Select(x => new { x.BatchId, x.JournalNumber, x.Narration }).ToListAsync();
        var batchIds = batches.Where(x => descriptions.Contains(Key(x.Description))).Select(x => x.Id)
            .Concat(journals.Select(x => x.BatchId))
            .Concat(receipts.Where(x => x.GLBatchId.HasValue).Select(x => x.GLBatchId!.Value)).Distinct().ToList();
        var postings = await db.GLTransactions.AsNoTracking().Where(x => x.CompanyId == companyId
            && (batchIds.Contains(x.BatchId) || (x.Narration != null && accruals.Contains(x.Narration.Trim().ToUpper()))))
            .Select(x => new { x.BatchId, x.Narration }).Distinct().ToListAsync();
        var postedIds = postings.Select(x => x.BatchId).ToHashSet();
        var candidates = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal);
        void Add(string number, Guid batchId)
        {
            if (!numberSet.Contains(number)) return;
            if (!candidates.TryGetValue(number, out var ids)) candidates[number] = ids = new();
            ids.Add(batchId);
        }
        foreach (var journal in journals)
        {
            Add(Key(journal.JournalNumber), journal.BatchId);
            var description = Key(journal.Narration);
            if (description.StartsWith("GRN ", StringComparison.Ordinal)) Add(description[4..], journal.BatchId);
        }
        foreach (var batch in batches)
        {
            var description = Key(batch.Description);
            if (description.StartsWith("GRN ", StringComparison.Ordinal)) Add(description[4..], batch.Id);
        }
        foreach (var posting in postings)
        {
            const string prefix = "GR/IR ACCRUAL FOR ";
            var description = Key(posting.Narration);
            if (description.StartsWith(prefix, StringComparison.Ordinal)) Add(description[prefix.Length..], posting.BatchId);
        }
        bool Posted(Guid id) => postedIds.Contains(id) || (batchById.TryGetValue(id, out var batch) && batch.Status == BatchStatus.Posted);
        (ReceiptRegisterState State, Guid? BatchId) Resolve(GoodsReceipt receipt)
        {
            // Committed ledger entries take precedence over stale workflow labels.
            if (receipt.GLBatchId is { } linked && (batchById.ContainsKey(linked) || postedIds.Contains(linked)))
                return (Posted(linked) ? ReceiptRegisterState.Posted : State(batchById[linked].Status), linked);
            var matches = candidates.GetValueOrDefault(Key(receipt.GrnNumber)) ?? new();
            var posted = matches.Where(Posted).ToList();
            if (posted.Count > 0) return (ReceiptRegisterState.Posted, posted.Count == 1 ? posted[0] : null);
            if (matches.Count == 1 && batchById.TryGetValue(matches.Single(), out var batch)) return (State(batch.Status), batch.Id);
            if (matches.Count > 0 || receipt.GLBatchId.HasValue) return (ReceiptRegisterState.NeedsReview, null);
            // Saved receipt rows predate the separate draft table. An unlinked
            // historical receipt is a recorded receipt, never a new draft.
            return (ReceiptRegisterState.Recorded, null);
        }
        var resolved = receipts.ToDictionary(x => x.Id, Resolve);
        var receiptIds = receipts.Select(x => x.Id).ToHashSet();
        var receiptNumbers = receipts.Select(x => Key(x.GrnNumber)).Where(x => x.Length > 0).ToHashSet();
        drafts = drafts.Where(x => !receiptIds.Contains(x.Id) && !receiptNumbers.Contains(Key(x.GrnNumber))
            && !(candidates.GetValueOrDefault(Key(x.GrnNumber))?.Any(Posted) ?? false)).ToList();
        var states = resolved.ToDictionary(x => x.Key, x => x.Value.State);
        foreach (var draft in drafts) states[draft.Id] = ReceiptRegisterState.Draft;
        var userIds = batches.Select(x => x.PostedByUserId ?? x.ReleasedByUserId ?? x.CreatedByUserId).Distinct().ToList();
        var emails = await db.Users.AsNoTracking().Where(x => x.CompanyDetailsId == companyId && userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Email);
        var confirmedBy = resolved.ToDictionary(x => x.Key, x => x.Value.BatchId is { } id && batchById.TryGetValue(id, out var batch)
            ? emails.GetValueOrDefault(batch.PostedByUserId ?? batch.ReleasedByUserId ?? batch.CreatedByUserId) ?? "Unavailable" : "Unavailable");
        return new(receipts, drafts, states, confirmedBy);
    }

    public static bool IsHistory(ReceiptRegisterState state) => state is ReceiptRegisterState.Posted or ReceiptRegisterState.Recorded;
    private static string Key(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;
    private static ReceiptRegisterState State(BatchStatus state) => state switch {
        BatchStatus.Ready => ReceiptRegisterState.InReview,
        BatchStatus.Posted => ReceiptRegisterState.Posted,
        BatchStatus.Rejected => ReceiptRegisterState.Rejected,
        _ => ReceiptRegisterState.GlDraft
    };
}
