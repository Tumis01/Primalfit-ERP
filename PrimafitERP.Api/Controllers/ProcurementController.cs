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

        public ProcurementController(PurchasingService purchasingService)
        {
            _purchasingService = purchasingService;
        }

        [HttpPost("create-po")]
        public async Task<IActionResult> CreatePurchaseOrder([FromBody] CreatePurchaseOrderDto dto)
        {
            if (!ModelState.IsValid || !dto.Lines.Any())
                return BadRequest("Invalid PO data or missing line items.");

            // Map the DTO strictly to your Entity Model
            var newPo = new PurchaseOrder
            {
                Id = Guid.NewGuid(),
                CompanyId = dto.CompanyId,
                VendorId = dto.VendorId,
                CurrencyId = dto.CurrencyId,
                ExchangeRate = dto.ExchangeRate > 0 ? dto.ExchangeRate : 1,
                OrderNumber = dto.OrderNumber,
                OrderDate = dto.OrderDate,
                Status = PurchaseOrderStatus.Open, // API orders default to Open status

                // Map Tax & Discounts
                TaxId = dto.TaxId,
                TaxGLAccountId = dto.TaxGLAccountId,
                DiscountPercentage = dto.DiscountPercentage,
                DiscountAmount = dto.DiscountAmount,
                DiscountGlAccountId = dto.DiscountGlAccountId,

                // Map the Lines
                Lines = dto.Lines.Select(l => new PurchaseOrderLine
                {
                    Id = Guid.NewGuid(),
                    ItemId = l.ItemId,
                    QuantityOrdered = l.Quantity,
                    UnitCost = l.UnitCost
                }).ToList()
            };

            // Hand off to your existing business logic!
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
    }
}