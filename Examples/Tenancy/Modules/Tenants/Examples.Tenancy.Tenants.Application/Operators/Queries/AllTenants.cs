using Mediator;

namespace Examples.Tenancy.Tenants.Application.Operators.Queries;

/// <summary>
/// Every tenant of the application, by slug and a page at a time, each with its status and how many of its seats
/// are active: what the application's own staff start from.
/// </summary>
/// <remarks>
/// For operators only. The Tenancy package's directory of tenants answers it, and asks the same of its caller
/// before it reads, so the two checks agree by construction; the read then runs as the operators' own
/// database role, which reads every tenant and writes nothing.
/// </remarks>
/// <param name="After">The marker the page before answered as its next, or <see langword="null"/> for the first page.</param>
/// <param name="Size">How many tenants a page holds: 1 to <see cref="TenancyUseCases.TenantDirectory.MostPerPage"/>.</param>
public sealed record AllTenants(string? After = null, int Size = AllTenants.DefaultPage) : IQuery<TenancyUseCases.TenantDirectoryPage>, ITenantsRequest
{
    /// <summary>How many tenants a page holds when the caller does not say.</summary>
    public const int DefaultPage = 50;

    /// <inheritdoc />
    AccessRequirement IRequireAccess.RequiredAccess => TenancyAccess.RequiresOperator();
}

/// <summary>Answers <see cref="AllTenants"/> from the Tenancy package's directory of tenants.</summary>
/// <param name="reads">Where Tenancy is read.</param>
public sealed class AllTenantsHandler(ITenancyReads reads) : IQueryHandler<AllTenants, TenancyUseCases.TenantDirectoryPage>
{
    /// <inheritdoc />
    /// <exception cref="Exceptions.RefusalException">
    /// The directory's own: <c>tenancy.page-size-invalid</c>, with <c>Max</c>, and <c>tenancy.cursor-invalid</c>.
    /// </exception>
    public async ValueTask<TenancyUseCases.TenantDirectoryPage> Handle(AllTenants query, CancellationToken cancellationToken)
        => await reads.TenantsAsync(query.After, query.Size, cancellationToken);
}
