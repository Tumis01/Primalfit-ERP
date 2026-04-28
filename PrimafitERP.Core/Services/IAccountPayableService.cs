using Primafit_ERP.Components.Models;

namespace Primafit_ERP.Services
{
    public interface IAccountsPayableService
    {
        // Bill Management
        Task<List<VendorBill>> GetPendingBillsAsync(Guid companyId);
        Task<VendorBill?> GetBillByIdAsync(Guid id);
        Task<string> SaveBillDraftAsync(VendorBill bill);

        // Event 1: Recognize Liability
        Task<string> PostVendorBillAsync(Guid billId, string userId);

        // Event 2: Discharge Liability (Payment)
        Task<string> PayVendorBillAsync(Guid billId, Guid bankAccountId, decimal amountPaid, DateTime paymentDate, string userId);
    }
}