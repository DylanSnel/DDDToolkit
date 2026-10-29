namespace Examples.Tenancy.Ui.Api.Wire;

/// <summary>A tenant as the API writes it: its id, its slug, its shape and its status. Its name is its organization's, and stands beside it.</summary>
public sealed record TenantInfo(Guid Id, string Slug, string Shape, string Status);
