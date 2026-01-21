using System.Net.Http.Json;
using Primafit_ERP.Components.Models;

namespace Primafit_ERP.Services
{
    public class AuthApiService
    {
        private readonly HttpClient _http;

        public AuthApiService(HttpClient http)
        {
            _http = http;
        }

        public async Task<bool> LoginAsync(LoginDto loginModel)
        {
            var response = await _http.PostAsJsonAsync("api/Auth/login", loginModel);
            return response.IsSuccessStatusCode;
        }

        public async Task LogoutAsync()
        {
            await _http.PostAsync("api/Auth/logout", null);
        }
    }
}