using System.Runtime.CompilerServices;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Membership.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>
/// The expert hold: every save that changes a resource with members is held to what the access check of the
/// request in hand read of that resource, with nothing written in the handler. A context gets it with one line,
/// <c>UseMemberHolds</c>; without it, nothing of this applies.
/// </summary>
/// <remarks>
/// <see cref="MemberAccessCheck{TResource, TResourceId}"/> keeps what it read of the resource a request requires
/// a key on, its version included (<see cref="MemberHold{TResourceId}"/>), with the request in hand
/// (<see cref="RequestInHand"/>): the request whose checks the flow of work asked last, which is the flow the
/// handler runs in and its save with it. Before anything is written, this goes through every resource with
/// members the save changes, a member or a role of one included, and finds that hold:
/// <list type="table">
/// <listheader><term>The save changes a resource</term><description>What happens</description></listheader>
/// <item><term>that the request in hand was checked on</term><description>It is saved only at the version the check read: one changed since is a <see cref="ConcurrencyConflictException"/>, a lost race. The save compares that version, as it compares every version.</description></item>
/// <item><term>with no hold for it, as the application's own work</term><description>Saved: system work that trusted code began, <c>Callers.Begin(Caller.System)</c> or a scope the resource's rules name, needs no check.</description></item>
/// <item><term>any other way</term><description>Refused with an <see cref="InvalidOperationException"/>, and nothing is saved: a handler called directly, a transport that runs it around the checks, a save that runs after the request's handling returned, or a handler that changes another resource than its request names.</description></item>
/// </list>
/// A new resource needs no hold: nothing was there to check. A request's handler that saves the same resource
/// twice is held to the version the check read at its first save, and, once that save succeeded, to the version
/// that save left. A save that failed leaves nothing behind: one that lost the race and is tried again, with the
/// stored values taken as the loaded ones, is still not at the version the check read, and loses again.
/// <para>
/// The hold relies on the save comparing the version, so a resource whose version the model does not map as a
/// concurrency token is refused at its first save, as <c>ExpectVersion</c> refuses it. And it holds what the
/// whole save changes, the domain event handlers' changes included, so <c>UseMemberHolds</c> comes after
/// <c>UseDDDToolkit</c>, which it checks.
/// </para>
/// <para>
/// Who is in hand is a matter of the flow, not of the scope: a context of another scope, saved in the request's
/// flow, is held to the request's hold as well, and the request a scope handled before is in no flow's hand once
/// its sender returned. So a context from a pool, one bound to no scope, and one of a scope of its own are all held
/// alike.
/// </para>
/// <para>
/// One instance for the application, which every context it is added to shares: it keeps nothing of a save but
/// which loaded resource a save held to which hold, and the version that save left, weakly, for a second save of
/// the same handling; and, while a context saves, what that save holds, until it succeeded.
/// </para>
/// </remarks>
internal sealed class MemberHoldInterceptor : SaveChangesInterceptor
{
    private const int MaxOwnershipDepth = 32;

    private readonly IReadOnlyList<HeldResource> _resources;
    private readonly ICallerAccessor _callers;

    /// <summary>Each loaded resource a successful save held to a hold, with that hold and the version the save left, for a later save of the same handling.</summary>
    private readonly ConditionalWeakTable<object, HeldSince> _held = new();

    /// <summary>What the save a context is in the middle of holds, until it succeeded: only then does it count as held.</summary>
    private readonly ConditionalWeakTable<DbContext, List<Holding>> _saving = new();

    /// <summary>The interceptor over every resource with members registered with the application.</summary>
    /// <param name="registrations">The resources, as their registrations added them.</param>
    /// <param name="callers">Who is calling, for the application's own work.</param>
    internal MemberHoldInterceptor(IEnumerable<MembershipRegistration> registrations, ICallerAccessor callers)
    {
        _resources = [.. registrations.Select(HeldResource.For)];
        _callers = callers;
    }

    /// <inheritdoc />
    /// <exception cref="ConcurrencyConflictException">A resource the request's check read changed since.</exception>
    /// <exception cref="InvalidOperationException">A resource with members was changed outside the handling of a request whose check read it.</exception>
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
        {
            Hold(context);
        }

