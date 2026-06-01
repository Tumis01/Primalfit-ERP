namespace Primafit_ERP.Components.Models
{
    // 1. The Master Catalogue of Permissions
    public class Permission
    {
        public int Id { get; set; }
        public string Module { get; set; } = string.Empty;
        public string PageKey { get; set; } = string.Empty;
        public string? ActionKey { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
    }

    // 2. Global Default Roles (CFO, Clerk, etc.)
    public class SystemRoleTemplate
    {
        public int Id { get; set; }
        public string RoleName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsSystem { get; set; } = true;
    }

    // 3. Mapping System Roles to Permissions
    public class SystemRolePermission
    {
        public int SystemRoleTemplateId { get; set; }
        public SystemRoleTemplate SystemRoleTemplate { get; set; } = null!;
        public int PermissionId { get; set; }
        public Permission Permission { get; set; } = null!;
    }

    // 4. Assigning System Roles to Users within a Company
    public class UserSystemRole
    {
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser User { get; set; } = null!;
        public int SystemRoleTemplateId { get; set; }
        public SystemRoleTemplate SystemRoleTemplate { get; set; } = null!;
        public Guid CompanyId { get; set; }
        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    }

    // 5. Mapping Custom Company Roles to Page Permissions
    public class CompanyRolePermission
    {
        public string ApplicationRoleId { get; set; } = string.Empty;
        public ApplicationRole ApplicationRole { get; set; } = null!;
        public int PermissionId { get; set; }
        public Permission Permission { get; set; } = null!;
    }
}