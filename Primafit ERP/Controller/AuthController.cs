using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Primafit_ERP.Components.Models;

namespace Primafit_ERP.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly SignInManager<ApplicationUser> _signInManager;

        public AuthController(SignInManager<ApplicationUser> signInManager)
        {
            _signInManager = signInManager;
        }

        [HttpPost("login-form")]
        public async Task<IActionResult> LoginFromForm([FromForm] LoginDto model)
        {
            // Force logout before attempting login (Clears old sessions)
            await _signInManager.SignOutAsync();

            // Attempt Sign In
            // isPersistent: false = Cookie dies when Browser closes.
            var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, isPersistent: false, lockoutOnFailure: false);

            if (result.Succeeded)
            {
                return LocalRedirect("/");
            }

            return Redirect("/login?error=Invalid credentials");
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();

            // Clear cookies manually to be safe
            Response.Cookies.Delete(".AspNetCore.Identity.Application");

            return LocalRedirect("/login");
        }
    }
}