namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>A permission a seat holds now: where it is granted, and every unit it reaches from there.</summary>
public sealed record PermissionOfMineInfo(string Permission, bool AtTenant, IReadOnlyList<UnitPath> GrantedAt, IReadOnlyList<UnitPath> Reaches);
