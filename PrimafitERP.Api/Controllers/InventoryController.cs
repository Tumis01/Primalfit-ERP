using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Primafit_ERP.Components.Models;
using PrimafitERP.Api.DTOs;
using Primafit_ERP.Services;

namespace PrimafitERP.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "SuperAdmin, CFO, Accountant")] // Locked to authorized backend and financial personnel
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
        // Secure Claim Extractions
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var companyClaim = User.FindFirst("CompanyId")?.Value;

        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(companyClaim, out Guid companyId))
            return Unauthorized("User session configuration is invalid or expired.");

        if (!ModelState.IsValid || dto.TotalLandedCost <= 0)
            return BadRequest("Invalid receipt data. Cost must be > 0.");

        // Synchronized with: ReceiveStockAsync(Guid companyId, ..., string userId)
        var err = await _inventoryService.ReceiveStockAsync(
            companyId,
            dto.ItemId,
            dto.WarehouseId,
            dto.Quantity,
            dto.TotalLandedCost,
            dto.VendorId,
            dto.Reference,
            userId
        );

        if (!string.IsNullOrEmpty(err)) return BadRequest(new { message = err });

        return Ok(new { message = "Stock received and WACC updated successfully." });
    }

    // 3. STOCK ADJUSTMENT (Audits, Damages, Revaluations)
    [HttpPost("adjust")]
    public async Task<IActionResult> AdjustStock([FromBody] StockAdjustmentDto dto)
    {
        // Secure Claim Extractions
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var companyClaim = User.FindFirst("CompanyId")?.Value;

        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(companyClaim, out Guid companyId))
            return Unauthorized("User session configuration is invalid or expired.");

        if (!ModelState.IsValid) return BadRequest("Invalid adjustment data.");

        if (dto.AdjustmentType == StockEntryType.DirectReceipt)
            return BadRequest("Use the /direct-receipt endpoint for vendor purchases.");

        // Synchronized with: AdjustStockAsync(Guid companyId, ..., string userId)
        var err = await _inventoryService.AdjustStockAsync(
            companyId,
            dto.ItemId,
            dto.WarehouseId,
            dto.AdjustmentType,
            dto.Quantity,
            dto.TotalValueChange,
            dto.Reference,
            userId
        );

        if (!string.IsNullOrEmpty(err)) return BadRequest(new { message = err });

        return Ok(new { message = $"Inventory adjustment ({dto.AdjustmentType}) processed successfully." });
    }

    // 4. SHIP TRANSFER (Warehouse A -> Transit)
    [HttpPost("transfer/ship")]
    public async Task<IActionResult> ShipTransfer([FromBody] ShipTransferDto dto)
    {
        // Secure Claim Extractions
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var companyClaim = User.FindFirst("CompanyId")?.Value;

        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(companyClaim, out Guid companyId))
            return Unauthorized("User session configuration is invalid or expired.");

        if (!ModelState.IsValid || dto.Quantity <= 0) return BadRequest("Invalid transfer data.");

        // Synchronized with: ShipTransferAsync(Guid companyId, ..., string userId)
        var err = await _inventoryService.ShipTransferAsync(
            companyId,
            dto.ItemId,
            dto.FromWarehouseId,
            dto.ToWarehouseId,
            dto.Quantity,
            dto.TransitAccountId,
            dto.Note,
            userId
        );

        if (!string.IsNullOrEmpty(err)) return BadRequest(new { message = err });

        return Ok(new { message = "Stock shipped to transit account successfully." });
    }

    // 5. RECEIVE TRANSFER (Transit -> Warehouse B)
    [HttpPost("transfer/receive")]
    public async Task<IActionResult> ReceiveTransfer([FromBody] ReceiveTransferDto dto)
    {
        // Secure Operator Extractions
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized("User session identifier context is invalid or expired.");

        if (!ModelState.IsValid || dto.ActualQuantityReceived < 0) return BadRequest("Invalid receipt data.");

        // Synchronized with: ReceiveTransferAsync(Guid transferId, decimal actualQtyReceived, string userId)
        var err = await _inventoryService.ReceiveTransferAsync(dto.TransferId, dto.ActualQuantityReceived, userId);

        if (!string.IsNullOrEmpty(err)) return BadRequest(new { message = err });

        return Ok(new { message = "Transferred stock received successfully. Transit account cleared and sub-ledger balanced." });
    }
}