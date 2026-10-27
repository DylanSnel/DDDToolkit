using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework;

/// <summary>Asks Tenancy inside a module's own queries.</summary>
public static class TenancyEntityFrameworkExtensions
{
    /// <summary>
    /// Tenancy's queries for the current caller over <paramref name="context"/>, at this moment: Tenancy's own context,
    /// or a module's that maps the read model with <c>AddTenancyReadModel</c> or, where the database keeps the rights,
    /// with <c>AddTenancyReadFunctions</c>. What they return are queries of that context, so composed into the module's
    /// own query they become subqueries of one statement:
    /// <code>
    /// var held = db.Tenancy().UnitsWhereIHold(ProjectPermissions.Edit);
    /// var mine = db.Projects.Where(project =&gt; held.Contains(project.UnitId));
    /// </code>
    /// <para>
    /// Your code calls <c>db.Tenancy()</c> without type arguments. The generator writes it closed over your ids, into the
    /// project that declares Tenancy's classes, or the project of the module that holds the context, and into the
    /// project of another module that holds its context, below the one that says
    /// <c>[assembly: TenancyIds&lt;TenantId, SeatId, OrganizationUnitId, RoleId&gt;]</c>. This method can be called as
    /// well, with the four ids.
    /// </para>
    /// <para>
    /// The caller and the moment are fixed once, so every answer of one set of queries agrees with the others. The
    /// catalogue and the clock come from the application's services, through the context's options: the services
    /// <c>UseDDDToolkit(serviceProvider)</c> wired Tenancy's part with, or else those <c>AddDbContext</c>, a context pool or a
    /// factory made the context with. The queries keep nothing of the context but the reference: ask over the context that
    /// runs the query they are composed into.
    /// </para>
    /// </summary>
    /// <typeparam name="TTenantId">The application's tenant id.</typeparam>
    /// <typeparam name="TSeatId">The application's seat id.</typeparam>
    /// <typeparam name="TUnitId">The application's unit id.</typeparam>
    /// <typeparam name="TRoleId">The application's role id.</typeparam>
    /// <param name="context">The context to ask over.</param>
    /// <returns>
    /// The queries for the caller of the current flow of work at this moment, as queries of <paramref name="context"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="context"/> is neither wired with <c>UseDDDToolkit</c> nor made by the application's services, or
    /// Tenancy is not registered in them. Or
    /// the database keeps the rights (<see cref="TenancyStoreOptions.DatabaseKeepsRights"/>), and
    /// <paramref name="context"/> is a module's that maps the read model as views over Tenancy's tables: there a module
    /// reads no table of Tenancy's, and asks its functions, mapped with <c>AddTenancyReadFunctions</c>.
    /// </exception>
    [TemplateRegistration]
    public static ITenancyQueries<TTenantId, TSeatId, TUnitId, TRoleId> Tenancy<
        [TemplateType(typeof(TenantAggregateAttribute<>))] TTenantId,
        [TemplateType(typeof(SeatAggregateAttribute<>))] TSeatId,
        [TemplateType(typeof(OrganizationUnitAttribute<>))] TUnitId,
        [TemplateType(typeof(RoleAggregateAttribute<>))] TRoleId>(
        this DbContext context)
        where TTenantId : struct, IEntityId, IEquatable<TTenantId>
        where TSeatId : struct, IEntityId, IEquatable<TSeatId>
        where TUnitId : struct, IEntityId, IEquatable<TUnitId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    {
        ArgumentNullException.ThrowIfNull(context);

        // The services Tenancy's part was wired with, which every context UseDDDToolkit wired carries in its save check;
        // else those the context was made by.
        var options = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>();
        var services = options?.Interceptors?.OfType<TenancySaveInterceptor>().FirstOrDefault()?.Services
            ?? options?.ApplicationServiceProvider
            ?? throw new InvalidOperationException(
                "'" + context.GetType().Name + "' is not wired with UseDDDToolkit(serviceProvider), nor made by the application's services, and db.Tenancy() takes "
                + "Tenancy's catalogue and clock from them. Wire it with options.UseDDDToolkit(serviceProvider), or register it with AddDbContext<" + context.GetType().Name + ">.");

        var catalogue = services.GetService<TenancyCatalogue>()
            ?? throw new InvalidOperationException(
                "The services that made '" + context.GetType().Name + "' have no Tenancy catalogue. Register Tenancy with services.AddTenancy<TContext>() first.");

        // The access manager closed over the ids, made for this one question: the catalogue and the clock are the
        // application's, and the caller is the flow of work's, read when the queries are made.
        var access = new TenancyAccessManager<TTenantId, TSeatId, TUnitId, TRoleId>(catalogue, services.GetService<TimeProvider>() ?? TimeProvider.System, services);
        return access.In(new EfTenancyReadSource<TTenantId, TSeatId, TUnitId, TRoleId>(context), EfQueryExecutor.Instance);
    }
}
