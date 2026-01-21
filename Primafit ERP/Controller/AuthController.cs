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

        // 👇 NEW METHOD: Specific for Browser Form Login
        [HttpPost("login-form")]
        public async Task<IActionResult> LoginFromForm([FromForm] LoginDto model)
        {
            var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, isPersistent: false, lockoutOnFailure: false);

            if (result.Succeeded)
            {
                // The Controller sets the cookie & sends browser to Dashboard
                return LocalRedirect("/");
            }

            // On failure, go back to login with error
            return Redirect("/login?error=Invalid credentials");
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return LocalRedirect("/login");
        }
    }
}