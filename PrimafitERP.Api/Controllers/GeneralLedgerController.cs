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
    public class GeneralLedgerController : ControllerBase
    {
        private readonly GLOperationsService _glOps;

        public GeneralLedgerController(GLOperationsService glOps)
        {
            _glOps = glOps;
        }

        [HttpPost("journal-entry")]
        public async Task<IActionResult> PostJournal([FromBody] CreateJournalDto dto)
        {
            if (!ModelState.IsValid || !dto.Lines.Any()) return BadRequest("Invalid journal data.");

            var glLines = dto.Lines.Select(l => new GLJournalLine
            {
                SegCoaId = l.AccountId,
                Reference = l.Description,
                Debit = l.Debit,
                Credit = l.Credit
            }).ToList();

            // 1. Create the Batch
            var (err, batchId) = await _glOps.CreateJournalEntryAsync(
                dto.CompanyId,
                DateOnly.FromDateTime(dto.TransactionDate),
                "API Journal Entry",
                dto.Description,
                glLines);

            if (!string.IsNullOrEmpty(err)) return BadRequest(new { message = err });

            // 2. Auto-Post the Batch
            if (batchId.HasValue)
            {
                var postErr = await _glOps.PostBatchAsync(dto.CompanyId, batchId.Value);
                if (!string.IsNullOrEmpty(postErr)) return BadRequest(new { message = $"Created but failed to post: {postErr}" });
            }

            return Ok(new { message = "Journal posted successfully", batchId = batchId });
        }
    }
}