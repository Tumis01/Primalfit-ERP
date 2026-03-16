using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Primafit_ERP.Components.Models;
using Primafit_ERP.Services;

namespace PrimafitERP.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class CreditNoteController : ControllerBase
    {
        private readonly CreditNoteService _creditNoteService;

        public CreditNoteController(CreditNoteService creditNoteService)
        {
            _creditNoteService = creditNoteService;
        }

        // 1. GENERATE DRAFT
        [HttpPost("generate/{orderId:guid}")]
        public async Task<IActionResult> GenerateDraft(Guid orderId, [FromQuery] Guid userId)
        {
            try
            {
                var cn = await _creditNoteService.CreateDraftFromOrderAsync(orderId, userId);
                return Ok(new { message = "Draft Credit Note generated.", creditNoteId = cn.Id });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // 2. POST REVERSAL
        [HttpPost("post/{creditNoteId:guid}")]
        public async Task<IActionResult> PostCreditNote(Guid creditNoteId, [FromQuery] Guid userId)
        {
            var error = await _creditNoteService.PostCreditNoteAsync(creditNoteId, userId);

            if (!string.IsNullOrEmpty(error)) return BadRequest(new { message = error });

            return Ok(new { message = "Credit Note posted and Inventory updated (if applicable)." });
        }
    }
}