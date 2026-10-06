using DDDToolkit.Supporting.Tenancy.Access;

namespace DDDToolkit.Supporting.Tenancy.UseCases;

public abstract partial class TenancyUseCases<TTenant, TTenantId, TOrganization, TUnit, TUnitId, TSeat, TSeatId, TRole, TRoleId>
{
    /// <summary>
    /// Where the use cases load and save: one unit of work per command. The storage package implements it.
    /// <para>
    /// Every lookup keeps to the current caller's tenant, as the storage's tenant filter does: a seat or a
    /// role of another tenant is simply not found. Two lookups go across tenants:
    /// <see cref="SlugTakenAsync"/>, which says no more than yes or no, and <see cref="ListTenantsAsync"/>, which
    /// is an operator's and reads what the operator may read.
    /// </para>
    /// </summary>
    public interface IStore
    {
        /// <summary>The rows the access questions read, as last saved.</summary>
        ITenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId> Reads { get; }

        /// <summary>Runs queries over <see cref="Reads"/>.</summary>
        IQueryExecutor Queries { get; }

        /// <summary>The tenant, or <see langword="null"/> when it is not the current caller's.</summary>
        Task<TTenant?> FindTenantAsync(TTenantId id, CancellationToken cancellationToken);

        /// <summary>Whether any tenant, the caller's or not, has the slug.</summary>
        Task<bool> SlugTakenAsync(string slug, CancellationToken cancellationToken);

        /// <summary>
        /// The tenants there are, past the tenant filter, in the order of their slugs: those whose slug comes
        /// after <paramref name="afterSlug"/>, at most <paramref name="take"/> of them, each with its organization's
        /// name and the number of its active seats. What the tenants' directory shows an operator, and read as that
        /// operator: a storage that keeps tenants apart by itself answers what the caller may read there, which for
        /// anyone else is nothing, or their own tenant.
        /// </summary>
        /// <param name="afterSlug">The slug the page comes after, or <see langword="null"/> for the first tenants.</param>
        /// <param name="take">How many tenants to answer at most.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        Task<IReadOnlyList<TenantListing>> ListTenantsAsync(string? afterSlug, int take, CancellationToken cancellationToken);

        /// <summary>The tenant's organization with all its units, or <see langword="null"/> when it is not the current caller's.</summary>
        Task<TOrganization?> FindOrganizationAsync(TTenantId id, CancellationToken cancellationToken);

        /// <summary>
        /// The seat, or <see langword="null"/> when it is not in the current caller's tenant. A storage that keeps
        /// grants to the seats that may read them, as Tenancy's policies on Postgres do, loads it with the grants
        /// the caller may read: all of its own seat's, and another seat's at the units where the caller manages
        /// grants, seats or units, or every one when it manages roles for the whole tenant. Each command acts only on
        /// grants at a unit where it asked one of those keys first, or for the whole tenant, so they are among them.
        /// </summary>
        Task<TSeat?> FindSeatAsync(TSeatId id, CancellationToken cancellationToken);

        /// <summary>
        /// The tenant's seats, for the directory to answer: all of them, or those among <paramref name="only"/>; an id
        /// that is no seat of the tenant is simply not among the answer. Read as the application's own seats, with
        /// every field it added, since the directory hands them to the application's view; and read only, never for a
        /// save: a change made to one is not written, by this unit of work or another. A storage that keeps grants to
        /// the seats that may read them loads each seat's grants as <see cref="FindSeatAsync"/> does.
        /// </summary>
        /// <param name="tenant">The tenant, which is the current caller's.</param>
        /// <param name="only">The seats asked about, or <see langword="null"/> for every seat of the tenant.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        Task<IReadOnlyList<TSeat>> ListSeatsAsync(TTenantId tenant, IReadOnlyCollection<TSeatId>? only, CancellationToken cancellationToken);

        /// <summary>Whether the identity already has a seat in the tenant.</summary>
        Task<bool> IdentityHasSeatAsync(TTenantId tenant, Guid identity, CancellationToken cancellationToken);

        /// <summary>The role, or <see langword="null"/> when it is not in the current caller's tenant.</summary>
        Task<TRole?> FindRoleAsync(TRoleId id, CancellationToken cancellationToken);

