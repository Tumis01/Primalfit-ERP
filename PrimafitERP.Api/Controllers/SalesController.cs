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
    public class SalesController : ControllerBase
    {
        private readonly SalesService _salesService;

        public SalesController(SalesService salesService)
        {
            _salesService = salesService;
        }

        [HttpPost("create-order")]
        public async Task<IActionResult> CreateOrder([FromBody] CreateSalesOrderDto dto)
        {
            if (!ModelState.IsValid || !dto.Lines.Any())
                return BadRequest("Invalid order data or missing lines.");

            var newOrder = new SalesOrder
            {
                Id = Guid.NewGuid(),
                CompanyId = dto.CompanyId,
                CustomerId = dto.CustomerId,
                WarehouseId = dto.WarehouseId,
                CurrencyId = dto.CurrencyId,
                ExchangeRate = dto.ExchangeRate > 0 ? dto.ExchangeRate : 1,

                // Map API DateTime to Entity DateOnly
                Date = DateOnly.FromDateTime(dto.OrderDate),
                OrderNumber = dto.OrderNumber,

                // Defaulting to Order instead of Draft so it actively reserves stock
                Status = OrderStatus.Order,

                // Map Tax & Discounts
                TaxId = dto.TaxId,
                TaxGLAccountId = dto.TaxGLAccountId,
                DiscountPercentage = dto.DiscountPercentage,
                DiscountAmount = dto.DiscountAmount,
                DiscountGlAccountId = dto.DiscountGlAccountId,

                // Map the Lines
                Lines = dto.Lines.Select(l => new SalesOrderLine
                {
                    Id = Guid.NewGuid(),
                    ItemId = l.ItemId,
                    Quantity = l.Quantity,
                    UnitPrice = l.UnitPrice
                }).ToList()
            };

            // Route it directly through your core business logic!
            var error = await _salesService.SaveOrderAsync(newOrder);

            if (!string.IsNullOrEmpty(error))
                return BadRequest(new { message = error });

            return Ok(new
            {
                message = "Sales Order created successfully",
                orderId = newOrder.Id,
                orderNumber = newOrder.OrderNumber
            });
        }
    }
}