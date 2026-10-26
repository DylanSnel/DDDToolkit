using System.Linq.Expressions;
using System.Reflection;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.Supporting.Tenancy.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>
/// Maps Tenancy into an Entity Framework model: its tables into the context the application keeps Tenancy
/// in, its read model into every other context that asks Tenancy inside its own queries, as views over those
/// tables or as Tenancy's read functions, and the tenant filter onto any entity of any module that belongs to
/// a tenant.
/// </summary>
public static class TenancyModelBuilderExtensions
{
    /// <summary>The registration that brings the save check, as a context that lacks it is told to write it.</summary>
    private const string TenancyRegistration = "services.AddTenancy<…, TContext>(…) of DDDToolkit.Supporting.Tenancy.EntityFramework";

    /// <summary>
    /// Maps Tenancy's tables, closed over the application's classes and ids, into the context's default
    /// schema: the tenants, organizations with their units, seats with their placements and grants, roles,
    /// the stored rights, the closure of each organization's tree and the tenants' access revisions, plus the
    /// read model's views over them. Call it from <c>OnModelCreating</c> of the context Tenancy lives in:
    /// <code>
    /// modelBuilder.HasDefaultSchema("tenancy");
    /// modelBuilder.AddTenancy();
    /// </code>
    /// <para>
    /// The call without type arguments is generated into the project that declares the classes, closed over
    /// them (<see cref="TemplateRegistrationAttribute"/>), or into a project of the same <c>[assembly: Module]</c>
    /// that declares none, such as the module's infrastructure project, where the context is, next to a domain
    /// project without Entity Framework. This is the method it calls, and it can be called as well:
    /// <c>modelBuilder.AddTenancy&lt;ShopTenant, TenantId, ShopOrganization, ShopUnit, OrganizationUnitId,
    /// ShopSeat, SeatId, ShopRole, RoleId&gt;()</c> is the same model.
    /// </para>
    /// <para>
    /// The fields, entities and rules the application adds to its classes are mapped by the toolkit's
    /// conventions, as on any aggregate; the ids by the application's generated converters. Register those
    /// and <c>AddDDDToolkitConventions()</c> in <c>ConfigureConventions</c>.
    /// </para>
    /// <para>
    /// Every table that has a tenant gets the tenant filter (<see cref="TenancyQueryFilter"/>), and the save
    /// check, which <c>UseDDDToolkit</c> adds, reads the tenant of every row written to them.
    /// </para>
    /// <para>
    /// The views of the read model carry access facts only, here as in a module's model: ids, keys, periods,
    /// statuses, a unit's parent, and a role's pack and keys. What a seat, a unit or a role is called is read
    /// from the tables themselves, by the directory (<c>TenancyDirectory</c>), which answers names by id.
    /// </para>
    /// <para>
    /// Pass the context's <c>Database</c> as well, <c>modelBuilder.AddTenancy(database: Database)</c>, and two
    /// rules the aggregates keep are kept by the database too, with filtered unique indexes in the provider's
    /// SQL: a tenant's organization has a single root, and a seat a single primary placement. They catch a write
    /// that goes past the aggregates, such as SQL of your own. Postgres, SQLite and SQL Server get them; another
    /// provider gets neither. Adding them to an existing database is a migration of your own, like any change to
    /// the model. A host that keeps Tenancy on Postgres under row level security
    /// (<c>DDDToolkit.Supporting.Tenancy.Postgres</c>) needs the first: its start-up check refuses a database
    /// without the unique index on a tenant's root.
    /// </para>
    /// <para>
    /// Every column, key and other index is named as the context names everything else, by Entity Framework or
    /// by a naming convention on its options. The condition of each filtered index reads its column by the name
    /// the model has for it when this is called, which a convention has given by then; a column of Tenancy's
    /// that the application names by hand, it names before this call.
    /// </para>
    /// </summary>
    /// <param name="modelBuilder">The model being built.</param>
    /// <param name="tables">The names of the tables, and of the two filtered indexes; <see cref="TenancyTableNames.Default"/> when not given.</param>
    /// <param name="database">The context's <c>Database</c>, read for its provider's name only; without it, no filtered indexes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="modelBuilder"/> is null.</exception>
    [TemplateRegistration]
    public static ModelBuilder AddTenancy<
        [TemplateType(typeof(TenantAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TTenant,
        [TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId,
        [TemplateType(typeof(OrganizationAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TOrganization,
        [TemplateType(typeof(OrganizationUnitAttribute<>), Take = TemplateArgumentKind.Type)] TUnit,
        [TemplateType(typeof(OrganizationUnitAttribute<>))] TUnitId,
        [TemplateType(typeof(SeatAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TSeat,
        [TemplateType(typeof(SeatAggregateAttribute<>))] TSeatId,
        [TemplateType(typeof(RoleAggregateAttribute<>), Take = TemplateArgumentKind.Type)] TRole,
        [TemplateType(typeof(RoleAggregateAttribute<>))] TRoleId>(
        this ModelBuilder modelBuilder,
        TenancyTableNames? tables = null,
        DatabaseFacade? database = null)
        where TTenant : TenantAggregate<TTenantId>
        where TOrganization : OrganizationAggregate<TTenantId, TUnit, TUnitId>
        where TUnit : OrganizationUnitEntity<TUnitId>
        where TSeat : SeatAggregate<TSeatId, TTenantId, TUnitId, TRoleId>
        where TRole : RoleAggregate<TRoleId, TTenantId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        tables ??= TenancyTableNames.Default;
        var unique = database is null ? null : TenancyMapping.UniqueFilters.For(database.ProviderName);

        modelBuilder.Entity<TTenant>(tenant => TenancyMapping.Tenants<TTenant, TTenantId>(tenant, tables));
        modelBuilder.Entity<TOrganization>(organization => TenancyMapping.Organizations<TOrganization, TTenantId, TUnit, TUnitId>(organization, tables, unique));
        modelBuilder.Entity<TSeat>(seat => TenancyMapping.Seats<TSeat, TSeatId, TTenantId, TUnitId, TRoleId>(seat, tables, unique));
        modelBuilder.Entity<TRole>(role => TenancyMapping.Roles<TRole, TRoleId, TTenantId>(role, tables));
        modelBuilder.Entity<SeatRight<TTenantId, TSeatId, TUnitId, TRoleId>>(right => TenancyMapping.Rights(right, tables, read: null));
        modelBuilder.Entity<OrganizationUnitPath<TTenantId, TUnitId>>(path => TenancyMapping.UnitPaths(path, tables, read: null));
        modelBuilder.Entity<TenancyAccessRevision<TTenantId>>(revision => TenancyMapping.AccessRevisions(revision, tables));

        // The rows the access questions read that are parts of an aggregate here: views over its tables, in the
        // same schema, so the questions read Tenancy's own context the way they read a consumer's.
        MapRows<TTenantId, TSeatId, TUnitId, TRoleId>(modelBuilder, tables, new TenancyMapping.ReadModel(Schema: null));

        // What the store asks a database that keeps the rights itself: rows of no table, read from its functions.
        modelBuilder.Entity<TenantAdministratorRow<TSeatId, TRoleId>>(TenancyMapping.TenantAdministrators);
        modelBuilder.Entity<MoveReach<TUnitId>>(TenancyMapping.MoveReaches);

        // And what system work that began in no tenant asks it: which tenants there are to visit.
        modelBuilder.Entity<TenantToSweepRow<TTenantId>>(TenancyMapping.TenantsToSweep);

        return modelBuilder;
    }

    /// <summary>
    /// Maps invitations into the context that maps Tenancy's tables, for an application that invites people into
    /// a tenant: the invitations, closed over the application's invitation class, and next to them the digests of
    /// their tokens, in a table of its own. Call it after <c>AddTenancy</c>, with the application's invitation class
    /// and its id:
    /// <code>
    /// modelBuilder.AddTenancy(database: Database);
    /// modelBuilder.AddTenancyInvitations&lt;ShopInvitation, InvitationId&gt;();
    /// </code>
    /// <para>
    /// That call is the one generated into the project that declares Tenancy's classes, closed over the tenant,
    /// unit, role and seat ids (<see cref="TemplateRegistrationAttribute"/>), or into a project of the same
    /// <c>[assembly: Module]</c> that declares none. The invitation class and its id are named in the call, since
    /// an application may have none: one that never calls it has no invitations and no tables for them.
    /// </para>
    /// <para>
    /// The digest of an invitation's token is kept apart from the invitation, so that listing invitations never
    /// reads it. On Postgres, <c>DDDToolkit.Supporting.Tenancy.Postgres</c> lets no caller read that table: a
    /// token is found through a function that answers which invitation a digest is for, and nothing else.
    /// </para>
    /// <para>
    /// Both tables get the tenant filter and the save check, as Tenancy's others do. The fields the application
    /// adds to its invitation class are mapped by the toolkit's conventions, as on any aggregate. Adding the
    /// tables to an existing database is a migration of your own.
    /// </para>
    /// </summary>
    /// <typeparam name="TInvitation">The application's invitation class, declared with <c>[InvitationAggregate&lt;TInvitationId&gt;]</c>.</typeparam>
    /// <typeparam name="TInvitationId">The application's invitation id.</typeparam>
    /// <typeparam name="TTenantId">The application's tenant id.</typeparam>
    /// <typeparam name="TUnitId">The application's unit id.</typeparam>
    /// <typeparam name="TRoleId">The application's role id.</typeparam>
    /// <typeparam name="TSeatId">The application's seat id.</typeparam>
    /// <param name="modelBuilder">The model being built.</param>
    /// <param name="tables">The names of the tables, the same as <c>AddTenancy</c> was given; <see cref="TenancyTableNames.Default"/> when not given.</param>
    /// <exception cref="ArgumentNullException"><paramref name="modelBuilder"/> is null.</exception>
    [TemplateRegistration]
    public static ModelBuilder AddTenancyInvitations<
        TInvitation,
        TInvitationId,
        [TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId,
        [TemplateType(typeof(OrganizationUnitAttribute<>))] TUnitId,
        [TemplateType(typeof(RoleAggregateAttribute<>))] TRoleId,
        [TemplateType(typeof(SeatAggregateAttribute<>))] TSeatId>(
        this ModelBuilder modelBuilder,
        TenancyTableNames? tables = null)
        where TInvitation : InvitationAggregate<TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>
        where TInvitationId : struct, IEntityId, IEquatable<TInvitationId>
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        tables ??= TenancyTableNames.Default;

        modelBuilder.Entity<TInvitation>(invitation => TenancyMapping.Invitations<TInvitation, TInvitationId, TTenantId, TUnitId, TRoleId, TSeatId>(invitation, tables));
        modelBuilder.Entity<TenancyInvitationDigest<TInvitationId, TTenantId>>(digest => TenancyMapping.InvitationDigests<TInvitation, TInvitationId, TTenantId>(digest, tables));

        // What the store asks a database that answers a token's digest through a function: a row of no table.
        modelBuilder.Entity<InvitationOfDigestRow<TTenantId, TInvitationId, TSeatId>>(TenancyMapping.InvitationsOfDigests);

        return modelBuilder;
    }

    /// <summary>
    /// Maps Tenancy's read model into a context that is not Tenancy's own, so its queries can ask Tenancy
    /// inside them: <c>answers.Over(thisContext).UnitsWhereIHold(key)</c> becomes a subquery of the module's
    /// own query, one statement and one round trip. The six rows the access questions read are keyless views
    /// over Tenancy's tables, their columns mapped exactly as <see cref="AddTenancy"/> maps them. Entity Framework
    /// creates no views, so this context's migrations never touch Tenancy's tables.
    /// <code>
    /// modelBuilder.AddTenancyReadModel&lt;TenantId, SeatId, OrganizationUnitId, RoleId&gt;();
    /// modelBuilder.Entity&lt;Project&gt;().ScopeToTenant(project =&gt; project.TenantId);
    /// </code>
    /// <para>
    /// The rows are access facts: ids, keys, periods, statuses, a unit's parent, and a role's pack and keys. None
    /// has a name, so a module that maps them cannot show what a seat, a unit or a role is called: it answers
    /// ids, and whoever shows them asks Tenancy's directory for the names, by id.
    /// <see cref="TenancyModel.ReadsBeyondAccessFacts"/> finds a module's model that maps more of Tenancy's than
    /// these rows.
    /// </para>
    /// <para>
    /// It needs Tenancy's tables in the same database: one database with a schema per module, or one SQLite
    /// file. Where they are apart, ask Tenancy's own context first and pass the answer in as a list.
    /// </para>
    /// <para>
    /// A module that sees only the application's ids, as above, writes them: nothing it sees says which of its ids
    /// are Tenancy's. Where the classes are visible, in the project that declares them or a project of the same
    /// module, <c>modelBuilder.AddTenancyReadModel()</c> is generated, closed over their four ids
    /// (<see cref="TemplateRegistrationAttribute"/>).
    /// </para>
    /// <para>
    /// Where the database keeps the rights itself and shows a seat only its own
    /// (<see cref="TenancyStoreOptions.DatabaseKeepsRights"/>, as on Postgres with
    /// <c>DDDToolkit.Supporting.Tenancy.Postgres</c>), a module reads no table of Tenancy's: map
    /// <see cref="AddTenancyReadFunctions{TTenantId, TSeatId, TUnitId, TRoleId}"/> there instead. The questions
    /// refuse a context of a module that maps these views on such a database.
    /// </para>
    /// </summary>
    /// <param name="modelBuilder">The consumer's model being built.</param>
    /// <param name="schema">The schema Tenancy's tables are in, or <see langword="null"/> for this model's default schema.</param>
    /// <param name="tables">The tables' names, the same as Tenancy's context was given; <see cref="TenancyTableNames.Default"/> when not given.</param>
    /// <exception cref="ArgumentNullException"><paramref name="modelBuilder"/> is null.</exception>
    [TemplateRegistration]
    public static ModelBuilder AddTenancyReadModel<
        [TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId,
        [TemplateType(typeof(SeatAggregateAttribute<>))] TSeatId,
        [TemplateType(typeof(OrganizationUnitAttribute<>))] TUnitId,
        [TemplateType(typeof(RoleAggregateAttribute<>))] TRoleId>(
        this ModelBuilder modelBuilder,
        string? schema = "tenancy",
        TenancyTableNames? tables = null)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        MapReadModel<TTenantId, TSeatId, TUnitId, TRoleId>(modelBuilder, tables ?? TenancyTableNames.Default, new TenancyMapping.ReadModel(schema));
        return modelBuilder;
    }

    /// <summary>
    /// Maps Tenancy's read model into a context that is not Tenancy's own as Tenancy's read functions, which a
    /// package for one database writes into it (<c>DDDToolkit.Supporting.Tenancy.Postgres</c> does): the six rows
    /// the access questions read, each from the function of its name (<see cref="TenancyFunctionNames"/>) in
    /// <paramref name="schema"/>, under the column names that function answers. The module's model then names no
    /// table of Tenancy's, only functions, which are what Tenancy offers other modules; and
    /// <c>answers.Over(thisContext)</c> works exactly as over the views of
    /// <see cref="AddTenancyReadModel{TTenantId, TSeatId, TUnitId, TRoleId}"/>: a question is a subquery of the
    /// module's own query, one statement and one round trip.
    /// <code>
    /// if (Database.IsNpgsql())
    /// {
    ///     modelBuilder.AddTenancyReadFunctions&lt;TenantId, SeatId, OrganizationUnitId, RoleId&gt;("tenancy");
    /// }
    /// else
    /// {
    ///     modelBuilder.AddTenancyReadModel&lt;TenantId, SeatId, OrganizationUnitId, RoleId&gt;("tenancy");
    /// }
    /// </code>
    /// <para>
    /// The functions run as their caller, so the database decides their rows as it decides the tables': a seat is
    /// answered its own rights and the rest of its tenant, system work in a tenant all of that tenant. Every row
    /// keeps the tenant filter here as well. Entity Framework creates no functions, so this context's migrations
    /// never touch them, and Tenancy's own naming of its tables and columns, and how it stores a status, never
    /// reach this model: the functions answer under names and values of their own.
    /// </para>
    /// <para>
    /// As with the views, the rows are access facts and none has a name: the functions answer no column a seat, a
    /// unit or a role is called by, and names are asked of Tenancy's directory, by id.
    /// </para>
    /// <para>
    /// Use it where the database keeps the rights (<see cref="TenancyStoreOptions.DatabaseKeepsRights"/>); on
    /// a database without such functions, SQLite among them, map
    /// <see cref="AddTenancyReadModel{TTenantId, TSeatId, TUnitId, TRoleId}"/>. A context's model is built once
    /// for the database it runs on, so choosing between the two in <c>OnModelCreating</c> by the provider, as
    /// above, is safe for a context taken from a pool as well.
    /// </para>
    /// <para>
    /// As for the views, a module that sees only the ids writes them, and where the classes are visible
    /// <c>modelBuilder.AddTenancyReadFunctions()</c> is generated, closed over their four ids.
    /// </para>
    /// </summary>
    /// <param name="modelBuilder">The consumer's model being built.</param>
    /// <param name="schema">The schema Tenancy's functions are in, that of the context that maps Tenancy's tables; <see langword="null"/> for this model's default schema.</param>
    /// <exception cref="ArgumentNullException"><paramref name="modelBuilder"/> is null.</exception>
    [TemplateRegistration]
    public static ModelBuilder AddTenancyReadFunctions<
        [TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId,
        [TemplateType(typeof(SeatAggregateAttribute<>))] TSeatId,
        [TemplateType(typeof(OrganizationUnitAttribute<>))] TUnitId,
        [TemplateType(typeof(RoleAggregateAttribute<>))] TRoleId>(
        this ModelBuilder modelBuilder,
        string? schema = "tenancy")
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Only a view is named after the table it reads; a function has the name Tenancy gives it.
        MapReadModel<TTenantId, TSeatId, TUnitId, TRoleId>(modelBuilder, TenancyTableNames.Default, new TenancyMapping.ReadModel(schema, ThroughFunctions: true));
        modelBuilder.HasAnnotation(TenancyModel.ReadFunctionsAnnotation, true);
        return modelBuilder;
    }

    /// <summary>
    /// Keeps an entity of any module to the current Tenancy caller's tenant. It adds the tenant filter, a named
    /// query filter that sits next to the entity's other named filters rather than replacing them, and marks
    /// the entity for Tenancy's save check, which <c>UseDDDToolkit</c> adds: a row of another tenant is refused before anything is
    /// written, whoever loaded or made it.
    /// <code>
    /// modelBuilder.Entity&lt;Project&gt;().ScopeToTenant(project =&gt; project.TenantId);
    /// </code>
    /// <para>
    /// The tenant of a row is fixed once the row is there (<c>IsFixedAfterInsert</c>): a save that changed it is
    /// refused, by the save check first, and where privileges are written from the policies its column is left
    /// out of <c>UPDATE</c>.
    /// </para>
    /// <para>
    /// Owned types are kept through their owner: scope the owner. The context must have the save check, which
    /// <c>UseDDDToolkit</c> adds (<c>UseTenancy</c> after <c>UseDDDToolkitCore</c>), and
    /// <see cref="TenancyChecks.EnsureWired"/> checks it.
    /// </para>
    /// </summary>
    /// <typeparam name="TEntity">The entity.</typeparam>
    /// <typeparam name="TTenantId">The application's tenant id.</typeparam>
    /// <param name="entity">The entity's builder.</param>
    /// <param name="tenant">The property that holds the entity's tenant, as <c>entity =&gt; entity.TenantId</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entity"/> or <paramref name="tenant"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="tenant"/> is not a property of the entity.</exception>
    public static EntityTypeBuilder<TEntity> ScopeToTenant<TEntity, TTenantId>(
        this EntityTypeBuilder<TEntity> entity,
        Expression<Func<TEntity, TTenantId>> tenant)
        where TEntity : class
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(tenant);

        return entity.ScopeToTenantThrough(tenant, PropertyOf(tenant));
    }

    /// <summary>
    /// <see cref="ScopeToTenant"/> with the property named: a shadow property, which Tenancy's own mapping reads
    /// as <c>EF.Property&lt;TTenantId&gt;(row, name)</c> and which the public overload has no member to find it by.
    /// </summary>
    internal static EntityTypeBuilder<TEntity> ScopeToTenantThrough<TEntity, TTenantId>(
        this EntityTypeBuilder<TEntity> entity,
        Expression<Func<TEntity, TTenantId>> tenant,
        string property)
        where TEntity : class
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
    {
        entity.HasQueryFilter(TenancyQueryFilter.Name, TenancyQueryFilter.Of(tenant));
        entity.HasAnnotation(TenancyMapping.TenantPropertyAnnotation, property);

        // A context of this model cannot do without the save check: where it lacks it, because nothing registered
        // Tenancy or the context was given the base alone, its first save is refused rather than written unchecked.
        entity.Metadata.Model.RequireContextPart(TenancySaveInterceptor.PartName, typeof(TenancySaveInterceptor), TenancyRegistration, "options.UseTenancy(serviceProvider)");

        // A row never moves to another tenant: Entity Framework refuses a save that changed the property, and the
        // privileges an export writes from the policies leave its column out of UPDATE.
        entity.Property(property).IsFixedAfterInsert();
        return entity;
    }

    /// <summary>A consumer's read model: the six rows the access questions read, from views or from Tenancy's functions.</summary>
    private static void MapReadModel<TTenantId, TSeatId, TUnitId, TRoleId>(ModelBuilder modelBuilder, TenancyTableNames tables, TenancyMapping.ReadModel read)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    {
        modelBuilder.Entity<SeatRight<TTenantId, TSeatId, TUnitId, TRoleId>>(right => TenancyMapping.Rights(right, tables, read));
        modelBuilder.Entity<OrganizationUnitPath<TTenantId, TUnitId>>(path => TenancyMapping.UnitPaths(path, tables, read));
        MapRows<TTenantId, TSeatId, TUnitId, TRoleId>(modelBuilder, tables, read);
    }

    private static void MapRows<TTenantId, TSeatId, TUnitId, TRoleId>(ModelBuilder modelBuilder, TenancyTableNames tables, TenancyMapping.ReadModel read)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    {
        modelBuilder.Entity<OrganizationUnitRow<TTenantId, TUnitId>>(unit => TenancyMapping.UnitRows(unit, tables, read));
        modelBuilder.Entity<RoleRow<TTenantId, TRoleId>>(role => TenancyMapping.RoleRows(role, tables, read));
        modelBuilder.Entity<PlacementRow<TSeatId, TUnitId>>(placement => TenancyMapping.PlacementRows<TTenantId, TSeatId, TUnitId>(placement, tables, read));
        modelBuilder.Entity<SeatRow<TTenantId, TSeatId>>(seat => TenancyMapping.SeatRows(seat, tables, read));

        // Who holds a key at a unit, where the database answers that itself: a row of no table or view, read
        // from Tenancy's function of that name, in the schema the rest of the read model is in.
        modelBuilder.Entity<SeatHolderRow<TSeatId>>(holder => TenancyMapping.SeatHolders(holder, read));
    }

    /// <summary>The name of the property <paramref name="tenant"/> reads, which the save check reads back.</summary>
    private static string PropertyOf<TEntity, TTenantId>(Expression<Func<TEntity, TTenantId>> tenant)
    {
        var body = tenant.Body is UnaryExpression { NodeType: ExpressionType.Convert } convert ? convert.Operand : tenant.Body;
        var owner = body is MemberExpression { Expression: { } target } ? Unconvert(target) : null;

        return body is MemberExpression { Member: PropertyInfo property } && owner == tenant.Parameters[0]
            ? property.Name
            : throw new ArgumentException(
                "Name the property that holds the tenant, as 'entity => entity.TenantId'; '" + tenant + "' is not a property of the entity.",
                nameof(tenant));
    }

    /// <summary>A parameter the compiler converted to the class that declares the member, as a base class's property on a type parameter.</summary>
    private static Expression Unconvert(Expression expression)
        => expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.TypeAs } convert ? Unconvert(convert.Operand) : expression;
}
