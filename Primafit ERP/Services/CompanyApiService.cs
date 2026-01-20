using System.Net.Http.Json;
using Primafit_ERP.Components.Models;

namespace Primafit_ERP.Services
{
    public class CompanyApiService
    {
        private readonly HttpClient _http;

        public CompanyApiService(HttpClient http)
        {
            _http = http;
        }

        // LIST: Calls [HttpGet] api/company-details
        public async Task<List<CompanyDetails>> GetCompaniesAsync()
        {
            return await _http.GetFromJsonAsync<List<CompanyDetails>>("api/company-details") 
                   ?? new List<CompanyDetails>();
        }

        // ADD: Calls [HttpPost] api/company-details
        public async Task<CompanyDetails?> CreateCompanyAsync(CompanyDetails company)
        {
            var response = await _http.PostAsJsonAsync("api/company-details", company);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<CompanyDetails>();
            }
            return null; // Handle error appropriately in production
        }

        // EDIT: Calls [HttpPut("{id}")] api/company-details/{id}
        public async Task<bool> UpdateCompanyAsync(Guid id, CompanyDetails company)
        {
            var response = await _http.PutAsJsonAsync($"api/company-details/{id}", company);
            return response.IsSuccessStatusCode;
        }
        public async Task<bool> DeleteCompanyAsync(Guid id)
        {
            var response = await _http.DeleteAsync($"api/company-details/{id}");
            return response.IsSuccessStatusCode;
        }
    }
}