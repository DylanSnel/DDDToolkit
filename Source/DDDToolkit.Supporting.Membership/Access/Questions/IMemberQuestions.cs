using DDDToolkit.Abstractions.Interfaces;

namespace DDDToolkit.Supporting.Membership.Access;

/// <summary>
/// The access questions about one kind of resource, for the current caller: what it reaches, and which keys it
/// holds on which resource. There is one per kind of resource an application has members on, asked for by the
/// resource's id, so the questions about documents never answer for folders.
/// <para>
/// A caller holds a key on a resource through its members: it is a member now, and holds a role there, now,
/// that gives the key. A role counts only inside its membership, and only for the keys a member's role can
/// give (<see cref="MembershipRules.MemberKeys"/>). Being a member gives one key by itself, when the rules
/// name one (<see cref="MembershipRules.SeeKey"/>). The owner of a resource holds every key of it
/// (<see cref="MembershipRules.Keys"/>), by owning it, and the application's own work holds every key: the
/// application itself, and its work in a scope the rules name (<see cref="MembershipRules.IsOwnWork"/>). Where
/// the rules say so (<see cref="MembershipRules.Above"/>), a key held where the resource sits, or above it,
/// is held on the resource as well, by a caller that is no member of it. A resource the caller reaches in
/// no way does not exist for it.
/// </para>
/// <para>
/// That is all these questions answer: what a caller holds. Who may add a member, give a role or hand a
/// resource on is the application's to decide, by requiring a key of its choice on each of its commands, and
/// by whatever further rule it writes over the answer (<see cref="MemberHold{TResourceId}"/>).
/// </para>
/// <para>
/// The storage that keeps the resources implements it, because whether a membership and a role apply now is
/// compared where the rows are, inside one statement. What is refused, and in which order, is not the
/// storage's: <see cref="MemberQuestions.RequireAsync"/> and <see cref="MemberQuestions.ViaAsync"/>
/// decide that over <see cref="HoldAsync"/>, the same for every storage.
/// </para>
/// </summary>
/// <typeparam name="TResourceId">The resource's id.</typeparam>
public interface IMemberQuestions<TResourceId>
    where TResourceId : struct, IEntityId, IEquatable<TResourceId>
{
    /// <summary>The rules of the resource these questions are about, with the codes it refuses under.</summary>
    MembershipRules Rules { get; }

    /// <summary>
    /// Refuses a caller the questions have no answer for at all, with that caller's own refusal. Where the
    /// caller's member id is its own id or a claim there is no such caller: one that is nobody's member simply
    /// reaches nothing, so this returns for everybody. Where the application resolves who a caller is as a
    /// member (<see cref="MemberSource.Resolved"/>), a caller the application refuses outright, somebody who
    /// is nobody in the organization the resources belong to, is refused here, before anything is read
    /// (<see cref="ICallerMember{TResourceId, TMemberId}.Require"/>).
    /// </summary>
    /// <exception cref="Exceptions.RefusalException">The caller is nobody the questions can be asked about.</exception>
    void RequireCaller();

    /// <summary>
    /// The resources the caller reaches for <paramref name="key"/>, for a statement of the module's own: those
    /// it is a member of now with a role that gives the key, for a key that being a member gives those it is a
    /// member of at all, for a key of the resource those it owns, and, where the rules let a resource be
    /// reached from above, those that sit where the caller holds the key, or below. Every resource for the
    /// application's own work; none for a caller that is nobody.
    /// <para>
    /// A reach says where one key is held, and nothing of the others. Through its members a resource is held
    /// only where it is seen. From above it is seen by the key that sees (<see cref="MembershipRules.SeeKey"/>),
    /// so a caller that holds another key there and not that one is within this reach without seeing the
    /// resource. <see cref="HoldAsync"/> and <see cref="KeysOnAsync"/> answer only for what the caller sees; a
    /// statement of the module's own that should do the same puts the reach for the key that sees in front
    /// of this one.
    /// </para>
    /// </summary>
    /// <param name="key">A permission key.</param>
    MemberReach<TResourceId> Reach(string key);

    /// <summary>
    /// Which of <paramref name="keys"/> the caller holds on which resources, for one statement of the module's
    /// own. Only resources the caller sees are answered about.
    /// </summary>
    /// <param name="keys">Permission keys.</param>
    MemberKeyReach<TResourceId> KeyReach(IReadOnlyCollection<string> keys);

    /// <summary>
    /// What the caller holds on one resource, in one statement: <see langword="null"/> when the caller does not
    /// see the resource, which is also the answer for a resource that does not exist; otherwise the resource,
    /// its version, and how the caller holds <paramref name="key"/> on it and until when, if it does. It
    /// refuses nobody: a module builds its own answers on it, such as what it tells another module that asks
    /// about a resource.
    /// </summary>
    /// <param name="resource">The resource.</param>
    /// <param name="key">A permission key.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<MemberHold<TResourceId>?> HoldAsync(TResourceId resource, string key, CancellationToken cancellationToken);

    /// <summary>
    /// The keys of <paramref name="keys"/> the caller holds on each of <paramref name="resources"/> it sees, in
    /// one statement whatever their number. A resource the caller does not see, or holds none of the keys on,
    /// is not in the answer, so asking about an id tells nothing about it. What is answered is for showing:
    /// whether a command may run is asked again when it runs.
    /// </summary>
    /// <param name="resources">The resources asked about, at most <see cref="MemberQuestions.MostResources"/> different ones.</param>
    /// <param name="keys">Permission keys.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <exception cref="ArgumentException">
    /// More than <see cref="MemberQuestions.MostResources"/> different resources are asked about. A use
    /// case that takes the ids from a request refuses its caller, under a code of its own, before it asks.
    /// </exception>
    Task<IReadOnlyDictionary<TResourceId, IReadOnlySet<string>>> KeysOnAsync(
        IReadOnlyCollection<TResourceId> resources,
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken);
}
