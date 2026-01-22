using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;

namespace PrimafitERP.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<CompanyDetails> CompanyDetails { get; set; }
    public DbSet<ApplicationUser> ApplicationUser { get; set; }
    public DbSet<ApplicationRole> ApplicationRoles { get; set; }
    public DbSet<GLMasterAccount> GLMasterAccounts { get; set; }
    public DbSet<GLSubAccount> GLSubAccounts { get; set; }
    public DbSet<Project> Projects { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        //  Single-Table Hierarchy Configuration
        modelBuilder.Entity<GLSubAccount>()
            .HasOne(s => s.MasterAccount)
            .WithMany(m => m.SubAccounts)
            .HasForeignKey(s => s.MasterAccountId)
            .OnDelete(DeleteBehavior.Cascade); // Prevent deleting a Master if Subs exist
    }


}
