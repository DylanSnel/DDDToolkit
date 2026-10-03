using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.Supporting.Tenancy.TestHost.Converters;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// What <c>AddTenancyReadFunctions</c> puts in a module's model: the six rows of the read model, each read from
/// Tenancy's function of its name rather than from a view over a table of Tenancy's, under the column names the
/// function answers; and the questions' refusal of a module that maps the views where the database keeps the rights.
/// The rows are access facts, whichever way they are mapped: no mapping gives one a name to read. The mapping is
/// the same for every provider, and no test here connects to one.
/// </summary>
public sealed class ReadFunctionModelTests
{
    /// <summary>The rows of the read model, with the function that answers each.</summary>
    private static readonly Dictionary<Type, string> Functions = new()
    {
        [typeof(SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>)] = TenancyFunctionNames.CallerRights,
        [typeof(OrganizationUnitPath<TenantId, OrganizationUnitId>)] = TenancyFunctionNames.TenantUnitPaths,
        [typeof(OrganizationUnitRow<TenantId, OrganizationUnitId>)] = TenancyFunctionNames.TenantUnits,
        [typeof(RoleRow<TenantId, RoleId>)] = TenancyFunctionNames.TenantRoles,
        [typeof(PlacementRow<SeatId, OrganizationUnitId>)] = TenancyFunctionNames.TenantPlacements,
        [typeof(SeatRow<TenantId, SeatId>)] = TenancyFunctionNames.TenantSeats,
    };

    /// <summary>The properties each row of the read model has in a model, the placements' shadow tenant included: no name among them.</summary>
    private static readonly Dictionary<Type, string[]> Properties = new()
    {
        [typeof(SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>)] = ["TenantId", "SeatId", "UnitId", "RoleId", "Key", "StartsAt", "EndsAt"],
        [typeof(OrganizationUnitPath<TenantId, OrganizationUnitId>)] = ["TenantId", "AncestorId", "DescendantId", "Distance"],
        [typeof(OrganizationUnitRow<TenantId, OrganizationUnitId>)] = ["Id", "TenantId", "ParentId", "Status"],
        [typeof(RoleRow<TenantId, RoleId>)] = ["Id", "TenantId", "FromPack", "Status", "Keys"],
        [typeof(PlacementRow<SeatId, OrganizationUnitId>)] = ["SeatId", "UnitId", "IsPrimary", "TenantId"],
        [typeof(SeatRow<TenantId, SeatId>)] = ["Id", "TenantId", "Status"],
    };

    public static TheoryData<string> Providers => ["Npgsql", "Sqlite", "SqlServer"];

    /// <summary>The three ways the read model gets into a model.</summary>
    public static TheoryData<string> Mappings => ["AddTenancy", "AddTenancyReadModel", "AddTenancyReadFunctions"];

