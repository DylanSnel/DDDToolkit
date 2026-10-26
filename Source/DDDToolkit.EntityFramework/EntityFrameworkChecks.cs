using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.EventLog;
using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.EntityFramework.Options;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.EntityFramework.Owners;
using DDDToolkit.Startup;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework;

/// <summary>
/// Checks an application runs at start-up, before its first request: that every context is wired the way the
/// toolkit needs it. What they catch is silent otherwise: a context built without the toolkit's interceptors
/// saves, and simply checks no invariant, bumps no version and stores no event.
/// <para>
/// <c>AddDDDToolkitEntityFramework</c> registers the check as <see cref="ToolkitWiredCheck"/>, which a host runs
/// with every other start-up check with <c>services.RunStartupChecks()</c>. The method stays for a host that runs
/// it by hand:
/// </para>
/// <code>
/// await using var scope = services.CreateAsyncScope();
/// foreach (var contextType in EntityFrameworkChecks.RegisteredContexts(scope.ServiceProvider))
/// {
///     EntityFrameworkChecks.EnsureToolkitWired((DbContext)scope.ServiceProvider.GetRequiredService(contextType));
/// }
/// </code>
/// </summary>
public static class EntityFrameworkChecks
{
    /// <summary>
    /// The start-up check <c>AddDDDToolkitEntityFramework</c> brings, by the name a host turns it off with
    /// (<c>services.SkipStartupCheck(...)</c>): <see cref="EnsureToolkitWired"/> for every registered context that maps
    /// one of the toolkit's classes, an entity or aggregate, or a table of the outbox, the inbox or the event log.
    /// A context that maps none of them, one a library brings for its own tables, needs none of the interceptors,
    /// and is passed over. It opens no connection, so it runs with the checks of the services, first.
    /// </summary>
    public const string ToolkitWiredCheck = "entity-framework.toolkit-wired";

    /// <summary>
    /// The start-up check <c>AddDDDToolkitEntityFramework</c> brings, by the name a host turns it off with:
    /// <see cref="EnsureOwnersMapped"/>, that the owner of every aggregate the host's requests ask about can be read.
    /// It finds, before the first request does, the context that maps each such aggregate and the column of its
    /// <c>[Owner]</c>, and keeps what it found for the requests. It opens no connection, so it runs with the checks of
    /// the services, after <see cref="ToolkitWiredCheck"/>. A host whose requests ask about no owner passes it at once.
    /// </summary>
    public const string OwnersMappedCheck = "entity-framework.owners-mapped";

    /// <summary>The check <see cref="ToolkitWiredCheck"/> names.</summary>
    internal static StartupCheck ToolkitWired { get; } = new(ToolkitWiredCheck, StartupCheckStage.Services, async (services, _) =>
    {
        await using var scope = services.CreateAsyncScope();
        foreach (var contextType in RegisteredContexts(scope.ServiceProvider))
        {
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
            if (MapsTheToolkit(context.Model))
            {
                EnsureToolkitWired(context);
            }
            else
            {
                // A model of a library's own may still require a part, Tenancy's save check for rows kept to a tenant.
                ContextPartRequirements.EnsureRequiredParts(context);
            }
        }
    });

    /// <summary>The check <see cref="OwnersMappedCheck"/> names, over the very collection it was brought by.</summary>
    internal static StartupCheck OwnersMapped(IServiceCollection registered) => new(OwnersMappedCheck, StartupCheckStage.Services, async (services, _) =>
    {
        await using var scope = services.CreateAsyncScope();
        EnsureOwnersMapped(registered, scope.ServiceProvider);
    });