        return result;
    }

    /// <inheritdoc />
    /// <exception cref="ConcurrencyConflictException">A resource the request's check read changed since.</exception>
    /// <exception cref="InvalidOperationException">A resource with members was changed outside the handling of a request whose check read it.</exception>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            Hold(context);
        }

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Saved(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Saved(eventData.Context);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Forget(eventData.Context);

    /// <inheritdoc />
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData.Context);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override void SaveChangesCanceled(DbContextEventData eventData) => Forget(eventData.Context);

    /// <inheritdoc />
    public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData.Context);
        return Task.CompletedTask;
    }

    /// <summary>Holds every resource with members this save changes to the hold of the request in hand, or lets the application's own work through.</summary>
    private void Hold(DbContext context)
    {
        // What an earlier save of this context held and did not see through counts for nothing: a save that another
        // interceptor stopped before it began reports neither success nor failure here.
        _saving.Remove(context);
        if (_resources.Count == 0)
        {
            return;
        }

        List<Holding> holding = [];
        foreach (var root in ChangedRoots(context))
        {
            if (Find(root.Entity.GetType()) is not { } resource)
            {
                continue;
            }

            if (resource.HoldInHand(root.Entity) is { } hold)
            {
                var version = root.Property(nameof(IAggregateRoot.Version));
                if (!version.Metadata.IsConcurrencyToken)
                {
                    throw new InvalidOperationException(
                        $"The model of {context.GetType().Name} does not map {root.Metadata.ClrType.Name}.{nameof(IAggregateRoot.Version)} as a concurrency token, "
                        + "so the save would not compare the version the hold holds it to, and a change made after the load would go unnoticed. "
                        + "Call configurationBuilder.AddDDDToolkitConventions() in ConfigureConventions.");
                }

                // The version the resource was loaded at, which is what the save puts in its WHERE clause. When it is
                // the one the check read, the save compares exactly that; when it is not, the resource changed between
                // the check and the load, and what was decided was decided about another version. Once a save of this
                // handling held the resource and succeeded, the version that save left is the one to be at.
                var expected = _held.TryGetValue(root.Entity, out var since) && ReferenceEquals(since.Kept, hold.Kept)
                    ? since.Version
                    : hold.Version;
                if (version.OriginalValue is not long loaded || loaded != expected)
                {
                    throw new ConcurrencyConflictException(root.Metadata.ClrType, hold.Resource);
                }

                holding.Add(new Holding(root, hold.Kept));
                continue;
            }

            if (IsOwnWork(resource.Rules))
            {
                continue;
            }

            throw new InvalidOperationException(Refusal(resource, root.Entity));
        }

        if (holding.Count > 0)
        {
            _saving.AddOrUpdate(context, holding);
        }
    }

    /// <summary>
    /// Counts what the save of <paramref name="context"/> held as held, now that it succeeded: each resource with
    /// its hold, and the version the save left it at, which a later save of the same handling is held to.
    /// </summary>
    private void Saved(DbContext? context)
    {
        if (context is null || !_saving.TryGetValue(context, out var holding))
        {
            return;
        }

        _saving.Remove(context);
        foreach (var held in holding)
        {
            if (held.Root.Property(nameof(IAggregateRoot.Version)).CurrentValue is long version)
            {
                _held.AddOrUpdate(held.Root.Entity, new HeldSince(held.Kept, version));
            }
        }
    }

    /// <summary>Drops what the save of <paramref name="context"/> held: it did not succeed, so none of it counts.</summary>
    private void Forget(DbContext? context)
    {
        if (context is not null)
        {
            _saving.Remove(context);
        }
    }

    /// <summary>
    /// Whether the save is the application's own work on the resource: the host answers it, and trusted code
    /// began it for this flow, as <see cref="AccessRequirement.RequiresSystemWork"/> takes it. A flow nobody began
    /// a caller for is not, even where the host answers the system for it.
    /// </summary>
    private bool IsOwnWork(MembershipRules rules)
        => rules.IsOwnWork(_callers.Current) && Callers.Ambient?.Kind is CallerKind.System or CallerKind.SystemIn;

    /// <summary>What a change no check read is refused with: which resource, and why there was no hold for it.</summary>
    private static string Refusal(HeldResource resource, object entity)
    {
        var name = resource.Resource.Name;
        var what = $"{name} {resource.IdOf(entity)}";
        var how =
            $"Under UseMemberHolds a resource with members is saved for the request whose access check read it (MemberAccess.On(key, {name.ToLowerInvariant()})), "
            + "or as the application's own work that trusted code began (Callers.Begin(Caller.System)). Nothing was saved.";

        return RequestInHand.Current is not { } inHand
            ? $"{what} was changed with no request in hand: no request's access checks let one through in the flow of work this save runs in. "
              + "Either the handler was reached around its checks, called directly or by a transport that runs it around them, or the save ran after "
              + "the request's handling returned: a unit-of-work behavior registered outside the access behavior, or an endpoint that saves after Send. "
              + $"Send the request through its checks, and save inside its handling. {how}"
            : resource.ResourceInHand() is { } checkedOne
                ? $"{what} was changed in the handling of {inHand.Request.GetType().Name}, whose access check read {name} {checkedOne}: "
                  + $"a handler changes the {name} its request requires a key on, and no other. {how}"
                : $"{what} was changed in the handling of {inHand.Request.GetType().Name}, whose access check read no {name}: "
                  + $"a handler changes the {name} its request requires a key on, and no other. {how}";
    }

    /// <summary>The registered resource <paramref name="type"/> is, or derives from; <see langword="null"/> for anything else.</summary>
    private HeldResource? Find(Type type)
    {
        foreach (var resource in _resources)
        {
            if (resource.Resource.IsAssignableFrom(type))
            {
                return resource;
            }
        }

        return null;
    }

    /// <summary>
    /// Every aggregate root this save writes that was there before it: one that is changed or deleted itself, and
    /// the root of any row that is added, changed or deleted beneath it, a member or a member's role. Each once. A
    /// root the save adds is left out: nothing was there to check.
    /// </summary>
    private static List<EntityEntry> ChangedRoots(DbContext context)
    {
        if (context.ChangeTracker.AutoDetectChangesEnabled)
        {
            context.ChangeTracker.DetectChanges();
        }

        var found = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var roots = new List<EntityEntry>();

        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.Entity is IAggregateRoot)
            {
                Add(entry, found, roots);
                continue;
            }

            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance) { entry.Entity };
            foreach (var root in OwningRoots(context, entry, visited, depth: 0))
            {
                Add(root, found, roots);
            }
        }

        return roots;
    }

    private static void Add(EntityEntry root, HashSet<object> found, List<EntityEntry> roots)
    {
        if (root.State != EntityState.Added && found.Add(root.Entity))
        {
            roots.Add(root);
        }
    }

    /// <summary>
    /// The aggregate roots a row belongs to: through its ownership when it is owned, as a member and its roles
    /// are, and otherwise through a foreign key whose principal is an aggregate root with a navigation to it.
    /// </summary>
    private static IEnumerable<EntityEntry> OwningRoots(DbContext context, EntityEntry current, HashSet<object> visited, int depth)
    {
        if (depth >= MaxOwnershipDepth)
        {
            yield break;
        }

        var ownership = current.Metadata.FindOwnership();
        IEnumerable<IForeignKey> owners = ownership is not null
            ? [ownership]
            : current.Metadata.GetForeignKeys().Where(static key =>
                key.PrincipalToDependent is not null && typeof(IAggregateRoot).IsAssignableFrom(key.PrincipalEntityType.ClrType));

        foreach (var key in owners)
        {
            if (Principal(context, current, key) is not { } principal || !visited.Add(principal.Entity))
            {
                continue;
            }

            if (principal.Entity is IAggregateRoot)
            {
                yield return principal;
                continue;
            }

            foreach (var root in OwningRoots(context, principal, visited, depth + 1))
            {
                yield return root;
            }
        }
    }

    private static EntityEntry? Principal(DbContext context, EntityEntry dependent, IForeignKey key)
    {
        if (key.DependentToPrincipal is { } navigation && dependent.Navigation(navigation.Name).CurrentValue is { } target)
        {
            return context.Entry(target);
        }

        // An owned row usually has no navigation back to its owner, only the key. The state manager finds the
        // tracked owner by it without reading anything, as the toolkit's own interceptors do (EF1001).
#pragma warning disable EF1001 // Internal EF Core API usage.
        var principal = context.GetService<IStateManager>().FindPrincipal(dependent.GetInfrastructure(), key);
        return principal is null ? null : new EntityEntry(principal);
#pragma warning restore EF1001
    }

    /// <summary>A resource the save in progress holds, with its hold: counted as held once the save succeeded.</summary>
    /// <param name="Root">The resource's entry.</param>
    /// <param name="Kept">The hold, one object per pass of a request's check.</param>
    private sealed record Holding(EntityEntry Root, object Kept);

    /// <summary>What a successful save held a resource to, and the version it left the resource at.</summary>
    /// <param name="Kept">The hold, one object per pass of a request's check.</param>
    /// <param name="Version">The version the save left.</param>
    private sealed record HeldSince(object Kept, long Version);
}
