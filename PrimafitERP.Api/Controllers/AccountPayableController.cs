using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Primafit_ERP.Components.Models;
using PrimafitERP.Api.DTOs;
using Primafit_ERP.Services;

namespace PrimafitERP.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "SuperAdmin, Chief Of Financial Officer, Accountant")] // Locked to finance users
    public class AccountsPayableController : ControllerBase
    {
        private readonly PurchasingService _purchasingService;

        public AccountsPayableController(PurchasingService purchasingService)
        {
            _purchasingService = purchasingService;
        }

        // 1. AUTO-INVOICE A PURCHASE ORDER
        

        // 2. CREATE MANUAL VENDOR BILL (Direct Expense without PO)
        [HttpPost("manual-bill")]
        public async Task<IActionResult> CreateManualBill([FromBody] CreateManualVendorBillDto dto)
        {
            if (!ModelState.IsValid || !dto.Lines.Any()) return BadRequest("Invalid Bill data.");

            var bill = new VendorBill
            {
                Id = Guid.NewGuid(),
                CompanyId = dto.CompanyId,
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

            // Calculate Totals based on Blazor logic
            bill.TotalAmountForeign = Math.Round(bill.Lines.Sum(l => l.QuantityBilled * l.UnitCostBilled), 2);
            bill.TotalAmount = bill.TotalAmountForeign * bill.ExchangeRate;

            // 1. Save Bill
            var saveErr = await _purchasingService.SaveVendorBillAsync(bill);
            if (!string.IsNullOrEmpty(saveErr)) return BadRequest(new { message = saveErr });

            // 2. Post Bill to GL
            var postErr = await _purchasingService.PostVendorBillAsync(bill.Id);
            if (!string.IsNullOrEmpty(postErr)) return BadRequest(new { message = $"Saved but failed to post: {postErr}" });

            return Ok(new { message = "Vendor Bill saved and posted successfully.", billId = bill.Id });
        }

        // 3. PAY VENDOR BILL
        [HttpPost("{billId:guid}/pay")]
        public async Task<IActionResult> PayVendorBill(Guid billId, [FromBody] PayVendorBillDto dto)
        {
            if (!ModelState.IsValid || dto.Amount <= 0) return BadRequest("Invalid payment amount.");

            var payment = new VendorPayment
            {
                Id = Guid.NewGuid(),
                VendorBillId = billId,
                BankGlAccountId = dto.BankGlAccountId,
                Amount = dto.Amount,
                Date = dto.Date,
                Reference = dto.Reference
            };

            var err = await _purchasingService.PostVendorPaymentAsync(payment, dto.CompanyId);

            if (!string.IsNullOrEmpty(err)) return BadRequest(new { message = err });

            return Ok(new { message = "Payment processed and posted to GL successfully." });
        }
    }
}