using DDDToolkit.Exceptions;
using Mediator;

namespace Examples.Tenancy.Tenants.Application.Tenant.Commands;

/// <summary>
/// Marks the tenant the caller works in as one that exists only to demonstrate the application.
/// </summary>
/// <remarks>
/// The one command of this module that is the application's own:
/// <see cref="Domain.Aggregates.Tenants.Tenant.IsDemo"/> is state the application adds to the package's tenant, so
/// no manager of the package sets it. Nobody in a tenant decides that their tenant is a demonstration; whoever
/// seeds one does, and no route sends it. The seeding sends it as system work in the tenant it marks, begun around
/// the send, <c>access.BeginSystemIn(tenant)</c>: system work is that scope, not a requirement of the
/// request. Who may send it is a caller in the tenant, <c>[InTenant]</c>, which lets that system work through, and
/// the handler says what only system work may do: every seat is refused there, whatever it holds,
/// <c>access.system-only</c>.
/// </remarks>
[InTenant]
public sealed record MarkTenantAsDemo : ICommand<Domain.Aggregates.Tenants.Tenant>, ITenantsRequest;

/// <summary>
/// Handles <see cref="MarkTenantAsDemo"/>: loads the tenant through the package's store, which the package's own
/// managers load and save through, marks it and saves.
/// </summary>
/// <remarks>
/// Every other command of this module hands its request to a manager of the package, which asks again for what
/// its request requires, and for the rest, whoever calls it. This one changes an aggregate of the package's with
/// nothing of the package in between, so it decides itself, and narrower than the door: system work in the very
/// tenant it marks goes on, and nothing else. The handler then decides the same wherever it runs, in a program that left the
/// module's access behavior out of the pipeline as much as in the host.
/// </remarks>
/// <param name="store">Where Tenancy's aggregates are loaded and saved: one unit of work per request.</param>
/// <param name="tenancy">Tenancy, asked about the current caller, for the tenant it works in.</param>
public sealed class MarkTenantAsDemoHandler(TenancyStore store, TenancyAccessManager tenancy) : ICommandHandler<MarkTenantAsDemo, Domain.Aggregates.Tenants.Tenant>
{
    /// <inheritdoc />
    /// <returns>The tenant, marked.</returns>
    /// <exception cref="RefusalException"><c>access.system-only</c> for a seat, whatever it holds.</exception>
    /// <exception cref="InvalidOperationException">The tenant the caller works in is not there: it was never provisioned.</exception>
    public async ValueTask<Domain.Aggregates.Tenants.Tenant> Handle(MarkTenantAsDemo command, CancellationToken cancellationToken)
    {
        var scope = tenancy.CurrentTenant.Require();
        if (!scope.BySystem)
        {
            throw ToolkitRefusals.Refuse(ToolkitRefusals.SystemOnly);
        }

        var tenant = scope.Id;
        var marked = await store.FindTenantAsync(tenant, cancellationToken)
            ?? throw new InvalidOperationException($"The tenant {tenant} is not there to mark: provision it first.");

        marked.MarkAsDemo();
        await store.SaveAsync(cancellationToken);
        return marked;
    }
}
