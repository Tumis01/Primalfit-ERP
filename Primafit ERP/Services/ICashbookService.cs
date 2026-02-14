using Primafit_ERP.Components.Models;

namespace Primafit_ERP.Services
{
    public interface ICashbookService
    {
        Task<List<CashbookBatch>> GetActiveBatchesAsync(Guid companyId);
        Task<CashbookBatch> GetBatchByIdAsync(Guid id);
        Task<CashbookBatch> CreateBatchAsync(Guid companyId, Guid bankSegCoaId, string userId);
        Task<string> AddEntryAsync(CashbookEntry entry);
        Task<string> UpdateEntryAsync(CashbookEntry entry);
        Task RemoveEntryAsync(Guid id);
        Task<string> SubmitForApprovalAsync(Guid batchId);
        Task<string> RevertToDraftAsync(Guid batchId);
        Task<string> PostBatchAsync(Guid batchId, string userId);
    }
}