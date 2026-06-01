using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Primafit_ERP.Components.Models;
using PrimafitERP.Api.DTOs;
using Primafit_ERP.Services;

namespace PrimafitERP.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "SuperAdmin, CFO, Accountant")] // Locked to authorized system procurement and finance roles
public class ProcurementController : ControllerBase
{
    private readonly PurchasingService _purchasingService;
    private readonly InventoryValuationService _valuationService;

    public ProcurementController(PurchasingService purchasingService, InventoryValuationService valuationService)
    {
        _purchasingService = purchasingService;
        _valuationService = valuationService;
    }

    // 1. GENERATE A NEW PURCHASE ORDER
    [HttpPost("create-po")]
    public async Task<IActionResult> CreatePurchaseOrder([FromBody] CreatePurchaseOrderDto dto)
    {
        // Secure Claims Extractions
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var companyClaim = User.FindFirst("CompanyId")?.Value;

        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(companyClaim, out Guid companyId))
            return Unauthorized("User session configuration is invalid or expired.");

        if (!ModelState.IsValid || dto.Lines == null || !dto.Lines.Any())
            return BadRequest("Invalid PO data or missing line items.");

        var newPo = new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId, // Overridden securely via claims to maintain multi-tenant validation boundaries
            VendorId = dto.VendorId,
            CurrencyId = dto.CurrencyId,
            ExchangeRate = dto.ExchangeRate > 0 ? dto.ExchangeRate : 1,
            OrderDate = dto.OrderDate,
            Status = PurchaseOrderStatus.Open,

            TaxId = dto.TaxId,
            TaxGLAccountId = dto.TaxGLAccountId,
            DiscountPercentage = dto.DiscountPercentage,
            DiscountAmount = dto.DiscountAmount,
            DiscountGlAccountId = dto.DiscountGlAccountId,

            Lines = dto.Lines.Select(l => new PurchaseOrderLine
            {
                Id = Guid.NewGuid(),
                ItemId = l.ItemId,
                QuantityOrdered = l.Quantity,
                UnitCost = l.UnitCost
            }).ToList()
        };

        var error = await _purchasingService.SavePurchaseOrderAsync(newPo);
        if (!string.IsNullOrEmpty(error)) return BadRequest(new { message = error });

        return Ok(new
        {
            message = "Purchase Order generated successfully",
            poId = newPo.Id,
            orderNumber = newPo.OrderNumber
        });
    }

    // 2. AUTO-INVOICE A PURCHASE ORDER
    [HttpPost("auto-invoice/{poId:guid}")]
    public async Task<IActionResult> AutoPostBillFromPo(Guid poId)
    {
        // Secure Claims Extractions
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var companyClaim = User.FindFirst("CompanyId")?.Value;

        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(companyClaim, out Guid companyId))
            return Unauthorized("User session configuration is invalid or expired.");

        // Synchronized parameter mismatch with: AutoPostVendorBillFromPOAsync(Guid poId, Guid companyId, string userId)
        var err = await _purchasingService.AutoPostVendorBillFromPOAsync(poId, companyId, userId);
        if (!string.IsNullOrEmpty(err)) return BadRequest(new { message = err });

        return Ok(new { message = "Vendor Bill auto-generated and posted to GL successfully from Goods Receipt Note references." });
    }

    // 3. RECEIVE GOODS AGAINST AN OPEN PO
    [HttpPost("{poId:guid}/receive-goods")]
    public async Task<IActionResult> ReceiveGoods(Guid poId, [FromBody] CreateGoodsReceiptDto dto)
    {
        // Secure Claims Extractions
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var companyClaim = User.FindFirst("CompanyId")?.Value;

        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(companyClaim, out Guid companyId))
            return Unauthorized("User session configuration is invalid or expired.");

        if (!ModelState.IsValid || dto.Lines == null || !dto.Lines.Any())
            return BadRequest("Invalid GRN data.");

        var grn = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId, // Bound directly to secure claims context to prevent payload poisoning
            PurchaseOrderId = poId,
            InventoryGlAccountId = dto.CreditLiabilityGlAccountId,
            GrnNumber = dto.GrnNumber,
            DateReceived = dto.DateReceived,
            Lines = dto.Lines.Select(l => new GoodsReceiptLine
            {
                Id = Guid.NewGuid(),
                PurchaseOrderLineId = l.PurchaseOrderLineId,
                QuantityReceived = l.QuantityReceived
            }).ToList()
        };

        // Synchronized parameter mismatch with: SaveGoodsReceiptAsync(GoodsReceipt grn, Guid warehouseId, string userId)
        var err = await _purchasingService.SaveGoodsReceiptAsync(grn, dto.WarehouseId, userId);
        if (!string.IsNullOrEmpty(err)) return BadRequest(new { message = err });

        // Automatically recalculate WACC based on the new receipt via your valuation component
        var waccErr = await _valuationService.RecalculateWACC(grn.Id);
        if (!string.IsNullOrEmpty(waccErr))
            return BadRequest(new { message = $"Goods Received, but matching WACC calculation failed: {waccErr}" });

        return Ok(new { message = "Goods received and WACC recalculated successfully.", grnId = grn.Id });
    }
}