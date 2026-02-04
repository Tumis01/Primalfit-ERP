using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Controllers
{
    [Route("auth")]
    [ApiController]
    public class AuthController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;

        public AuthController(SignInManager<ApplicationUser> signInManager)
        {
            _signInManager = signInManager;
        }

        [HttpPost("login")]
        [ValidateAntiForgeryToken] // Security best practice
        public async Task<IActionResult> Login([FromForm] LoginDto model)
        {
            if (string.IsNullOrEmpty(model.Email) || string.IsNullOrEmpty(model.Password))
            {
                return Redirect("/login?error=Email and Password are required");
            }

            // Attempt Sign In
            var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, isPersistent: true, lockoutOnFailure: false);

            if (result.Succeeded)
            {
                return Redirect("/"); // Success -> Go to Dashboard
            }
            
            if (result.IsLockedOut)
            {
                return Redirect("/login?error=Account is locked out");
            }

            return Redirect("/login?error=Invalid login credentials");
        }

        [HttpGet("logout")]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return Redirect("/login");
        }
    }
}