namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>The permissions the application knows (<c>GET /tenancy/catalogue</c>), for the permission pickers.</summary>
public sealed record CatalogueInfo(IReadOnlyList<PermissionInfo> Permissions);
