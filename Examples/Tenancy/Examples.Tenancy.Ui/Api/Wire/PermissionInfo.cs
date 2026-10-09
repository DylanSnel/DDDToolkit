namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>A key of the catalogue, and whether it manages access.</summary>
public sealed record PermissionInfo(string Key, string Module, string Description, bool Retired, bool ManagesAccess = false);