    /// <summary>
    /// Throws when the owner of an aggregate the host's requests ask about cannot be read from the contexts it
    /// registered: no context maps an aggregate of the id, two do, the aggregate marks no property <c>[Owner]</c> or
    /// two, the context does not map the property, or its type is one nothing can say the caller's own of. The message
    /// of each is the one the first request about it would fail with, and where several cannot be read it names each.
    /// What it finds is kept for the requests, which then find nothing more.
    /// <para>
    /// The ids are the ones the generator says the requests ask about, <c>[assembly: AsksOwnerOf(typeof(BoardId))]</c>,
    /// read from the assembly of every request interface whose checks <paramref name="services"/> register: its lines
    /// for <c>[Own]</c> and <c>[OrOwn]</c> on an aggregate's id, and every call of <c>AccessRequirement.OwnerOf</c> of
    /// that project. An id the host registers an <see cref="IAggregateOwners{TId}"/> of its own for is that reader's,
    /// and is passed over. A call in a project no request interface is declared in is found by its first request.
    /// </para>
    /// </summary>
    /// <param name="services">The host's services, as registered.</param>
    /// <param name="scopedServices">A scope's services, where the contexts are made.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">The owner of an aggregate the requests ask about cannot be read.</exception>
    public static void EnsureOwnersMapped(IServiceCollection services, IServiceProvider scopedServices)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(scopedServices);

        var asked = OwnerColumns.AskedOf(services);
        if (asked.Count == 0)
        {
            return;
        }

        var columns = scopedServices.GetRequiredService<OwnerColumns>();
        var unread = asked.Select(id => columns.ProblemWith(id, scopedServices)).OfType<string>().ToList();
        if (unread.Count == 1)
        {
            throw new InvalidOperationException(unread[0]);
        }

