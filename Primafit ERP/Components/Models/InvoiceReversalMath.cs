namespace Primafit_ERP.Components.Models;

public static class InvoiceReversalMath
{
    public static decimal Portion(decimal originalValue, decimal previouslyReversedQty, decimal quantity, decimal invoicedQty)
    {
        if (invoicedQty <= 0) return 0;
        // Allocate rounding cumulatively so successive partial notes together
        // reverse exactly the original amount, including its last penny.
        return Math.Round(originalValue * (previouslyReversedQty + quantity) / invoicedQty, 2, MidpointRounding.AwayFromZero)
             - Math.Round(originalValue * previouslyReversedQty / invoicedQty, 2, MidpointRounding.AwayFromZero);
    }
}
