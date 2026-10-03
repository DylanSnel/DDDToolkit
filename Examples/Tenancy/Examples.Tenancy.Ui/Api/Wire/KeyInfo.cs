namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>A key a seat holds now: where it is granted, and every unit it reaches from there.</summary>
public sealed record KeyInfo(string Key, bool WholeTenant, IReadOnlyList<UnitPath> GrantedAt, IReadOnlyList<UnitPath> Reaches);
