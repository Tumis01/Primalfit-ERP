using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class TestDataSeederService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public TestDataSeederService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<string> SeedFullScenarioAsync(Guid companyId)
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();

            // 1. Setup Fiscal Period
            var period = new AccountingPeriod
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                PeriodName = "FY 2026",
                StartDate = new DateOnly(2026, 1, 1),
                EndDate = new DateOnly(2026, 12, 31),
                IsClosed = false
            };
            ctx.AccountingPeriods.Add(period);

            // 2. Setup GL Accounts (Mapped to your SegAccountType seed data IDs)
            var bankAcct = CreateAccount(companyId, "1000", "Main Bank Account", 1); // Cash
            var invAcct = CreateAccount(companyId, "1200", "Inventory Asset", 24); // Inventory
            var arAcct = CreateAccount(companyId, "1100", "Accounts Receivable", 25); // AR
            var apAcct = CreateAccount(companyId, "2000", "Accounts Payable", 26); // AP
            var capitalAcct = CreateAccount(companyId, "3000", "Share Capital", 7); // Equity
            var revAcct = CreateAccount(companyId, "4000", "Product Sales", 9); // Revenue
            var cogsAcct = CreateAccount(companyId, "5000", "Cost of Goods Sold", 10); // COGS
            var rentAcct = CreateAccount(companyId, "6000", "Rent Expense", 36); // Admin Exp
            var adjAcct = CreateAccount(companyId, "6100", "Inventory Adjustment", 36); // Admin Exp

            ctx.SegChartOfAccounts.AddRange(bankAcct, invAcct, arAcct, apAcct, capitalAcct, revAcct, cogsAcct, rentAcct, adjAcct);

            // 3. Setup Warehouse
            var warehouse = new Warehouse { Id = Guid.NewGuid(), CompanyId = companyId, Name = "Main Warehouse" };
            ctx.Warehouses.Add(warehouse);

            // 4. Setup Items
            var laptop = new Item
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                SKU = "TECH-001",
                Name = "Pro Laptop",
                IsService = false,
                WeightedAverageCost = 500000,
                SellingPrice = 850000,
                InventoryAssetAccountId = invAcct.Id,
                CostOfGoodsSoldAccountId = cogsAcct.Id,
                SalesIncomeAccountId = revAcct.Id,
                AdjustmentExpenseAccountId = adjAcct.Id
            };

            var desk = new Item // For Stock Aging (Old item)
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                SKU = "FUR-999",
                Name = "Old Office Desk",
                IsService = false,
                WeightedAverageCost = 45000,
                SellingPrice = 60000,
                InventoryAssetAccountId = invAcct.Id,
                CostOfGoodsSoldAccountId = cogsAcct.Id,
                SalesIncomeAccountId = revAcct.Id,
                AdjustmentExpenseAccountId = adjAcct.Id
            };
            ctx.Items.AddRange(laptop, desk);

            // 5. Setup Budget (Limit for Rent is 2M)
            var budget = new BudgetHeader
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                AccountingPeriodId = period.Id,
                BudgetName = "Master Budget 2026",
                IsActive = true,
                Lines = new List<BudgetLine> { new BudgetLine { GlAccountId = rentAcct.Id, LimitAmount = 2000000 } }
            };
            ctx.BudgetHeaders.Add(budget);

            // 6. Setup Open Purchase Order (Encumbrance for Budget Variance)
            // Note: Since Rent is an expense, let's say we have an open PO for renting equipment mapped to rentAcct
            
            var vendor = new Vendor
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                Name = "Test Landlord Vendor",
                PayablesAccountId = apAcct.Id
            };
            ctx.Vendors.Add(vendor);

            // B. Create the Lease Service Item
            var dummyRentItem = new Item
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                SKU = "SRV-RENT",
                Name = "Office Space Lease",
                IsService = true,
                CostOfGoodsSoldAccountId = rentAcct.Id
            };
            ctx.Items.Add(dummyRentItem);

            // C. Create the PO with the required OrderNumber and real VendorId
            var po = new PurchaseOrder
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                Status = PurchaseOrderStatus.Open,
                OrderNumber = "PO-TEST-001", // FIXED: Required field added
                OrderDate = DateTime.Today,
                VendorId = vendor.Id,        // FIXED: Using the actual vendor we just created
                Lines = new List<PurchaseOrderLine> {
                    new PurchaseOrderLine { ItemId = dummyRentItem.Id, QuantityOrdered = 1, UnitCost = 500000 }
                }
            };
            ctx.PurchaseOrders.Add(po);

            // 7. Setup Stock Ledger Movements
            var today = DateTime.UtcNow;
            ctx.StockLedgers.AddRange(
                new StockLedger { CompanyId = companyId, WarehouseId = warehouse.Id, ItemId = laptop.Id, Date = today, Type = StockMovementType.Purchase, QuantityChanged = 10, CostAtTime = 500000, Reference = "PO-001" },
                new StockLedger { CompanyId = companyId, WarehouseId = warehouse.Id, ItemId = laptop.Id, Date = today, Type = StockMovementType.Sale, QuantityChanged = -2, CostAtTime = 500000, Reference = "PRJ: Server Setup" },
                new StockLedger { CompanyId = companyId, WarehouseId = warehouse.Id, ItemId = desk.Id, Date = today.AddDays(-100), Type = StockMovementType.Purchase, QuantityChanged = 5, CostAtTime = 45000, Reference = "PO-OLD" }
            );
            // 8. Financial Transactions (Balanced Journal Entries)
            var batchId = Guid.NewGuid();
            var journalId = Guid.NewGuid();
            var postingDate = new DateOnly(2026, 2, 1);

            // A. Capital Injection (Dr Bank 15M, Cr Capital 15M)
            ctx.GLTransactions.Add(CreateTxn(companyId, period.Id, batchId, journalId, bankAcct.Id, postingDate, 15000000, 0, "Capital Injection"));
            ctx.GLTransactions.Add(CreateTxn(companyId, period.Id, batchId, journalId, capitalAcct.Id, postingDate, 0, 15000000, "Capital Injection"));

            // B. Purchase Inventory (Dr Inventory 5.225M, Cr AP 5.225M)
            ctx.GLTransactions.Add(CreateTxn(companyId, period.Id, batchId, journalId, invAcct.Id, postingDate, 5225000, 0, "Purchase Stock"));
            ctx.GLTransactions.Add(CreateTxn(companyId, period.Id, batchId, journalId, apAcct.Id, postingDate, 0, 5225000, "Purchase Stock"));

            // C. Sell 3 Laptops (Dr AR 2.55M, Cr Revenue 2.55M)
            ctx.GLTransactions.Add(CreateTxn(companyId, period.Id, batchId, journalId, arAcct.Id, postingDate, 2550000, 0, "Sale INV-001"));
            ctx.GLTransactions.Add(CreateTxn(companyId, period.Id, batchId, journalId, revAcct.Id, postingDate, 0, 2550000, "Sale INV-001"));

            // D. COGS for Sale (Dr COGS 1.5M, Cr Inventory 1.5M)
            ctx.GLTransactions.Add(CreateTxn(companyId, period.Id, batchId, journalId, cogsAcct.Id, postingDate, 1500000, 0, "COGS INV-001"));
            ctx.GLTransactions.Add(CreateTxn(companyId, period.Id, batchId, journalId, invAcct.Id, postingDate, 0, 1500000, "COGS INV-001"));

            // E. Pay Rent (Dr Rent Exp 800k, Cr Bank 800k)
            ctx.GLTransactions.Add(CreateTxn(companyId, period.Id, batchId, journalId, rentAcct.Id, postingDate, 800000, 0, "Office Rent"));
            ctx.GLTransactions.Add(CreateTxn(companyId, period.Id, batchId, journalId, bankAcct.Id, postingDate, 0, 800000, "Office Rent"));

            // F. Internal Consumption Project (Dr COGS 1M, Cr Inventory 1M)
            ctx.GLTransactions.Add(CreateTxn(companyId, period.Id, batchId, journalId, cogsAcct.Id, postingDate, 1000000, 0, "PRJ: Server Setup"));
            ctx.GLTransactions.Add(CreateTxn(companyId, period.Id, batchId, journalId, invAcct.Id, postingDate, 0, 1000000, "PRJ: Server Setup"));

            await ctx.SaveChangesAsync();
            return "Test Data Seeded Successfully! Go check the reports.";
        }

        private SegChartOfAccount CreateAccount(Guid compId, string code, string desc, int typeId)
        {
            return new SegChartOfAccount { Id = Guid.NewGuid(), CompanyId = compId, AccountCode = code, Description = desc, SegAccountTypeId = typeId, AllowJournal = true, IsActive = true };
        }

        private GLTransaction CreateTxn(Guid compId, Guid periodId, Guid batchId, Guid journalId, Guid accId, DateOnly date, decimal dr, decimal cr, string note)
        {
            return new GLTransaction { Id = Guid.NewGuid(), CompanyId = compId, AccountingPeriodId = periodId, BatchId = batchId, JournalId = journalId, SegCoaId = accId, PostingDate = date, Debit = dr, Credit = cr, Narration = note };
        }
    }
}