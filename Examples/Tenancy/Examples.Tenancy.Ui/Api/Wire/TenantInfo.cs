namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>The tenant a seat is in.</summary>
public sealed record TenantInfo(Guid Id, string Slug, string Name, string Shape, string Status);
