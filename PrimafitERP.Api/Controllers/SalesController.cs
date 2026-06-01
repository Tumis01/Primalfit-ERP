using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Primafit_ERP.Components.Models;
using PrimafitERP.Api.DTOs;
using Primafit_ERP.Services;

namespace PrimafitERP.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "SuperAdmin, CFO, Accountant")] // Locked down strictly to validated operational system roles
public class SalesController : ControllerBase
{
    private readonly SalesService _salesService;

    public SalesController(SalesService salesService)
    {
        _salesService = salesService;
    }

    // 1. CREATE A SECURE SALES ORDER
    [HttpPost("create-order")]
    public async Task<IActionResult> CreateOrder([FromBody] CreateSalesOrderDto dto)
    {
        // Secure Claim Extractions
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var companyClaim = User.FindFirst("CompanyId")?.Value;

        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(companyClaim, out Guid companyId))
            return Unauthorized("User session configuration is invalid or expired.");

        if (!ModelState.IsValid || dto.Lines == null || !dto.Lines.Any())
            return BadRequest("Invalid order data or missing lines.");

        var newOrder = new SalesOrder
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId, // Overridden securely via server-side claims context to enforce multi-tenancy boundaries
            CustomerId = dto.CustomerId,
            WarehouseId = dto.WarehouseId,
            CurrencyId = dto.CurrencyId,
            ExchangeRate = dto.ExchangeRate > 0 ? dto.ExchangeRate : 1,

            // Map API DateTime to Entity DateOnly
            Date = DateOnly.FromDateTime(dto.OrderDate),

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
        if (!string.IsNullOrEmpty(error)) return BadRequest(new { message = error });

        return Ok(new
        {
            message = "Sales Order created successfully",
            orderId = newOrder.Id,
            orderNumber = newOrder.OrderNumber
        });
    }

    // 2. CONVERT AN OPEN ORDER TO A DRAFT INVOICE
    [HttpPost("{orderId:guid}/convert-to-invoice")]
    public async Task<IActionResult> ConvertToInvoice(Guid orderId)
    {
        var error = await _salesService.ConvertOrderToInvoiceAsync(orderId);
        if (!string.IsNullOrEmpty(error)) return BadRequest(new { message = error });

        return Ok(new { message = "Order successfully converted to Invoice draft." });
    }

    // 3. FULFILL & SHIP INVOICE ITEMS (Physical Stock Movement)
    [HttpPost("{orderId:guid}/ship-goods")]
    public async Task<IActionResult> ShipOrder(Guid orderId, [FromQuery] Guid warehouseId)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized("User identifier missing from security context token.");

        if (warehouseId == Guid.Empty) return BadRequest("A valid source fulfillment warehouseId is required.");

        // Maps to: ShipOrderAsync(Guid orderId, Guid warehouseId, string userId)
        var error = await _salesService.ShipOrderAsync(orderId, warehouseId, userId);
        if (!string.IsNullOrEmpty(error)) return BadRequest(new { message = error });

        return Ok(new { message = "Goods dispatched successfully. Inventory accounts reduced and COGS revalued." });
    }

    // 4. POST COMPLETED INVOICE (Financial Ledger Transaction)
    [HttpPost("{invoiceId:guid}/post-invoice")]
    public async Task<IActionResult> PostInvoice(Guid invoiceId)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized("User identifier missing from security context token.");

        // Maps to: InvoiceOrderAsync(Guid orderId, string userId)
        var error = await _salesService.InvoiceOrderAsync(invoiceId, userId);
        if (!string.IsNullOrEmpty(error)) return BadRequest(new { message = error });

        return Ok(new { message = "Invoice finalized and posted to General Ledger accounts successfully." });
    }
}