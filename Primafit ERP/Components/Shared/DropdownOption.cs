namespace Primafit_ERP.Components.Shared;

public sealed record DropdownOption(Guid Id, string Label, string? Detail = null, bool Disabled = false);
