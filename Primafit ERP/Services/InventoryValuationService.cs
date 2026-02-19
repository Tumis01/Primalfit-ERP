using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class InventoryValuationService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public InventoryValuationService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        
        public async Task<string> RecalculateWACC(Guid grnId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // 1. Fetch GRN with Lines and Landed Costs
            var grn = await ctx.GoodsReceipts
                .Include(g => g.Lines)
                .FirstOrDefaultAsync(g => g.Id == grnId);

            if (grn == null) return "GRN not found";

            // 2. Fetch Landed Costs assigned to this GRN
            var landedCosts = await ctx.GrnLandedCosts
                .Where(x => x.GoodsReceiptId == grnId)
                .ToListAsync();

            decimal totalLandedCost = landedCosts.Sum(x => x.Amount);

            // 3. Get PO Lines to determine Base Cost (The Vendor's Price)
            var poLineIds = grn.Lines.Select(l => l.PurchaseOrderLineId).ToList();
            var poLines = await ctx.PurchaseOrderLines
                .Where(p => poLineIds.Contains(p.Id))
                .ToListAsync();

            // 4. Calculate Total Receipt Value (Base Cost of goods)
            decimal totalBaseValue = 0;
            var lineValuations = new Dictionary<Guid, decimal>(); // GrnLineId -> Total Cost (Base + Allocated)

            foreach (var line in grn.Lines)
            {
                var poLine = poLines.FirstOrDefault(p => p.Id == line.PurchaseOrderLineId);
                decimal lineBaseCost = (poLine?.UnitCost ?? 0) * line.QuantityReceived;
                totalBaseValue += lineBaseCost;

                // Initialize dictionary
                lineValuations[line.Id] = lineBaseCost;
            }

            // 5. ALLOCATE Landed Costs (Distribute the extra fees across items)
            if (totalBaseValue > 0 && totalLandedCost > 0)
            {
                foreach (var line in grn.Lines)
                {
                    decimal currentLineBaseVal = lineValuations[line.Id];

                    // Ratio based on Value (Standard GAAP approach)
                    decimal ratio = currentLineBaseVal / totalBaseValue;

                    decimal allocatedCost = totalLandedCost * ratio;
                    lineValuations[line.Id] += allocatedCost;
                }
            }

            // 6. UPDATE ITEM WACC (The Formula)
            foreach (var line in grn.Lines)
            {
                var item = await ctx.Items.FindAsync(poLines.First(p => p.Id == line.PurchaseOrderLineId).ItemId);
                if (item == null || item.IsService) continue;

                // --- THE WACC FORMULA ---
                // WACC = ((OldQty * OldCost) + (NewQty * NewCost)) / (OldQty + NewQty)

                //  Get Old State (Before this receipt)
                decimal oldQty = await GetCurrentStockQty(ctx, item.Id, grn.CompanyId) - line.QuantityReceived;

                if (oldQty < 0) oldQty = 0; // Safety

                decimal oldWacc = item.WeightedAverageCost;
                decimal oldTotalValue = oldQty * oldWacc;

                //  New Incoming Batch
                decimal newBatchTotalValue = lineValuations[line.Id];
                decimal newBatchQty = line.QuantityReceived;

                // Calculation
                decimal finalTotalQty = oldQty + newBatchQty;

                if (finalTotalQty > 0)
                {
                    decimal newWacc = (oldTotalValue + newBatchTotalValue) / finalTotalQty;

                    //  Update Item
                    item.WeightedAverageCost = newWacc;

                    //  Record History
                    ctx.ItemCostHistories.Add(new ItemCostHistory
                    {
                        ItemId = item.Id,
                        OldQty = oldQty,
                        OldWacc = oldWacc,
                        NewQtyIn = newBatchQty,
                        NewCostIn = newBatchTotalValue / newBatchQty, // Unit Cost of this specific batch
                        ResultingWacc = newWacc,
                        Reference = $"GRN Valuation: {grn.GrnNumber}",
                        DateChanged = DateTime.UtcNow
                    });
                }
            }

            await ctx.SaveChangesAsync();
            return string.Empty;
        }

        private async Task<decimal> GetCurrentStockQty(AppDbContext ctx, Guid itemId, Guid companyId)
        {
            return await ctx.StockLedgers
                .Where(s => s.ItemId == itemId && s.CompanyId == companyId)
                .SumAsync(s => s.QuantityChanged);
        }
    }
}