using System.ComponentModel.DataAnnotations;

namespace PrimafitERP.Api.DTOs
{
    public class CreateSalesOrderDto
    {
        [Required] public Guid CompanyId { get; set; }
        [Required] public Guid CustomerId { get; set; }
        [Required] public Guid WarehouseId { get; set; }
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

        [Required] public List<CreateSalesOrderLineDto> Lines { get; set; } = new();
    }

    public class CreateSalesOrderLineDto
    {
        [Required] public Guid ItemId { get; set; }
        [Required] public decimal Quantity { get; set; }
        [Required] public decimal UnitPrice { get; set; }
    }
}
