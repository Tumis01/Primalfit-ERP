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

        public async Task<List<TransactionGlMapping>> GetMappingsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var existing = await ctx.TransactionGlMappings
                .Include(m => m.CustomTransactionType)
                .Where(m => m.CompanyId == companyId)
                .ToListAsync();

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

        public async Task<string> PostArAdjustmentsAsync(
            Guid companyId,
            DateOnly postingDate,
            List<OpeningBalanceLineDto> adjustmentLines,
            string userId,
            Guid? customTransactionTypeId = null,
            Guid? directArControlAccountId = null,
            Guid? directBalancingAccountId = null)
        {
            var targetLines = adjustmentLines.Where(x => x.EntityId != Guid.Empty && x.BalanceAmount != 0).ToList();
            if (!targetLines.Any()) return "STOP: No valid adjustment lines with non-zero amounts were provided.";

            try
            {
                using var ctx = await _dbFactory.CreateDbContextAsync();
                var glLines = new List<GLJournalLine>();

                // 1. Resolve custom template mapping if assigned
                TransactionGlMapping? customMapping = null;
                if (customTransactionTypeId.HasValue && customTransactionTypeId.Value != Guid.Empty)
                {
                    customMapping = await ctx.TransactionGlMappings
                        .FirstOrDefaultAsync(m => m.CompanyId == companyId && m.CustomTransactionTypeId == customTransactionTypeId.Value);
                }

                // 2. Resolve Balancing Account (Credit/Debit Suspense)
                Guid arBalancingAccount = directBalancingAccountId
                    ?? customMapping?.OverrideCreditGlAccountId
                    ?? Guid.Empty;

                if (arBalancingAccount == Guid.Empty)
                {
                    arBalancingAccount = await GetMappedAccountAsync(companyId, SystemTransactionType.ArAdjustment, isDebit: false, defaultAccountId: Guid.Empty);
                }

                if (arBalancingAccount == Guid.Empty)
                {
                    return "CONFIGURATION ERROR: No Balancing/Suspense Account has been mapped for 'ArAdjustment' (Credit Side) in GL Mapping Settings or Modal Override.";
                }

                // 3. Resolve AR Control Override
                Guid globalArControlOverride = directArControlAccountId
                    ?? customMapping?.OverrideDebitGlAccountId
                    ?? Guid.Empty;

                if (globalArControlOverride == Guid.Empty)
                {
                    globalArControlOverride = await GetMappedAccountAsync(companyId, SystemTransactionType.ArAdjustment, isDebit: true, defaultAccountId: Guid.Empty);
                }

                foreach (var line in targetLines)
                {
                    decimal absoluteAmount = Math.Abs(line.BalanceAmount);
                    Guid customerArAccount = globalArControlOverride;

                    if (customerArAccount == Guid.Empty)
                    {
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
                        glLines.Add(new GLJournalLine { SegCoaId = arBalancingAccount, Debit = 0, Credit = absoluteAmount, Reference = $"AR Adj Balancing Cr - {line.EntityName}" });
                    }
                    else
                    {
                        glLines.Add(new GLJournalLine { SegCoaId = arBalancingAccount, Debit = absoluteAmount, Credit = 0, Reference = $"AR Adj Balancing Dr - {line.EntityName}" });
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

        public async Task<string> PostApAdjustmentsAsync(
            Guid companyId,
            DateOnly postingDate,
            List<OpeningBalanceLineDto> adjustmentLines,
            string userId,
            Guid? customTransactionTypeId = null,
            Guid? directApControlAccountId = null,
            Guid? directBalancingAccountId = null)
        {
            var targetLines = adjustmentLines.Where(x => x.EntityId != Guid.Empty && x.BalanceAmount != 0).ToList();
            if (!targetLines.Any()) return "STOP: No valid adjustment lines with non-zero amounts were provided.";

            try
            {
                using var ctx = await _dbFactory.CreateDbContextAsync();
                var glLines = new List<GLJournalLine>();

                // 1. Resolve custom template mapping if assigned
                TransactionGlMapping? customMapping = null;
                if (customTransactionTypeId.HasValue && customTransactionTypeId.Value != Guid.Empty)
                {
                    customMapping = await ctx.TransactionGlMappings
                        .FirstOrDefaultAsync(m => m.CompanyId == companyId && m.CustomTransactionTypeId == customTransactionTypeId.Value);
                }

                // 2. Resolve Balancing/Suspense Offset Account (Debit Side for AP Opening Balance)
                Guid apBalancingAccount = directBalancingAccountId
                    ?? customMapping?.OverrideDebitGlAccountId
                    ?? Guid.Empty;

                if (apBalancingAccount == Guid.Empty)
                {
                    apBalancingAccount = await GetMappedAccountAsync(companyId, SystemTransactionType.ApAdjustment, isDebit: true, defaultAccountId: Guid.Empty);
                }

                if (apBalancingAccount == Guid.Empty)
                {
                    return "CONFIGURATION ERROR: No Balancing/Suspense Account has been mapped for 'ApAdjustment' in GL Mapping Settings or Modal Override.";
                }

                // 3. Resolve AP Control Account Override (Credit Side for AP Liability)
                Guid globalApControlOverride = directApControlAccountId
                    ?? customMapping?.OverrideCreditGlAccountId
                    ?? Guid.Empty;

                if (globalApControlOverride == Guid.Empty)
                {
                    globalApControlOverride = await GetMappedAccountAsync(companyId, SystemTransactionType.ApAdjustment, isDebit: false, defaultAccountId: Guid.Empty);
                }

                foreach (var line in targetLines)
                {
                    decimal absoluteAmount = Math.Abs(line.BalanceAmount);
                    Guid vendorApAccount = globalApControlOverride;

                    if (vendorApAccount == Guid.Empty)
                    {
                        var vendor = await ctx.Vendors.AsNoTracking().FirstOrDefaultAsync(v => v.Id == line.EntityId);
                        vendorApAccount = vendor?.PayablesAccountId ?? Guid.Empty;

                        if (vendorApAccount == Guid.Empty)
                        {
                            return $"MASTER DATA ERROR: Vendor '{line.EntityName}' has no configured Accounts Payable Account, and no global 'ApAdjustment' override is mapped.";
                        }
                    }

                    if (line.BalanceAmount > 0)
                    {
                        glLines.Add(new GLJournalLine { SegCoaId = apBalancingAccount, Debit = absoluteAmount, Credit = 0, Reference = $"AP Adj Balancing Dr - {line.EntityName}" });
                        glLines.Add(new GLJournalLine { SegCoaId = vendorApAccount, Debit = 0, Credit = absoluteAmount, Reference = $"AP Adj Cr - {line.EntityName}" });
                    }
                    else
                    {
                        glLines.Add(new GLJournalLine { SegCoaId = vendorApAccount, Debit = absoluteAmount, Credit = 0, Reference = $"AP Adj Dr - {line.EntityName}" });
                        glLines.Add(new GLJournalLine { SegCoaId = apBalancingAccount, Debit = 0, Credit = absoluteAmount, Reference = $"AP Adj Balancing Cr - {line.EntityName}" });
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

                string batchRefName = $"INVADJ-{DateTime.UtcNow:yyMMddHHmm}";
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

        public async Task<List<TransactionTypeOptionDto>> GetAvailableTransactionTypesAsync(Guid companyId, bool arOnly = false)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var mappings = await ctx.TransactionGlMappings
                .Include(m => m.CustomTransactionType)
                .Where(m => m.CompanyId == companyId && m.IsActive)
                .ToListAsync();

            var options = new List<TransactionTypeOptionDto>();

            // Distinct AR system transaction types
            var arSystemTypes = new HashSet<SystemTransactionType>
            {
                SystemTransactionType.SalesInvoice,
                SystemTransactionType.DirectSalesInvoice,
                SystemTransactionType.ArAdjustment,
                SystemTransactionType.CustomerPayment,
                SystemTransactionType.CreditNote,
                SystemTransactionType.ReceiptRefundStock,
                SystemTransactionType.ReceiptRefundCash,
                SystemTransactionType.ShipmentDispatch,
                SystemTransactionType.DiscountAllowed
            };

            // 1. Add System Transaction Types
            foreach (var map in mappings.Where(m => m.TransactionType != SystemTransactionType.CustomGlAdjustment))
            {
                if (arOnly && !arSystemTypes.Contains(map.TransactionType))
                    continue;

                options.Add(new TransactionTypeOptionDto
                {
                    ValueKey = $"SYS_{(int)map.TransactionType}",
                    DisplayName = System.Text.RegularExpressions.Regex.Replace(map.TransactionType.ToString(), "([a-z])([A-Z])", "$1 $2"),
                    Description = $"Core System Route for {map.TransactionType}",
                    IsCustom = false,
                    SystemType = map.TransactionType,
                    CustomTransactionTypeId = null,
                    DefaultDebitAccountId = map.OverrideDebitGlAccountId,
                    DefaultCreditAccountId = map.OverrideCreditGlAccountId
                });
            }

            // 2. Add Custom Transaction Types Created via Form
            foreach (var map in mappings.Where(m => m.TransactionType == SystemTransactionType.CustomGlAdjustment && m.CustomTransactionType != null))
            {
                options.Add(new TransactionTypeOptionDto
                {
                    ValueKey = $"CUST_{map.CustomTransactionTypeId}",
                    DisplayName = $"{map.CustomTransactionType!.Name} (Custom)",
                    Description = map.CustomTransactionType.Description,
                    IsCustom = true,
                    SystemType = SystemTransactionType.CustomGlAdjustment,
                    CustomTransactionTypeId = map.CustomTransactionTypeId,
                    DefaultDebitAccountId = map.OverrideDebitGlAccountId,
                    DefaultCreditAccountId = map.OverrideCreditGlAccountId
                });
            }

            return options
                .OrderBy(o => o.IsCustom ? 1 : 0)
                .ThenBy(o => o.DisplayName)
                .ToList();
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