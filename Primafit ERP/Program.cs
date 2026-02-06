using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Primafit_ERP.Components;
using Primafit_ERP.Components.Models;
using Primafit_ERP.Services;
using PrimafitERP.Data;

var builder = WebApplication.CreateBuilder(args);

// --- 1. DATABASE CONFIGURATION ---
// Standard Context for Controllers/Identity
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Factory for Blazor Components (Scoped to prevent concurrency issues)
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")),
    ServiceLifetime.Scoped);

// --- 2. IDENTITY CONFIGURATION ---
builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options => {
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 6;
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = false; // Easier for MVP
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options => {
    options.LoginPath = "/login";
    options.ExpireTimeSpan = TimeSpan.FromDays(1);
});

// --- 3. UI & FRAMEWORK SERVICES ---
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options => options.DetailedErrors = true); // Add this
builder.Services.AddMudServices();
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR(e => { e.MaximumReceiveMessageSize = 10 * 1024 * 1024; });

// --- 4. APPLICATION SERVICES (Scoped) ---
// Core Auth & Identity
builder.Services.AddScoped<AuthService>(); // <--- NEW: Handles the Tenant Registration Logic
builder.Services.AddScoped<UserApiService>();
builder.Services.AddScoped<RoleApiService>();
builder.Services.AddScoped<CompanyApiService>();

// Operations
builder.Services.AddScoped<GLSetupService>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<CurrencyService>();
builder.Services.AddScoped<TaxService>();
builder.Services.AddScoped<AccountingPeriodService>();
builder.Services.AddScoped<GLOperationsService>();
builder.Services.AddScoped<ReconciliationService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<SalesService>();
builder.Services.AddScoped<WarehouseService>();
builder.Services.AddScoped<MasterDataService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<PurchasingService>();
builder.Services.AddScoped<AssetService>();
builder.Services.AddScoped<BudgetService>();
builder.Services.AddScoped<ICashbookService, CashbookService>();

// Security (Optional helper)
builder.Services.AddScoped<IPermissionGuard, PermissionGuard>();

var app = builder.Build();

// --- 5. HTTP REQUEST PIPELINE ---
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();