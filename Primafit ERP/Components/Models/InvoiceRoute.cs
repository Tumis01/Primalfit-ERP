namespace Primafit_ERP.Components.Models;

public enum TransactionTemplateModule { General, AR, AP }

public sealed class InvoiceRoute
{
    public Guid? TemplateId { get; set; }
    public Guid? DebitAccountId { get; set; }
    public Guid? CreditAccountId { get; set; }
    public Guid? TaxAccountId { get; set; }
    public Guid? DiscountAccountId { get; set; }
    public InvoiceRoute Copy() => (InvoiceRoute)MemberwiseClone();
}
