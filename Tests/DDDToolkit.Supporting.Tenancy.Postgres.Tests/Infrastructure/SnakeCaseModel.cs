using System.Text.RegularExpressions;
using DDDToolkit.EntityFramework.Outbox;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.TestHost.Domain;
using DDDToolkit.Supporting.Tenancy.TestHost.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure;

/// <summary>
/// The model of a host that keeps its database in snake_case, built over the TestHost's classes without changing
/// them: it stands where Entity Framework calls a context's <c>OnModelCreating</c>, and for Tenancy's context
/// writes what such a host writes there. The naming convention on the context's options names everything that
/// was left to it, the columns, keys and indexes among them; the tables Tenancy names itself are named with
/// <see cref="TenancyTableNames.SnakeCase"/>; and every enum is stored in snake_case, after <c>AddTenancy</c>,
/// since what is configured last holds. A module's context is built as it builds itself: the convention names its
/// tables, and Tenancy's functions answer it under names and values of their own.
/// </summary>
/// <remarks>
/// It replaces a service of the context's options, so a context under this naming has a model of its own, built
/// once and cached apart from the model the same context class has under the default names.
/// </remarks>
internal class SnakeCaseModel(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    /// <summary>The outbox's table, which <c>AddDomainEventOutbox</c> names itself too.</summary>
    public const string OutboxTable = "outbox_messages";

    /// <summary>The access history's table, named by hand as the outbox is: it is the application's to name.</summary>
    public const string EventLogTable = "event_log";

    /// <summary>Whether the host names its model itself, where no naming convention is on its options.</summary>
    protected virtual bool ByHand => false;

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        if (context is TestTenancyContext)
        {
            if (ByHand)
            {
                // The two columns the conditions of Tenancy's filtered indexes read are named first: a condition
                // is SQL, written when AddTenancy maps the index, with the column's name as it is then.
                modelBuilder.Entity<HostOrganization>().OwnsMany(organization => organization.Units)
                    .Property(unit => unit.ParentId).HasColumnName(SnakeCaseNaming.Of(nameof(HostUnit.ParentId)));
                modelBuilder.Entity<HostSeat>().OwnsMany(seat => seat.Placements)
                    .Property(placement => placement.IsPrimary).HasColumnName(SnakeCaseNaming.Of(nameof(Placement<SeatId, OrganizationUnitId, RoleId>.IsPrimary)));
            }

            modelBuilder.HasDefaultSchema(TestTenancyContext.Schema);
            modelBuilder.AddTenancy<HostTenant, TenantId, HostOrganization, HostUnit, OrganizationUnitId, HostSeat, SeatId, HostRole, RoleId>(
                TenancyTableNames.SnakeCase,
                context.Database);
            modelBuilder.AddTenancyInvitations<HostInvitation, InvitationId, TenantId, OrganizationUnitId, RoleId, SeatId>(TenancyTableNames.SnakeCase);
            modelBuilder.AddDomainEventOutbox(context.Database, tableName: OutboxTable);
            modelBuilder.AddTenancyEventLogTable<TenantId>(context.Database, tableName: EventLogTable);
        }
        else
        {
            base.Customize(modelBuilder, context);
        }

        if (ByHand)
        {
            SnakeCaseNaming.Name(modelBuilder);
        }

        SnakeCaseNaming.StoreEnums(modelBuilder);
    }
}

/// <summary>
/// <see cref="SnakeCaseModel"/> for a host without a naming convention, which names its model in a loop of its own
/// once everything is mapped. What the suite runs under where the convention's package cannot be restored, and
/// what a test compares the convention's names with where it can.
/// </summary>
internal sealed class SnakeCaseByHandModel(ModelCustomizerDependencies dependencies) : SnakeCaseModel(dependencies)
{
    protected override bool ByHand => true;
}

/// <summary>What a host that keeps its database in snake_case does to a model, beyond naming Tenancy's tables.</summary>
internal static partial class SnakeCaseNaming
{
    /// <summary><paramref name="name"/> in snake_case: <c>PlacedAt</c> as <c>placed_at</c>, <c>PK_seats</c> as <c>pk_seats</c>.</summary>
    public static string Of(string name) => WordStarts().Replace(name, "_").ToLowerInvariant();

    /// <summary>
    /// Stores the enums of <paramref name="modelBuilder"/>'s model as snake_case text, <c>Active</c> as
    /// <c>active</c>, where <c>AddTenancy</c> stores their names: how a host gives Tenancy's statuses a spelling of
    /// its own. Every enum property that is stored as its name gets the converter, the rows a context reads Tenancy's
    /// tables through included, since they read the same columns. A property a context stores its own way keeps
    /// that, and a row read from one of Tenancy's functions keeps reading names, which is what the functions answer
    /// whatever the tables store.
    /// </summary>
    public static void StoreEnums(ModelBuilder modelBuilder)
    {
        var stored = modelBuilder.Model.GetEntityTypes()
            .Where(entityType => entityType.GetFunctionName() is null)
            .SelectMany(entityType => entityType.GetProperties())
            .Where(property => property.ClrType.IsEnum && property.GetValueConverter() is null && property.GetProviderClrType() == typeof(string));

        foreach (var property in stored)
        {
            property.SetValueConverter((ValueConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(property.ClrType))!);
        }
    }

    /// <summary>
    /// Names in snake_case every table, column, key, foreign key and index of <paramref name="modelBuilder"/>'s
    /// model that nobody gave a name: what a naming convention does, done by hand at the end of
    /// <c>OnModelCreating</c>. A name given explicitly stays, as it does under a convention: the tables Tenancy
    /// names, its two filtered indexes, and the columns its functions answer under.
    /// </summary>
    public static void Name(ModelBuilder modelBuilder)
    {
        var entityTypes = modelBuilder.Model.GetEntityTypes().ToList();
        foreach (var entityType in entityTypes)
        {
            if (entityType.GetTableName() is { } table && !Given(((IConventionEntityType)entityType).GetTableNameConfigurationSource()))
            {
                entityType.SetTableName(Of(table));
            }

            foreach (var property in entityType.GetProperties().Where(property => !Given(((IConventionProperty)property).GetColumnNameConfigurationSource())))
            {
                property.SetColumnName(Of(property.GetColumnName()));
            }
        }

        // After the tables and the columns: the names of these are made of theirs.
        foreach (var entityType in entityTypes.Where(entityType => entityType.GetTableName() is not null))
        {
            foreach (var key in entityType.GetKeys().Where(key => !Given(((IConventionKey)key).GetNameConfigurationSource())))
            {
                key.SetName(Of(key.GetDefaultName()!));
            }

            foreach (var foreignKey in entityType.GetForeignKeys().Where(foreignKey => !Given(((IConventionForeignKey)foreignKey).GetConstraintNameConfigurationSource())))
            {
                foreignKey.SetConstraintName(Of(foreignKey.GetDefaultName()!));
            }

            foreach (var index in entityType.GetIndexes().Where(index => !Given(((IConventionIndex)index).GetDatabaseNameConfigurationSource())))
            {
                index.SetDatabaseName(Of(index.GetDefaultDatabaseName()!));
            }
        }
    }

    private static bool Given(ConfigurationSource? source) => source == ConfigurationSource.Explicit;

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex WordStarts();

    private sealed class Converter<TEnum>() : ValueConverter<TEnum, string>(
        value => Of(value.ToString()),
        text => Enum.Parse<TEnum>(text.Replace("_", string.Empty), true))
        where TEnum : struct, Enum;
}