        /// <summary>Every role of the tenant, archived ones included.</summary>
        Task<IReadOnlyList<TRole>> ListRolesAsync(TTenantId tenant, CancellationToken cancellationToken);

        /// <summary>Whether another role of the tenant than <paramref name="except"/> has the name, ignoring case.</summary>
        Task<bool> RoleNameTakenAsync(TTenantId tenant, string name, TRoleId? except, CancellationToken cancellationToken);

        /// <summary>
        /// The tenant's administrators as last saved, as (seat, role) pairs: a right at the root for
        /// <c>tenancy.roles.manage</c>, with no end, applying at <paramref name="now"/>. What the rule that a tenant
        /// keeps an administrator reads. It is about every seat's rights, which a storage may keep from a seat
        /// that reads <see cref="Reads"/>: the storage answers it from wherever it can, as far as the caller may
        /// read the grants at the root. That is whole for every command that asks: each one that could take an
        /// administrator away asks a key at the root first, and one that acts below the root does not ask.
        /// </summary>
        /// <param name="tenant">The tenant, which is the current caller's.</param>
        /// <param name="now">The moment a right must apply at.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        Task<IReadOnlyList<(TSeatId Seat, TRoleId Role)>> AdministratorsAsync(TTenantId tenant, DateTimeOffset now, CancellationToken cancellationToken);

        /// <summary>
        /// Every right that has not ended at <paramref name="now"/>, one still to start included, held at a unit at
        /// or above <paramref name="parent"/> or <paramref name="newParent"/>, of <paramref name="seat"/> or of a
        /// key in <paramref name="managing"/>, once for each of the two it reaches. What the check of a move
        /// reads. Another seat's right is answered only where it reaches one of the two and not the other, which is
        /// a right the move changes: one that reaches both reaches the unit wherever it hangs, so the check never
        /// weighs it, and a seat that manages only part of the tree learns nothing of the rights above that part that
        /// stay. Like <see cref="AdministratorsAsync"/>, it is about every seat's rights, and the storage answers
        /// it whole. The use case asks once it has found <paramref name="seat"/> to hold
        /// <c>tenancy.units.manage</c> at both parents, so the seat's own rights for that key are always among the
        /// rows: a storage that cannot tell so much has no answer, and fails rather than answer with less, which
        /// would read as a move that changes nothing.
        /// </summary>
        /// <param name="tenant">The tenant, which is the current caller's.</param>
        /// <param name="seat">The seat that moves the unit, whose own rights count whatever their key.</param>
        /// <param name="parent">The unit the moved unit hangs under.</param>
        /// <param name="newParent">The unit it would hang under.</param>
        /// <param name="managing">The keys that manage access, whose rights count whoever holds them.</param>
        /// <param name="now">The moment a right must not have ended at.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        Task<IReadOnlyList<MoveReach<TUnitId>>> RightsAMoveChangesAsync(
            TTenantId tenant,
            TSeatId seat,
            TUnitId parent,
            TUnitId newParent,
            IReadOnlyCollection<string> managing,
            DateTimeOffset now,
            CancellationToken cancellationToken);

        /// <summary>Adds a new tenant to the unit of work.</summary>
        void Add(TTenant tenant);

        /// <summary>Adds a new organization to the unit of work.</summary>
        void Add(TOrganization organization);

        /// <summary>Adds a new seat to the unit of work.</summary>
        void Add(TSeat seat);

        /// <summary>Adds a new role to the unit of work.</summary>
        void Add(TRole role);

        /// <summary>Adds a new tenant's access revision, which every later change of rights in it takes first.</summary>
        void AddAccessRevision(TTenantId tenant);

        /// <summary>
        /// Takes the tenant's access revision for this unit of work: reads it and bumps it, so the save fails
        /// when another change of rights in the tenant was saved after it was read.
        /// </summary>
        Task SerializeAccessChangesAsync(TTenantId tenant, CancellationToken cancellationToken);

        /// <summary>Saves the unit of work, in one transaction.</summary>
        Task SaveAsync(CancellationToken cancellationToken);
    }
}
