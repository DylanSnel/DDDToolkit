namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>A tenant, as a seat list shows it.</summary>
public sealed record TenantOfSeat(Guid Id, string Slug, string Name, string Status);