        if (unread.Count > 1)
        {
            throw new InvalidOperationException(
                $"The owners of {unread.Count} aggregates the host's requests ask about cannot be read:"
                + string.Concat(unread.Select(static each => Environment.NewLine + "- " + each)));
        }
    }

    /// <summary>
    /// Throws when <paramref name="context"/> was built without <c>UseDDDToolkit</c> or <c>UseDDDToolkitCore</c>:
    /// its domain events would stay on their aggregates, its invariants would not be checked and its versions not
    /// bumped, without a word. It also throws when the context's model and its outbox disagree: the model maps an event log that
    /// no <c>outbox.KeepEventLog()</c> keeps, or the context has an outbox of its own
    /// (<c>UseOutbox&lt;TContext&gt;</c>) whose table, or whose event log, the model does not map. And it throws when
    /// the model requires a part of a context its options do not have
    /// (<see cref="ContextPartRequirements.EnsureRequiredParts"/>): a context that keeps rows to a tenant without
    /// Tenancy's save check, say, because nothing registered Tenancy.
    /// <para>
    /// The interceptors are read from the context's options each time, because two contexts of one type can be
    /// given different options. A host that adds the toolkit's interceptors by hand passes as long as the three
    /// that decide what is saved are there, in the order <c>UseDDDToolkit</c> adds them: domain events first,
    /// then invariants, which so see what the handlers changed, then versions, which so are not bumped by a
    /// save the invariants refused.
    /// </para>
    /// </summary>
    /// <param name="context">The context to check; it is not opened.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The context is not wired as the toolkit needs it.</exception>
    public static void EnsureToolkitWired(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var name = context.GetType().Name;
        var interceptors = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.Interceptors?.ToList() ?? [];
        var events = IndexOf<PublishDomainEventsInterceptor>(interceptors);
        var invariants = IndexOf<InvariantInterceptor>(interceptors);
        var versions = IndexOf<AggregateVersionInterceptor>(interceptors);

        List<string> missing = [];
        if (events < 0)
        {
            missing.Add($"{nameof(PublishDomainEventsInterceptor)} (its domain events would stay on their aggregates, delivered to nobody and stored nowhere)");
        }

        if (invariants < 0)
        {
            missing.Add($"{nameof(InvariantInterceptor)} (its aggregates' invariants would not be checked)");
        }

        if (versions < 0)
        {
            missing.Add($"{nameof(AggregateVersionInterceptor)} (its aggregates' versions would not be bumped, so two saves of one aggregate would not conflict)");
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"'{name}' was built without the DDDToolkit interceptors: it has no {string.Join(", no ", missing)}. " +
                "Configure it with options.UseDDDToolkit(serviceProvider), in the options callback of AddDbContext or of a context pool.");
        }

        if (!(events < invariants && invariants < versions))
        {
            throw new InvalidOperationException(
                $"'{name}' has the DDDToolkit interceptors in another order than UseDDDToolkit adds them, so an invariant could miss what a handler changed, or a version be bumped by a save that was refused. " +
                "Configure it with options.UseDDDToolkit(serviceProvider), which adds domain events, then invariants, then versions.");
        }

        ContextPartRequirements.EnsureRequiredParts(context);

        var options = ((PublishDomainEventsInterceptor)interceptors[events]).Options;
        var outbox = options.OutboxFor(context.GetType());
        var mapsLog = context.Model.FindEntityType(typeof(EventLogEntry)) is not null;

        if (mapsLog && outbox?.EventLog is null)
        {
            throw new InvalidOperationException(
                $"The model of '{name}' maps an event log, but nothing keeps events in it, so the table would stay empty. " +
                $"Call outbox.{nameof(OutboxOptions.KeepEventLog)}() in {nameof(DDDEntityFrameworkOptions.UseOutbox)}<{name}>(...), or take modelBuilder.{nameof(EventLogModelBuilderExtensions.AddEventLog)}(...) out.");
        }

        // What an outbox of the context's own asks of its model. The outbox every context shares says nothing
        // about one of them: a context that raises no event needs none of its tables.
        if (outbox is null || ReferenceEquals(outbox, options.Outbox))
        {
            return;
        }

        if (context.Model.FindEntityType(typeof(OutboxMessage)) is null)
        {
            throw new InvalidOperationException(
                $"An outbox is configured for '{name}' but its model does not contain the outbox table, so its first save that raises an event would fail. " +
                $"Call modelBuilder.{nameof(OutboxModelBuilderExtensions.AddDomainEventOutbox)}(Database) in OnModelCreating.");
        }

        if (!mapsLog && outbox.EventLog is not null)
        {
            throw new InvalidOperationException(
                $"The outbox of '{name}' keeps an event log but the context's model does not contain the event log table, so its first save that raises a kept event would fail. " +
                $"Call modelBuilder.{nameof(EventLogModelBuilderExtensions.AddEventLog)}(Database) in OnModelCreating, or take {nameof(OutboxOptions.KeepEventLog)}() out.");
        }
    }

    /// <summary>
    /// Every context type registered in <paramref name="scopedServices"/>, ordered by full name, so a start-up
    /// check covers a module added later without anyone remembering to list it. Entity Framework registers the
    /// options of each context once more under the non-generic <see cref="DbContextOptions"/>, and those name
    /// the context they are for; a context registered with a factory or a pool next to it has more than one
    /// such registration, and is listed once.
    /// </summary>
    /// <param name="scopedServices">A scope's services: the options of a context from <c>AddDbContext</c> are scoped, like the context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scopedServices"/> is null.</exception>
    public static IReadOnlyList<Type> RegisteredContexts(IServiceProvider scopedServices)
    {
        ArgumentNullException.ThrowIfNull(scopedServices);

        return [.. scopedServices.GetServices<DbContextOptions>()
            .Select(options => options.ContextType)
            .Distinct()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Whether <paramref name="model"/> maps a class the toolkit's interceptors act on: an entity or an aggregate
    /// root, or one of the toolkit's own tables, the outbox, the inbox and the event log.
    /// </summary>
    private static bool MapsTheToolkit(IModel model)
        => model.GetEntityTypes().Any(entity => typeof(IEntity).IsAssignableFrom(entity.ClrType) || entity.ClrType.Assembly == typeof(EntityFrameworkChecks).Assembly);

    private static int IndexOf<TInterceptor>(List<IInterceptor> interceptors)
    {
        for (var index = 0; index < interceptors.Count; index++)
        {
            if (interceptors[index] is TInterceptor)
            {
                return index;
            }
        }

        return -1;
    }
}
