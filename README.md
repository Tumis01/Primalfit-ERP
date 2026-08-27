- `SegAccountType` lookup data is blocked from add/edit/delete in `SaveChanges`.

### 2.6 Configuration

Both the Blazor app and API use `DefaultConnection` from `appsettings.json`.

Local development currently expects SQL Server and a database named `PrimalfitERPDbSoft`. Update the connection string for your local machine before running.

Important security note:

- Do not commit production secrets in `appsettings.json`.
- Move SMTP passwords, JWT signing keys, and production database credentials to user secrets, environment variables, Azure Key Vault, GitHub Actions secrets, or another secure secret store.
- Treat existing local values as development-only and rotate anything that has been shared outside the development environment.

Recommended local secret commands:

```powershell
dotnet user-secrets init --project "Primafit ERP/Primafit ERP.csproj"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=YOUR_SERVER;Database=PrimalfitERPDbSoft;Trusted_Connection=True;TrustServerCertificate=True" --project "Primafit ERP/Primafit ERP.csproj"

dotnet user-secrets init --project "PrimafitERP.Api/PrimafitERP.Api.csproj"
dotnet user-secrets set "Jwt:Key" "replace-with-a-long-random-development-key" --project "PrimafitERP.Api/PrimafitERP.Api.csproj"
```

### 2.7 Running Locally

Prerequisites:

- .NET 10 SDK
- SQL Server or SQL Server Express
- Node.js if you need to work on Tailwind/PostCSS assets

Restore and build:

```powershell
dotnet restore "Primafit ERP.slnx"
dotnet build "Primafit ERP.slnx"
```

Run the Blazor app:

```powershell
dotnet run --project "Primafit ERP/Primafit ERP.csproj" --launch-profile http
```

Default app URLs:

- HTTP: `http://localhost:5038`
- HTTPS: `https://localhost:7069`

Run the API:

```powershell
dotnet run --project "PrimafitERP.Api/PrimafitERP.Api.csproj" --launch-profile http
```

Default API URLs:

- HTTP: `http://localhost:5137`
- HTTPS: `https://localhost:7026`
- Swagger: `http://localhost:5137/swagger`

### 2.8 Database And Migrations

The Blazor app applies pending migrations on startup:

```csharp
if (context.Database.GetPendingMigrations().Any())
{
    context.Database.Migrate();
}
```

After migrations, the app runs RBAC seeding:

```csharp
await Primafit_ERP.Data.Seed.RbacSeeder.SeedAsync(context, roleManager);
```

Common EF commands:

```powershell
dotnet ef migrations add MigrationName --project "Primafit ERP/Primafit ERP.csproj"
dotnet ef database update --project "Primafit ERP/Primafit ERP.csproj"
```

When adding new entities:

1. Add the model under `Components/Models` or the shared Core model location used by the target module.
2. Add a `DbSet` to `AppDbContext`.
3. Add relationship, index, and delete behavior rules in `OnModelCreating` if needed.
4. Create a migration.
5. Update services and UI/API entry points.

### 2.9 Services

Most domain behavior is implemented in scoped services. Examples include:

- `AuthService`: tenant/user registration logic.
- `CompanyApiService`, `UserApiService`, `RoleApiService`: company, users, roles, and permissions.
- `GLSetupService`, `GLOperationsService`, `SegCoaService`, `SegmentsSetupService`: finance setup and GL operations.
- `ReconciliationService`, `StatementImportService`, `CsvStatementParser`, `ExcelStatementParser`: bank reconciliation.
- `SalesService`, `PaymentService`, `CreditNoteService`, `ReceiptRefundService`, `ShipmentService`: AR/sales workflows.
- `PurchasingService`, `PurchaseReturnService`: AP/purchasing workflows.
- `InventoryService`, `InventoryValuationService`, `WarehouseService`, `MasterDataService`: stock and master data workflows.
- `AssetService`, `AssetCategoryService`, `DepreciationWorker`: fixed assets.
- `BudgetService`: budgeting.
- `PayrollService`, `PayrollJournalService`, `EmployeeService`, `PayrollSettingsService`, `PayrollReportingService`, `PayslipService`, `HrSetupService`, `ComplianceService`: HR/payroll.
- `OperationalReportingService`, `FinancialReportingService`, `ReportExportService`: reports and export output.
- `TransactionMappingService`: GL routing for system and custom transactions.
- `EmailService`: outgoing email.

When adding a service:

1. Keep business rules in the service rather than directly in the Razor page.
2. Register the service in `Program.cs`.
3. Inject it into the page/controller that needs it.
4. Use `IDbContextFactory<AppDbContext>` for service database work to avoid Blazor Server DbContext concurrency issues.

### 2.10 API Surface

The API project exposes REST controllers for integration workflows:

- `AuthController`: API login and JWT generation.
- `SalesController`: create sales orders, convert to invoice, ship goods, and post invoices.
- `ProcurementController`: create purchase orders, auto invoice POs, and receive goods.
- `InventoryController`: stock lookup, direct receipt, adjustments, and transfers.
- `CashbookController`: cashbook batches and entries.
- `JournalEntryController`: journal posting.
- `CustomerReceiptController`: customer payment receipt.
- `CreditNoteController`: shipped/paid invoice lookup, credit note drafts, stock returns, and financial refunds.
- `ShipmentController`: shipment generation and posting.
- `VendorsPaymentController`: manual vendor bills and vendor payments.
- `CompanyController`: company data access.
- `ChartOfAccountsController`: chart of accounts access.

Use Swagger during development to inspect exact request/response shapes:

```text
http://localhost:5137/swagger
```

### 2.11 Frontend Routing And Navigation

Pages are under `Primafit ERP/Components/Pages` and declare routes using `@page`.

Navigation is centralized in:

```text
Primafit ERP/Components/Layout/NavMenu.razor
```

When adding a new page:

1. Create a Razor page in `Components/Pages`.
2. Add an `@page` route.
3. Add or reuse a service method for business logic.
4. Add a `NavLink` in `NavMenu.razor` if the page should be user-accessible.
5. Wrap the menu link or sensitive page actions in `PermissionGate` when access should be restricted.
6. Update RBAC seed data if the page requires a new permission key.

### 2.12 RBAC And Permission Keys

RBAC is implemented through:

- Identity roles.
- Permission records.
- System role templates.
- Company role permission mappings.
- User system roles.
- `PermissionCacheService`.
- `PermissionGate.razor`.

Developer checklist for a new permission:

1. Define the permission in the RBAC seed.
2. Add it to the appropriate role template or company role assignment.
3. Use the permission key in `PermissionGate`.
4. Reload user permissions after role changes.
5. Test with a user that has access and a user that does not.

### 2.13 Reporting And Exports

Reporting services generate operational and financial reports. Export helpers use spreadsheet and PDF libraries.

Common report areas:

- financial reports: trial balance, profit and loss, balance sheet, ledger
- budget variance
- customer and vendor statements
- sales analysis
- inventory valuation, consumption, and stock aging
- fixed asset reports
- HR/payroll reports

When adding a report:

1. Put data shaping in a reporting service.
2. Keep page code focused on filters, user actions, and display.
3. Add PDF/Excel export logic through `ReportExportService` when needed.