using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Primafit_ERP.Components.Models;
using PrimafitERP.Api.DTOs;
using Primafit_ERP.Services;
using System.Security.Claims;

namespace PrimafitERP.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class CashbookController : ControllerBase
    {
        private readonly CashbookService _cashbookService;

        public CashbookController(CashbookService cashbookService)
        {
            _cashbookService = cashbookService;
        }

        // --- GET METHODS ---

        [HttpGet("active-batches/{companyId:guid}")]
        public async Task<IActionResult> GetActiveBatches(Guid companyId)
        {
            var batches = await _cashbookService.GetActiveBatchesAsync(companyId);
            return Ok(batches);
        }

        [HttpGet("batch/{batchId:guid}")]
        public async Task<IActionResult> GetBatchById(Guid batchId)
        {
            var batch = await _cashbookService.GetBatchByIdAsync(batchId);
            if (batch == null) return NotFound(new { message = "Batch not found." });
            
            return Ok(batch);
        }

        // --- BATCH LIFECYCLE METHODS ---

        [HttpPost("create-batch")]
        public async Task<IActionResult> CreateBatch([FromBody] CreateCashbookBatchDto dto)
        {
            if (!ModelState.IsValid) return BadRequest("Invalid batch data.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "API_USER";

            try
            {
                var batch = await _cashbookService.CreateBatchAsync(dto.CompanyId, dto.BankAccountId, userId);
                return Ok(new { message = "Cashbook batch created successfully.", batchId = batch.Id, reference = batch.BatchReference });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{batchId:guid}/lock")]
        public async Task<IActionResult> LockBatch(Guid batchId)
        {
            var err = await _cashbookService.SubmitForApprovalAsync(batchId);
            if (!string.IsNullOrEmpty(err)) return BadRequest(new { message = err });

            return Ok(new { message = "Batch locked and ready for posting." });
        }

        [HttpPost("{batchId:guid}/unlock")]
        public async Task<IActionResult> UnlockBatch(Guid batchId)
        {
            var err = await _cashbookService.RevertToDraftAsync(batchId);
            if (!string.IsNullOrEmpty(err)) return BadRequest(new { message = err });

            return Ok(new { message = "Batch unlocked and reverted to Draft." });
        }

        [HttpPost("{batchId:guid}/post")]
        public async Task<IActionResult> PostBatch(Guid batchId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "API_USER";

            var err = await _cashbookService.PostBatchAsync(batchId, userId);
            if (!string.IsNullOrEmpty(err)) return BadRequest(new { message = err });

            return Ok(new { message = "Cashbook batch successfully posted to the General Ledger." });
        }

        [HttpDelete("batch/{batchId:guid}")]
        public async Task<IActionResult> DeleteBatch(Guid batchId)
        {
            var err = await _cashbookService.DeleteDraftBatchAsync(batchId);
            if (!string.IsNullOrEmpty(err)) return BadRequest(new { message = err });

            return Ok(new { message = "Draft batch deleted successfully." });
        }

        // --- ENTRY (LINE) METHODS ---

        [HttpPost("{batchId:guid}/add-entry")]
        public async Task<IActionResult> AddEntry(Guid batchId, [FromBody] CreateCashbookEntryDto dto)
        {
            if (!ModelState.IsValid) return BadRequest("Invalid entry data.");

            var entry = new CashbookEntry
            {
                Id = Guid.NewGuid(),
                CashbookBatchId = batchId,
                TransactionDate = dto.TransactionDate,
                Reference = dto.Reference,
                Description = dto.Description,
                OffsetSegCoaId = dto.OffsetSegCoaId,
                Debit = dto.Debit,
                Credit = dto.Credit
            };

            var err = await _cashbookService.AddEntryAsync(entry);
            if (!string.IsNullOrEmpty(err)) return BadRequest(new { message = err });

            return Ok(new { message = "Entry added successfully.", entryId = entry.Id });
        }

        [HttpPut("update-entry")]
        public async Task<IActionResult> UpdateEntry([FromBody] UpdateCashbookEntryDto dto)
        {
            if (!ModelState.IsValid) return BadRequest("Invalid entry data.");

            var entry = new CashbookEntry
            {
                Id = dto.Id,
                TransactionDate = dto.TransactionDate,
                Reference = dto.Reference,
                Description = dto.Description,
                OffsetSegCoaId = dto.OffsetSegCoaId,
                Debit = dto.Debit,
                Credit = dto.Credit
            };

            var err = await _cashbookService.UpdateEntryAsync(entry);
            if (!string.IsNullOrEmpty(err)) return BadRequest(new { message = err });

            return Ok(new { message = "Entry updated successfully." });
        }

        [HttpDelete("entry/{entryId:guid}")]
        public async Task<IActionResult> RemoveEntry(Guid entryId)
        {
            await _cashbookService.RemoveEntryAsync(entryId);
            return Ok(new { message = "Entry removed successfully." });
        }
    }
}