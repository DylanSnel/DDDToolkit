using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests;

/// <summary>
/// What <c>IsCustomRole</c> maps of a role class, the mark it leaves, which is how the roles of a resource are
/// found without anything registered for them, and what is said when the rules, the registration and the
/// model do not agree about a resource's roles.
/// </summary>
public sealed class RoleClassMappingTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static IModel GardenModel()
    {
        using var garden = new SqliteGarden();
        using var scope = garden.Services.Provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<GardenContext>().Model;
    }

    [Fact]
    public void The_packages_part_of_a_role_is_mapped_and_the_rest_is_the_hosts()
    {
        var role = GardenModel().FindEntityType(typeof(PlotRole))!;

        // A table of its own, named as the context names an aggregate's: the role is the host's aggregate.
        (role.GetTableName(), role.GetSchema()).Should().Be(("PlotRoles", GardenContext.Schema));
        role.FindOwnership().Should().BeNull("a role is an aggregate beside the plot, not a part of one");
        role.GetProperties().Select(property => property.Name).Should().BeEquivalentTo(
            ["Id", "Name", "Description", "Permissions", "Status", "MadeFrom", "GardenId", "Version"],
            "the package's six, the host's garden, and the version every aggregate has");

        role.FindProperty("Id")!.ValueGenerated.Should().Be(ValueGenerated.Never, "the host makes a role's id");
        role.FindProperty("Name")!.GetMaxLength().Should().Be(CustomRoleAggregate<PlotRoleId>.MaxNameLength).And.Be(120);
        role.FindProperty("Description")!.GetMaxLength().Should().Be(CustomRoleAggregate<PlotRoleId>.MaxDescriptionLength).And.Be(1000);
        role.FindProperty("MadeFrom")!.GetMaxLength().Should().Be(NamedRole.MaxLength, "a starter role is one the rules declare, by a name a role can have");
        role.FindProperty("MadeFrom")!.IsNullable.Should().BeTrue("a role a garden made itself came from no starter role");

        // The status as its name, and the permissions as one collection in one column, read inside a statement.
        role.FindProperty("Status")!.GetProviderClrType().Should().Be(typeof(string));
        role.FindProperty("Permissions")!.IsPrimitiveCollection.Should().BeTrue();

        // What is the host's: the two indexes over its garden, and the rule about which roles a request reads.
        role.GetIndexes().Select(index => string.Join("+", index.Properties.Select(property => property.Name)) + (index.IsUnique ? " once" : string.Empty))
            .Should().BeEquivalentTo("GardenId+Name once", "GardenId+MadeFrom once");
        role.GetDeclaredQueryFilters().Should().ContainSingle();
    }

    [Fact]
    public void The_mapping_marks_whose_roles_they_are_and_that_is_how_a_resources_roles_are_found()
    {
        var model = GardenModel();
        var plot = model.FindEntityType(typeof(Plot))!;
        var role = model.FindEntityType(typeof(PlotRole))!;

        // The resource is the one the role class's template names: nothing is said where the context maps it.
        role.FindAnnotation(MembershipModel.RoleOfAnnotation)!.Value.Should().Be(typeof(Plot).FullName);
        MembershipModel.MembersOf(plot)!.RoleClass.Should().BeSameAs(role);
        MembershipModel.For(model, typeof(PlotGardener))!.RoleClass.Should().BeSameAs(role);

        // A gardener holds a role by the role class's id, stored as the host's converter stores it.
        MembershipModel.MembersOf(plot)!.Roles.FindProperty("RoleId")!.ClrType.Should().Be(typeof(PlotRoleId));
    }

    [Fact]
    public void The_other_resources_of_the_host_have_no_role_class_and_are_mapped_as_they_were()
    {
        using var filing = new SqliteFiling();
        using var depot = new SqliteDepot();
        using var filed = filing.Services.Provider.CreateScope();
        using var stored = depot.Services.Provider.CreateScope();
        var filingModel = filed.ServiceProvider.GetRequiredService<FilingContext>().Model;
        var depotModel = stored.ServiceProvider.GetRequiredService<DepotContext>().Model;

        foreach (var (model, resource) in new[] { (filingModel, typeof(Document)), (filingModel, typeof(Folder)), (depotModel, typeof(Pallet)), (depotModel, typeof(Crate)) })
        {
            MembershipModel.MembersOf(model.FindEntityType(resource)!)!.RoleClass.Should().BeNull(resource.Name + " keeps no roles of its own");
            model.GetEntityTypes().Should().NotContain(entity => entity.FindAnnotation(MembershipModel.RoleOfAnnotation) != null);
        }

        filingModel.GetEntityTypes().Select(entity => entity.GetTableName()).Should().BeEquivalentTo(
            "Documents", "DocumentShares", "DocumentShareRoles", "Folders", FilingContext.FolderStaffTable, FilingContext.FolderStaffRolesTable);
        depotModel.FindEntityType(typeof(PlotRole)).Should().BeNull();
        (DocumentMembership.Rules.Roles.Kind, FolderMembership.Rules.Roles.Kind, PalletMembership.Rules.Roles.Kind, CrateMembership.Rules.Roles.Kind)
            .Should().Be((RolesKind.Fixed, RolesKind.Fixed, RolesKind.Fixed, RolesKind.External));
    }

    [Fact]
    public void A_class_that_was_not_declared_with_the_role_template_is_not_mapped_as_one()
    {
        FluentActions.Invoking(() => ModelOf(builder => builder.Entity<Barrow>().IsCustomRole()))
            .Should().Throw<ArgumentException>()
            .WithMessage("Barrow is not a role class. Declare it with the role template*[CustomRole<BarrowId, Document>] public sealed partial class Barrow*");

        FluentActions.Invoking(() => MembershipEntityTypeBuilderExtensions.IsCustomRole<PlotRole>(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_resource_has_one_role_class_and_a_mark_the_model_no_longer_matches_is_refused_where_it_is_read()
    {
        // Two classes marked as the roles of one resource: a member holds a role by one class's id.
        var two = ModelOf(builder =>
        {
            builder.Entity<Plot>().HasMembers(plot => plot.Gardeners);
            builder.Entity<PlotRole>().IsCustomRole();
            builder.Entity<StrayRole>().HasAnnotation(MembershipModel.RoleOfAnnotation, typeof(Plot).FullName);
        });
        FluentActions.Invoking(() => MembershipModel.MembersOf(two.FindEntityType(typeof(Plot))!))
            .Should().Throw<InvalidOperationException>().WithMessage("Plot has two role classes in this model, *Role and *Role. A resource has one*");

        // A mark on something that has no permissions: the mapping was changed after IsCustomRole, or never was one.
        var unkeyed = ModelOf(builder =>
        {
            builder.Entity<Plot>().HasMembers(plot => plot.Gardeners);
            builder.Entity<Barrow>().HasAnnotation(MembershipModel.RoleOfAnnotation, typeof(Plot).FullName);
        });
        FluentActions.Invoking(() => MembershipModel.MembersOf(unkeyed.FindEntityType(typeof(Plot))!))
            .Should().Throw<InvalidOperationException>().WithMessage("Barrow was mapped with IsCustomRole as the role class of Plot, and the model no longer has its permissions, its status or its id.*");
    }

    [Fact]
    public void Rules_that_keep_the_roles_are_for_members_that_hold_a_role_by_an_id_of_the_hosts()
    {
        var kept = MembershipRules.Declare(typeof(Document), "papers", [new("papers.view")], Roles.Custom(starters: [new("reader", ["papers.view"])]));

        // A document's members hold their roles by name, and a role that is a row is not held by a name.
        FluentActions.Invoking(() => new ServiceCollection().AddMembership<DocumentShare, DocumentShareId, UserId, NamedRole, FilingContext, Document, DocumentId>(kept))
            .Should().Throw<ArgumentException>().WithParameterName("rules")
            .WithMessage("The rules 'papers' say a customer makes the roles of Document, as rows of a role class, and its members hold roles by name.*[CustomRole<DocumentRoleId, Document>]*[Member<DocumentShareId, UserId, DocumentRoleId, Document>]*");

        // And rules that declare the roles are not for members that hold a role by an id: the plots' gardeners do.
        var declared = MembershipRules.Declare(typeof(Plot), "beds", [new("beds.view")], Roles.Fixed([new("reader", ["beds.view"])]));
        FluentActions.Invoking(() => new ServiceCollection().AddMembership<PlotGardener, PlotGardenerId, UserId, PlotRoleId, GardenContext, Plot, PlotId>(declared))
            .Should().Throw<ArgumentException>().WithParameterName("rules")
            .WithMessage("The members of Plot hold roles known by PlotRoleId, and the rules 'beds' have fixed roles*Roles.Custom() for a role class of this resource*");

        // Nothing is registered for the roles themselves: the rules say they are kept, and the model says where.
        var services = new ServiceCollection();
        services.AddMembership<PlotGardener, PlotGardenerId, UserId, PlotRoleId, GardenContext, Plot, PlotId>(PlotMembership.Rules);
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(IMemberRoles<PlotId, PlotRoleId>));
        services.Select(descriptor => descriptor.ServiceType).Should().NotContain(typeof(IRolesWithPermission<PlotId, PlotRoleId>));

        // A host that answers which roles there are itself, by a scope it reads from elsewhere say, registers
        // its answer before the resource, and that one stays: the admission asks it, and not the rows.
        var answered = new ServiceCollection();
        answered.AddScoped<IMemberRoles<PlotId, PlotRoleId>, RolesTheHostAnswers>();
        answered.AddMembership<PlotGardener, PlotGardenerId, UserId, PlotRoleId, GardenContext, Plot, PlotId>(PlotMembership.Rules);
        answered.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(IMemberRoles<PlotId, PlotRoleId>))
            .Which.ImplementationType.Should().Be(typeof(RolesTheHostAnswers));
    }

    [Fact]
    public async Task The_admission_of_a_resource_whose_roles_are_kept_asks_the_hosts_own_answer_where_it_registered_one()
    {
        var owners = PlotRoleId.CreateSequential();
        var services = new ServiceCollection();
        services.AddSingleton(new RolesTheHostAnswers(owners));
        services.AddScoped<IMemberRoles<PlotId, PlotRoleId>>(provider => provider.GetRequiredService<RolesTheHostAnswers>());
        services.AddMembership<PlotGardener, PlotGardenerId, UserId, PlotRoleId, GardenContext, Plot, PlotId>(PlotMembership.Rules);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        // No context is registered here at all: nothing of the roles' rows is read for what the member manager asks.
        await Admitted.RoleAsync<PlotId, PlotRoleId>(scope.ServiceProvider, PlotMembership.Codes, owners, Cancellation);
        await Refused.WithCodeAsync(PlotMembership.Codes, MembershipRefusals.RoleNotForMembers, () => Admitted.RoleAsync<PlotId, PlotRoleId>(scope.ServiceProvider, PlotMembership.Codes, PlotRoleId.CreateSequential(), Cancellation));
    }

    [Fact]
    public async Task Rules_that_keep_the_roles_need_the_model_to_map_the_role_class_and_say_so_at_the_first_question()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");

        async Task<InvalidOperationException> AskedAsync<TContext>()
            where TContext : DbContext
        {
            var services = new ServiceCollection();
            services.AddDbContext<TContext>(options => options.UseSqlite(connection));
            services.AddMembership<PlotGardener, PlotGardenerId, UserId, PlotRoleId, TContext, Plot, PlotId>(PlotMembership.Rules);
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            using (TestUser.BeginAs(TestUsers.User(UserId.CreateSequential())))
            {
                var asked = FluentActions.Invoking(() => scope.ServiceProvider.GetRequiredService<MembershipAccessManager<Plot>>().Reach(PlotPermissions.Plant))
                    .Should().Throw<InvalidOperationException>().Which;

                // What the member manager asks of a role is told the same: it reads the same rows.
                (await FluentActions.Awaiting(() => Admitted.RoleAsync<PlotId, PlotRoleId>(scope.ServiceProvider, PlotMembership.Codes, PlotRoleId.CreateSequential(), Cancellation))
                    .Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be(asked.Message);

                return asked;
            }
        }

        // The plots are mapped with their gardeners, and the context never mapped a role class for them.
        (await AskedAsync<PlotsWithoutRoles>()).Message.Should()
            .StartWith("The rules 'plots' say a customer makes the roles of Plot, and PlotsWithoutRoles maps no role class for it.")
            .And.Contain("[CustomRole<PlotRoleId, Plot>]")
            .And.EndWith("modelBuilder.Entity<PlotRole>().IsCustomRole().");

        // A role class known by another id than the gardeners hold their roles by.
        (await AskedAsync<PlotsWithStrayRoles>()).Message.Should()
            .Be("The members of Plot hold roles known by PlotRoleId, and its role class StrayRole is known by GardenId. A member holds a role of the resource by that role's id: "
                + "declare the member class with GardenId as its role.");
    }

    [Fact]
    public void A_context_that_says_nothing_of_a_role_class_maps_it_by_convention_as_IsCustomRole_does()
    {
        var byHand = ModelOf(builder =>
        {
            builder.Entity<Plot>();
            builder.Entity<PlotRole>().IsCustomRole();
        });
        var byConvention = ModelOf(builder =>
        {
            builder.Entity<Plot>();
            builder.Entity<PlotRole>();
        });

        byConvention.ToDebugString(Microsoft.EntityFrameworkCore.Infrastructure.MetadataDebugStringOptions.LongDefault).Should().Be(byHand.ToDebugString(Microsoft.EntityFrameworkCore.Infrastructure.MetadataDebugStringOptions.LongDefault));
        byConvention.FindEntityType(typeof(PlotRole))!.FindAnnotation(MembershipModel.RoleOfAnnotation)!.Value.Should().Be(typeof(Plot).FullName);

        // A garden is the host's own column, which the package knows nothing of: the indexes over it are the host's to say.
        byConvention.FindEntityType(typeof(PlotRole))!.GetIndexes().Should().BeEmpty();
    }

    /// <summary>The model of a context that maps what <paramref name="map"/> says.</summary>
    private static IModel ModelOf(Action<ModelBuilder> map)
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        using var context = new MappedContext(new DbContextOptionsBuilder<MappedContext>().UseSqlite(connection).Options, map);
        return context.Model;
    }

    /// <summary>A context whose mapping is the test's to say. Its model is built for each test, never cached.</summary>
    private sealed class MappedContext(DbContextOptions<MappedContext> options, Action<ModelBuilder> map) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => map(modelBuilder);

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, EveryTime>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            DDDToolkit.EntityFramework.Conventions.ModelConfigurationBuilderExtensions.AddDDDToolkitConventions(configurationBuilder);
            DDDToolkit.Supporting.Membership.TestHost.Converters.ConverterExtensions.AddFilingConverters(configurationBuilder);
        }

        private sealed class EveryTime : Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory
        {
            public object Create(DbContext context, bool designTime) => new object();
        }
    }

    /// <summary>A context that maps the plots with their gardeners, and no role class for them.</summary>
    private sealed class PlotsWithoutRoles(DbContextOptions<PlotsWithoutRoles> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Plot>().HasMembers(plot => plot.Gardeners);

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            DDDToolkit.EntityFramework.Conventions.ModelConfigurationBuilderExtensions.AddDDDToolkitConventions(configurationBuilder);
            DDDToolkit.Supporting.Membership.TestHost.Converters.ConverterExtensions.AddFilingConverters(configurationBuilder);
        }
    }

    /// <summary>A context that maps the plots, and marks as their roles a class that is known by another id.</summary>
    private sealed class PlotsWithStrayRoles(DbContextOptions<PlotsWithStrayRoles> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Plot>().HasMembers(plot => plot.Gardeners);
            modelBuilder.Entity<StrayRole>().HasAnnotation(MembershipModel.RoleOfAnnotation, typeof(Plot).FullName);
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            DDDToolkit.EntityFramework.Conventions.ModelConfigurationBuilderExtensions.AddDDDToolkitConventions(configurationBuilder);
            DDDToolkit.Supporting.Membership.TestHost.Converters.ConverterExtensions.AddFilingConverters(configurationBuilder);
        }
    }

    /// <summary>Rows that look like roles and are known by something a gardener holds no role by.</summary>
    private sealed class StrayRole
    {
        public GardenId Id { get; set; }

        public List<string> Permissions { get; set; } = [];

        public CustomRoleStatus Status { get; set; }
    }

    /// <summary>A host's own answer to which roles there are for its plots: one role.</summary>
    private sealed class RolesTheHostAnswers(PlotRoleId owners) : IMemberRoles<PlotId, PlotRoleId>
    {
        public ValueTask<bool> ExistsAsync(PlotRoleId role, CancellationToken cancellationToken) => ValueTask.FromResult(role == owners);
    }

    /// <summary>Something that is no role class.</summary>
    private sealed class Barrow
    {
        public Guid Id { get; set; }
    }
}
