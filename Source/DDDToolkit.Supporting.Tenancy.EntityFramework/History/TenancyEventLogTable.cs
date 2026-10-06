using System.Collections.Concurrent;
using System.Linq.Expressions;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.EntityFramework.EventLog;
using DDDToolkit.EntityFramework.Storage;
using DDDToolkit.Interfaces;
using DDDToolkit.Supporting.Tenancy.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// Tenancy's access history: the toolkit's event log with the tenant of each event on its row, so the history
/// of one tenant is read without opening a payload, and a database can keep a tenant's history to that tenant.
/// </summary>
public static class TenancyEventLogTable
{
    /// <summary>The column that holds the tenant an event is about: a shadow property of the log's row.</summary>
    public const string TenantId = "TenantId";

    /// <summary>
    /// The annotation <see cref="AddTenancyEventLogTable"/> puts on the log's entity type: whether the table lets
    /// a row go once it is old enough. <see cref="TenancyModel.EventLogOf"/> finds the log by it.
    /// </summary>
    internal const string Annotation = "DDDToolkit:Tenancy:EventLog";

    /// <summary>
    /// Maps the toolkit's event log into the context Tenancy lives in, with a <see cref="TenantId"/> column of the
    /// application's tenant id and an index on the tenant and the time a row was written, which is what a reader
    /// of one tenant's history pages by. Call it from <c>OnModelCreating</c> next to <c>AddTenancy</c>, and keep
    /// the events with the outbox's <c>KeepEventLog</c>:
    /// <code>
    /// modelBuilder.AddTenancy(database: Database);
    /// modelBuilder.AddDomainEventOutbox(Database);
    /// modelBuilder.AddTenancyEventLogTable(Database);
    ///
    /// options.UseOutbox&lt;TenancyContext&gt;(outbox =&gt; outbox
    ///     .AddTenancyDomainEvents()
    ///     .KeepEventLog(log =&gt; log.AddTenancyEventLog()));
    /// </code>
    /// <para>
    /// Every row says which tenant its event is about, taken from the event itself, which every event of Tenancy's
    /// carries, and who acted, from the Tenancy caller of the save: a seat, an operator, the system or a token
    /// (<see cref="TenancyActedByAccessor"/>). An event of the application's own that names no tenant gets the
    /// tenant the save runs in. The payload is the event as the outbox stores it: ids, keys and dates, who made
    /// the change, and never what a tenant, a unit, a seat or a role is called.
    /// <see cref="TenancyEventLogExtensions.AddTenancyEventLog{TTenantId, TSeatId, TUnitId, TRoleId}"/> chooses
    /// the events that change access.
    /// </para>
    /// <para>
    /// The rows are not kept to the caller's tenant by a query filter, since the toolkit's retention reads the
    /// table whole: code that reads the history names the tenant itself, and asks
    /// <c>tenancy.history.view</c> for the whole tenant first. On Postgres,
    /// <c>DDDToolkit.Supporting.Tenancy.Postgres</c> writes the table's policies: a seat adds rows about itself in
    /// its tenant and reads its tenant's with that key, and system work adds and reads its tenant's. It writes
    /// them with those of Tenancy's own tables, so the log belongs in the context that maps them: an export
    /// refuses it in any other, where it would be left without a policy.
    /// </para>
    /// <para>
    /// The call without type arguments is generated into the project that declares the tenant class, and into the
    /// projects of its module (<see cref="TemplateRegistrationAttribute"/>). Everything else is
    /// <c>AddEventLog</c>'s: the table only grows, and its name, its schema and how long it keeps a row are that
    /// method's to explain.
    /// </para>
    /// </summary>
    /// <typeparam name="TTenantId">The application's tenant id.</typeparam>
    /// <param name="modelBuilder">The model being built.</param>
    /// <param name="database">The context's <c>Database</c>, read for its provider name only.</param>
    /// <param name="tableName">The table to map to.</param>
    /// <param name="schema">The schema to put it in, or <see langword="null"/> for the provider's default.</param>
    /// <param name="keepFor">How long a row is kept before it may be deleted; <see langword="null"/>, the default, keeps every row.</param>
    /// <param name="configure">Further configuration of the table, after the tenant's column is there.</param>
    /// <exception cref="ArgumentNullException"><paramref name="modelBuilder"/> or <paramref name="database"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="tableName"/> is empty or white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="keepFor"/> is zero, negative or longer than a thousand years.</exception>
    [TemplateRegistration]
    public static ModelBuilder AddTenancyEventLogTable<[TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId>(
        this ModelBuilder modelBuilder,
        DatabaseFacade database,
        string tableName = EventLogModelBuilderExtensions.DefaultTableName,
        string? schema = DomainEventStorage.DefaultSchema,
        TimeSpan? keepFor = null,
        Action<EntityTypeBuilder<EventLogEntry>>? configure = null)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        return modelBuilder.AddEventLog(database, tableName, schema, keepFor, log =>
        {
            log.Property<TTenantId>(TenantId);
            log.HasIndex(TenantId, nameof(EventLogEntry.RecordedAt));
            log.HasAnnotation(Annotation, keepFor is not null);
            configure?.Invoke(log);
        });
    }
}

