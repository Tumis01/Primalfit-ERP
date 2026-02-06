using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using System.Reflection.Emit;

namespace PrimafitERP.Data
{
    // Ensure the generic types match your ApplicationUser/Role definitions (default is string for Identity)
    public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // --- MASTER DATA ---
        public DbSet<CompanyDetails> CompanyDetails { get; set; }
        public DbSet<Currency> Currencies { get; set; }
        public DbSet<CurrencyManagement> CurrencyManagements { get; set; }
        public DbSet<Tax> Taxes { get; set; }
        public DbSet<Project> Projects { get; set; }
        public DbSet<Warehouse> Warehouses { get; set; }

        // --- FINANCE (GL) ---
        public DbSet<AccountingPeriod> AccountingPeriods { get; set; }
        public DbSet<GLAccountType> GLAccountTypes { get; set; }
        public DbSet<GLMainAccount> GLMainAccounts { get; set; }
        public DbSet<GLChartOfAccount> GLChartOfAccounts { get; set; }
        public DbSet<GLBatch> GLBatches { get; set; }
        public DbSet<GLJournalHeader> GLJournalHeaders { get; set; }
        public DbSet<GLJournalLine> GLJournalLines { get; set; }
        public DbSet<GLTransaction> GLTransactions { get; set; } // If you use a flattened view
        public DbSet<BankReconciliation> BankReconciliations { get; set; }
        public DbSet<BankStatementLine> BankStatementLines { get; set; }

        // --- SUPPLY CHAIN ---
        public DbSet<Item> Items { get; set; }
        public DbSet<StockLedger> StockLedgers { get; set; }
        public DbSet<StockTransfer> StockTransfers { get; set; }
        public DbSet<SalesOrder> SalesOrders { get; set; }
        public DbSet<SalesOrderLine> SalesOrderLines { get; set; }
        public DbSet<CustomerPayment> CustomerPayments { get; set; }
        public DbSet<PaymentApplication> PaymentApplications { get; set; }
        public DbSet<VendorBill> VendorBills { get; set; }
        public DbSet<VendorBillLine> VendorBillLines { get; set; }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Vendor> Vendors { get; set; }
        public DbSet<PurchaseOrder> PurchaseOrders { get; set; }
        public DbSet<GoodsReceipt> GoodsReceipts { get; set; }
        public DbSet<SalesInvoice> SalesInvoices { get; set; }
        public DbSet<SalesInvoiceLine> SalesInvoiceLines { get; set; }
        public DbSet<PurchaseOrderLine> PurchaseOrderLines { get; set; }
        public DbSet<CashbookBatch> CashbookBatches { get; set; }
        public DbSet<CashbookEntry> CashbookEntries { get; set; }
        public DbSet<FinancialDashboardSnapshot> FinancialDashboardSnapshots { get; set; }
        public DbSet<ItemCostHistory> ItemCostHistories { get; set; }
        public DbSet<WaccHistory> WaccHistories { get; set; }
        public DbSet<FixedAsset> FixedAssets { get; set; }
        public DbSet<AssetDepreciationHistory> AssetDepreciationHistories { get; set; }
        public DbSet<BudgetHeader> BudgetHeaders { get; set; }
        public DbSet<BudgetLine> BudgetLines { get; set; }

        // --- SYSTEM ---
        public DbSet<AuditLog> AuditLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            foreach (var property in builder.Model.GetEntityTypes()
                .SelectMany(t => t.GetProperties())
                .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
            {
                property.SetColumnType("decimal(18, 6)");
            }

            foreach (var relationship in builder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            {
                if (relationship.PrincipalEntityType.ClrType == typeof(CompanyDetails))
                {
                    relationship.DeleteBehavior = DeleteBehavior.Restrict;
                }
            }

            // Specific GL Hierarchies
            builder.Entity<GLMainAccount>()
                .HasOne(m => m.AccountType)
                .WithMany()
                .HasForeignKey(m => m.AccountTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<GLChartOfAccount>()
                .HasOne(c => c.MainAccount)
                .WithMany()
                .HasForeignKey(c => c.MainAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            // GL Entries (Header -> Lines is safe to Cascade, but Batch -> Header might not be)
            builder.Entity<GLJournalHeader>()
                .HasOne(h => h.Batch)
                .WithMany(b => b.Journals)
                .HasForeignKey(h => h.BatchId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<GLJournalLine>()
                .HasOne(l => l.Header)
                .WithMany(h => h.Lines)
                .HasForeignKey(l => l.HeaderId)
                .OnDelete(DeleteBehavior.Cascade);

            // Warehousing (Prevent Circular Paths)
            builder.Entity<StockTransfer>()
                .HasOne(t => t.FromWarehouse)
                .WithMany()
                .HasForeignKey(t => t.FromWarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<StockTransfer>()
                .HasOne(t => t.ToWarehouse)
                .WithMany()
                .HasForeignKey(t => t.ToWarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            // Sales (Protect History)
            builder.Entity<SalesOrder>()
                .HasOne(s => s.Customer)
                .WithMany()
                .HasForeignKey(s => s.CustomerId)
                .OnDelete(DeleteBehavior.Restrict); // Don't delete customer if they have orders

            builder.Entity<SalesOrderLine>()
                .HasOne(l => l.Item)
                .WithMany()
                .HasForeignKey(l => l.ItemId)
                .OnDelete(DeleteBehavior.Restrict); // Don't delete Item if it's on an order

         
            // Ensure Sales Order Numbers are unique PER COMPANY
            builder.Entity<SalesOrder>()
                .HasIndex(s => new { s.CompanyId, s.OrderNumber })
                .IsUnique();

            // Ensure SKU is unique PER COMPANY
            builder.Entity<Item>()
                .HasIndex(i => new { i.CompanyId, i.SKU })
                .IsUnique();
        }

        // 4. AUTOMATIC AUDITING (Populate AuditLogs)
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            // Get modified entries
            var entries = ChangeTracker.Entries()
                .Where(e => e.Entity is not AuditLog && (e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted));


            foreach (var entry in entries)
            {
                
            }

            return await base.SaveChangesAsync(cancellationToken);
        }
    }
}