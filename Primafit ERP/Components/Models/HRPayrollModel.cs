using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Primafit_ERP.Components.Models
{
    public enum EmploymentStatus { Active, Suspended, Terminated }
    public enum PayrollRunStatus { Draft, Approved, Paid }

    // --- PHASE 1: FOUNDATION ---
    public class Branch
    {
        [Key] public Guid Id { get; set; }
        [Required] public Guid CompanyId { get; set; }
        [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    }

    public class Department
    {
        [Key] public Guid Id { get; set; }
        [Required] public Guid CompanyId { get; set; }
        [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    }

    public class JobRole
    {
        [Key] public Guid Id { get; set; }
        [Required] public Guid CompanyId { get; set; }
        [Required] public Guid DepartmentId { get; set; }
        [Required, StringLength(100)] public string Title { get; set; } = string.Empty;
        public Department? Department { get; set; }
    }

    public class EmployeeSalaryStructure
    {
        [Key] public Guid Id { get; set; }
        [Required] public Guid CompanyId { get; set; }
        [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
        [Required] public decimal BasicSalary { get; set; }
        public decimal HousingAllowance { get; set; }
        public decimal TransportAllowance { get; set; }
        public decimal UtilityAllowance { get; set; }
        public decimal MealAllowance { get; set; }
        public bool IsPensionable { get; set; } = true;
        public bool IsTaxable { get; set; } = true;

        [NotMapped]
        public decimal GrossMonthlyPay => BasicSalary + HousingAllowance + TransportAllowance + UtilityAllowance + MealAllowance;
    }

    public class Employee
    {
        [Key] public Guid Id { get; set; }
        [Required] public Guid CompanyId { get; set; }
        [Required, StringLength(50)] public string EmployeeCode { get; set; } = string.Empty;
        [Required, StringLength(100)] public string FirstName { get; set; } = string.Empty;
        [Required, StringLength(100)] public string LastName { get; set; } = string.Empty;

        [Required] public Guid BranchId { get; set; }
        [Required] public Guid JobRoleId { get; set; }
        [Required] public Guid SalaryStructureId { get; set; }

        public string BankName { get; set; } = string.Empty;
        public string AccountNumber { get; set; } = string.Empty;
        public string PensionFundAdministrator { get; set; } = string.Empty;
        public string RSAPin { get; set; } = string.Empty;

        public EmploymentStatus Status { get; set; } = EmploymentStatus.Active;
        public DateTime DateJoined { get; set; } = DateTime.UtcNow;

        public Branch? Branch { get; set; }
        public JobRole? JobRole { get; set; }
        public EmployeeSalaryStructure? SalaryStructure { get; set; }
    }

    // --- PHASE 2: PAYROLL ENGINE ---
    public class PayrollRun
    {
        [Key] public Guid Id { get; set; }
        [Required] public Guid CompanyId { get; set; }
        [Required] public string Period { get; set; } = string.Empty; // e.g., "Oct-2026"
        public DateTime RunDate { get; set; } = DateTime.UtcNow;
        public PayrollRunStatus Status { get; set; } = PayrollRunStatus.Draft;

        public decimal TotalGrossPay { get; set; }
        public decimal TotalDeductions { get; set; }
        public decimal TotalNetPay { get; set; }
        public decimal TotalEmployerPension { get; set; }

        public Guid? GLBatchId { get; set; } // Link to Accounting\
        public Guid? DisbursementGLBatchId { get; set; }
        public bool IsPayeRemitted { get; set; }
        public Guid? PayeRemittanceGLBatchId { get; set; }
        public bool IsPensionRemitted { get; set; }
        public Guid? PensionRemittanceGLBatchId { get; set; }
        public List<PayrollItem> PayrollItems { get; set; } = new();
    }

    public class PayrollItem
    {
        [Key] public Guid Id { get; set; }
        [Required] public Guid PayrollRunId { get; set; }
        [Required] public Guid EmployeeId { get; set; }

        public decimal GrossPay { get; set; }
        public decimal PAYETax { get; set; }
        public decimal EmployeePension { get; set; }
        public decimal EmployerPension { get; set; }
        public decimal OtherDeductions { get; set; }
        public decimal OtherEarnings { get; set; }
        public decimal NetPay { get; set; }

        public Employee? Employee { get; set; }
        public PayrollRun? PayrollRun { get; set; }
        public List<PayrollEarning> CustomEarnings { get; set; } = new();
        public List<PayrollDeduction> CustomDeductions { get; set; } = new();
    }

    // --- PHASE 5: COMPLIANCE ---
    public class PayrollSetting
    {
        [Key] public Guid Id { get; set; }
        [Required] public Guid CompanyId { get; set; }

        // GL Accounts for Phase 3 integration
        public Guid SalariesExpenseAccountId { get; set; }
        public Guid SalariesPayableAccountId { get; set; }
        public Guid PAYEPayableAccountId { get; set; }
        public Guid PensionPayableAccountId { get; set; }

        // Nigeria Statutory Rates
        public decimal PensionEmployeeRate { get; set; } = 0.08m; // 8%
        public decimal PensionEmployerRate { get; set; } = 0.10m; // 10%
    }
    public class PayrollEarning
    {
        [Key] public Guid Id { get; set; }
        [Required] public Guid PayrollItemId { get; set; }
        [Required, StringLength(100)] public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    public class PayrollDeduction
    {
        [Key] public Guid Id { get; set; }
        [Required] public Guid PayrollItemId { get; set; }
        [Required, StringLength(100)] public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    // --- PHASE 5: REPORTING DTOs ---
    public class PayeScheduleRow
    {
        public string EmployeeName { get; set; } = string.Empty;
        public string TaxId { get; set; } = string.Empty; // TIN
        public decimal GrossPay { get; set; }
        public decimal PensionDeducted { get; set; }
        public decimal PayeDeducted { get; set; }
    }

    public class PensionScheduleRow
    {
        public string EmployeeName { get; set; } = string.Empty;
        public string PFA { get; set; } = string.Empty;
        public string RSAPin { get; set; } = string.Empty;
        public decimal EmployeeContribution { get; set; }
        public decimal EmployerContribution { get; set; }
        public decimal TotalRemittance => EmployeeContribution + EmployerContribution;
    }

    public class PayrollSummaryRow
    {
        public string BranchName { get; set; } = string.Empty;
        public int EmployeeCount { get; set; }
        public decimal TotalGross { get; set; }
        public decimal TotalNet { get; set; }
        public decimal TotalPAYE { get; set; }
        public decimal TotalPension { get; set; } // Emp + Emplr
    }
    public class PayrollVarianceRow
    {
        public string EmployeeName { get; set; } = string.Empty;
        public decimal PreviousGross { get; set; }
        public decimal CurrentGross { get; set; }
        public decimal VarianceGross => CurrentGross - PreviousGross;

        public decimal PreviousNet { get; set; }
        public decimal CurrentNet { get; set; }
        public decimal VarianceNet => CurrentNet - PreviousNet;
    }

    public class YtdTaxReportRow
    {
        public string EmployeeName { get; set; } = string.Empty;
        public string TaxId { get; set; } = string.Empty;
        public int MonthsWorked { get; set; }
        public decimal TotalGross { get; set; }
        public decimal TotalPension { get; set; }
        public decimal TotalPAYE { get; set; }
        public decimal TotalNet { get; set; }
    }
}