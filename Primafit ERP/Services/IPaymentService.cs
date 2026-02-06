using Primafit_ERP.Components.Models;

namespace Primafit_ERP.Services
{
    public interface IPaymentService
    {
        // Data Retrieval
        Task<List<CustomerPayment>> GetPaymentsAsync(Guid companyId);
        Task<CustomerPayment?> GetPaymentByIdAsync(Guid id);

        // Operations
        Task<string> SaveDraftAsync(CustomerPayment payment);
        Task<string> ApplyInvoiceToPaymentAsync(Guid paymentId, Guid invoiceId, decimal amount);
        Task<string> PostPaymentAsync(Guid paymentId, string userId);
    }
}