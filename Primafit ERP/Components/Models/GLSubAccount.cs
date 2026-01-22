using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public class GLSubAccount
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public Guid MasterAccountId { get; set; } // Foreign Key to Master Table

        [ForeignKey("MasterAccountId")]
        public virtual GLMasterAccount? MasterAccount { get; set; }

        [Required]
        [MaxLength(20)]
        public string SubCode { get; set; } // e.g., "001"

        [Required]
        [MaxLength(100)]
        public string AccountName { get; set; } // e.g., "Chase Bank Main"

        // The "Link-Later" Bridge to Projects
        public Guid? ProjectId { get; set; }

        [ForeignKey("ProjectId")]
        public virtual Project? LinkedProject { get; set; }

        public decimal Budget { get; set; }
        public bool IsActive { get; set; } = true;

        [NotMapped]
        public string FullAccountCode => $"{MasterAccount?.AccountCode}-{SubCode}";
    }
}