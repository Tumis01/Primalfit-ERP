using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Primafit_ERP.Components.Models;
using Primafit_ERP.Services;

namespace PrimafitERP.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "SuperAdmin, CFO, Accountant")] // Locked to authorized financial personnel
public class CreditNoteController : ControllerBase
{
    private readonly CreditNoteService _creditNoteService;

    public CreditNoteController(CreditNoteService creditNoteService)
    {
        _creditNoteService = creditNoteService;
    }

    // --- LOOKUP ROUTING WORKFLOWS ---

    [HttpGet("shipped-invoices/{customerId:guid}")]
    public async Task<IActionResult> GetShippedInvoices(Guid customerId)
    {
        var companyClaim = User.FindFirst("CompanyId")?.Value;
        if (!Guid.TryParse(companyClaim, out Guid companyId))
            return Unauthorized("User session company context is missing or invalid.");

        var invoices = await _creditNoteService.GetShippedInvoicesByCustomerAsync(companyId, customerId);
        return Ok(invoices);
    }

    [HttpGet("paid-invoices/{customerId:guid}")]
    public async Task<IActionResult> GetPaidInvoices(Guid customerId)
    {
        var companyClaim = User.FindFirst("CompanyId")?.Value;
        if (!Guid.TryParse(companyClaim, out Guid companyId))
            return Unauthorized("User session company context is missing or invalid.");

        var invoices = await _creditNoteService.GetPaidInvoicesByCustomerAsync(companyId, customerId);
        return Ok(invoices);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var companyClaim = User.FindFirst("CompanyId")?.Value;
        if (!Guid.TryParse(companyClaim, out Guid companyId))
            return Unauthorized("User session company context is missing or invalid.");

        var cn = await _creditNoteService.GetByIdAsync(id, companyId);
        if (cn == null) return NotFound(new { message = "Credit Note record not found." });

        return Ok(cn);
    }

    // --- FLOW A: PHYSICAL STOCK RETURNS ---

    [HttpPost("generate-stock-return/{orderId:guid}")]
    public async Task<IActionResult> GenerateStockReturnDraft(Guid orderId)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out Guid userId))
            return Unauthorized("User session identifier context is invalid or expired.");

        try
        {
            var cn = await _creditNoteService.CreateStockReturnDraftAsync(orderId, userId);
            return Ok(new { message = "Draft Stock Return Credit Note generated.", creditNoteId = cn.Id });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("post-stock/{creditNoteId:guid}")]
    public async Task<IActionResult> PostStockCreditNote(Guid creditNoteId)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out Guid userId))
            return Unauthorized("User session identifier context is invalid or expired.");

        var error = await _creditNoteService.PostStockCreditNoteAsync(creditNoteId, userId);
        if (!string.IsNullOrEmpty(error)) return BadRequest(new { message = error });

        return Ok(new { message = "Stock Credit Note posted, inventory restocked, and GL revalued successfully." });
    }

    // --- FLOW B: FINANCIAL PAYMENT REFUNDS ---

    [HttpPost("generate-financial-refund/{orderId:guid}")]
    public async Task<IActionResult> GenerateFinancialRefundDraft(Guid orderId)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out Guid userId))
            return Unauthorized("User session identifier context is invalid or expired.");

        try
        {
            var cn = await _creditNoteService.CreateFinancialRefundDraftAsync(orderId, userId);
            return Ok(new { message = "Draft Financial Refund Credit Note generated.", creditNoteId = cn.Id });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("post-financial/{creditNoteId:guid}")]
    public async Task<IActionResult> PostFinancialRefund(Guid creditNoteId, [FromQuery] Guid targetBankGlId)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out Guid userId))
            return Unauthorized("User session identifier context is invalid or expired.");

        if (targetBankGlId == Guid.Empty)
            return BadRequest(new { message = "You must provide a valid Target Bank GL Account ID to distribute the outflow refund." });

        var error = await _creditNoteService.PostFinancialRefundDraftAsync(creditNoteId, targetBankGlId, userId);
        if (!string.IsNullOrEmpty(error)) return BadRequest(new { message = error });

        return Ok(new { message = "Financial Refund posted, bank assets reduced, and AR claims adjusted successfully." });
    }

    // --- SHARED MANAGEMENT UTILITIES ---

    [HttpPut("save-draft")]
    public async Task<IActionResult> SaveDraft([FromBody] CreditNote note)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var error = await _creditNoteService.SaveDraftAsync(note);
        if (!string.IsNullOrEmpty(error)) return BadRequest(new { message = error });

        return Ok(new { message = "Draft modifications recorded successfully." });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteDraft(Guid id)
    {
        var error = await _creditNoteService.DeleteDraftAsync(id);
        if (!string.IsNullOrEmpty(error)) return BadRequest(new { message = error });

        return Ok(new { message = "Draft Credit Note dropped successfully." });
    }
}