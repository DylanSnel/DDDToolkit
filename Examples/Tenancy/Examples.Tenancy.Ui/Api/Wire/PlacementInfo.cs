namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>A unit the seat is placed in, with the roles granted to it at that unit.</summary>
public sealed record PlacementInfo(UnitPath Unit, bool IsPrimary, IReadOnlyList<GrantInfo> Grants);
