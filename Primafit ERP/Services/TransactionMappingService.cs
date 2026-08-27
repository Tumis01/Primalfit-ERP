using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Primafit_ERP.Services
{
    public class TransactionMappingService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly GLOperationsService _glOps;

        public TransactionMappingService(IDbContextFactory<AppDbContext> dbFactory, GLOperationsService glOps)
        {
            _dbFactory = dbFactory;
            _glOps = glOps;
        }

        // =========================================================
        // 1. CONFIGURATION CRUD & AUTO-SEEDING
        // =========================================================

        public async Task<List<TransactionGlMapping>> GetMappingsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var existing = await ctx.TransactionGlMappings
                .Include(m => m.CustomTransactionType)
                .Where(m => m.CompanyId == companyId)
                .ToListAsync();

            // Auto-seed ONLY standard system enums (exclude CustomGlAdjustment so deletions stay deleted)
            var systemTypes = Enum.GetValues<SystemTransactionType>()
                                  .Where(t => t != SystemTransactionType.CustomGlAdjustment);

            bool addedNew = false;

            foreach (var type in systemTypes)
            {
                if (!existing.Any(e => e.TransactionType == type && e.CustomTransactionTypeId == null))
                {
                    var newMapping = new TransactionGlMapping
                    {
                        CompanyId = companyId,
                        TransactionType = type,
                        IsActive = true,
                        UpdatedAt = DateTime.UtcNow
                    };
                    ctx.TransactionGlMappings.Add(newMapping);
                    existing.Add(newMapping);
                    addedNew = true;
                }
            }

            if (addedNew) await ctx.SaveChangesAsync();

            return existing
                .OrderBy(e => e.TransactionType == SystemTransactionType.CustomGlAdjustment ? 1 : 0)
                .ThenBy(e => e.TransactionType)
                .ThenBy(e => e.CustomTransactionType?.Name)
                .ToList();
        }

        public async Task<string> SaveMappingsAsync(Guid companyId, List<TransactionGlMapping> mappings, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            foreach (var mapping in mappings)
            {
                var existing = await ctx.TransactionGlMappings.FindAsync(mapping.Id);
                if (existing != null)
                {
                    existing.OverrideDebitGlAccountId = mapping.OverrideDebitGlAccountId;
                    existing.OverrideCreditGlAccountId = mapping.OverrideCreditGlAccountId;
                    existing.UpdatedByUserId = userId;
                    existing.UpdatedAt = DateTime.UtcNow;
                }
            }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        public async Task<Guid> GetMappedAccountAsync(Guid companyId, SystemTransactionType type, bool isDebit, Guid defaultAccountId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var mapping = await ctx.TransactionGlMappings
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.CompanyId == companyId && m.TransactionType == type && m.IsActive);

            if (mapping == null) return defaultAccountId;

            if (isDebit && mapping.OverrideDebitGlAccountId.HasValue && mapping.OverrideDebitGlAccountId.Value != Guid.Empty)
                return mapping.OverrideDebitGlAccountId.Value;

            if (!isDebit && mapping.OverrideCreditGlAccountId.HasValue && mapping.OverrideCreditGlAccountId.Value != Guid.Empty)
                return mapping.OverrideCreditGlAccountId.Value;

            return defaultAccountId;
        }

        public async Task<bool> DeleteCustomTransactionTypeAsync(Guid mappingId, Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var mapping = await ctx.TransactionGlMappings.FirstOrDefaultAsync(m => m.Id == mappingId && m.CompanyId == companyId);
            if (mapping == null) return false;

            if (mapping.CustomTransactionTypeId.HasValue)
            {
                var customType = await ctx.CustomTransactionTypes.FindAsync(mapping.CustomTransactionTypeId.Value);
                if (customType != null)
                {
                    ctx.CustomTransactionTypes.Remove(customType);
                }
            }

            ctx.TransactionGlMappings.Remove(mapping);
            await ctx.SaveChangesAsync();
            return true;
        }

        // =========================================================
        // 2. OPENING BALANCES & SUB-LEDGER POSTING ENGINES
        // =========================================================

        public async Task<string> PostOpeningBalancesAsync(Guid companyId, DateOnly postingDate, bool isCustomer, List<OpeningBalanceLineDto> lines, string userId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            using var tx = await ctx.Database.BeginTransactionAsync();

            try
            {
                var validLines = lines.Where(l => l.EntityId != Guid.Empty && l.BalanceAmount != 0).ToList();
                if (!validLines.Any()) return "No valid balances to post.";

                var mappingType = isCustomer ? SystemTransactionType.CustomerOpeningBalance : SystemTransactionType.VendorOpeningBalance;
                var config = await ctx.TransactionGlMappings.FirstOrDefaultAsync(m => m.CompanyId == companyId && m.TransactionType == mappingType);

                var glLines = new List<GLJournalLine>();

                if (isCustomer)
                {
                    Guid suspenseAccountId = config?.OverrideCreditGlAccountId ?? Guid.Empty;
                    if (suspenseAccountId == Guid.Empty) return "STOP: You must configure a Credit Account (Suspense/Equity) for Customer Opening Balances in the Setup Module.";

                    foreach (var line in validLines)
                    {
                        var customer = await ctx.Customers.FindAsync(line.EntityId);
                        if (customer == null || customer.ReceivablesAccountId == null) return $"Customer '{line.EntityName}' is missing an AR Account.";

                        Guid arAccount = config?.OverrideDebitGlAccountId ?? customer.ReceivablesAccountId.Value;
                        decimal absoluteAmount = Math.Abs(line.BalanceAmount);

                        if (line.BalanceAmount > 0)
                        {
                            glLines.Add(new GLJournalLine { SegCoaId = arAccount, Debit = absoluteAmount, Credit = 0, Reference = $"OB Dr: {customer.Name}" });
                            glLines.Add(new GLJournalLine { SegCoaId = suspenseAccountId, Debit = 0, Credit = absoluteAmount, Reference = $"OB Offset Cr: {customer.Name}" });
                        }
                        else
                        {
                            glLines.Add(new GLJournalLine { SegCoaId = suspenseAccountId, Debit = absoluteAmount, Credit = 0, Reference = $"OB Offset Dr: {customer.Name}" });
                            glLines.Add(new GLJournalLine { SegCoaId = arAccount, Debit = 0, Credit = absoluteAmount, Reference = $"OB Cr: {customer.Name}" });
                        }
                    }
                }
                else
                {
                    Guid suspenseAccountId = config?.OverrideDebitGlAccountId ?? Guid.Empty;
                    if (suspenseAccountId == Guid.Empty) return "STOP: You must configure a Debit Account (Suspense/Equity) for Vendor Opening Balances in the Setup Module.";

                    foreach (var line in validLines)
                    {
                        var vendor = await ctx.Vendors.FindAsync(line.EntityId);
                        if (vendor == null || vendor.PayablesAccountId == null) return $"Vendor '{line.EntityName}' is missing an AP Account.";

                        Guid apAccount = config?.OverrideCreditGlAccountId ?? vendor.PayablesAccountId.Value;
                        decimal absoluteAmount = Math.Abs(line.BalanceAmount);

                        if (line.BalanceAmount > 0)
                        {
                            glLines.Add(new GLJournalLine { SegCoaId = suspenseAccountId, Debit = absoluteAmount, Credit = 0, Reference = $"OB Offset Dr: {vendor.Name}" });
                            glLines.Add(new GLJournalLine { SegCoaId = apAccount, Debit = 0, Credit = absoluteAmount, Reference = $"OB Cr: {vendor.Name}" });
                        }
                        else
                        {
                            glLines.Add(new GLJournalLine { SegCoaId = apAccount, Debit = absoluteAmount, Credit = 0, Reference = $"OB Dr: {vendor.Name}" });
                            glLines.Add(new GLJournalLine { SegCoaId = suspenseAccountId, Debit = 0, Credit = absoluteAmount, Reference = $"OB Offset Cr: {vendor.Name}" });
                        }
                    }
                }

                string batchName = $"OB-{(isCustomer ? "CUST" : "VEND")}-{DateTime.UtcNow:yyMMddHHmm}";

                var (err, batchId) = await _glOps.CreateJournalEntryAsync(companyId, postingDate, batchName, "Opening Balances Migration Batch", glLines, userId);
                if (!string.IsNullOrEmpty(err)) throw new Exception(err);

                if (batchId.HasValue)
                {
                    var postErr = await _glOps.PostBatchAsync(companyId, batchId.Value, userId);
                    if (!string.IsNullOrEmpty(postErr)) throw new Exception($"GL Post Error: {postErr}");
                }

                await ctx.SaveChangesAsync();
                await tx.CommitAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return $"Migration Error: {ex.Message}";
            }
        }

        public async Task<string> PostArAdjustmentsAsync(Guid companyId, DateOnly postingDate, List<OpeningBalanceLineDto> adjustmentLines, string userId)
        {
            var targetLines = adjustmentLines.Where(x => x.EntityId != Guid.Empty && x.BalanceAmount != 0).ToList();
            if (!targetLines.Any()) return "STOP: No valid adjustment lines with non-zero amounts were provided.";

            try
            {
                var glLines = new List<GLJournalLine>();

                Guid arControlAccountOverride = await GetMappedAccountAsync(companyId, SystemTransactionType.ArAdjustment, isDebit: true, defaultAccountId: Guid.Empty);
                Guid arBalancingAccountOverride = await GetMappedAccountAsync(companyId, SystemTransactionType.ArAdjustment, isDebit: false, defaultAccountId: Guid.Empty);

                if (arBalancingAccountOverride == Guid.Empty)
                {
                    return "CONFIGURATION ERROR: No Balancing/Suspense Account has been mapped for 'ArAdjustment' (Credit Side) in GL Mapping Settings.";
                }

                foreach (var line in targetLines)
                {
                    decimal absoluteAmount = Math.Abs(line.BalanceAmount);
                    Guid customerArAccount = arControlAccountOverride;

                    if (customerArAccount == Guid.Empty)
                    {
                        using var ctx = await _dbFactory.CreateDbContextAsync();
                        var customer = await ctx.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == line.EntityId);
                        customerArAccount = customer?.ReceivablesAccountId ?? Guid.Empty;

                        if (customerArAccount == Guid.Empty)
                        {
                            return $"MASTER DATA ERROR: Customer '{line.EntityName}' has no configured Receivables Account, and no global 'ArAdjustment' Debit override is mapped.";
                        }
                    }

                    if (line.BalanceAmount > 0)
                    {
                        glLines.Add(new GLJournalLine { SegCoaId = customerArAccount, Debit = absoluteAmount, Credit = 0, Reference = $"AR Adj Dr - {line.EntityName}" });
                        glLines.Add(new GLJournalLine { SegCoaId = arBalancingAccountOverride, Debit = 0, Credit = absoluteAmount, Reference = $"AR Adj Balancing Cr - {line.EntityName}" });
                    }
                    else
                    {
                        glLines.Add(new GLJournalLine { SegCoaId = arBalancingAccountOverride, Debit = absoluteAmount, Credit = 0, Reference = $"AR Adj Balancing Dr - {line.EntityName}" });
                        glLines.Add(new GLJournalLine { SegCoaId = customerArAccount, Debit = 0, Credit = absoluteAmount, Reference = $"AR Adj Cr - {line.EntityName}" });
                    }
                }

                string batchRefName = $"ARADJ-{DateTime.UtcNow:yyMMddHHmm}";
                string memoNarration = $"AR Subsidiary Ledger Adjustment batch containing {targetLines.Count} rows.";

                var (glError, batchId) = await _glOps.CreateJournalEntryAsync(companyId, postingDate, batchRefName, memoNarration, glLines, userId);
                if (!string.IsNullOrEmpty(glError)) return $"JOURNAL ENGINE FAILURE: {glError}";

                if (batchId.HasValue)
                {
                    var postError = await _glOps.PostBatchAsync(companyId, batchId.Value, userId);
                    if (!string.IsNullOrEmpty(postError)) return $"LEDGER POSTING FAILURE: {postError}";
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                return $"FATAL SYSTEM ERROR: {ex.Message}";
            }
        }

        public async Task<string> PostApAdjustmentsAsync(Guid companyId, DateOnly postingDate, List<OpeningBalanceLineDto> adjustmentLines, string userId)
        {
            var targetLines = adjustmentLines.Where(x => x.EntityId != Guid.Empty && x.BalanceAmount != 0).ToList();
            if (!targetLines.Any()) return "STOP: No valid adjustment lines with non-zero amounts were provided.";

            try
            {
                var glLines = new List<GLJournalLine>();

                Guid apControlAccountOverride = await GetMappedAccountAsync(companyId, SystemTransactionType.ApAdjustment, isDebit: true, defaultAccountId: Guid.Empty);
                Guid apBalancingAccountOverride = await GetMappedAccountAsync(companyId, SystemTransactionType.ApAdjustment, isDebit: false, defaultAccountId: Guid.Empty);

                if (apBalancingAccountOverride == Guid.Empty)
                {
                    return "CONFIGURATION ERROR: No Balancing/Suspense Account has been mapped for 'ApAdjustment' (Debit Side) in GL Mapping Settings.";
                }

                foreach (var line in targetLines)
                {
                    decimal absoluteAmount = Math.Abs(line.BalanceAmount);
                    Guid vendorApAccount = apControlAccountOverride;

                    if (vendorApAccount == Guid.Empty)
                    {
                        using var ctx = await _dbFactory.CreateDbContextAsync();
                        var vendor = await ctx.Vendors.AsNoTracking().FirstOrDefaultAsync(v => v.Id == line.EntityId);
                        vendorApAccount = vendor?.PayablesAccountId ?? Guid.Empty;

                        if (vendorApAccount == Guid.Empty)
                        {
                            return $"MASTER DATA ERROR: Vendor '{line.EntityName}' has no configured Payables Account, and no global 'ApAdjustment' Credit override is mapped.";
                        }
                    }

                    if (line.BalanceAmount > 0)
                    {
                        glLines.Add(new GLJournalLine { SegCoaId = apBalancingAccountOverride, Debit = absoluteAmount, Credit = 0, Reference = $"AP Adj Balancing Dr - {line.EntityName}" });
                        glLines.Add(new GLJournalLine { SegCoaId = vendorApAccount, Debit = 0, Credit = absoluteAmount, Reference = $"AP Adj Cr - {line.EntityName}" });
                    }
                    else
                    {
                        glLines.Add(new GLJournalLine { SegCoaId = vendorApAccount, Debit = absoluteAmount, Credit = 0, Reference = $"AP Adj Dr - {line.EntityName}" });
                        glLines.Add(new GLJournalLine { SegCoaId = apBalancingAccountOverride, Debit = 0, Credit = absoluteAmount, Reference = $"AP Adj Balancing Cr - {line.EntityName}" });
                    }
                }

                string batchRefName = $"APADJ-{DateTime.UtcNow:yyMMddHHmm}";
                string memoNarration = $"AP Subsidiary Ledger Adjustment batch containing {targetLines.Count} rows.";

                var (glError, batchId) = await _glOps.CreateJournalEntryAsync(companyId, postingDate, batchRefName, memoNarration, glLines, userId);
                if (!string.IsNullOrEmpty(glError)) return $"JOURNAL ENGINE FAILURE: {glError}";

                if (batchId.HasValue)
                {
                    var postError = await _glOps.PostBatchAsync(companyId, batchId.Value, userId);
                    if (!string.IsNullOrEmpty(postError)) return $"LEDGER POSTING FAILURE: {postError}";
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                return $"FATAL SYSTEM ERROR: {ex.Message}";
            }
        }

        public async Task<string> PostInventoryAdjustmentBatchAsync(
            Guid companyId,
            DateOnly postingDate,
            Guid balancingGlAccountId,
            List<InventoryAdjustmentLineDto> adjustmentLines,
            string userId)
        {
            if (balancingGlAccountId == Guid.Empty) return "STOP: A valid Balancing/Offset GL Account is required.";

            var validLines = adjustmentLines.Where(x => x.ItemId != Guid.Empty && (x.QuantityChange != 0 || x.TotalValueChange != 0)).ToList();
            if (!validLines.Any()) return "STOP: No active lines with structural adjustments were provided.";

            await using var ctx = await _dbFactory.CreateDbContextAsync();
            await using var tx = await ctx.Database.BeginTransactionAsync();

            try
            {
                var glLines = new List<GLJournalLine>();

                foreach (var line in validLines)
                {
                    var item = await ctx.Items.FindAsync(line.ItemId);
                    if (item == null) return $"Item with ID '{line.ItemId}' could not be resolved.";
                    if (item.IsService) return $"STOP: '{item.Name}' is a service item. Physical inventory parameters cannot be adjusted.";
                    if (item.InventoryAssetAccountId == Guid.Empty) return $"CONFIGURATION ERROR: '{item.Name}' is missing an Inventory Asset GL Account mapping.";

                    decimal currentTotalQty = await ctx.StockLedgers
                        .Where(s => s.ItemId == line.ItemId)
                        .SumAsync(s => s.QuantityChanged);
                    if (currentTotalQty < 0) currentTotalQty = 0;

                    decimal oldWacc = item.WeightedAverageCost;
                    decimal oldValuation = currentTotalQty * oldWacc;

                    decimal finalLineCostChange = Math.Abs(line.TotalValueChange);

                    if (line.IsDebitInventory)
                    {
                        glLines.Add(new GLJournalLine { SegCoaId = item.InventoryAssetAccountId, Debit = finalLineCostChange, Credit = 0, Reference = $"Inv Adj Dr - {item.Name}" });
                        glLines.Add(new GLJournalLine { SegCoaId = balancingGlAccountId, Debit = 0, Credit = finalLineCostChange, Reference = $"Inv Adj Bal Cr - {item.Name}" });
                    }
                    else
                    {
                        glLines.Add(new GLJournalLine { SegCoaId = balancingGlAccountId, Debit = finalLineCostChange, Credit = 0, Reference = $"Inv Adj Bal Dr - {item.Name}" });
                        glLines.Add(new GLJournalLine { SegCoaId = item.InventoryAssetAccountId, Debit = 0, Credit = finalLineCostChange, Reference = $"Inv Adj Cr - {item.Name}" });
                    }

                    decimal absoluteValueShift = line.IsDebitInventory ? finalLineCostChange : -finalLineCostChange;
                    decimal newTotalQty = currentTotalQty + line.QuantityChange;
                    decimal newValuation = oldValuation + absoluteValueShift;

                    if (newTotalQty < 0) return $"VALUATION BLOCKED: Adjustment forces absolute quantity of '{item.Name}' below zero to ({newTotalQty}). Transaction aborted.";

                    item.WeightedAverageCost = newTotalQty > 0 ? Math.Round(newValuation / newTotalQty, 4) : 0;

                    ctx.ItemCostHistories.Add(new ItemCostHistory
                    {
                        Id = Guid.NewGuid(),
                        ItemId = item.Id,
                        OldQty = currentTotalQty,
                        OldWacc = oldWacc,
                        NewQtyIn = line.QuantityChange,
                        NewCostIn = line.QuantityChange != 0 ? absoluteValueShift / line.QuantityChange : absoluteValueShift,
                        ResultingWacc = item.WeightedAverageCost,
                        Reference = $"Batch Adjustment - {postingDate:yyyy-MM-dd}",
                        DateChanged = DateTime.UtcNow
                    });

                    if (line.QuantityChange != 0)
                    {
                        ctx.StockLedgers.Add(new StockLedger
                        {
                            Id = Guid.NewGuid(),
                            CompanyId = companyId,
                            ItemId = item.Id,
                            WarehouseId = line.WarehouseId,
                            QuantityChanged = line.QuantityChange,
                            Type = StockMovementType.Adjustment,
                            CostAtTime = item.WeightedAverageCost,
                            Reference = $"ADJ-{postingDate:yyMMdd}",
                            Date = DateTime.UtcNow
                        });
                    }
                }

                string batchRefName = $"INVADJ-{DateTime.UtcNow:yyMMDDHHmm}";
                var (glError, batchId) = await _glOps.CreateJournalEntryAsync(companyId, postingDate, batchRefName, "Inventory Batch Sub-ledger Adjustment", glLines, userId);
                if (!string.IsNullOrEmpty(glError)) throw new Exception(glError);

                if (batchId.HasValue)
                {
                    var postError = await _glOps.PostBatchAsync(companyId, batchId.Value, userId);
                    if (!string.IsNullOrEmpty(postError)) throw new Exception(postError);
                }

                await ctx.SaveChangesAsync();
                await tx.CommitAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return $"INVENTORY SYSTEM ADJ ERROR: {ex.Message}";
            }
        }

        public async Task<List<CustomTransactionType>> GetCustomTransactionTypesAsync(Guid companyId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.CustomTransactionTypes
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .OrderBy(x => x.Name)
                .ToListAsync();
        }

        public async Task<string> SaveCustomTransactionTypeAsync(CustomTransactionType customType)
        {
            if (string.IsNullOrWhiteSpace(customType.Name)) return "Transaction Type Name is required.";

            await using var ctx = await _dbFactory.CreateDbContextAsync();
            bool structuralExists = await ctx.CustomTransactionTypes
                .AnyAsync(x => x.CompanyId == customType.CompanyId
                            && x.Name.ToLower() == customType.Name.ToLower()
                            && x.Id != customType.Id);

            if (structuralExists) return $"A transaction template named '{customType.Name}' already exists for this company.";

            if (customType.Id == Guid.Empty || !await ctx.CustomTransactionTypes.AnyAsync(x => x.Id == customType.Id))
            {
                if (customType.Id == Guid.Empty) customType.Id = Guid.NewGuid();
                ctx.CustomTransactionTypes.Add(customType);

                ctx.TransactionGlMappings.Add(new TransactionGlMapping
                {
                    CompanyId = customType.CompanyId,
                    TransactionType = SystemTransactionType.CustomGlAdjustment,
                    CustomTransactionTypeId = customType.Id,
                    IsActive = true
                });
            }
            else
            {
                ctx.CustomTransactionTypes.Update(customType);
            }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }
    }
}