namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>The keys the application knows (<c>GET /tenancy/catalogue</c>), for the key pickers.</summary>
public sealed record CatalogueInfo(IReadOnlyList<PermissionInfo> Permissions);
