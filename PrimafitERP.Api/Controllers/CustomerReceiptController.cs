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
    public class CustomerReceiptController : ControllerBase
    {
        private readonly PaymentService _paymentService;

        public CustomerReceiptController(PaymentService paymentService)
        {
            _paymentService = paymentService;
        }

        // CREATE & POST PAYMENT IN ONE SHOT
        [HttpPost("receive-payment")]
        public async Task<IActionResult> ReceivePayment([FromBody] CreateCustomerPaymentDto dto)
        {
            if (!ModelState.IsValid || !dto.Applications.Any()) return BadRequest("Invalid payment data.");

            var payment = new CustomerPayment
            {
                Id = Guid.NewGuid(),
                CompanyId = dto.CompanyId,
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

            // 1. Save it to DB
            var saveErr = await _paymentService.SavePaymentAsync(payment);
            if (!string.IsNullOrEmpty(saveErr)) return BadRequest(new { message = saveErr });

            // 2. Immediately Post it to GL
            var postErr = await _paymentService.PostPaymentAsync(payment.Id);
            if (!string.IsNullOrEmpty(postErr)) return BadRequest(new { message = $"Saved, but failed to post: {postErr}" });

            return Ok(new { message = "Payment received and applied successfully.", paymentId = payment.Id });
        }
    }
}