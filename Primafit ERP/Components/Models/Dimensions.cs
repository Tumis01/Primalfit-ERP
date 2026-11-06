using System.ComponentModel.DataAnnotations;

namespace Primafit_ERP.Components.Models;

public interface IDimensionedLine
{
    Guid? DimensionSetId { get; set; }
}

public class DimensionType
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    [Required, MaxLength(50)] public string Code { get; set; } = "";
    [Required, MaxLength(200)] public string Name { get; set; } = "";
    public bool IsRequired { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public class DimensionValue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid DimensionTypeId { get; set; }
    [Required, MaxLength(50)] public string Code { get; set; } = "";
    [Required, MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(2000)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    // Retains the original project identity for migration and legacy integrations.
    public Guid? LegacyProjectId { get; set; }
}

public class DimensionSet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<DimensionSetValue> Values { get; set; } = new();
}

public class DimensionSetValue
{
    public Guid DimensionSetId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DimensionTypeId { get; set; }
    public Guid DimensionValueId { get; set; }
}
