namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>A unit of the tenant (<c>GET /tenancy/units</c>, and <c>POST /tenancy/directory/units</c>).</summary>
public sealed record UnitInfo(Guid Id, Guid? ParentId, string Name, string Kind, string Status, string Path, int Depth)
{
    /// <summary>Whether the unit is in use; nothing new goes to an archived one.</summary>
    public bool IsActive => string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase);
}
