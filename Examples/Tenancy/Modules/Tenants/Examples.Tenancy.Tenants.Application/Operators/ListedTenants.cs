namespace Examples.Tenancy.Tenants.Application.Operators;

/// <summary>One page of the tenants the application's operators see listed.</summary>
/// <param name="Items">The tenants of the page, by slug.</param>
/// <param name="Next">What to ask the next page with, or <see langword="null"/> when this is the last one.</param>
public sealed record ListedTenants(IReadOnlyList<ListedTenant> Items, string? Next);
