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
        private readonly PermissionCacheService _cache;

        public PermissionGuard(PermissionCacheService cache)
        {
            _cache = cache;
        }

        public void Require(ClaimsPrincipal user, string permissionKey)
        {
            // SuperAdmin bypass - if the user has this claim, they bypass all checks
            if (user.HasClaim("IsSuperAdmin", "true")) return;

            // Check against the in-memory cache loaded during Phase 2
            if (!_cache.Has(permissionKey))
            {
                throw new UnauthorizedAccessException($"Access denied. Required permission: {permissionKey}");
            }
        }

        public string RequireUserId(ClaimsPrincipal user)
        {
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                throw new UnauthorizedAccessException("User is not authenticated or User ID is missing.");
            }

            return userId;
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