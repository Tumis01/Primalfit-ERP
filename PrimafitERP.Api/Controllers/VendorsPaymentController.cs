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
    [Authorize(Roles = "SuperAdmin, CFO, Accountant")] 
    public class AccountsPayableController : ControllerBase
    {
        private readonly PurchasingService _purchasingService;

        public AccountsPayableController(PurchasingService purchasingService)
        {
            _purchasingService = purchasingService;
        }

        [HttpPost("manual-bill")]
        public async Task<IActionResult> CreateManualBill([FromBody] CreateManualVendorBillDto dto)
        {
            // Secure Claims Extractions
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var companyClaim = User.FindFirst("CompanyId")?.Value;

            if (string.IsNullOrEmpty(userId) || !Guid.TryParse(companyClaim, out Guid companyId))
                return Unauthorized("User session configuration is invalid or expired.");

            if (!ModelState.IsValid || dto.Lines == null || !dto.Lines.Any())
                return BadRequest("Invalid Bill data.");

            var bill = new VendorBill
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId, 
                VendorId = dto.VendorId,
                AccountsPayableGlId = dto.AccountsPayableGlId,
                CurrencyId = dto.CurrencyId,
                ExchangeRate = dto.ExchangeRate > 0 ? dto.ExchangeRate : 1,
                ExternalInvoiceNumber = dto.ExternalInvoiceNumber,
                BillDate = dto.BillDate,
                PurchaseOrderId = dto.PurchaseOrderId,
                Lines = dto.Lines.Select(l => new VendorBillLine
                {
                    Id = Guid.NewGuid(),
                    ItemId = l.ItemId,
                    ExpenseGlAccountId = l.ExpenseGlAccountId,
                    QuantityBilled = l.QuantityBilled,
                    UnitCostBilled = l.UnitCostBilled
                }).ToList()
            };

            // Calculate Totals based on business logic rules
            bill.TotalAmountForeign = Math.Round(bill.Lines.Sum(l => l.QuantityBilled * l.UnitCostBilled), 2);
            bill.TotalAmount = bill.TotalAmountForeign * bill.ExchangeRate;

            // Step A: Save Draft Bill
            var saveErr = await _purchasingService.SaveVendorBillAsync(bill);
            if (!string.IsNullOrEmpty(saveErr)) return BadRequest(new { message = saveErr });

            // Step B: Post Bill to GL
            // AUTOMATED FIX: Passed the verified claims string 'userId' into the signature parameters block
            var postErr = await _purchasingService.PostVendorBillAsync(bill.Id, userId);
            if (!string.IsNullOrEmpty(postErr)) return BadRequest(new { message = $"Saved but failed to post: {postErr}" });

            return Ok(new { message = "Vendor Bill saved and posted successfully.", billId = bill.Id });
        }

        // 2. PAY VENDOR BILL
        [HttpPost("{billId:guid}/pay")]
        public async Task<IActionResult> PayVendorBill(Guid billId, [FromBody] PayVendorBillDto dto)
        {
            // Secure Claims Extractions
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var companyClaim = User.FindFirst("CompanyId")?.Value;

            if (string.IsNullOrEmpty(userId) || !Guid.TryParse(companyClaim, out Guid companyId))
                return Unauthorized("User session configuration is invalid or expired.");

            if (!ModelState.IsValid || dto.Amount <= 0)
                return BadRequest("Invalid payment amount.");

            var payment = new VendorPayment
            {
                Id = Guid.NewGuid(),
                VendorBillId = billId,
                BankGlAccountId = dto.BankGlAccountId,
                Amount = dto.Amount,
                Date = dto.Date,
                Reference = dto.Reference
            };

            // AUTOMATED FIX: Synchronized method call to expect both company context and operator metadata string parameters
            // Matches signature layout: PostVendorPaymentAsync(VendorPayment payment, Guid companyId, string userId)
            var err = await _purchasingService.PostVendorPaymentAsync(payment, companyId, userId);

            if (!string.IsNullOrEmpty(err)) return BadRequest(new { message = err });

            return Ok(new { message = "Payment processed and posted to GL successfully." });
        }
    }
}