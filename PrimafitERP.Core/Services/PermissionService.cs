using System.Security.Claims;

namespace Primafit_ERP.Services
{
    // ==========================================
    // 1. THE INTERFACE
    // ==========================================
    public interface IPermissionGuard
    {
        /// <summary>
        /// Checks if the user has a specific permission. Throws UnauthorizedAccessException if not.
        /// </summary>
        void Require(ClaimsPrincipal user, string permissionKey);

        /// <summary>
        /// Extracts the User ID from the claims. Throws if not found.
        /// </summary>
        string RequireUserId(ClaimsPrincipal user);
    }

    // ==========================================
    // 2. THE IMPLEMENTATION (AUTH DISABLED)
    // ==========================================
    public class PermissionGuard : IPermissionGuard
    {
        public void Require(ClaimsPrincipal user, string permissionKey)
        {
            // ---------------------------------------------------------
            // SECURITY DISABLED: This method intentionally does nothing.
            // It allows any user (even unauthenticated ones) to pass.
            // ---------------------------------------------------------
            return;
        }

        public string RequireUserId(ClaimsPrincipal user)
        {
            // ---------------------------------------------------------
            // SECURITY DISABLED: Always return a default "System" user.
            // This prevents null reference errors in your logic.
            // ---------------------------------------------------------
            return "SYSTEM_USER";
        }
    }

    // ==========================================
    // 3. PERMISSION KEYS CONSTANTS
    // ==========================================
    public static class PermissionKeys
    {
        // General Ledger Keys
        public const string GL_ENTRY_CREATE = "GL_ENTRY_CREATE";
        public const string GL_BATCH_APPROVE = "GL_BATCH_APPROVE";
        public const string GL_POST_TRANSACTION = "GL_POST_TRANSACTION";
        public const string GL_VIEW_REPORTS = "GL_VIEW_REPORTS";

        // Reconciliation Keys
        public const string RECON_IMPORT = "RECON_IMPORT";
        public const string RECON_EDIT = "RECON_EDIT";
        public const string RECON_APPROVE = "RECON_APPROVE";
    }
}