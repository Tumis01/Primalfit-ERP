//using System.Security.Claims;

//namespace Primafit_ERP.Services
//{
//    // 1. Define the Interface
//    public interface IPermissionGuard
//    {
//        void Require(ClaimsPrincipal user, string permissionKey);
//        string RequireUserId(ClaimsPrincipal user);
//    }

//    // 2. Define the "Disabled" Implementation
//    public class PermissionGuard : IPermissionGuard
//    {
//        public void Require(ClaimsPrincipal user, string permissionKey)
//        {
//            // SECURITY DISABLED: Do nothing
//            return;
//        }

//        public string RequireUserId(ClaimsPrincipal user)
//        {
//            // SECURITY DISABLED: Return dummy user
//            return "SYSTEM_USER";
//        }
//    }

//    // 3. Define the Keys
//    public static class PermissionKeys
//    {
//        public const string GL_ENTRY_CREATE = "GL_ENTRY_CREATE";
//        public const string GL_BATCH_APPROVE = "GL_BATCH_APPROVE";
//        public const string GL_POST_TRANSACTION = "GL_POST_TRANSACTION";
//        public const string GL_VIEW_REPORTS = "GL_VIEW_REPORTS";
//        public const string RECON_IMPORT = "RECON_IMPORT";
//        public const string RECON_EDIT = "RECON_EDIT";
//        public const string RECON_APPROVE = "RECON_APPROVE";
//    }
//}