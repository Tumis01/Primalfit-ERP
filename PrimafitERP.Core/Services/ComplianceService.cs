using Primafit_ERP.Components.Models;

namespace Primafit_ERP.Services
{
    public class ComplianceService
    {
        public (decimal PAYE, decimal PensionEmp, decimal PensionEmplr) CalculateNigeriaTaxes(
            decimal grossAnnual,
            decimal empPensionRate,
            decimal emplrPensionRate,
            bool isPensionable)
        {
            decimal pensionEmp = isPensionable ? grossAnnual * empPensionRate : 0;
            decimal pensionEmplr = isPensionable ? grossAnnual * emplrPensionRate : 0;

            // CRA: Higher of N200,000 or 1% of Gross + 20% of Gross
            decimal craBase = Math.Max(200000m, grossAnnual * 0.01m);
            decimal cra = craBase + (grossAnnual * 0.20m);

            decimal taxableIncome = grossAnnual - cra - pensionEmp;
            decimal annualPaye = 0;

            if (taxableIncome <= 0)
            {
                // Minimum tax: 1% of gross
                return (grossAnnual * 0.01m / 12m, pensionEmp / 12m, pensionEmplr / 12m);
            }

            // Standard Nigeria PAYE Brackets
            decimal[] brackets = { 300000m, 300000m, 500000m, 500000m, 1600000m };
            decimal[] rates = { 0.07m, 0.11m, 0.15m, 0.19m, 0.21m };

            decimal remainingTaxable = taxableIncome;

            for (int i = 0; i < brackets.Length; i++)
            {
                if (remainingTaxable > brackets[i])
                {
                    annualPaye += brackets[i] * rates[i];
                    remainingTaxable -= brackets[i];
                }
                else
                {
                    annualPaye += remainingTaxable * rates[i];
                    remainingTaxable = 0;
                    break;
                }
            }

            if (remainingTaxable > 0)
            {
                annualPaye += remainingTaxable * 0.24m; // Excess above 3.2m @ 24%
            }

            // Convert annual calculations back to monthly values
            return (annualPaye / 12m, pensionEmp / 12m, pensionEmplr / 12m);
        }
    }
}