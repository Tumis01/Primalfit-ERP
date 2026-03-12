using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Primafit_ERP.Services;

namespace PrimafitERP.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class InventoryController : ControllerBase
    {
        private readonly InventoryService _inventoryService;

        public InventoryController(InventoryService inventoryService)
        {
            _inventoryService = inventoryService;
        }

        // GET: api/inventory/stock/{warehouseId}/{itemId}
        [HttpGet("stock/{warehouseId:guid}/{itemId:guid}")]
        public async Task<IActionResult> GetItemStock(Guid warehouseId, Guid itemId)
        {
            var stockLevel = await _inventoryService.GetStockLevel(itemId, warehouseId);

            return Ok(new
            {
                WarehouseId = warehouseId,
                ItemId = itemId,
                QuantityOnHand = stockLevel
            });
        }
    }
}