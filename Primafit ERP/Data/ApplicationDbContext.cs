using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data.Seed;
using System.Reflection.Emit;

namespace PrimafitERP.Data
{
    public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // --- MASTER DATA ---
        public DbSet<CompanyDetails> CompanyDetails { get; set; }
        public DbSet<Currency> Currencies { get; set; }
        public DbSet<CurrencyManagement> CurrencyManagements { get; set; }
        public DbSet<Tax> Taxes { get; set; }
        public DbSet<WithholdingTax> WithholdingTaxes { get; set; }
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
        public DbSet<GLTransaction> GLTransactions { get; set; }
        public DbSet<BankReconciliation> BankReconciliations { get; set; }
        public DbSet<BankStatementLine> BankStatementLines { get; set; }

        // --- SUPPLY CHAIN ---
        public DbSet<Item> Items { get; set; }
        public DbSet<UnitOfMeasure> UnitOfMeasures { get; set; }
        public DbSet<UomConversionRule> UomConversionRules { get; set; }
        public DbSet<ItemUomConversionLine> ItemUomConversionLines { get; set; }
        public DbSet<ReleasedDocumentNumber> ReleasedDocumentNumbers { get; set; }
        public DbSet<StockLedger> StockLedgers { get; set; }
        public DbSet<StockTransfer> StockTransfers { get; set; }
        public DbSet<StockTransferLine> StockTransferLines { get; set; }
        public DbSet<SalesOrder> SalesOrders { get; set; }
        public DbSet<SalesOrderLine> SalesOrderLines { get; set; }
        public DbSet<SalesQuote> SalesQuotes { get; set; }
        public DbSet<SalesQuoteLine> SalesQuoteLines { get; set; }
        public DbSet<CustomerPayment> CustomerPayments { get; set; }
        public DbSet<DebitNote> DebitNotes => Set<DebitNote>();
        public DbSet<DebitNoteLine> DebitNoteLines => Set<DebitNoteLine>();
        public DbSet<PaymentApplication> PaymentApplications { get; set; }
        public DbSet<PurchaseInvoice> PurchaseInvoices { get; set; }
        public DbSet<PurchaseInvoiceLine> PurchaseInvoiceLines { get; set; }
        public DbSet<VendorPayment> VendorPayments { get; set; }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Vendor> Vendors { get; set; }
        public DbSet<PurchaseOrder> PurchaseOrders { get; set; }
        public DbSet<GoodsReceipt> GoodsReceipts { get; set; }
        public DbSet<GoodsReceiptLine> GoodsReceiptLines { get; set; }
        public DbSet<GoodsReceiptDraft> GoodsReceiptDrafts { get; set; }
        public DbSet<GoodsReceiptDraftLine> GoodsReceiptDraftLines { get; set; }
        public DbSet<PurchaseRequest> PurchaseRequests { get; set; }
        public DbSet<PurchaseRequestLine> PurchaseRequestLines { get; set; }
        public DbSet<SalesInvoice> SalesInvoices { get; set; }
        public DbSet<SalesInvoiceLine> SalesInvoiceLines { get; set; }
        public DbSet<PurchaseOrderLine> PurchaseOrderLines { get; set; }
        public DbSet<CashbookBatch> CashbookBatches { get; set; }
        public DbSet<CashbookEntry> CashbookEntries { get; set; }
        public DbSet<ItemCostHistory> ItemCostHistories { get; set; }
        public DbSet<WaccHistory> WaccHistories { get; set; }
        public DbSet<FixedAsset> FixedAssets { get; set; }
        public DbSet<AssetDepreciationHistory> AssetDepreciationHistories { get; set; }
        public DbSet<BudgetHeader> BudgetHeaders { get; set; }
        public DbSet<BudgetLine> BudgetLines { get; set; }
        public DbSet<ParsedStatementRow> ParsedStatementRows { get; set; }
        public DbSet<Segment0> Segment0s { get; set; }
        public DbSet<Segment1> Segment1s { get; set; }
        public DbSet<Segment2> Segment2s { get; set; }
        public DbSet<Segment3> Segment3s { get; set; }
        public DbSet<Segment4> Segment4s { get; set; }
        public DbSet<Segment5> Segment5s { get; set; }
        public DbSet<CreditNote> CreditNotes { get; set; }
        public DbSet<CreditNoteLine> CreditNoteLines { get; set; }
        public DbSet<Permission> Permissions { get; set; }
        public DbSet<SystemRoleTemplate> SystemRoleTemplates { get; set; }
        public DbSet<SystemRolePermission> SystemRolePermissions { get; set; }
        public DbSet<UserSystemRole> UserSystemRoles { get; set; }
        public DbSet<CompanyRolePermission> CompanyRolePermissions { get; set; }
        public DbSet<SegCoaConfig> SegCoaConfigs => Set<SegCoaConfig>();
        public DbSet<SegChartOfAccount> SegChartOfAccounts => Set<SegChartOfAccount>();
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<CustomerGroup> CustomerGroups { get; set; }
        public DbSet<VendorGroup> VendorGroups { get; set; }
        public DbSet<ItemCategory> ItemCategories { get; set; }
        public DbSet<GrnLandedCost> GrnLandedCosts { get; set; }
        public DbSet<AssetCategory> AssetCategories { get; set; }
        public DbSet<AssetUsageLog> AssetUsageLogs { get; set; }
        public DbSet<SalesShipment> SalesShipments { get; set; }
        public DbSet<SalesShipmentLine> SalesShipmentLines { get; set; }
        public DbSet<VendorReturn> VendorReturns => Set<VendorReturn>();
        public DbSet<VendorReturnLine> VendorReturnLines => Set<VendorReturnLine>();
        // HR & Payroll
        public DbSet<Branch> Branches { get; set; }
        public DbSet<Department> Departments { get; set; }
        public DbSet<JobRole> JobRoles { get; set; }
        public DbSet<EmployeeSalaryStructure> SalaryStructures { get; set; }
        public DbSet<Employee> Employees { get; set; }
        public DbSet<PayrollRun> PayrollRuns { get; set; }
        public DbSet<PayrollItem> PayrollItems { get; set; }
        public DbSet<PayrollSetting> PayrollSettings { get; set; }
        public DbSet<PayrollEarning> PayrollEarnings { get; set; }
        public DbSet<PayrollDeduction> PayrollDeductions { get; set; }
        public DbSet<TransactionGlMapping> TransactionGlMappings { get; set; }
        public DbSet<CompanyEmailSetting> CompanyEmailSettings { get; set; }
        public DbSet<CustomTransactionType> CustomTransactionTypes { get; set; }
        public DbSet<ReceiptRefund> ReceiptRefunds { get; set; }
        public DbSet<ReceiptRefundLine> ReceiptRefundLines { get; set; }
        public DbSet<SequenceDefinition> SequenceDefinitions => Set<SequenceDefinition>();
        public DbSet<SequenceComponent> SequenceComponents => Set<SequenceComponent>();
        public DbSet<SequenceTracker> SequenceTrackers => Set<SequenceTracker>();
        public DbSet<DimensionType> DimensionTypes => Set<DimensionType>();
        public DbSet<DimensionValue> DimensionValues => Set<DimensionValue>();
        public DbSet<DimensionSet> DimensionSets => Set<DimensionSet>();
        public DbSet<DimensionSetValue> DimensionSetValues => Set<DimensionSetValue>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            foreach (var property in builder.Model.GetEntityTypes()
                .SelectMany(t => t.GetProperties())
                .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
            {
                // Transactional amounts use four decimal places consistently.
                property.SetColumnType("decimal(18, 4)");
            }

            foreach (var relationship in builder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            {
                if (relationship.PrincipalEntityType.ClrType == typeof(CompanyDetails))
                {
                    relationship.DeleteBehavior = DeleteBehavior.Restrict;
                }
            }

            // Unposted/rejected linked inventory movements must not affect any
            // balance, report, or availability query. Null batch IDs are kept
            // visible for historical ledger rows created before GL linking.
            builder.Entity<StockLedger>().HasQueryFilter(x =>
                x.GLBatchId == null || GLBatches.Any(batch => batch.Id == x.GLBatchId
                    && batch.CompanyId == x.CompanyId && batch.Status == BatchStatus.Posted));

            builder.Entity<UomConversionRule>()
                .HasIndex(x => new { x.CompanyId, x.FromUomId, x.ToUomId })
                .IsUnique();
            builder.Entity<UomConversionRule>()
                .HasOne(x => x.FromUom).WithMany().HasForeignKey(x => x.FromUomId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SequenceDefinition>()
                .HasOne<CompanyDetails>()
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .HasPrincipalKey(x => x.CompanyDetailsId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<SequenceDefinition>()
                .HasIndex(x => new { x.CompanyId, x.TargetType, x.Name })
                .IsUnique();
            builder.Entity<SequenceDefinition>()
                .HasIndex(x => new { x.CompanyId, x.TargetType })
                .IsUnique()
                .HasFilter("[IsActive] = 1");
            builder.Entity<SequenceDefinition>()
                .HasMany(x => x.Components)
                .WithOne(x => x.SequenceDefinition)
                .HasForeignKey(x => x.SequenceDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.Entity<SequenceComponent>()
                .HasIndex(x => new { x.SequenceDefinitionId, x.OrderIndex })
                .IsUnique();
            builder.Entity<SequenceTracker>()
                .HasIndex(x => new { x.SequenceDefinitionId, x.ScopePeriodKey })
                .IsUnique();
            builder.Entity<SequenceDefinition>().Property(x => x.TargetType).HasConversion<int>();
            builder.Entity<SequenceDefinition>().Property(x => x.ResetInterval).HasConversion<int>();
            builder.Entity<SequenceComponent>().Property(x => x.ComponentType).HasConversion<int>();
            builder.Entity<CustomerGroup>()
                .HasOne(x => x.SequenceDefinition).WithMany().HasForeignKey(x => x.SequenceDefinitionId)
                .OnDelete(DeleteBehavior.SetNull);
            builder.Entity<VendorGroup>()
                .HasOne(x => x.SequenceDefinition).WithMany().HasForeignKey(x => x.SequenceDefinitionId)
                .OnDelete(DeleteBehavior.SetNull);
            builder.Entity<ItemCategory>()
                .HasOne(x => x.ItemCodeSequenceDefinition).WithMany().HasForeignKey(x => x.ItemCodeSequenceDefinitionId)
                .OnDelete(DeleteBehavior.NoAction);
            builder.Entity<ItemCategory>().HasIndex(x => x.ItemCodeSequenceDefinitionId);
            builder.Entity<Customer>()
                .HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            builder.Entity<Vendor>()
                .HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            builder.Entity<ItemCategory>()
                .HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            builder.Entity<UomConversionRule>()
                .HasOne(x => x.ToUom).WithMany().HasForeignKey(x => x.ToUomId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<ItemUomConversionLine>()
                .HasIndex(x => new { x.ItemId, x.UomId }).IsUnique();
            builder.Entity<ItemUomConversionLine>()
                .HasOne(x => x.Uom).WithMany().HasForeignKey(x => x.UomId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<ItemUomConversionLine>()
                .HasOne(x => x.Item).WithMany(x => x.UomConversions).HasForeignKey(x => x.ItemId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Currency>()
                .HasIndex(x => x.CompanyId)
                .IsUnique()
                .HasFilter("[IsBaseCurrency] = 1");
            builder.Entity<Project>()
                .HasIndex(x => new { x.CompanyId, x.ProjectCode })
                .IsUnique()
                .HasFilter("[IsAccountingProject] = 1");
            builder.Entity<GLJournalLine>()
                .HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
            builder.Entity<GLTransaction>()
                .HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
            builder.Entity<StockLedger>()
                .HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
            builder.Entity<StockTransfer>()
                .HasMany(x => x.Lines).WithOne(x => x.Transfer).HasForeignKey(x => x.StockTransferId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.Entity<StockTransferLine>()
                .HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
            builder.Entity<StockTransferLine>()
                .HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId)
                .OnDelete(DeleteBehavior.NoAction);
            builder.Entity<SalesOrder>()
                .HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
            builder.Entity<SalesQuote>()
                .HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
            builder.Entity<PurchaseRequest>()
                .HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
            builder.Entity<PurchaseOrder>()
                .HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
            builder.Entity<GoodsReceipt>()
                .HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
            builder.Entity<PurchaseInvoice>()
                .HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
            builder.Entity<VendorPayment>()
                .HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
            builder.Entity<CreditNote>()
                .HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
            builder.Entity<DebitNote>()
                .HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
            builder.Entity<ReceiptRefund>()
                .HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.Entity<PurchaseRequest>().ToTable("PurchaseRequests");
            builder.Entity<PurchaseRequestLine>().ToTable("PurchaseRequestLines");
            builder.Entity<SalesQuote>().ToTable("SalesQuotes");
            builder.Entity<SalesQuoteLine>().ToTable("SalesQuoteLines");
            builder.Entity<PurchaseInvoice>().ToTable("PurchaseInvoices");
            builder.Entity<PurchaseInvoiceLine>().ToTable("PurchaseInvoiceLines");
            builder.Entity<SalesInvoice>().ToTable("SalesInvoices");
            builder.Entity<SalesInvoiceLine>().ToTable("SalesInvoiceLines");

            builder.Entity<PurchaseRequest>()
                .HasOne(x => x.Vendor).WithMany().HasForeignKey(x => x.VendorId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<PurchaseRequest>()
                .HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<PurchaseRequest>()
                .HasOne(x => x.Currency).WithMany().HasForeignKey(x => x.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<PurchaseRequestLine>()
                .HasOne(x => x.PurchaseRequest).WithMany(x => x.Lines)
                .HasForeignKey(x => x.PurchaseRequestId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<PurchaseRequestLine>()
                .HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<PurchaseRequest>()
                .HasIndex(x => new { x.CompanyId, x.RequestNumber }).IsUnique();
            builder.Entity<PurchaseOrder>()
                .HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<PurchaseOrderLine>()
                .HasOne(x => x.PurchaseRequestLine).WithMany()
                .HasForeignKey(x => x.PurchaseRequestLineId).OnDelete(DeleteBehavior.NoAction);
            builder.Entity<PurchaseOrderLine>()
                .HasIndex(x => x.PurchaseRequestLineId).IsUnique()
                .HasFilter("[PurchaseRequestLineId] IS NOT NULL");
            builder.Entity<GoodsReceipt>()
                .HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<GoodsReceiptLine>()
                .HasOne(x => x.PurchaseOrderLine).WithMany().HasForeignKey(x => x.PurchaseOrderLineId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<GoodsReceiptLine>()
                .HasIndex(x => new { x.GoodsReceiptId, x.PurchaseOrderLineId }).IsUnique()
                .HasFilter("[PurchaseOrderLineId] IS NOT NULL");
            builder.Entity<GoodsReceiptDraftLine>()
                .HasOne(x => x.GoodsReceiptDraft).WithMany(x => x.Lines)
                .HasForeignKey(x => x.GoodsReceiptDraftId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<GoodsReceiptDraftLine>()
                .HasOne(x => x.PurchaseOrderLine).WithMany()
                .HasForeignKey(x => x.PurchaseOrderLineId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<GoodsReceiptDraftLine>()
                .HasIndex(x => new { x.GoodsReceiptDraftId, x.PurchaseOrderLineId }).IsUnique().HasFilter("[PurchaseOrderLineId] IS NOT NULL");
            builder.Entity<GoodsReceiptDraftLine>()
                .HasOne(x => x.PurchaseInvoiceLine).WithMany().HasForeignKey(x => x.PurchaseInvoiceLineId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<GoodsReceiptDraftLine>()
                .HasIndex(x => new { x.GoodsReceiptDraftId, x.PurchaseInvoiceLineId }).IsUnique().HasFilter("[PurchaseInvoiceLineId] IS NOT NULL");
            builder.Entity<GoodsReceiptLine>()
                .HasOne(x => x.PurchaseInvoiceLine).WithMany().HasForeignKey(x => x.PurchaseInvoiceLineId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<GoodsReceiptLine>()
                .HasIndex(x => new { x.GoodsReceiptId, x.PurchaseInvoiceLineId }).IsUnique().HasFilter("[PurchaseInvoiceLineId] IS NOT NULL");
            builder.Entity<PurchaseInvoice>()
                .HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<PurchaseInvoiceLine>()
                .HasOne(x => x.PurchaseInvoice).WithMany(x => x.Lines)
                .HasForeignKey(x => x.PurchaseInvoiceId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<PurchaseInvoiceLine>()
                .HasOne(x => x.GoodsReceiptLine).WithMany()
                .HasForeignKey(x => x.GoodsReceiptLineId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<PurchaseInvoiceLine>()
                .HasOne(x => x.PurchaseOrderLine).WithMany()
                .HasForeignKey(x => x.PurchaseOrderLineId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<PurchaseInvoiceLine>()
                .HasIndex(x => x.GoodsReceiptLineId).IsUnique()
                .HasFilter("[GoodsReceiptLineId] IS NOT NULL");

            builder.Entity<SalesQuote>()
                .HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<SalesQuote>()
                .HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<SalesQuote>()
                .HasOne(x => x.Currency).WithMany().HasForeignKey(x => x.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<SalesQuoteLine>()
                .HasOne(x => x.SalesQuote).WithMany(x => x.Lines)
                .HasForeignKey(x => x.SalesQuoteId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<SalesQuoteLine>()
                .HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<SalesQuote>()
                .HasIndex(x => new { x.CompanyId, x.QuoteNumber }).IsUnique();
            builder.Entity<SalesOrderLine>()
                .HasOne(x => x.SalesQuoteLine).WithMany()
                .HasForeignKey(x => x.SalesQuoteLineId).OnDelete(DeleteBehavior.NoAction);
            builder.Entity<SalesOrderLine>()
                .HasIndex(x => x.SalesQuoteLineId).IsUnique()
                .HasFilter("[SalesQuoteLineId] IS NOT NULL");
            builder.Entity<SalesInvoice>()
                .HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<SalesInvoice>()
                .HasOne(x => x.Currency).WithMany().HasForeignKey(x => x.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<SalesInvoice>()
                .HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<SalesInvoiceLine>()
                .HasOne(x => x.SalesInvoice).WithMany(x => x.Lines)
                .HasForeignKey(x => x.SalesInvoiceId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<SalesInvoiceLine>().Property(x => x.LegacyAmount).HasColumnName("Amount");
            builder.Entity<SalesInvoiceLine>()
                .HasOne(x => x.ShipmentLine).WithMany()
                .HasForeignKey(x => x.ShipmentLineId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<SalesInvoiceLine>()
                .HasOne(x => x.SalesOrderLine).WithMany()
                .HasForeignKey(x => x.SalesOrderLineId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<SalesInvoiceLine>()
                .HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<SalesInvoiceLine>()
                .HasIndex(x => x.ShipmentLineId).IsUnique()
                .HasFilter("[ShipmentLineId] IS NOT NULL");
            builder.Entity<PurchaseInvoice>()
                .HasIndex(x => new { x.CompanyId, x.PurchaseInvoiceNumber }).IsUnique();
            builder.Entity<SalesInvoice>()
                .HasIndex(x => new { x.CompanyId, x.InvoiceNumber }).IsUnique();
            builder.Entity<VendorReturn>()
                .HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
            builder.Entity<PaymentApplication>()
                .HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.NoAction);
            builder.Entity<CurrencyManagement>()
                .HasOne(x => x.Currency).WithMany().HasForeignKey(x => x.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<CurrencyManagement>()
                .HasOne(x => x.ExchangeCurrencyRecord).WithMany().HasForeignKey(x => x.ExchangeCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<SegChartOfAccount>()
                .HasOne(x => x.Currency).WithMany().HasForeignKey(x => x.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);


            builder.Entity<CreditNote>(entity =>
            {

                entity.HasOne(c => c.Customer)
                      .WithMany()
                      .HasForeignKey(c => c.CustomerId)
                      .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne(c => c.SalesOrder)
                      .WithMany()
                      .HasForeignKey(c => c.SalesOrderId)
                      .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne(c => c.Warehouse)
                      .WithMany()
                      .HasForeignKey(c => c.WarehouseId)
                      .OnDelete(DeleteBehavior.NoAction);

                // 4. Ensure Unique Number per Company
                entity.HasIndex(c => new { c.CompanyId, c.CreditNoteNumber })
                      .IsUnique();
            });
            builder.Entity<ReceiptRefund>()
                .HasMany(r => r.Lines)
                .WithOne(l => l.Header)
                .HasForeignKey(l => l.HeaderId)
                .OnDelete(DeleteBehavior.Cascade);

            // A refund line is linked to the original sales-order line for
            // traceability. It must not be cascade-deleted through the sales
            // order because the refund already has its own header cascade
            // path. SQL Server rejects the resulting multiple cascade paths
            // when a new database is created.
            builder.Entity<ReceiptRefundLine>()
                .HasOne(l => l.SalesOrderLine)
                .WithMany()
                .HasForeignKey(l => l.SalesOrderLineId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.Entity<CreditNote>().HasOne(x => x.SalesInvoice).WithMany().HasForeignKey(x => x.SalesInvoiceId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<CreditNoteLine>().HasOne(x => x.SalesInvoiceLine).WithMany().HasForeignKey(x => x.SalesInvoiceLineId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<DebitNote>().HasOne(x => x.PurchaseInvoice).WithMany().HasForeignKey(x => x.PurchaseInvoiceId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<DebitNoteLine>().HasOne(x => x.PurchaseInvoiceLine).WithMany().HasForeignKey(x => x.PurchaseInvoiceLineId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<ReceiptRefund>().HasOne(x => x.SalesInvoice).WithMany().HasForeignKey(x => x.SalesInvoiceId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<ReceiptRefundLine>().HasOne(x => x.SalesInvoiceLine).WithMany().HasForeignKey(x => x.SalesInvoiceLineId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<VendorReturn>().HasOne(x => x.PurchaseInvoice).WithMany().HasForeignKey(x => x.PurchaseInvoiceId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<VendorReturnLine>().HasOne(x => x.PurchaseInvoiceLine).WithMany().HasForeignKey(x => x.PurchaseInvoiceLineId).OnDelete(DeleteBehavior.Restrict);

            // Credit Note Lines can cascade (if header dies, lines die)
            builder.Entity<CreditNoteLine>()
                   .HasOne(l => l.Header)
                   .WithMany(h => h.Lines)
                   .HasForeignKey(l => l.HeaderId)
                   .OnDelete(DeleteBehavior.Cascade);
            builder.Entity<CreditNoteLine>()
                   .HasOne(l => l.Item)
                   .WithMany()
                   .HasForeignKey(l => l.ItemId)
                   .OnDelete(DeleteBehavior.NoAction);



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

            builder.Entity<SalesOrder>()
                .HasOne(s => s.Customer)
                .WithMany()
                .HasForeignKey(s => s.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SalesShipment>()
                .HasOne(s => s.SalesOrder).WithMany()
                .HasForeignKey(s => s.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<SalesShipmentLine>()
                .HasOne(s => s.SalesOrderLine).WithMany()
                .HasForeignKey(s => s.SalesOrderLineId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<SalesShipmentLine>()
                .HasIndex(s => new { s.ShipmentId, s.SalesOrderLineId }).IsUnique()
                .HasFilter("[SalesOrderLineId] IS NOT NULL");
            builder.Entity<SalesShipmentLine>()
                .HasOne(s => s.SalesInvoiceLine).WithMany()
                .HasForeignKey(s => s.SalesInvoiceLineId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<SalesShipmentLine>()
                .HasIndex(s => new { s.ShipmentId, s.SalesInvoiceLineId }).IsUnique()
                .HasFilter("[SalesInvoiceLineId] IS NOT NULL");

            builder.Entity<SalesOrderLine>()
                .HasOne(l => l.Item)
                .WithMany()
                .HasForeignKey(l => l.ItemId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SalesOrder>()
                .HasIndex(s => new { s.CompanyId, s.OrderNumber })
                .IsUnique();

            builder.ApplyConfiguration(new SegAccountTypeSeed());
            builder.Entity<SegChartOfAccount>()
                .HasIndex(x => new { x.CompanyId, x.AccountCode })
                .IsUnique();

            builder.Entity<SegCoaConfig>()
                .HasIndex(x => x.CompanyId)
                .IsUnique();
            builder.Entity<SystemRolePermission>()
        .HasKey(srp => new { srp.SystemRoleTemplateId, srp.PermissionId });

            builder.Entity<UserSystemRole>()
                .HasKey(usr => new { usr.UserId, usr.SystemRoleTemplateId, usr.CompanyId });

            builder.Entity<CompanyRolePermission>()
                .HasKey(crp => new { crp.ApplicationRoleId, crp.PermissionId });
            builder.Entity<PurchaseInvoice>()
                .HasOne(b => b.Vendor)
                .WithMany()
                .HasForeignKey(b => b.VendorId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<PurchaseInvoice>()
                .HasOne(b => b.PurchaseOrder)
                .WithMany()
                .HasForeignKey(b => b.PurchaseOrderId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<UnitOfMeasure>()
                .Property(u => u.Code).HasMaxLength(32).IsRequired(false);
            builder.Entity<UnitOfMeasure>()
                .HasIndex(u => new { u.CompanyId, u.Code }).IsUnique().HasFilter("[Code] IS NOT NULL");

            builder.Entity<UnitOfMeasure>()
                .HasOne(u => u.ConversionUom)
                .WithMany()
                .HasForeignKey(u => u.ConversionUomId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Item>()
                .HasOne(i => i.PrimaryUom)
                .WithMany()
                .HasForeignKey(i => i.UomId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Item>()
                .HasOne(i => i.AlternateUom)
                .WithMany()
                .HasForeignKey(i => i.AlternateUomId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<DebitNote>(entity =>
            {
                entity.HasOne(d => d.Vendor)
                      .WithMany()
                      .HasForeignKey(d => d.VendorId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(d => d.PurchaseOrder)
                      .WithMany()
                      .HasForeignKey(d => d.PurchaseOrderId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(d => d.Currency)
                      .WithMany()
                      .HasForeignKey(d => d.CurrencyId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(d => d.Warehouse)
                      .WithMany()
                      .HasForeignKey(d => d.WarehouseId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // Also safeguard DebitNoteLine foreign keys
            builder.Entity<DebitNoteLine>(entity =>
            {
                entity.HasOne(l => l.Item)
                      .WithMany()
                      .HasForeignKey(l => l.ItemId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(l => l.Header)
                      .WithMany(h => h.Lines)
                      .HasForeignKey(l => l.HeaderId)
                      .OnDelete(DeleteBehavior.Cascade); // Deleting the header deletes lines
            });
            builder.Entity<VendorReturn>(entity =>
            {
                entity.HasOne(v => v.Vendor)
                      .WithMany()
                      .HasForeignKey(v => v.VendorId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(v => v.PurchaseOrder)
                      .WithMany()
                      .HasForeignKey(v => v.PurchaseOrderId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(v => v.Warehouse)
                      .WithMany()
                      .HasForeignKey(v => v.WarehouseId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(v => v.BankAccount)
                      .WithMany()
                      .HasForeignKey(v => v.BankAccountId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(v => v.Currency)
                      .WithMany()
                      .HasForeignKey(v => v.CurrencyId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<VendorReturnLine>(entity =>
            {
                entity.HasOne(l => l.Header)
                      .WithMany(h => h.Lines)
                      .HasForeignKey(l => l.HeaderId)
                      .OnDelete(DeleteBehavior.Cascade); // Deleting header drops its child lines

                entity.HasOne(l => l.Item)
                      .WithMany()
                      .HasForeignKey(l => l.ItemId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<Segment0>().HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            builder.Entity<Segment1>().HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            builder.Entity<Segment2>().HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            builder.Entity<Segment3>().HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            builder.Entity<Segment4>().HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            builder.Entity<Segment5>().HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            builder.Entity<ReceiptRefund>()
        .HasMany(r => r.Lines)
        .WithOne(l => l.Header)
        .HasForeignKey(l => l.HeaderId)
        .OnDelete(DeleteBehavior.Cascade);

            // 2. FIXED: Restrict parent Customer cascade pathway to resolve the cyclical collision
            builder.Entity<ReceiptRefund>()
                .HasOne(r => r.Customer)
                .WithMany() // Leave empty if Customer doesn't have an explicit virtual collection property
                .HasForeignKey(r => r.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            // 3. FIXED: Restrict parent SalesOrder cascade pathway to ensure transaction isolation
            builder.Entity<ReceiptRefund>()
                .HasOne(r => r.SalesOrder)
                .WithMany() // Leave empty if SalesOrder doesn't have an explicit virtual collection property
                .HasForeignKey(r => r.SalesOrderId)
                .OnDelete(DeleteBehavior.Restrict);

            // Index released numbers by company and document type.
            builder.Entity<ReleasedDocumentNumber>().HasIndex(x => new { x.CompanyId, x.TargetType });

            // A primary carton can contain, for example, 24 pieces. Preserve
            // fractional primary quantities through orders, movements and returns.
            foreach (var property in builder.Model.GetEntityTypes()
                .Where(x => x.ClrType.Assembly == typeof(Item).Assembly)
                .SelectMany(x => x.GetProperties())
                .Where(x => x.ClrType == typeof(decimal) || x.ClrType == typeof(decimal?)))
            {
                if (property.Name is "UomConversionFactor" or "ConversionFactorToBase") property.SetColumnType("decimal(28,16)");
                else if (property.Name.Contains("Quantity", StringComparison.Ordinal) || property.Name.Contains("Qty", StringComparison.Ordinal))
                    property.SetColumnType("decimal(28,12)");
            }

            DimensionModel.Configure(builder);
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var entries = ChangeTracker.Entries()
                .Where(e => e.Entity is not AuditLog && (e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted));

            foreach (var entry in entries)
            {
                // Auditing logic placeholder
            }

            return await base.SaveChangesAsync(cancellationToken);
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            DimensionPersistence.ValidateAsync(this).GetAwaiter().GetResult();
            BlockSegAccountTypeChanges();
            TrackDeletedDraftNumbers();
            var locks = ReleasedNumberLocks();
            if (locks.Count == 0) return base.SaveChanges(acceptAllChangesOnSuccess);
            using var transaction = Database.CurrentTransaction == null ? Database.BeginTransaction() : null;
            foreach (var resource in locks)
                Database.ExecuteSqlInterpolated($"DECLARE @result int; EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000; IF @result < 0 THROW 51000, 'Document numbering is busy. Please try again.', 1;");
            var count = base.SaveChanges(acceptAllChangesOnSuccess);
            transaction?.Commit();
            return count;
        }

        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            await DimensionPersistence.ValidateAsync(this, cancellationToken);
            BlockSegAccountTypeChanges();
            TrackDeletedDraftNumbers();
            var locks = ReleasedNumberLocks();
            if (locks.Count == 0) return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            await using var transaction = Database.CurrentTransaction == null ? await Database.BeginTransactionAsync(cancellationToken) : null;
            foreach (var resource in locks)
                await Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000; IF @result < 0 THROW 51000, 'Document numbering is busy. Please try again.', 1;", cancellationToken);
            var count = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            if (transaction != null) await transaction.CommitAsync(cancellationToken);
            return count;
        }

        private List<string> ReleasedNumberLocks() => ChangeTracker.Entries<ReleasedDocumentNumber>()
            .Where(x => x.State == EntityState.Added).Select(x => ReleasedDocumentNumber.LockKey(x.Entity.CompanyId, x.Entity.TargetType))
            .Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();

        private void TrackDeletedDraftNumbers()
        {
            foreach (var entry in ChangeTracker.Entries().Where(x => x.State == EntityState.Deleted).ToList())
            {
                (Guid Company, SequenceTargetType Target, string Number, DateTime Date)? released = entry.Entity switch
                {
                    SalesQuote x when x.Status is SalesQuoteStatus.Draft or SalesQuoteStatus.Open => (x.CompanyId, SequenceTargetType.AR_Quote, x.QuoteNumber, x.Date.ToDateTime(TimeOnly.MinValue)),
                    PurchaseRequest x when x.Status is PurchaseRequestStatus.Draft or PurchaseRequestStatus.Open => (x.CompanyId, SequenceTargetType.AP_Request, x.RequestNumber, x.RequestDate),
                    SalesInvoice x when x.Status == SalesInvoiceStatus.Draft && !x.GLBatchId.HasValue => (x.CompanyId, SequenceTargetType.AR_Invoice, x.InvoiceNumber, x.Date.ToDateTime(TimeOnly.MinValue)),
                    PurchaseInvoice x when !x.IsPosted && !x.GLBatchId.HasValue => (x.CompanyId, SequenceTargetType.AP_Invoice, x.PurchaseInvoiceNumber, x.InvoiceDate),
                    SalesShipment x when x.Status == ShipmentStatus.Pending && !x.ShipmentBatchId.HasValue => (x.CompanyId, SequenceTargetType.AR_Shipment, x.ShipmentNumber, x.CreatedDate),
                    GoodsReceiptDraft x when !ChangeTracker.Entries<GoodsReceipt>().Any(r => r.State == EntityState.Added && r.Entity.CompanyId == x.CompanyId && r.Entity.GrnNumber == x.GrnNumber) => (x.CompanyId, SequenceTargetType.AP_GoodsReceipt, x.GrnNumber, x.DateReceived),
                    SalesOrder x when !x.InvoiceBatchId.HasValue && !x.ShipmentBatchId.HasValue && x.Status is OrderStatus.Draft or OrderStatus.Order or OrderStatus.Quote =>
                        (x.CompanyId, x.IsDirectInvoice || x.Status == OrderStatus.Draft ? SequenceTargetType.AR_Invoice : x.Status == OrderStatus.Quote ? SequenceTargetType.AR_Quote : SequenceTargetType.AR_Order, x.OrderNumber, x.Date.ToDateTime(TimeOnly.MinValue)),
                    PurchaseOrder x when !x.GLBatchId.HasValue && x.Status is PurchaseOrderStatus.Open or PurchaseOrderStatus.Request or PurchaseOrderStatus.DraftInvoice =>
                        (x.CompanyId, x.IsDirectInvoice || x.Status == PurchaseOrderStatus.DraftInvoice ? SequenceTargetType.AP_Invoice : x.Status == PurchaseOrderStatus.Request ? SequenceTargetType.AP_Request : SequenceTargetType.AP_PurchaseOrder, x.OrderNumber, x.OrderDate),
                    CreditNote x when x.Status == CreditNoteStatus.Draft && !x.GlBatchId.HasValue => (x.CompanyId, SequenceTargetType.AR_CreditNote, x.CreditNoteNumber, x.Date.ToDateTime(TimeOnly.MinValue)),
                    DebitNote x when x.Status == DebitNoteStatus.Draft && !x.GlBatchId.HasValue => (x.CompanyId, SequenceTargetType.AP_DebitNote, x.DebitNoteNumber, x.Date.ToDateTime(TimeOnly.MinValue)),
                    ReceiptRefund x when x.Status == ReceiptRefundStatus.Draft && !x.GlBatchId.HasValue => (x.CompanyId, SequenceTargetType.AR_ReceiptRefund, x.RefundNumber, x.Date.ToDateTime(TimeOnly.MinValue)),
                    VendorReturn x when x.Status == VendorReturnStatus.Draft && !x.GlBatchId.HasValue => (x.CompanyId, SequenceTargetType.AP_ReturnToVendor, x.ReturnNumber, x.Date.ToDateTime(TimeOnly.MinValue)),
                    CustomerPayment x when x.Status == PaymentStatus.Draft && !x.GLBatchId.HasValue => (x.CompanyId, SequenceTargetType.AR_CustomerReceipt, x.Reference, x.Date),
                    _ => null
                };
                if (released is not { } value || value.Company == Guid.Empty || string.IsNullOrWhiteSpace(value.Number) || value.Number.Length > 200) continue;
                if (!ChangeTracker.Entries<ReleasedDocumentNumber>().Any(x => x.State == EntityState.Added && x.Entity.CompanyId == value.Company && x.Entity.TargetType == value.Target && x.Entity.DocumentNumber == value.Number))
                    ReleasedDocumentNumbers.Add(new ReleasedDocumentNumber { CompanyId = value.Company, TargetType = value.Target, DocumentNumber = value.Number, DocumentDate = value.Date });
            }
        }

        private void BlockSegAccountTypeChanges()
        {
            var blocked = ChangeTracker.Entries<SegAccountType>()
                .Where(e => e.State == EntityState.Modified || e.State == EntityState.Deleted || e.State == EntityState.Added)
                .ToList();

            if (blocked.Any())
                throw new InvalidOperationException("SegAccountTypes is fixed lookup data and cannot be added/edited/deleted.");
        }
    }
}