    [Theory]
    [MemberData(nameof(Mappings))]
    public void The_read_rows_map_no_name_column(string mapping)
    {
        using DbContext context = mapping switch
        {
            "AddTenancy" => new OwnTables(On<OwnTables>("Npgsql")),
            "AddTenancyReadModel" => new ThroughViews(On<ThroughViews>("Npgsql")),
            _ => new ThroughFunctions(On<ThroughFunctions>("Npgsql")),
        };

        foreach (var (row, properties) in Properties)
        {
            var entityType = context.Model.FindEntityType(row);
            entityType.Should().NotBeNull("{0} maps {1}", mapping, row.Name);
            entityType!.GetProperties().Select(property => property.Name).Should().BeEquivalentTo(properties, "{0} maps exactly the access facts of {1}", mapping, row.Name);
            entityType.GetNavigations().Should().BeEmpty();

            // Whatever the columns are called, none is one a seat, a unit or a role is called by.
            var columns = entityType.GetProperties().Select(ColumnOf).ToArray();
            columns.Should().NotContain(["Name", "DisplayName", "Kind", "Description", "Identity"], "{0} reads no text that is shown to people through {1}", mapping, row.Name);
        }

        // The tables keep their names, where the model has the tables: only the rows went without.
        if (mapping == "AddTenancy")
        {
            context.Model.FindEntityType(typeof(HostSeat))!.FindProperty(nameof(HostSeat.DisplayName))!.GetMaxLength().Should().Be(HostSeat.MaxDisplayNameLength);
            context.Model.FindEntityType(typeof(HostRole))!.FindProperty(nameof(HostRole.Name))!.GetMaxLength().Should().Be(HostRole.MaxNameLength);
            context.Model.FindEntityType(typeof(HostUnit))!.FindProperty(nameof(HostUnit.Name))!.GetMaxLength().Should().Be(HostUnit.MaxNameLength);
            context.Model.FindEntityType(typeof(HostUnit))!.FindProperty(nameof(HostUnit.Kind))!.GetMaxLength().Should().Be(HostUnit.MaxKindLength);
        }

        static string ColumnOf(IProperty property)
        {
            var entityType = property.DeclaringType.ContainingEntityType;
            var mapped = entityType.GetFunctionName() is { } function ? StoreObjectIdentifier.DbFunction(function)
                : entityType.GetViewName() is { } view ? StoreObjectIdentifier.View(view, entityType.GetViewSchema())
                : StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());
            return property.GetColumnName(mapped)!;
        }
    }

    [Fact]
    public void The_function_rows_answer_ids_only()
    {
        using var tenancy = new OwnTables(On<OwnTables>("Npgsql"));
        using var module = new ThroughFunctions(On<ThroughFunctions>("Npgsql"));

        // The rows the package's own functions answer are internal classes, so they are found in the models by name:
        // what the store asks about other seats' rights, and which tenants system work visits.
        PropertiesOfTheRowNamed(tenancy.Model, "TenantAdministratorRow").Should().BeEquivalentTo(["SeatId", "RoleId"]);
        PropertiesOfTheRowNamed(tenancy.Model, "TenantToSweepRow").Should().BeEquivalentTo(["TenantId"]);
        PropertiesOfTheRowNamed(tenancy.Model, "SeatHolderRow").Should().BeEquivalentTo(["SeatId"]);
        PropertiesOfTheRowNamed(module.Model, "SeatHolderRow").Should().BeEquivalentTo(["SeatId"]);
        PropertiesOfTheRowNamed(tenancy.Model, "MoveReach").Should().BeEquivalentTo(["UnitId", "Key", "EndsAt", "Parent", "OfCaller"]);

        // And these are all of them: every type either model maps to nothing is one of the four.
        tenancy.Model.GetEntityTypes().Concat(module.Model.GetEntityTypes())
            .Where(entityType => entityType.GetTableName() is null && entityType.GetViewName() is null && entityType.GetFunctionName() is null)
            .Select(entityType => entityType.ClrType.Name.Split('`')[0])
            .Distinct()
            .Should().BeEquivalentTo(["TenantAdministratorRow", "TenantToSweepRow", "SeatHolderRow", "MoveReach"]);

        static IEnumerable<string> PropertiesOfTheRowNamed(IModel model, string name)
            => model.GetEntityTypes().Single(entityType => entityType.ClrType.Name.Split('`')[0] == name).GetProperties().Select(property => property.Name);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void AddTenancyReadFunctions_maps_the_six_rows_to_tenancys_functions(string provider)
    {
        using var context = new ThroughFunctions(On<ThroughFunctions>(provider));
        var model = context.Model;

        TenancyModel.ReadsThroughFunctions(model).Should().BeTrue();
        model.GetEntityTypes().Where(entityType => entityType.GetFunctionName() is not null).Select(entityType => entityType.ClrType).Should().BeEquivalentTo(Functions.Keys);

        foreach (var (row, name) in Functions)
        {
            var entityType = model.FindEntityType(row)!;
            entityType.GetFunctionName().Should().Be(name, "{0} is read from Tenancy's function", row.Name);
            var function = model.FindDbFunction(name)!;
            function.Name.Should().Be(name);
            function.Schema.Should().Be(TestTenancyContext.Schema, "the function is Tenancy's, in the schema of its tables");
            function.Parameters.Should().BeEmpty();

            // No table, no view and no key: nothing a migration creates, and nothing of Tenancy's tables.
            entityType.GetTableName().Should().BeNull();
            entityType.GetViewName().Should().BeNull();
            entityType.FindPrimaryKey().Should().BeNull();
            TenancyModel.TableOf(entityType).Should().BeNull();

            // Every column is the function's, named as the row's property is, the placements' tenant included.
            var answered = StoreObjectIdentifier.DbFunction(function.ModelName);
            entityType.GetProperties().Should().OnlyContain(property => property.GetColumnName(answered) == property.Name);

            // And the row is kept to the caller's tenant here as well, whatever the function answers.
            TenancyModel.TenantPropertyOf(entityType)!.Name.Should().Be("TenantId");
            entityType.FindDeclaredQueryFilter(TenancyQueryFilter.Name).Should().NotBeNull();

            // A status is read by the name of its enum member, and an instant as the UTC moment it is.
            foreach (var property in entityType.GetProperties().Where(property => property.ClrType.IsEnum))
            {
                property.GetTypeMapping().Converter!.ProviderClrType.Should().Be<string>("{0}.{1} is answered as a name", row.Name, property.Name);
            }

            foreach (var property in entityType.GetProperties().Where(property => (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(DateTimeOffset)))
            {
                (Nullable.GetUnderlyingType(property.GetTypeMapping().Converter!.ProviderClrType) ?? property.GetTypeMapping().Converter!.ProviderClrType).Should().Be<DateTime>();
            }
        }

        model.FindEntityType(typeof(PlacementRow<SeatId, OrganizationUnitId>))!.FindProperty("TenantId")!.IsShadowProperty().Should().BeTrue();

        // Besides the six, who holds a key at a unit: mapped to nothing, and asked of its function when the question is.
        model.GetEntityTypes().Where(entityType => entityType.GetFunctionName() is null).Should().ContainSingle()
            .Which.Should().Match<IEntityType>(holder => holder.GetTableName() == null && holder.GetViewName() == null);

        // Nothing here is created by this context: no table, no view, no function.
        var script = context.Database.GenerateCreateScript();
        script.Should().NotContain("CREATE TABLE").And.NotContain("CREATE VIEW").And.NotContain("CREATE FUNCTION");
    }

    [Fact]
    public void The_functions_are_read_in_the_schema_named_or_in_the_models_own()
    {
        using var named = new ThroughFunctions(On<ThroughFunctions>("Npgsql"));
        using var own = new ThroughFunctionsOfItsOwnSchema(On<ThroughFunctionsOfItsOwnSchema>("Npgsql"));

        named.Model.GetDbFunctions().Should().HaveCount(6).And.OnlyContain(function => function.Schema == TestTenancyContext.Schema);
        own.Model.GetDbFunctions().Should().HaveCount(6).And.OnlyContain(function => function.Schema == TestWidgetContext.Schema, "without a schema, the functions are looked for where the model's own tables are");

        named.Set<SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>>().ToQueryString().Should().Contain("FROM tenancy.caller_rights() AS");
        own.Set<SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>>().ToQueryString().Should().Contain("FROM widgets.caller_rights() AS");

        // On Npgsql a role's keys are the array the function answers, asked in SQL.
        named.Model.FindEntityType(typeof(RoleRow<TenantId, RoleId>))!.FindProperty(nameof(RoleRow<TenantId, RoleId>.Keys))!.GetColumnType().Should().Be("text[]");
    }

    [Fact]
    public void Function_columns_keep_their_names_under_a_naming_convention_of_the_contexts()
    {
        using var functions = new LowerCasedThroughFunctions(On<LowerCasedThroughFunctions>("Npgsql"));
        using var views = new LowerCasedThroughViews(On<LowerCasedThroughViews>("Npgsql"));

        // The convention reaches the columns of a view, which are the table's and follow the context's naming as the
        // table's do: Tenancy's context under the same convention has them under the same names.
        var right = views.Model.FindEntityType(typeof(SeatRight<TenantId, SeatId, OrganizationUnitId, RoleId>))!;
        right.GetProperties().Select(property => property.GetColumnName(StoreObjectIdentifier.View(right.GetViewName()!, right.GetViewSchema())))
            .Should().OnlyContain(column => column == column!.ToLowerInvariant());

        // A function answers under names of its own, whatever anything is called: the mapping names every column itself.
        foreach (var (row, name) in Functions)
        {
            functions.Model.FindEntityType(row)!.GetProperties().Select(property => property.GetColumnName(StoreObjectIdentifier.DbFunction(name)))
                .Should().Equal(functions.Model.FindEntityType(row)!.GetProperties().Select(property => property.Name), "{0} answers under the properties' names", name);
        }

        functions.Set<RoleRow<TenantId, RoleId>>().ToQueryString().Should().Contain("\"FromPack\"").And.NotContain("frompack");
    }

    [Fact]
    public void The_views_and_tenancys_own_tables_are_not_read_through_functions()
    {
        using var services = new TestServices();
        using var scope = services.Scope();

        TenancyModel.ReadsThroughFunctions(scope.ServiceProvider.Widgets().Model).Should().BeFalse("it maps the views");
        TenancyModel.ReadsThroughFunctions(scope.ServiceProvider.Tenancy().Model).Should().BeFalse("it maps the tables");
        scope.ServiceProvider.Widgets().Model.GetDbFunctions().Should().BeEmpty();
    }

    [Fact]
    public async Task The_questions_refuse_a_modules_views_where_the_database_keeps_the_rights()
    {
        // The option a package for one database turns on: there a seat reads only its own rights, and a module reads
        // Tenancy through its functions.
        using var keeping = new TestServices(configure: services => services.Configure<TenancyStoreOptions>(options => options.DatabaseKeepsRights = true));
        await keeping.BySystemIn(new TenantId(1), scoped =>
        {
            var answers = scoped.Answers();

            FluentActions.Invoking(() => answers.Over(scoped.Widgets())).Should().Throw<InvalidOperationException>().WithMessage(
                "'TestWidgetContext' maps Tenancy's read model as views over Tenancy's tables, with AddTenancyReadModel, and the database keeps the rights*" +
                "no module reads Tenancy's tables*Map the read model with modelBuilder.AddTenancyReadFunctions<TTenantId, TSeatId, TUnitId, TRoleId>(schema)*");
            FluentActions.Invoking(() => new EfTenancyReadSource<TenantId, SeatId, OrganizationUnitId, RoleId>(scoped.Widgets())).Should().Throw<InvalidOperationException>();

            // Tenancy's own context reads its own tables, and a module that maps the functions reads none of them.
            FluentActions.Invoking(() => answers.Over(scoped.Tenancy())).Should().NotThrow();
            using var functions = new ThroughFunctions(new DbContextOptionsBuilder<ThroughFunctions>().UseSqlite(keeping.Sqlite.Connection).UseApplicationServiceProvider(scoped).Options);
            FluentActions.Invoking(() => answers.Over(functions)).Should().NotThrow();

            // A context made by hand, which knows no application, is read as one whose database does not keep them.
            using var byHand = new TestWidgetContext(new DbContextOptionsBuilder<TestWidgetContext>().UseSqlite(keeping.Sqlite.Connection).Options);
            FluentActions.Invoking(() => answers.Over(byHand)).Should().NotThrow();
            return Task.CompletedTask;
        });

        // Where the save writes the rights, as on every database without such a package, the views are how a module asks.
        using var writing = new TestServices();
        await writing.BySystemIn(new TenantId(1), scoped =>
        {
            FluentActions.Invoking(() => scoped.Answers().Over(scoped.Widgets())).Should().NotThrow();
            return Task.CompletedTask;
        });
    }

    /// <summary>Options for a context that is asked for its model only: it never connects.</summary>
    private static DbContextOptions<TContext> On<TContext>(string provider)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>();
        return (provider switch
        {
            "Npgsql" => options.UseNpgsql("Host=model-only"),
            "Sqlite" => options.UseSqlite("Data Source=:memory:"),
            "SqlServer" => options.UseSqlServer("Server=model-only"),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "No such provider here."),
        }).Options;
    }

    /// <summary>A module's context that reads Tenancy through its functions, in Tenancy's schema.</summary>
    private sealed class ThroughFunctions(DbContextOptions<ThroughFunctions> options) : ModuleContext(options)
    {
        protected override void MapReadModel(ModelBuilder modelBuilder) => modelBuilder.AddTenancyReadFunctions<TenantId, SeatId, OrganizationUnitId, RoleId>(TestTenancyContext.Schema);
    }

    /// <summary>A module's context that reads Tenancy's tables through views.</summary>
    private sealed class ThroughViews(DbContextOptions<ThroughViews> options) : ModuleContext(options)
    {
        protected override void MapReadModel(ModelBuilder modelBuilder) => modelBuilder.AddTenancyReadModel<TenantId, SeatId, OrganizationUnitId, RoleId>(TestTenancyContext.Schema);
    }

    /// <summary>The context that maps Tenancy's own tables, with the views of the read model over them.</summary>
    private sealed class OwnTables(DbContextOptions<OwnTables> options) : ModuleContext(options)
    {
        protected override void MapReadModel(ModelBuilder modelBuilder)
            => modelBuilder.AddTenancy<HostTenant, TenantId, HostOrganization, HostUnit, OrganizationUnitId, HostSeat, SeatId, HostRole, RoleId>();
    }

    /// <summary>The same, for an application that keeps Tenancy's tables in the module's own schema.</summary>
    private sealed class ThroughFunctionsOfItsOwnSchema(DbContextOptions<ThroughFunctionsOfItsOwnSchema> options) : ModuleContext(options)
    {
        protected override void MapReadModel(ModelBuilder modelBuilder) => modelBuilder.AddTenancyReadFunctions<TenantId, SeatId, OrganizationUnitId, RoleId>(schema: null);
    }

    /// <summary>A module's context that names its columns in lower case, and reads Tenancy through its functions.</summary>
    private sealed class LowerCasedThroughFunctions(DbContextOptions<LowerCasedThroughFunctions> options) : ModuleContext(options, lowerCased: true)
    {
        protected override void MapReadModel(ModelBuilder modelBuilder) => modelBuilder.AddTenancyReadFunctions<TenantId, SeatId, OrganizationUnitId, RoleId>(TestTenancyContext.Schema);
    }

    /// <summary>A module's context that names its columns in lower case, and reads Tenancy's tables through views.</summary>
    private sealed class LowerCasedThroughViews(DbContextOptions<LowerCasedThroughViews> options) : ModuleContext(options, lowerCased: true)
    {
        protected override void MapReadModel(ModelBuilder modelBuilder) => modelBuilder.AddTenancyReadModel<TenantId, SeatId, OrganizationUnitId, RoleId>(TestTenancyContext.Schema);
    }

    /// <summary>A module's context in the widgets' schema, with nothing of its own but Tenancy's read model.</summary>
    private abstract class ModuleContext(DbContextOptions options, bool lowerCased = false) : DbContext(options)
    {
        protected abstract void MapReadModel(ModelBuilder modelBuilder);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema(TestWidgetContext.Schema);
            MapReadModel(modelBuilder);
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddDDDToolkitConventions();
            configurationBuilder.AddTenancyConverters();
            configurationBuilder.StoreDateTimeOffsetsAsUtc();
            if (lowerCased)
            {
                configurationBuilder.Conventions.Add(_ => new LowerCasedColumns());
            }
        }
    }

    /// <summary>
    /// A naming convention, as an application adds one for the whole of its context: every column named after its
    /// property in lower case, unless the mapping named it itself.
    /// </summary>
    private sealed class LowerCasedColumns : IModelFinalizingConvention
    {
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (var property in modelBuilder.Metadata.GetEntityTypes().SelectMany(entityType => entityType.GetDeclaredProperties()))
            {
                property.Builder.HasColumnName(property.Name.ToLowerInvariant());
            }
        }
    }
}
