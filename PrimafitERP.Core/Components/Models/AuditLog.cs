using System.ComponentModel.DataAnnotations;

namespace Primafit_ERP.Components.Models
{
    public class AuditLog
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid CompanyId { get; set; }
        public string UserId { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty; 
        public string EntityType { get; set; } = string.Empty; 
        public Guid EntityId { get; set; }

        public string? Details { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
