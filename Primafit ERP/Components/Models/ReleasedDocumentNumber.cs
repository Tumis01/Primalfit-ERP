using System.ComponentModel.DataAnnotations;

namespace Primafit_ERP.Components.Models;

// A number enters this pool only when its draft is deleted successfully.
// Keeping the allocation high-water mark protects numbers reserved by other users.
public sealed class ReleasedDocumentNumber
{
    public static string LockKey(Guid companyId, SequenceTargetType target) => $"ERP:DocumentNumber:{companyId:N}:{(int)target}";
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public SequenceTargetType TargetType { get; set; }
    [MaxLength(200)] public string DocumentNumber { get; set; } = "";
    public DateTime DocumentDate { get; set; }
    public DateTime ReleasedAt { get; set; } = DateTime.UtcNow;
}
