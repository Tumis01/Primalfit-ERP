using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Primafit_ERP.Components.Models;
using PrimafitERP.Api.DTOs;
using Primafit_ERP.Services;

namespace PrimafitERP.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "SuperAdmin, CFO, Accountant")] // Locked strictly to authorized system operations and warehouse supervisors
public class ShipmentController : ControllerBase
{
    private readonly ShipmentService _shipmentService;

    public ShipmentController(ShipmentService shipmentService)
    {
        _shipmentService = shipmentService;
    }

    // 1. GENERATE PENDING DISPATCH DOCUMENT FROM COMPLETED SALES ORDER
    [HttpPost("generate/{orderId:guid}")]
    public async Task<IActionResult> GenerateShipment(Guid orderId)
    {
        var error = await _shipmentService.CreateShipmentFromOrderAsync(orderId);
        if (!string.IsNullOrEmpty(error)) return BadRequest(new { message = error });

        return Ok(new { message = "Pending shipment document generated and added to dispatch queue successfully." });
    }

    // 2. POST ACTUAL SHIPMENT (Deducts Physical Stock, Evaluates Dynamic Costing & Posts COGS)
    [HttpPost("post/{shipmentId:guid}")]
    public async Task<IActionResult> PostShipment(Guid shipmentId, [FromBody] PostShipmentDto dto)
    {
        // 1. Secure Server-Side Claims Identification Extraction
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized("User session identifier context is invalid or has expired.");

        // 2. Payload Model State Verification Checks
        if (!ModelState.IsValid || dto.Lines == null || !dto.Lines.Any())
            return BadRequest("Missing shipment lines data.");

        var actualShippedLines = dto.Lines.Select(l => new SalesShipmentLine
        {
            Id = l.ShipmentLineId, // Direct linkage mapping back to the tracking item inside the database records
            QtyShipped = l.QtyShipped
        }).ToList();

        // 3. Commit Balanced Transactions through the core business engine
        // Synchronized with: PostShipmentAsync(Guid shipmentId, List<SalesShipmentLine> actualShippedLines, string confirmedBy, string userId)
        var error = await _shipmentService.PostShipmentAsync(shipmentId, actualShippedLines, dto.ConfirmedBy, userId);

        if (!string.IsNullOrEmpty(error)) return BadRequest(new { message = error });

        return Ok(new { message = "Stock deducted, dispatch records updated, and matching sub-ledger costings posted to GL." });
    }
}