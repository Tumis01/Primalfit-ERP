using Primafit_ERP.Components.Models;
using System.Net.Http.Json;

namespace Primafit_ERP.Services
{
    public class WarehouseService
    {
        private readonly HttpClient _httpClient;

        public WarehouseService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<List<Warehouse>> GetWarehousesAsync()
        {
            // Replace with your actual API endpoint
            return await _httpClient.GetFromJsonAsync<List<Warehouse>>("api/warehouses") ?? new List<Warehouse>();
        }

        public async Task<bool> CreateWarehouseAsync(Warehouse warehouse)
        {
            var response = await _httpClient.PostAsJsonAsync("api/warehouses", warehouse);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> UpdateWarehouseAsync(Warehouse warehouse)
        {
            var response = await _httpClient.PutAsJsonAsync($"api/warehouses/{warehouse.ID}", warehouse);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> DeleteWarehouseAsync(Guid id)
        {
            var response = await _httpClient.DeleteAsync($"api/warehouses/{id}");
            return response.IsSuccessStatusCode;
        }
    }
}