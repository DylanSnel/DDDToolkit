using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.Supporting.Tenancy.Access;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// Who wrote a row first and who changed it last, kept on the row itself: six columns a module adds to an entity
/// it keeps to a tenant, which every save fills in.
/// </summary>
public static class TenancyAttribution
{
    /// <summary>The seat that wrote the row first, when a seat did.</summary>
    public const string CreatedBySeat = "CreatedBySeat";

    /// <summary>What kind of actor wrote the row first: one of <see cref="TenancyActorKinds"/>.</summary>
    public const string CreatedByKind = "CreatedByKind";

    /// <summary>The verified identity of the operator the row was first written for, when it was one.</summary>
    public const string CreatedByIdentity = "CreatedByIdentity";

    /// <summary>The seat that changed the row last, when a seat did.</summary>
    public const string ChangedBySeat = "ChangedBySeat";

    /// <summary>What kind of actor changed the row last: one of <see cref="TenancyActorKinds"/>.</summary>
    public const string ChangedByKind = "ChangedByKind";

    /// <summary>The verified identity of the operator the row was last changed for, when it was one.</summary>
    public const string ChangedByIdentity = "ChangedByIdentity";

    /// <summary>The longest kind a column holds.</summary>
    public const int KindLength = 32;

    /// <summary>The annotation <see cref="RecordsWhoChanged"/> puts on an entity type; <see cref="TenancyModel.RecordsWhoChanged"/> answers it.</summary>
    internal const string Annotation = "DDDToolkit:Tenancy:RecordsWhoChanged";

    /// <summary>
    /// The six columns, in one order for everything that reads them by position: who wrote the row first as seat,
    /// kind and identity, then who changed it last as seat, kind and identity.
    /// </summary>
    public static IReadOnlyList<string> Columns { get; } = [CreatedBySeat, CreatedByKind, CreatedByIdentity, ChangedBySeat, ChangedByKind, ChangedByIdentity];

    /// <summary>The three columns that say who wrote the row first, which never change once it is there.</summary>
    internal static readonly string[] Created = [CreatedBySeat, CreatedByKind, CreatedByIdentity];

    /// <summary>The three columns that say who changed the row last.</summary>
    internal static readonly string[] Changed = [ChangedBySeat, ChangedByKind, ChangedByIdentity];

    /// <summary>
    /// Adds six shadow columns to an entity that is kept to a tenant, and has every save fill them in: who wrote
    /// the row first and who changed it last, each as a kind (<see cref="TenancyActorKinds"/>), a seat when a seat
    /// did it, and an operator's verified identity when it was done for an operator. The application's own work
    /// leaves a kind and neither id.
    /// <code>
    /// modelBuilder.Entity&lt;Project&gt;(project =&gt;
    /// {
    ///     project.ScopeToTenant(row =&gt; row.TenantId);
    ///     project.RecordsWhoChanged&lt;Project, SeatId&gt;();
    /// });
    /// </code>
    /// <para>
    /// Tenancy's save interceptor, which <c>UseDDDToolkit</c> adds, fills them from the Tenancy caller of the save
    /// (<see cref="TenancyCaller{TTenantId, TSeatId}.Actor"/>): both sets for a new row, and the last three for a
    /// row that changes. A row whose own columns did not change, because only a row of one of its entities in
    /// another table did, keeps who changed it last; an aggregate with a version changes its row with every
    /// change of its parts. The first three are fixed once the row is there: Entity Framework refuses a save that
    /// changes one, and the privileges an export writes leave them out of <c>UPDATE</c>.
    /// </para>
    /// <para>
    /// They are ids, never a name: what a seat is shown by is the application's, which the directory answers by id.
    /// They are shadow properties, so the entity's class does not change; read one with
    /// <c>EF.Property&lt;SeatId?&gt;(row, TenancyAttribution.ChangedBySeat)</c>. Adding them to a table that exists
    /// is a migration of your own, as any change to the model is.
    /// </para>
    /// <para>
    /// On Postgres, <c>DDDToolkit.Supporting.Tenancy.Postgres</c> writes a trigger on the table that holds a
    /// signed-in user to its own seat in these columns, and the application's own work to a kind that is no seat,
    /// so a statement that goes around the model cannot write somebody else's name.
    /// </para>
    /// <para>
    /// The call without the seat's id is generated into the project that declares the seat class, and into the
    /// projects of its module (<see cref="TemplateRegistrationAttribute"/>). A module of its own names the id:
    /// <c>project.RecordsWhoChanged&lt;Project, SeatId&gt;()</c>.
    /// </para>
    /// </summary>
    /// <typeparam name="T">
    /// The entity, which the model keeps to a tenant with <c>ScopeToTenant</c>. In a hierarchy stored in one table
    /// it is the type at the root, so every row of the table keeps the columns; the first save refuses a model that
    /// leaves either out.
    /// </typeparam>
    /// <typeparam name="TSeatId">The application's seat id.</typeparam>
    /// <param name="entity">The entity's builder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entity"/> is null.</exception>
    [TemplateRegistration]
    public static EntityTypeBuilder<T> RecordsWhoChanged<T, [TemplateType(typeof(SeatAggregateAttribute<>))] TSeatId>(this EntityTypeBuilder<T> entity)
        where T : class
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.Property<TSeatId?>(CreatedBySeat).IsFixedAfterInsert();
        entity.Property<string>(CreatedByKind).IsRequired().HasMaxLength(KindLength).IsFixedAfterInsert();
        entity.Property<Guid?>(CreatedByIdentity).IsFixedAfterInsert();
        entity.Property<TSeatId?>(ChangedBySeat);
        entity.Property<string>(ChangedByKind).IsRequired().HasMaxLength(KindLength);
        entity.Property<Guid?>(ChangedByIdentity);
        entity.HasAnnotation(Annotation, true);
        return entity;
    }
}
