using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Primafit_ERP.Components.Models;
using PrimafitERP.Api.DTOs;
using Primafit_ERP.Services;

namespace PrimafitERP.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "SuperAdmin, CFO, Accountant")] // Locked to validated financial roles
    public class CustomerReceiptController : ControllerBase
    {
        private readonly PaymentService _paymentService;

        public CustomerReceiptController(PaymentService paymentService)
        {
            _paymentService = paymentService;
        }

        // CREATE & POST PAYMENT IN ONE ATOMIC OPERATION
        [HttpPost("receive-payment")]
        public async Task<IActionResult> ReceivePayment([FromBody] CreateCustomerPaymentDto dto)
        {
            // 1. Secure Server-Side Claims Extraction
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var companyClaim = User.FindFirst("CompanyId")?.Value;

            if (string.IsNullOrEmpty(userId) || !Guid.TryParse(companyClaim, out Guid companyId))
                return Unauthorized("User session context is invalid or has expired.");

            // 2. Validate Inbound Request Payload Details
            if (!ModelState.IsValid || dto.Applications == null || !dto.Applications.Any())
                return BadRequest("Invalid payment application data.");

            var payment = new CustomerPayment
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId, // Bound directly to claims context to prevent cross-tenant data poisoning
                CustomerId = dto.CustomerId,
                DepositToGlAccountId = dto.DepositToGlAccountId,
                CreditGlAccountId = dto.CreditGlAccountId,
                AmountReceived = dto.AmountReceived,
                ExchangeRate = dto.ExchangeRate > 0 ? dto.ExchangeRate : 1,
                Date = dto.Date,
                Reference = dto.Reference,
                Status = PaymentStatus.Draft,
                Applications = dto.Applications.Select(a => new PaymentApplication
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = a.InvoiceId,
                    AppliedAmount = a.AppliedAmount
                }).ToList()
            };

            // 3. Persist Draft Receipt Entry to Database
            var saveErr = await _paymentService.SavePaymentAsync(payment);
            if (!string.IsNullOrEmpty(saveErr)) return BadRequest(new { message = saveErr });

            // 4. Post Payment Directly to General Ledger with User Audit Controls
            // Synchronized with: PostPaymentAsync(Guid paymentId, string userId)
            var postErr = await _paymentService.PostPaymentAsync(payment.Id, userId);
            if (!string.IsNullOrEmpty(postErr))
                return BadRequest(new { message = $"Receipt saved as draft ({payment.Id}), but failed to commit post to GL: {postErr}" });

            return Ok(new { message = "Customer payment entry received, distributed, and posted successfully to the GL.", paymentId = payment.Id });
        }
    }
}