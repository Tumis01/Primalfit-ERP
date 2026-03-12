using System.ComponentModel.DataAnnotations;

namespace PrimafitERP.Api.DTOs
{
    // --- PROCUREMENT DTOs ---
    public class CreatePurchaseOrderDto
    {
        [Required] public Guid CompanyId { get; set; }
        [Required] public Guid VendorId { get; set; }
        [Required] public Guid CurrencyId { get; set; }

        public string OrderNumber { get; set; } = string.Empty;
        public DateTime OrderDate { get; set; } = DateTime.UtcNow;
        public decimal ExchangeRate { get; set; } = 1;

        // --- Tax & Discount Fields ---
        public Guid? TaxId { get; set; }
        public Guid? TaxGLAccountId { get; set; }
        public decimal DiscountPercentage { get; set; } = 0;
        public decimal DiscountAmount { get; set; } = 0;
        public Guid? DiscountGlAccountId { get; set; }

        [Required] public List<CreatePurchaseOrderLineDto> Lines { get; set; } = new();
    }

    public class CreatePurchaseOrderLineDto
    {
        [Required] public Guid ItemId { get; set; }
        [Required] public decimal Quantity { get; set; }
        [Required] public decimal UnitCost { get; set; }
    }
}
