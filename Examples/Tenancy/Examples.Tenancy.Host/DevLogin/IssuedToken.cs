namespace Examples.Tenancy.Host.DevLogin;

/// <summary>An access token, and when it stops being accepted.</summary>
/// <param name="AccessToken">The token, for the <c>Authorization: Bearer</c> header.</param>
/// <param name="ExpiresAt">When it expires.</param>
public sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAt);
