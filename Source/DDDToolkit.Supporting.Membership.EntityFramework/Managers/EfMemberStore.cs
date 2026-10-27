using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.BaseTypes;
using DDDToolkit.EntityFramework;
using DDDToolkit.Supporting.Membership.Access;
using DDDToolkit.Supporting.Membership.Managers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// Where the member manager of one kind of resource loads, adds and saves it: the request's own context, its unit of
/// work. What it reads for showing it reads on a context of its own where the application registered a factory for the
/// context, as the access questions do, since the reads of one request may run side by side.
/// </summary>
/// <param name="registration">The resource, with its rules.</param>
/// <param name="asMember">
/// Who a caller is as a member, as the rules say; <see langword="null"/> where the application resolves it, and is asked.
/// </param>
/// <param name="services">The services of the scope.</param>
internal sealed class EfMemberStore<TContext, TResource, TResourceId, TMember, TId, TMemberId, TRoleId>(
    MembershipRegistration registration,
    Func<CurrentUserSnapshot, TMemberId?>? asMember,
    IServiceProvider services)
    : IMemberStore<TResource, TResourceId, TMemberId>
    where TContext : DbContext
    where TResource : AggregateRoot<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    where TMember : MemberEntity<TId, TMemberId, TRoleId>
    where TId : struct, IEntityId, IEquatable<TId>
    where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
{
    private readonly ICurrentUser _user = services.GetRequiredService<ICurrentUser>();

    /// <summary>The request's own context: the unit of work a command changes the resource in.</summary>
    private TContext Context => services.GetRequiredService<TContext>();

    /// <inheritdoc />
    /// <remarks>Only a signed-in user is anybody's member, as the access questions answer it.</remarks>
    public TMemberId? CurrentMember
    {
        get
        {
            var user = _user.Snapshot;
            if (user.Kind != CurrentUserKind.User)
            {
                return null;
            }

            return asMember is { } known ? known(user) : services.GetRequiredService<ICallerMember<TResourceId, TMemberId>>().Find(user);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// A plain tracked load by id, under the context's own rules, of the whole aggregate: the resource, its members and
    /// each member's roles. The access rules are not part of it: the manager asked them first.
    /// </remarks>
    public async Task<TResource?> FindAsync(TResourceId resource, long? expectedVersion, CancellationToken cancellationToken)
    {
        var context = Context;
        var found = await context.Set<TResource>().AsTracking().FirstOrDefaultAsync(candidate => candidate.Id.Equals(resource), cancellationToken).ConfigureAwait(false);
        if (found is not null)
        {
            context.ExpectVersion(found, expectedVersion);
        }

        return found;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TResource>> ReadAsync(IReadOnlyCollection<TResourceId> resources, MemberReach<TResource> reach, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(reach);

        var about = resources.Distinct().ToList();
        return await ReadAsync(
            context => context.Set<TResource>().AsNoTracking().Where(candidate => about.Contains(candidate.Id)).Within(reach, context).ToListAsync(cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Add(TResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        Context.Set<TResource>().Add(resource);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Asked of the change tracker before anything is written. The outbox rows of the resource's events are added by the
    /// save itself, after this.
    /// </remarks>
    public IReadOnlyList<string> ChangedBesides(IReadOnlySet<object> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        return [.. Context.ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && !parts.Contains(entry.Entity))
            .Select(entry => entry.Metadata.ClrType.Name)
            .Distinct(StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    public Task SaveAsync(CancellationToken cancellationToken) => Context.SaveChangesAsync(cancellationToken);

    /// <summary>
    /// Runs one reading: on a context of its own where the application registered a factory for the context, disposed when
    /// the reading is done, and otherwise on the context of the scope. Where the rows it reads are under a query filter
    /// that reads its context, on the context of the scope wherever it has one, as the access questions read.
    /// </summary>
    private async Task<T> ReadAsync<T>(Func<TContext, Task<T>> read, CancellationToken cancellationToken)
    {
        if (services.GetService<IDbContextFactory<TContext>>() is not { } contexts)
        {
            return await read(Context).ConfigureAwait(false);
        }

        if (services.GetRequiredService<MemberNamesOf<TContext, TResource, TRoleId>>().Read(services, registration).FollowTheRequest
            && services.GetService<TContext>() is { } own)
        {
            return await read(own).ConfigureAwait(false);
        }

        var context = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            return await read(context).ConfigureAwait(false);
        }
    }
}
