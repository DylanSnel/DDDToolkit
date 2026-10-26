namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>A permission of the catalogue, and whether it manages access.</summary>
public sealed record PermissionInfo(string Name, string Module, string Description, bool Retired, bool ManagesAccess = false);
