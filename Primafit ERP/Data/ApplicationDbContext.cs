using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using Primafit_ERP.Services;

namespace PrimafitERP.Data
{
    public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<CompanyDetails> CompanyDetails { get; set; }
        public DbSet<Project> Projects { get; set; }
        public DbSet<GLAccountType> GLAccountTypes { get; set; }
        public DbSet<GLMainAccount> GLMainAccounts { get; set; }
        public DbSet<GLChartOfAccount> GLChartOfAccounts { get; set; }
        public DbSet<Currency> Currencies { get; set; }
        public DbSet<CurrencyManagement> CurrencyManagements { get; set; }
        public DbSet<Tax> Taxes { get; set; }
        public DbSet<AccountingPeriod> AccountingPeriods { get; set; }
        public DbSet<GLBatch> GLBatches { get; set; }
        public DbSet<GLJournalHeader> GLJournalHeaders { get; set; }
        public DbSet<GLJournalLine> GLJournalLines { get; set; }
        public DbSet<GLTransaction> GLTransactions { get; set; }

        public DbSet<BankReconciliation> BankReconciliations { get; set; }
        public DbSet<BankStatementLine> BankStatementLines { get; set; }

        public DbSet<AuditLog> AuditLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // -----------------------------
            // STOP multi-cascade paths from Company
            // -----------------------------
            modelBuilder.Entity<AccountingPeriod>()
                .HasOne(p => p.Company)
                .WithMany()
                .HasForeignKey(p => p.CompanyId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<GLAccountType>()
                .HasOne(t => t.Company)
                .WithMany()
                .HasForeignKey(t => t.CompanyId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<GLMainAccount>()
                .HasOne(m => m.Company)
                .WithMany()
                .HasForeignKey(m => m.CompanyId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<GLChartOfAccount>()
                .HasOne(c => c.Company)
                .WithMany()
                .HasForeignKey(c => c.CompanyId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<GLBatch>()
                .HasOne(b => b.Company)
                .WithMany()
                .HasForeignKey(b => b.CompanyId)
                .OnDelete(DeleteBehavior.NoAction);

            
            modelBuilder.Entity<GLBatch>()
                .HasOne(b => b.AccountingPeriod)
                .WithMany()
                .HasForeignKey(b => b.AccountingPeriodId)
                .OnDelete(DeleteBehavior.NoAction);
            
            modelBuilder.Entity<GLMainAccount>()
                .HasOne(m => m.AccountType)
                .WithMany()
                .HasForeignKey(m => m.AccountTypeId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<GLChartOfAccount>()
                .HasOne(c => c.MainAccount)
                .WithMany()
                .HasForeignKey(c => c.MainAccountId)
                .OnDelete(DeleteBehavior.NoAction);

           
            modelBuilder.Entity<GLJournalHeader>()
                .HasOne(h => h.Batch)
                .WithMany(b => b.Journals)
                .HasForeignKey(h => h.BatchId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<GLJournalLine>()
                .HasOne(l => l.Header)
                .WithMany(h => h.Lines)
                .HasForeignKey(l => l.HeaderId)
                .OnDelete(DeleteBehavior.Cascade);
        }






        //protected override void OnModelCreating(ModelBuilder builder)
        //{
        //    base.OnModelCreating(builder);

        //    builder.Entity<GLChartOfAccount>()
        //        .HasOne(c => c.AccountType)
        //        .WithMany()
        //        .HasForeignKey(c => c.AccountTypeId)
        //        .OnDelete(DeleteBehavior.NoAction); 
        //}
    }
}