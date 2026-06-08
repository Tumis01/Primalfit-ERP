using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Primafit_ERP.Components.Models;
using PrimafitERP.Api.DTOs;
using Primafit_ERP.Services;
using System.Security.Claims;

namespace PrimafitERP.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "SuperAdmin, CFO, Accountant")] // Locked down to authorized financial roles
public class JournalEntryController : ControllerBase
{
    private readonly GLOperationsService _glOps;

    public JournalEntryController(GLOperationsService glOps)
    {
        _glOps = glOps;
    }

    [HttpPost("journal-entry")]
    public async Task<IActionResult> PostJournal([FromBody] CreateJournalDto dto)
    {
        // 1. Secure Claim Extractions
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var companyClaim = User.FindFirst("CompanyId")?.Value;

        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(companyClaim, out Guid companyId))
            return Unauthorized("User session configuration is invalid or expired.");

        // 2. Payload Assertions
        if (!ModelState.IsValid || dto.Lines == null || !dto.Lines.Any())
            return BadRequest("Invalid journal data.");

        var glLines = dto.Lines.Select(l => new GLJournalLine
        {
            Id = Guid.NewGuid(),
            SegCoaId = l.AccountId,
            Reference = l.Description,
            Debit = l.Debit,
            Credit = l.Credit,
            IsPosted = false
        }).ToList();

        // 3. Create the Journal Entry Batch Wrapper
        // Maps to: CreateJournalEntryAsync(Guid companyId, DateOnly txnDate, string batchName, string? description, List<GLJournalLine> lines, string userId, bool requireAllowJournal = false)
        var (err, batchId) = await _glOps.CreateJournalEntryAsync(
            companyId,
            DateOnly.FromDateTime(dto.TransactionDate),
            $"API-{DateTime.UtcNow:yyMMdd}",
            dto.Description,
            glLines,
            userId,
            requireAllowJournal: true // Enforces standard manual journal business validation checks
        );

        if (!string.IsNullOrEmpty(err)) return BadRequest(new { message = err });

        // 4. Post the Batch to the General Ledger Atomically
        // Maps to: PostBatchAsync(Guid companyId, Guid batchId, string userId)
        if (batchId.HasValue)
        {
            var postErr = await _glOps.PostBatchAsync(companyId, batchId.Value, userId);
            if (!string.IsNullOrEmpty(postErr))
                return BadRequest(new { message = $"Journal created as draft ({batchId}), but failed to commit post to GL: {postErr}" });
        }

        return Ok(new { message = "Journal entry created and posted successfully to the General Ledger.", batchId = batchId });
    }
}