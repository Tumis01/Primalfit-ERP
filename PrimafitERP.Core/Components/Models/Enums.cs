namespace Primafit_ERP.Components.Models
{
    public enum PaymentStatus
    {
        Draft = 0,
        Posted = 1,
        Reversed = 2
    }

    public enum BillStatus
    {
        Draft = 0,
        PendingApproval = 1,
        Approved = 2,
        Paid = 3,
        Void = 4
    }

    public enum PaymentTerms
    {
        Immediate = 0,
        Net15 = 15,
        Net30 = 30,
        Net60 = 60
    }
    public enum StockEntryType
    {
        DirectReceipt,          // Vendor Purchase
        QuantityIncrease,       // Found stock (Free)
        QuantityDecrease,       // Shrinkage / Damaged
        CostIncrease,           // Revaluation 
        CostDecrease,           // Devaluation 
        BothIncrease,           // Found stock + Value
        BothDecrease            // Lost stock + Value
    }
}