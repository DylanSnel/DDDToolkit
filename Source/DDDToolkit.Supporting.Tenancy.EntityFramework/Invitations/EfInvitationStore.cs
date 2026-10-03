using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.UseCases;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// Where the invitation use cases load and keep invitations, over Tenancy's context: the same context, and so the
/// same unit of work, as Tenancy's own store, whose save writes what is added or changed here. Every load goes
/// through the context's sets, so the tenant filter keeps it to the current caller's tenant.
/// <para>
/// A token's digest goes into a table of its own, in a row added with its invitation, and is never loaded again.
/// The one question asked of it, which invitation a digest is for, is one of the few reads Tenancy makes across
/// tenants (<see cref="TenancySystemReads"/>): on a context of its own, as a caller begun around it alone. Where
/// the database keeps the rights (<see cref="TenancyStoreOptions.DatabaseKeepsRights"/>, as on Postgres with row
/// level security), that caller is Tenancy's scoped system work in no tenant, and the question is asked of the
/// database's function <see cref="TenancyFunctionNames.InvitationOfDigest"/>, since no caller's role reads the
/// table; anywhere else it is the application itself, reading the table.
/// </para>
/// </summary>
internal sealed class EfInvitationStore<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId, TInvitation, TInvitationId, TContext>(TContext context)
    : TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>.IInvitationStore<TInvitation, TInvitationId>
    where TTenant : TenantAggregate<TTenantId>
    where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
    where TUnit : OrganizationUnitEntity<TUnitId>
    where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
    where TRole : RoleAggregate<TRoleId, TTenantId>
    where TInvitation : InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>
    where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    where TUnitId : struct, IEntityId, IEquatable<TUnitId>
    where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
    where TContext : DbContext
{
    /// <inheritdoc />
    public Task<TInvitation?> FindAsync(TInvitationId id, CancellationToken cancellationToken)
        => Invitations().Where(invitation => invitation.Id.Equals(id)).FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    /// <remarks>Not tracked: a list is read, and nothing of it is saved.</remarks>
    public async Task<IReadOnlyList<TInvitation>> ListOpenAsync(TTenantId tenant, DateTimeOffset now, CancellationToken cancellationToken)
        => await Invitations()
            .AsNoTracking()
            .Where(invitation => invitation.TenantId.Equals(tenant) && invitation.State == InvitationState.Open && invitation.ExpiresAt > now)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>.InvitationOfToken<TInvitationId>?> FindByDigestAsync(
        byte[] digest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(digest);
        RequireMapped();

        return TenancySystemReads.RunAsync(
            context,
            TenancyWork.SystemScope,
            overTables: async (own, cancellation) =>
            {
                // Past every filter: whoever accepts names no tenant, and the token is what finds one. Ids alone
                // leave the query.
                var found = await (
                        from kept in own.Set<TenancyInvitationDigest<TInvitationId, TTenantId>>().IgnoreQueryFilters()
                        where kept.Digest == digest
                        join invitation in own.Set<TInvitation>().IgnoreQueryFilters() on kept.InvitationId equals invitation.Id
                        select new { invitation.TenantId, invitation.Id, invitation.IssuedBy })
                    .FirstOrDefaultAsync(cancellation)
                    .ConfigureAwait(false);
                return found is null ? null : Found(found.TenantId, found.Id, found.IssuedBy);
            },
            throughFunctions: async (own, cancellation) =>
            {
                // A row of the model, so the application's converters make the ids; sent as it is written here.
                var answered = await own.Set<InvitationOfDigestRow<TTenantId, TInvitationId, TSeatId>>()
                    .FromSqlRaw(
                        TenancyFunctionSql.Select(
                            own,
                            schema: null,
                            TenancyFunctionNames.InvitationOfDigest,
                            parameters: 1,
                            nameof(InvitationOfDigestRow<TTenantId, TInvitationId, TSeatId>.TenantId),
                            nameof(InvitationOfDigestRow<TTenantId, TInvitationId, TSeatId>.InvitationId),
                            nameof(InvitationOfDigestRow<TTenantId, TInvitationId, TSeatId>.IssuedBy)),
                        digest)
                    .ToListAsync(cancellation)
                    .ConfigureAwait(false);
                return answered is [var row] ? Found(row.TenantId, row.InvitationId, row.IssuedBy) : null;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public void Add(TInvitation invitation, byte[] digest)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        ArgumentNullException.ThrowIfNull(digest);
        RequireMapped();

        context.Add(invitation);
        context.Add(new TenancyInvitationDigest<TInvitationId, TTenantId>
        {
            InvitationId = invitation.Id,
            TenantId = invitation.TenantId,
            Digest = digest,
        });
    }

    private static TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>.InvitationOfToken<TInvitationId> Found(
        TTenantId tenant,
        TInvitationId invitation,
        TSeatId? issuedBy)
        => new(tenant, invitation, issuedBy);

    /// <summary>The invitations, once the context is known to map them.</summary>
    private DbSet<TInvitation> Invitations()
    {
        RequireMapped();
        return context.Set<TInvitation>();
    }

    /// <summary>
    /// Says what is missing where the context maps no invitations, rather than let Entity Framework say that a type
    /// is no part of the model.
    /// </summary>
    /// <exception cref="InvalidOperationException">The context's model maps no invitations.</exception>
    private void RequireMapped()
    {
        if (context.Model.FindEntityType(typeof(TInvitation)) is null)
        {
            throw new InvalidOperationException(
                "'" + context.GetType().Name + "' maps no invitations, and they are registered over it. Call modelBuilder.AddTenancyInvitations<"
                + typeof(TInvitation).Name + ", " + typeof(TInvitationId).Name + ">() in its OnModelCreating, after AddTenancy, and add the migration that makes the two tables.");
        }
    }
}
