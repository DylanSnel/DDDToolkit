namespace Examples.Tenancy.Ui.Session;

/// <summary>
/// What survives a reload: who is signed in, their token until it expires, their tenant, and the language the tab
/// reads; a copy kept before there were two has none, and starts in the first.
/// </summary>
public sealed record SessionSnapshot(string Person, string PersonName, string AccessToken, DateTimeOffset ExpiresAt, string? Tenant, string? Language = null);
