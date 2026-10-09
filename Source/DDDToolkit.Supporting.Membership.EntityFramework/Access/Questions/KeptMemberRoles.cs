using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Membership.UseCases;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// The roles there are for the members of a resource whose roles are kept, as the admission asks about them:
/// answered from the rows of the application's role class, on the request's own context, so the application's
/// rule about whose roles a request reads applies to what is answered. An application that registered a
/// factory for the context and no context for a request is read on a context of the factory's, as its access
/// questions are.
/// </summary>
/// <param name="registration">The resource, with its rules.</param>
/// <param name="services">The services of the scope the admission is asked in.</param>
internal sealed class KeptMemberRoles<TContext, TResource, TResourceId, TRoleId>(MembershipRegistration registration, IServiceProvider services)
    : IMemberRoles<TResourceId, TRoleId>
    where TContext : DbContext
    where TResource : class
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    /// <inheritdoc />
    /// <remarks>A role in use, among the rows the request reads: an archived role is not given again, and neither is a role of another scope.</remarks>
    public async ValueTask<bool> ExistsAsync(TRoleId role, CancellationToken cancellationToken)
        => await ReadAsync((rows, context) => rows.InUseAsync(context, role, cancellationToken), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    /// <remarks>The role in use that was made from the starter role the rules name as the owner's, whatever it is called now.</remarks>
    public async ValueTask<TRoleId?> FindOwnerRoleAsync(CancellationToken cancellationToken)
        => await ReadAsync((rows, context) => rows.MadeFromAsync(context, registration.Rules.OwnerRole, cancellationToken), cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Runs one reading of the roles' rows: on the request's own context where the scope has one, and
    /// otherwise on a context of the factory's, disposed when the reading is done.
    /// </summary>
    private async Task<T> ReadAsync<T>(Func<KeptRoles<TRoleId>, DbContext, Task<T>> read, CancellationToken cancellationToken)
    {
        var rows = services.GetRequiredService<MemberNamesOf<TContext, TResource, TRoleId>>().Read(services, registration).Roles!;
        if (services.GetService<TContext>() is { } own)
        {
            return await read(rows, own).ConfigureAwait(false);
        }

        var context = await services.GetRequiredService<IDbContextFactory<TContext>>().CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            return await read(rows, context).ConfigureAwait(false);
        }
    }
}
