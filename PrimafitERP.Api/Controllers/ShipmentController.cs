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
    public class ShipmentController : ControllerBase
    {
        private readonly ShipmentService _shipmentService;

        public ShipmentController(ShipmentService shipmentService)
        {
            _shipmentService = shipmentService;
        }

        // 1. GENERATE DISPATCH DOCUMENT
        [HttpPost("generate/{orderId:guid}")]
        public async Task<IActionResult> GenerateShipment(Guid orderId)
        {
            var error = await _shipmentService.CreateShipmentFromOrderAsync(orderId);
            if (!string.IsNullOrEmpty(error)) return BadRequest(new { message = error });

            return Ok(new { message = "Pending shipment document generated." });
        }

        // 2. POST ACTUAL SHIPMENT (Deducts Stock, Posts COGS)
        [HttpPost("post/{shipmentId:guid}")]
        public async Task<IActionResult> PostShipment(Guid shipmentId, [FromBody] PostShipmentDto dto)
        {
            if (!ModelState.IsValid || !dto.Lines.Any()) return BadRequest("Missing shipment lines.");

            var actualShippedLines = dto.Lines.Select(l => new SalesShipmentLine
            {
                Id = l.ShipmentLineId, // This must match the DB ShipmentLine ID
                QtyShipped = l.QtyShipped
            }).ToList();

            var error = await _shipmentService.PostShipmentAsync(shipmentId, actualShippedLines, dto.ConfirmedBy);

            if (!string.IsNullOrEmpty(error)) return BadRequest(new { message = error });

            return Ok(new { message = "Stock deducted and shipment posted." });
        }
    }
}