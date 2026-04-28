using Primafit_ERP.Components.Models;
using System.ComponentModel.DataAnnotations;

namespace PrimafitERP.Api.DTOs
{
    
    public class DirectReceiptDto
    {
        [Required] public Guid CompanyId { get; set; }
        [Required] public Guid ItemId { get; set; }
        public Guid WarehouseId { get; set; } // Can be empty for Service items
        [Required] public Guid VendorId { get; set; }
        [Required] public decimal Quantity { get; set; }
        [Required] public decimal TotalLandedCost { get; set; }
        public string Reference { get; set; } = string.Empty;
    }

    public class StockAdjustmentDto
    {
        [Required] public Guid CompanyId { get; set; }
        [Required] public Guid ItemId { get; set; }
        [Required] public Guid WarehouseId { get; set; }
        [Required] public StockEntryType AdjustmentType { get; set; } // Must map to your enum
        public decimal Quantity { get; set; }
        public decimal TotalValueChange { get; set; }
        public string Reference { get; set; } = string.Empty;
    }

    public class ProjectIssueDto
    {
        [Required] public Guid CompanyId { get; set; }
        [Required] public Guid ItemId { get; set; }
        [Required] public Guid WarehouseId { get; set; }
        [Required] public Guid ProjectId { get; set; }
        [Required] public decimal Quantity { get; set; }
        public string Note { get; set; } = string.Empty;
    }

    public class ShipTransferDto
    {
        [Required] public Guid CompanyId { get; set; }
        [Required] public Guid ItemId { get; set; }
        [Required] public Guid FromWarehouseId { get; set; }
        [Required] public Guid ToWarehouseId { get; set; }
        [Required] public decimal Quantity { get; set; }
        [Required] public Guid TransitAccountId { get; set; }
        public string Note { get; set; } = string.Empty;
    }

    public class ReceiveTransferDto
    {
        [Required] public Guid TransferId { get; set; }
        [Required] public decimal ActualQuantityReceived { get; set; }
    }
}
