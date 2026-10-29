namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>A unit the seat is placed in, by id, with the roles granted to it at that unit.</summary>
public sealed record PlacementInfo(Guid UnitId, bool IsPrimary, IReadOnlyList<GrantInfo> Grants);