/// <summary>
/// Fills the tenant of every row of Tenancy's access history, the log <c>AddTenancyEventLogTable</c> maps: the
/// tenant the kept event names, or, for an event that names none, the tenant the save runs in. A log without
/// that column is not its business, and is left as it is.
/// <para>
/// A singleton, as every <see cref="IEventLogFields"/> is: it reads the event and the ambient Tenancy caller,
/// keeps nothing of either, and takes no scoped service.
/// </para>
/// </summary>
internal sealed class TenancyEventLogFields : IEventLogFields
{
    /// <summary>How to read the tenant of each event type, as each tenant id type: found once, and none for an event that names no such tenant.</summary>
    private static readonly ConcurrentDictionary<(Type Event, Type Tenant), Func<IDomainEvent, object?>?> TenantOf = new();

    /// <inheritdoc />
    public void Fill(IDomainEvent domainEvent, EntityEntry<EventLogEntry> entry)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Metadata.FindAnnotation(TenancyEventLogTable.Annotation) is null)
        {
            return;
        }

        var column = entry.Property(TenancyEventLogTable.TenantId);
        var tenantType = Nullable.GetUnderlyingType(column.Metadata.ClrType) ?? column.Metadata.ClrType;
        var named = TenantOf.GetOrAdd((domainEvent.GetType(), tenantType), static key => ReaderOf(key.Event, key.Tenant))?.Invoke(domainEvent);

        // The save check has let the save through, so whatever is saved here is of the caller's tenant.
        column.CurrentValue = named
            ?? (TenancyCallers.CurrentTenantOrNull() is { } current && current.GetType() == tenantType ? current : null)
            ?? throw new InvalidOperationException(
                "The event " + domainEvent.GetType().Name + " goes into Tenancy's access history, and neither names a tenant, as a property TenantId of type "
                + tenantType.Name + ", nor is saved by a Tenancy caller that acts in one. Give the event its tenant, or keep it out of the log with KeepEventLog(log => log.Keep<...>()).");
    }

    /// <summary>
    /// Reads <c>TenantId</c> of an event of <paramref name="eventType"/>, when it has one of
    /// <paramref name="tenantType"/>; <see langword="null"/> when it has none. Compiled once per event type.
    /// </summary>
    private static Func<IDomainEvent, object?>? ReaderOf(Type eventType, Type tenantType)
    {
        var property = eventType.GetProperty(TenancyEventLogTable.TenantId);
        if (property?.GetMethod is null || (Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType) != tenantType)
        {
            return null;
        }

        var domainEvent = Expression.Parameter(typeof(IDomainEvent), "domainEvent");
        var tenant = Expression.Property(Expression.Convert(domainEvent, eventType), property);
        return Expression.Lambda<Func<IDomainEvent, object?>>(Expression.Convert(tenant, typeof(object)), domainEvent).Compile();
    }
}
