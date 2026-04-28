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
    public class ProcurementController : ControllerBase
    {
        private readonly PurchasingService _purchasingService;
        private readonly InventoryValuationService _valuationService; 

        // injected via constructor here!
        public ProcurementController(PurchasingService purchasingService, InventoryValuationService valuationService)
        {
            _purchasingService = purchasingService;
            _valuationService = valuationService;
        }

        [HttpPost("create-po")]
        public async Task<IActionResult> CreatePurchaseOrder([FromBody] CreatePurchaseOrderDto dto)
        {
            if (!ModelState.IsValid || !dto.Lines.Any())
                return BadRequest("Invalid PO data or missing line items.");

            var newPo = new PurchaseOrder
            {
                Id = Guid.NewGuid(),
                CompanyId = dto.CompanyId,
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

            if (!string.IsNullOrEmpty(error))
                return BadRequest(new { message = error });

            return Ok(new
            {
                message = "Purchase Order generated successfully",
                poId = newPo.Id,
                orderNumber = newPo.OrderNumber
            });
        }
        [HttpPost("auto-invoice/{poId:guid}")]
        public async Task<IActionResult> AutoPostBillFromPo(Guid poId, [FromQuery] Guid companyId)
        {
            if (companyId == Guid.Empty) return BadRequest("CompanyId query parameter is required.");

            // This hits your brilliant AutoPostVendorBillFromPOAsync method which handles Tax, Discounts, and the GR/IR clearing!
            var err = await _purchasingService.AutoPostVendorBillFromPOAsync(poId, companyId);

            if (!string.IsNullOrEmpty(err)) return BadRequest(new { message = err });

            return Ok(new { message = "Vendor Bill auto-generated and posted to GL successfully." });
        }
        [HttpPost("{poId:guid}/receive-goods")]
        public async Task<IActionResult> ReceiveGoods(Guid poId, [FromBody] CreateGoodsReceiptDto dto)
        {
            if (!ModelState.IsValid || !dto.Lines.Any()) return BadRequest("Invalid GRN data.");

            var grn = new GoodsReceipt
            {
                Id = Guid.NewGuid(),
                CompanyId = dto.CompanyId,
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

            // 1. Save GRN & Post initial Stock/GL Impact
            var err = await _purchasingService.SaveGoodsReceiptAsync(grn, dto.WarehouseId);
            if (!string.IsNullOrEmpty(err)) return BadRequest(new { message = err });

            // 2. Automatically recalculate WACC based on the new receipt
            var waccErr = await _valuationService.RecalculateWACC(grn.Id);
            if (!string.IsNullOrEmpty(waccErr)) return BadRequest(new { message = $"Goods Received, but WACC calculation failed: {waccErr}" });

            return Ok(new { message = "Goods received and WACC recalculated successfully.", grnId = grn.Id });
        }
    }
}