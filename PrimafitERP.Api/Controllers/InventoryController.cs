using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Primafit_ERP.Components.Models;
using PrimafitERP.Api.DTOs;
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

        // 1. GET STOCK LEVEL (Existing)
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

        // 2. DIRECT RECEIPT (Purchasing without a PO)
        [HttpPost("direct-receipt")]
        public async Task<IActionResult> DirectReceipt([FromBody] DirectReceiptDto dto)
        {
            if (!ModelState.IsValid || dto.TotalLandedCost <= 0) return BadRequest("Invalid receipt data. Cost must be > 0.");

            var err = await _inventoryService.ReceiveStockAsync(
                dto.CompanyId, dto.ItemId, dto.WarehouseId, dto.Quantity, dto.TotalLandedCost, dto.VendorId, dto.Reference);

            if (!string.IsNullOrEmpty(err)) return BadRequest(new { message = err });

            return Ok(new { message = "Stock received and WACC updated successfully." });
        }

        // 3. STOCK ADJUSTMENT (Audits, Damages, Revaluations)
        [HttpPost("adjust")]
        public async Task<IActionResult> AdjustStock([FromBody] StockAdjustmentDto dto)
        {
            if (!ModelState.IsValid) return BadRequest("Invalid adjustment data.");

            if (dto.AdjustmentType == StockEntryType.DirectReceipt)
                return BadRequest("Use the /direct-receipt endpoint for vendor purchases.");

            var err = await _inventoryService.AdjustStockAsync(
                dto.CompanyId, dto.ItemId, dto.WarehouseId, dto.AdjustmentType, dto.Quantity, dto.TotalValueChange, dto.Reference);

            if (!string.IsNullOrEmpty(err)) return BadRequest(new { message = err });

            return Ok(new { message = $"Inventory adjustment ({dto.AdjustmentType}) processed successfully." });
        }

       

        // 5. SHIP TRANSFER (Warehouse A -> Transit)
        [HttpPost("transfer/ship")]
        public async Task<IActionResult> ShipTransfer([FromBody] ShipTransferDto dto)
        {
            if (!ModelState.IsValid || dto.Quantity <= 0) return BadRequest("Invalid transfer data.");

            var err = await _inventoryService.ShipTransferAsync(
                dto.CompanyId, dto.ItemId, dto.FromWarehouseId, dto.ToWarehouseId, dto.Quantity, dto.TransitAccountId, dto.Note);

            if (!string.IsNullOrEmpty(err)) return BadRequest(new { message = err });

            return Ok(new { message = "Stock shipped to transit account successfully." });
        }

        // 6. RECEIVE TRANSFER (Transit -> Warehouse B)
        [HttpPost("transfer/receive")]
        public async Task<IActionResult> ReceiveTransfer([FromBody] ReceiveTransferDto dto)
        {
            if (!ModelState.IsValid || dto.ActualQuantityReceived < 0) return BadRequest("Invalid receipt data.");

            var err = await _inventoryService.ReceiveTransferAsync(dto.TransferId, dto.ActualQuantityReceived);

            if (!string.IsNullOrEmpty(err)) return BadRequest(new { message = err });

            return Ok(new { message = "Transferred stock received successfully. Transit account cleared." });
        }
    }
}