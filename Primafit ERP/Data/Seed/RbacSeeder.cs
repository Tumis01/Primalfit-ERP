using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Data.Seed
{
    public static class RbacSeeder
    {
        public static async Task SeedAsync(AppDbContext context, RoleManager<ApplicationRole> roleManager)
        {
            // 1. Seed Identity Roles into ApplicationRole table
            var systemRoles = new[] { "CFO", "Accountant", "Clerk", "IT/Admin" };
            var systemCompanyId = Guid.Empty; // Global identifier

            foreach (var roleName in systemRoles)
            {
                var uniqueName = $"{systemCompanyId}_{roleName}";
                if (!await roleManager.RoleExistsAsync(uniqueName))
                {
                    var newRole = new ApplicationRole(roleName, systemCompanyId, $"System Default Role: {roleName}");
                    await roleManager.CreateAsync(newRole);
                }
            }

            // 2. Define Master Permissions (Only insert if empty)
            if (!await context.Permissions.AnyAsync())
            {
                var permissions = new List<Permission>
                {
                    // General Ledger & Chart of Accounts
                    new() { Module = "GL", PageKey = "GL_SEGMENT_SETUP_PAGE", ActionKey = null, DisplayName = "Access Segment Setup", Category = "Finance" },
                    new() { Module = "GL", PageKey = "GL_SEGMENT_SETUP_PAGE", ActionKey = "GL_SEGMENT_CONFIG", DisplayName = "Configure Segment Structure", Category = "Finance" },
                    new() { Module = "GL", PageKey = "GL_SEGMENT_SETUP_PAGE", ActionKey = "GL_SEGMENT_MANAGE_VALUES", DisplayName = "Manage Segment Values", Category = "Finance" },
                    new() { Module = "GL", PageKey = "GL_COA_PAGE", ActionKey = null, DisplayName = "Access Chart of Accounts", Category = "Finance" },
                    new() { Module = "GL", PageKey = "GL_COA_PAGE", ActionKey = "GL_COA_MANAGE", DisplayName = "Create & Manage GL Accounts", Category = "Finance" },
                    new() { Module = "GL", PageKey = "GL_COA_PAGE", ActionKey = "GL_COA_IMPORT_EXPORT", DisplayName = "Import & Export COA", Category = "Finance" },
                    new() { Module = "GL", PageKey = "GL_PAGE", ActionKey = null, DisplayName = "Access General Ledger", Category = "Finance" },
                    new() { Module = "GL", PageKey = "GL_PAGE", ActionKey = "GL_JOURNAL_CREATE", DisplayName = "Create GL Journals", Category = "Finance" },
                    new() { Module = "GL", PageKey = "GL_PAGE", ActionKey = "GL_BATCH_APPROVE", DisplayName = "Approve GL Batches", Category = "Finance" },
                    new() { Module = "GL", PageKey = "GL_PAGE", ActionKey = "GL_BATCH_POST", DisplayName = "Post GL Batches", Category = "Finance" }, 

                    // Cashbook & Reconciliation
                    new() { Module = "CASHBOOK", PageKey = "CASHBOOK_PAGE", ActionKey = null, DisplayName = "Access Cashbook", Category = "Finance" },
                    new() { Module = "CASHBOOK", PageKey = "CASHBOOK_PAGE", ActionKey = "CASHBOOK_BATCH_CREATE", DisplayName = "Create & Edit Cashbook Batches", Category = "Finance" },
                    new() { Module = "CASHBOOK", PageKey = "CASHBOOK_PAGE", ActionKey = "CASHBOOK_BATCH_APPROVE", DisplayName = "Approve Cashbook Batches", Category = "Finance" },
                    new() { Module = "CASHBOOK", PageKey = "CASHBOOK_PAGE", ActionKey = "CASHBOOK_BATCH_POST", DisplayName = "Post Cashbook Batches", Category = "Finance" },
                    new() { Module = "RECONCILIATION", PageKey = "RECON_PAGE", ActionKey = null, DisplayName = "Access Bank Reconciliation", Category = "Finance" },
                    new() { Module = "RECONCILIATION", PageKey = "RECON_PAGE", ActionKey = "RECON_START", DisplayName = "Start New Reconciliation", Category = "Finance" },
                    new() { Module = "RECONCILIATION", PageKey = "RECON_PAGE", ActionKey = "RECON_FINALIZE", DisplayName = "Finalize Reconciliation", Category = "Finance" },

                    // Budgeting & Reporting
                    new() { Module = "BUDGET", PageKey = "BUDGET_PAGE", ActionKey = null, DisplayName = "Access Budgets", Category = "Finance" },
                    new() { Module = "BUDGET", PageKey = "BUDGET_PAGE", ActionKey = "BUDGET_MANAGE", DisplayName = "Create & Edit Budgets", Category = "Finance" },
                    new() { Module = "BUDGET", PageKey = "BUDGET_PAGE", ActionKey = "BUDGET_IMPORT_EXPORT", DisplayName = "Import & Export Budgets", Category = "Finance" },
                    new() { Module = "REPORTS", PageKey = "FINANCE_REPORTS_PAGE", ActionKey = null, DisplayName = "Access Financial Reports", Category = "Reporting" },
                    new() { Module = "REPORTS", PageKey = "SALES_REPORTS_PAGE", ActionKey = null, DisplayName = "Access Sales & A/R Reports", Category = "Reporting" },
                    new() { Module = "REPORTS", PageKey = "REPORTS_PAGE", ActionKey = null, DisplayName = "Access General Reports", Category = "Reporting" },

                    // Sales & Accounts Receivable
                    new() { Module = "MASTER_DATA", PageKey = "MD_CUSTOMER_GROUPS_PAGE", ActionKey = null, DisplayName = "Access Customer Groups", Category = "Operations" },
                    new() { Module = "MASTER_DATA", PageKey = "MD_CUSTOMER_GROUPS_PAGE", ActionKey = "CUSTOMER_GROUP_MANAGE", DisplayName = "Manage Customer Groups", Category = "Operations" },
                    new() { Module = "SALES", PageKey = "SALES_ORDER_PAGE", ActionKey = null, DisplayName = "Access Sales Orders & Invoices", Category = "Operations" },
                    new() { Module = "SALES", PageKey = "SALES_ORDER_PAGE", ActionKey = "SALES_ORDER_CREATE", DisplayName = "Create/Edit Quotes & Orders", Category = "Operations" },
                    new() { Module = "SALES", PageKey = "SALES_ORDER_PAGE", ActionKey = "SALES_INVOICE_CREATE", DisplayName = "Generate Invoices", Category = "Operations" },
                    new() { Module = "SALES", PageKey = "SALES_ORDER_PAGE", ActionKey = "SALES_INVOICE_POST", DisplayName = "Post Invoices to GL", Category = "Finance" },
                    new() { Module = "RECEIVABLES", PageKey = "AR_PAYMENT_PAGE", ActionKey = null, DisplayName = "Access A/R Payments", Category = "Finance" },
                    new() { Module = "RECEIVABLES", PageKey = "AR_PAYMENT_PAGE", ActionKey = "AR_PAYMENT_CREATE", DisplayName = "Post Customer Payments", Category = "Finance" },
                    new() { Module = "SALES", PageKey = "CREDIT_NOTE_PAGE", ActionKey = null, DisplayName = "Access Credit Notes", Category = "Finance" },
                    new() { Module = "SALES", PageKey = "CREDIT_NOTE_PAGE", ActionKey = "CREDIT_NOTE_CREATE", DisplayName = "Create/Edit Credit Notes", Category = "Finance" },
                    new() { Module = "SALES", PageKey = "CREDIT_NOTE_PAGE", ActionKey = "CREDIT_NOTE_DELETE", DisplayName = "Delete Draft Credit Notes", Category = "Finance" },
                    new() { Module = "SALES", PageKey = "CREDIT_NOTE_PAGE", ActionKey = "CREDIT_NOTE_POST", DisplayName = "Post Credit Notes & Reverse GL", Category = "Finance" },

                    // Procurement & Vendors
                    new() { Module = "MASTER_DATA", PageKey = "MD_VENDORS_PAGE", ActionKey = null, DisplayName = "Access Vendors", Category = "Operations" },
                    new() { Module = "MASTER_DATA", PageKey = "MD_VENDORS_PAGE", ActionKey = "VENDOR_MANAGE", DisplayName = "Manage Vendors", Category = "Operations" },
                    new() { Module = "MASTER_DATA", PageKey = "MD_VENDOR_GROUPS_PAGE", ActionKey = null, DisplayName = "Access Vendor Groups", Category = "Operations" },
                    new() { Module = "MASTER_DATA", PageKey = "MD_VENDOR_GROUPS_PAGE", ActionKey = "VENDOR_GROUP_MANAGE", DisplayName = "Manage Vendor Groups", Category = "Operations" },
                    new() { Module = "PURCHASING", PageKey = "PURCHASING_PAGE", ActionKey = null, DisplayName = "Access Purchasing", Category = "Operations" },
                    new() { Module = "PURCHASING", PageKey = "PURCHASING_PAGE", ActionKey = "PO_CREATE", DisplayName = "Create & Edit Purchase Orders", Category = "Operations" },
                    new() { Module = "PURCHASING", PageKey = "PURCHASING_PAGE", ActionKey = "PO_INVOICE_POST", DisplayName = "Post Purchase Invoices", Category = "Operations" },
                    new() { Module = "PAYABLES", PageKey = "AP_PAYMENT_PAGE", ActionKey = null, DisplayName = "Post Vendor Payments", Category = "Finance" },
                    new() { Module = "PAYABLES", PageKey = "AP_PAYMENT_PAGE", ActionKey = "AP_PAYMENT_CREATE", DisplayName = "Post Vendor Payments", Category = "Finance" },
                    new() { Module = "REPORTS", PageKey = "PURCHASING_REPORTS_PAGE", ActionKey = null, DisplayName = "Access Purchasing Reports", Category = "Reporting" },

                    // Inventory Module
                    new() { Module = "WAREHOUSE", PageKey = "WH_SHIPMENTS_PAGE", ActionKey = null, DisplayName = "Access Warehouse Dispatch", Category = "Operations" },
                    new() { Module = "WAREHOUSE", PageKey = "WH_SHIPMENTS_PAGE", ActionKey = "SHIPMENT_CREATE", DisplayName = "Load/Queue Shipments", Category = "Operations" },
                    new() { Module = "WAREHOUSE", PageKey = "WH_SHIPMENTS_PAGE", ActionKey = "SHIPMENT_DISPATCH", DisplayName = "Confirm Physical Dispatch", Category = "Operations" },
                    new() { Module = "INVENTORY", PageKey = "INVENTORY_SETUP_PAGE", ActionKey = null, DisplayName = "Access Inventory Setup", Category = "Operations" },
                    new() { Module = "INVENTORY", PageKey = "INVENTORY_SETUP_PAGE", ActionKey = "INVENTORY_SETUP_MANAGE", DisplayName = "Manage Items, Categories, UoM & Warehouses", Category = "Operations" },
                    new() { Module = "INVENTORY", PageKey = "INVENTORY_OPERATIONS_PAGE", ActionKey = null, DisplayName = "Access Inventory Operations", Category = "Operations" },
                    new() { Module = "INVENTORY", PageKey = "INVENTORY_OPERATIONS_PAGE", ActionKey = "INVENTORY_OPERATIONS_MANAGE", DisplayName = "Manage Stock Adjustments & Transfers", Category = "Operations" },
                    new() { Module = "REPORTS", PageKey = "INVENTORY_REPORTS_PAGE", ActionKey = null, DisplayName = "Access Inventory & Stock Reports", Category = "Reporting" },

                    // Fixed Assets Module
                    new() { Module = "ASSETS", PageKey = "FIXED_ASSETS_PAGE", ActionKey = null, DisplayName = "Access Fixed Assets", Category = "Finance" },
                    new() { Module = "ASSETS", PageKey = "FIXED_ASSETS_PAGE", ActionKey = "FIXED_ASSETS_MANAGE", DisplayName = "Manage Assets, Categories & Usage", Category = "Finance" },
                    new() { Module = "ASSETS", PageKey = "FIXED_ASSETS_PAGE", ActionKey = "FIXED_ASSETS_DEPRECIATE", DisplayName = "Run Asset Depreciation", Category = "Finance" },

                    // HR & Payroll Module
                    new() { Module = "HR", PageKey = "HR_MANAGEMENT_PAGE", ActionKey = null, DisplayName = "Access HR & Payroll Pages", Category = "Human Resources" },
                    new() { Module = "HR", PageKey = "HR_MANAGEMENT_PAGE", ActionKey = "HR_MANAGEMENT_MANAGE", DisplayName = "Manage HR Setup, Employees & Payroll", Category = "Human Resources" },
                    
                    // --- SHARED SYSTEM SETUP (Company, Taxes, Currencies) ---
                    new() { Module = "SETUP", PageKey = "SYSTEM_SETUP_PAGE", ActionKey = null, DisplayName = "Access General System Setup", Category = "Administration" },
                    new() { Module = "SETUP", PageKey = "SYSTEM_SETUP_PAGE", ActionKey = "SYSTEM_SETUP_MANAGE", DisplayName = "Manage Setup (Create/Delete)", Category = "Administration" },
                    new() { Module = "SETUP", PageKey = "SYSTEM_SETUP_PAGE", ActionKey = "COMPANY_PROFILE_UPDATE", DisplayName = "Update Company Profile", Category = "Administration" },

                    // --- FISCAL PERIODS ---
                    new() { Module = "SETUP", PageKey = "FISCAL_PERIODS_PAGE", ActionKey = null, DisplayName = "Access Fiscal Periods", Category = "Administration" },
                    new() { Module = "SETUP", PageKey = "FISCAL_PERIODS_PAGE", ActionKey = "FISCAL_PERIODS_MANAGE", DisplayName = "Generate & Close Periods", Category = "Administration" },

                    // --- ROLES ---
                    new() { Module = "SETUP", PageKey = "ROLES_SETUP_PAGE", ActionKey = null, DisplayName = "Access Roles Setup", Category = "Administration" },
                    new() { Module = "SETUP", PageKey = "ROLES_SETUP_PAGE", ActionKey = "ROLES_SETUP_MANAGE", DisplayName = "Manage Custom Roles", Category = "Administration" },

                    // --- USERS ---
                    new() { Module = "SETUP", PageKey = "USERS_SETUP_PAGE", ActionKey = null, DisplayName = "Access Users Setup", Category = "Administration" },
                    new() { Module = "SETUP", PageKey = "USERS_SETUP_PAGE", ActionKey = "USERS_SETUP_MANAGE", DisplayName = "Create & Manage Users", Category = "Administration" }
                };

                context.Permissions.AddRange(permissions);
                await context.SaveChangesAsync();
            }

            // Always fetch the DB dictionary (whether just seeded or already existed)
            var pDict = await context.Permissions.ToDictionaryAsync(p => p.ActionKey ?? p.PageKey, p => p.Id);

            // 3. Define System Role Templates (Only insert if empty)
            if (!await context.SystemRoleTemplates.AnyAsync())
            {
                var roles = new List<SystemRoleTemplate>
                {
                    new() { RoleName = "CFO", Description = "Chief Financial Officer - Full Financial & Operational Access", IsSystem = true },
                    new() { RoleName = "Accountant", Description = "Finance Operations, Posting, and Reporting", IsSystem = true },
                    new() { RoleName = "Clerk", Description = "Data Entry & Drafts (Limited Posting)", IsSystem = true },
                    new() { RoleName = "IT/Admin", Description = "System Administration", IsSystem = true }
                };

                context.SystemRoleTemplates.AddRange(roles);
                await context.SaveChangesAsync();
            }

            // Always fetch the DB dictionary (whether just seeded or already existed)
            var roleMap = await context.SystemRoleTemplates.ToDictionaryAsync(r => r.RoleName, r => r.Id);

            // 4. Map and Insert System Role Permissions (Only if empty)
            if (!await context.SystemRolePermissions.AnyAsync())
            {
                var systemRolePermissions = new List<SystemRolePermission>();

                void Grant(string roleName, params string[] permKeys)
                {
                    if (!roleMap.ContainsKey(roleName)) return;

                    foreach (var key in permKeys)
                    {
                        if (pDict.ContainsKey(key))
                        {
                            systemRolePermissions.Add(new SystemRolePermission
                            {
                                SystemRoleTemplateId = roleMap[roleName],
                                PermissionId = pDict[key]
                            });
                        }
                    }
                }

                // CFO - ALL PERMISSIONS
                Grant("CFO", pDict.Keys.ToArray());

                // Accountant - Day-to-Day Operations, Posting, and Reporting
                Grant("Accountant",
                      "GL_PAGE", "GL_JOURNAL_CREATE", "GL_BATCH_POST",
                      "GL_COA_PAGE", "GL_COA_MANAGE", "GL_BATCH_APPROVE", "BUDGET_MANAGE", "BUDGET_IMPORT_EXPORT",
                      "CASHBOOK_PAGE", "CASHBOOK_BATCH_CREATE", "CASHBOOK_BATCH_POST", "CASHBOOK_BATCH_APPROVE",
                      "RECON_PAGE", "RECON_START", "RECON_MATCH",
                      "FINANCE_REPORTS_PAGE", "SALES_REPORTS_PAGE", "REPORTS_PAGE", "INVENTORY_REPORTS_PAGE", "PURCHASING_REPORTS_PAGE",
                      "SALES_ORDER_PAGE", "SALES_INVOICE_CREATE", "SALES_INVOICE_POST",
                      "AR_PAYMENT_PAGE", "AR_PAYMENT_CREATE",
                      "CREDIT_NOTE_PAGE", "CREDIT_NOTE_CREATE", "CREDIT_NOTE_POST",
                      "PURCHASING_PAGE", "PO_CREATE", "PO_INVOICE_POST", "INVENTORY_SETUP_MANAGE",
                      "INVENTORY_OPERATIONS_PAGE", "INVENTORY_OPERATIONS_MANAGE", "INVENTORY_SETUP_PAGE", "FISCAL_PERIODS_PAGE",
                      "FIXED_ASSETS_PAGE", "FIXED_ASSETS_MANAGE", "FIXED_ASSETS_DEPRECIATE", "WH_SHIPMENTS_PAGE", "SHIPMENT_CREATE", "SHIPMENT_DISPATCH", "SYSTEM_SETUP_PAGE",
                      "HR_MANAGEMENT_PAGE", "HR_MANAGEMENT_MANAGE", "MD_CUSTOMER_GROUPS_PAGE", "CUSTOMER_GROUP_MANAGE", "MD_VENDORS_PAGE", "MD_VENDOR_GROUPS_PAGE");

                // Clerk - Draft creation only. No posting, no deleting, no approvals.
                Grant("Clerk",
                      "GL_PAGE", "GL_JOURNAL_CREATE",
                      "CASHBOOK_PAGE", "CASHBOOK_BATCH_CREATE", "MD_CUSTOMER_GROUPS_PAGE", "CUSTOMER_GROUP_MANAGE", "MD_VENDORS_PAGE", "MD_VENDOR_GROUPS_PAGE",
                      "SALES_ORDER_PAGE", "SALES_ORDER_CREATE",
                      "WH_SHIPMENTS_PAGE", "SHIPMENT_CREATE", "CREDIT_NOTE_PAGE", "CREDIT_NOTE_CREATE",
                      "PURCHASING_PAGE", "PO_CREATE", "SYSTEM_SETUP_PAGE",
                      "INVENTORY_SETUP_PAGE", "INVENTORY_OPERATIONS_PAGE",
                      "FIXED_ASSETS_PAGE", "HR_MANAGEMENT_PAGE");

                // IT / Admin - System Admin only
                Grant("IT/Admin", "GL_SEGMENT_SETUP_PAGE", "ROLE_MANAGE", "GL_COA_PAGE", "GL_PAGE",
                    "FINANCE_REPORTS_PAGE", "REPORTS_PAGE", "SALES_REPORTS_PAGE", "SALES_ORDER_PAGE", "SYSTEM_SETUP_PAGE", "ROLES_SETUP_PAGE", "ROLES_SETUP_MANAGE",
                    "INVENTORY_SETUP_PAGE", "INVENTORY_OPERATIONS_PAGE", "WH_SHIPMENTS_PAGE", "INVENTORY_REPORTS_PAGE", "FIXED_ASSETS_PAGE", "FISCAL_PERIODS_PAGE", "USERS_SETUP_PAGE", "USERS_SETUP_MANAGE",
                    "MD_CUSTOMER_GROUPS_PAGE", "MD_VENDORS_PAGE", "MD_VENDOR_GROUPS_PAGE", "PURCHASING_REPORTS_PAGE", "PURCHASING_PAGE", "CREDIT_NOTE_PAGE", "HR_MANAGEMENT_PAGE");

                context.SystemRolePermissions.AddRange(systemRolePermissions);
                await context.SaveChangesAsync();
            }
        }
    }
}