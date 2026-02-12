using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Primafit_ERP.Components.Models;

namespace PrimafitERP.Data.Seed
{
    public sealed class SegAccountTypeSeed : IEntityTypeConfiguration<SegAccountType>
    {
        public void Configure(EntityTypeBuilder<SegAccountType> b)
        {
            b.ToTable("SegAccountTypes");
            b.HasKey(x => x.Id);

            b.Property(x => x.Description).HasMaxLength(150).IsRequired();

            // ---- SEED DATA (from your uploaded excel) ----
            b.HasData(
            new SegAccountType { Id = 1, Description = "Cash and Cash Equivalents", IsBalanceSheet = true, IsDebit = true },
            new SegAccountType { Id = 2, Description = "Other Expense", IsBalanceSheet = false, IsDebit = true },
            new SegAccountType { Id = 3, Description = "Other Current Asset", IsBalanceSheet = true, IsDebit = true },
            new SegAccountType { Id = 4, Description = "Other Income", IsBalanceSheet = false, IsDebit = false },
            new SegAccountType { Id = 5, Description = "Other Current Liability", IsBalanceSheet = true, IsDebit = false },
            new SegAccountType { Id = 6, Description = "Non Current Liability", IsBalanceSheet = true, IsDebit = false },
            new SegAccountType { Id = 7, Description = "Share Capital", IsBalanceSheet = true, IsDebit = false },
            new SegAccountType { Id = 8, Description = "Property, Plant and Equipment", IsBalanceSheet = true, IsDebit = true },
            new SegAccountType { Id = 9, Description = "Revenue", IsBalanceSheet = false, IsDebit = false },
            new SegAccountType { Id = 10, Description = "Cost of Sales", IsBalanceSheet = false, IsDebit = true },
            new SegAccountType { Id = 11, Description = "Retained Earnings", IsBalanceSheet = true, IsDebit = false },
            new SegAccountType { Id = 12, Description = "Tax Expense", IsBalanceSheet = false, IsDebit = true },
            new SegAccountType { Id = 13, Description = "Unallocated IS", IsBalanceSheet = false, IsDebit = true },
            new SegAccountType { Id = 14, Description = "Dividends Paid", IsBalanceSheet = false, IsDebit = true },
            new SegAccountType { Id = 15, Description = "Dividends Received", IsBalanceSheet = false, IsDebit = false },
            new SegAccountType { Id = 16, Description = "Profit/Loss on Sale of Non-Current Asset", IsBalanceSheet = false, IsDebit = false },
            new SegAccountType { Id = 17, Description = "Finance Cost", IsBalanceSheet = false, IsDebit = true },
            new SegAccountType { Id = 18, Description = "Profit/Loss On Exchange", IsBalanceSheet = false, IsDebit = false },
            new SegAccountType { Id = 19, Description = "Unallocated BS", IsBalanceSheet = true, IsDebit = true },
            new SegAccountType { Id = 20, Description = "Shareholders Loan", IsBalanceSheet = true, IsDebit = false },
            new SegAccountType { Id = 21, Description = "Other Non Current Liability", IsBalanceSheet = true, IsDebit = false },
            new SegAccountType { Id = 22, Description = "Investments", IsBalanceSheet = true, IsDebit = true },
            new SegAccountType { Id = 23, Description = "Other Fixed Assets", IsBalanceSheet = true, IsDebit = true },
            new SegAccountType { Id = 24, Description = "Inventories", IsBalanceSheet = true, IsDebit = true },
            new SegAccountType { Id = 25, Description = "Trade Receivables", IsBalanceSheet = true, IsDebit = true },
            new SegAccountType { Id = 26, Description = "Trade Payables", IsBalanceSheet = true, IsDebit = false },
            new SegAccountType { Id = 27, Description = "Taxation Liability", IsBalanceSheet = true, IsDebit = false },
            new SegAccountType { Id = 28, Description = "Deferred Tax", IsBalanceSheet = true, IsDebit = false },
            new SegAccountType { Id = 29, Description = "Non Distributable Reserves", IsBalanceSheet = true, IsDebit = false },
            new SegAccountType { Id = 30, Description = "Other Non Current Asset", IsBalanceSheet = true, IsDebit = true },
            new SegAccountType { Id = 31, Description = "Intangible Asset", IsBalanceSheet = true, IsDebit = true },
            new SegAccountType { Id = 32, Description = "Other Comprehensive Income", IsBalanceSheet = false, IsDebit = false },
            new SegAccountType { Id = 33, Description = "Investment Property", IsBalanceSheet = true, IsDebit = true },
            new SegAccountType { Id = 34, Description = "Financial Asset", IsBalanceSheet = true, IsDebit = true },
            new SegAccountType { Id = 35, Description = "Distribution Cost", IsBalanceSheet = false, IsDebit = true },
            new SegAccountType { Id = 36, Description = "Administration Expense", IsBalanceSheet = false, IsDebit = true }
            );
        }
    }
}
