using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.Supporting.Membership.Postgres.Tests;

/// <summary>
/// Two ways of saying a resource's rules that the other suites of this project run on SQLite alone, run here
/// against the database: plain users with the roles their rules declare, reached from above; and roles kept
/// for the resource, for members the host resolves, reached from above. For each, the four functions asked
/// past the application, the access questions asked in C#, and the rules read over the rows in memory have to
/// agree, for everybody and every permission.
/// </summary>
public sealed class ReachedFromAboveOnPostgresTests(FilingPostgres postgres)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Users_with_the_roles_their_rules_declare_are_reached_from_above_in_the_database_as_in_the_application()
    {
        await using var desk = await Desk.CreateAsync(postgres);
        string[] permissions = [DocumentPermissions.View, DocumentPermissions.Edit, DocumentPermissions.Share, "documents.unheard-of"];

        foreach (var (name, user) in desk.People)
        {
            await using var session = await CallerSession.BeginAsync(desk.Database, TestCallers.User(user));

            var seen = desk.SeenBy(user).Select(id => id.Value).ToList();
            (await session.ListAsync<Guid>("SELECT id FROM desk.documents_i_see() AS id")).Should().BeEquivalentTo(seen, "of what " + name + " sees");
            (await desk.AsAsync(TestCallers.User(user), async provider =>
            {
                var see = provider.GetRequiredService<MembershipAccessManager<Document>>().ReachSeen();
                return await provider.GetRequiredService<DeskContext>().Documents.Within(see).Select(document => document.Id.Value).ToListAsync(Cancellation);
            })).Should().BeEquivalentTo(seen, "of what " + name + " sees, asked in the application");

            foreach (var permission in permissions)
            {
                var held = desk.HeldBy(user, permission).Select(id => id.Value).ToList();
                var because = "of what " + name + " holds " + permission + " on";

                (await session.ListAsync<Guid>("SELECT id FROM desk.documents_where_i_hold($1) AS id", permission)).Should().BeEquivalentTo(held, because);
                (await desk.AsAsync(TestCallers.User(user), async provider =>
                {
                    var reach = provider.GetRequiredService<MembershipAccessManager<Document>>().Reach(permission);
                    return await provider.GetRequiredService<DeskContext>().Documents.Within(reach).Select(document => document.Id.Value).ToListAsync(Cancellation);
                })).Should().BeEquivalentTo(held, because + ", asked in the application");
            }
        }

        // And what the SQLite suite pins of it: Cy is Ada's deputy for seeing and changing, and a member of
        // nothing; Dee changes what Ada owns and sees only what is shared with her.
        (await desk.HoldAsync(desk.Cy, desk.Minutes, DocumentPermissions.Edit))!.Via.Should().Be(MemberVia.Above);
        (await desk.HoldAsync(desk.Cy, desk.Minutes, DocumentPermissions.Share))!.Via.Should().BeNull();
        (await desk.HoldAsync(desk.Cy, desk.Notes, DocumentPermissions.View)).Should().BeNull();
        (await desk.HoldAsync(desk.Dee, desk.Agenda, DocumentPermissions.Edit)).Should().BeNull("within the reach of the permission, and not there for her");
        (await desk.HoldAsync(desk.Ben, desk.Minutes, DocumentPermissions.View))!.Via.Should().Be(MemberVia.Members);
    }

    [Fact]
    public async Task Roles_kept_for_a_resource_with_members_the_host_resolves_are_reached_from_above_in_the_database_as_in_the_application()
    {
        await using var gardens = await WardenedGardens.CreateAsync(postgres);
        var data = gardens.Data;

        foreach (var person in gardens.People)
        {
            await using var session = await CallerSession.BeginAsync(gardens.Database, person.Caller);

            var seen = gardens.SeenBy(person).Select(id => id.Value).ToList();
            (await session.ListAsync<Guid>("SELECT id FROM wardens.plots_i_see() AS id")).Should().BeEquivalentTo(seen, "of what " + person + " sees");
            (await gardens.AsAsync(person.Caller, async provider =>
            {
                var see = provider.GetRequiredService<MembershipAccessManager<Plot>>().ReachSeen();
                return await provider.GetRequiredService<WardensContext>().Plots.Within(see).Select(plot => plot.Id.Value).ToListAsync(Cancellation);
            })).Should().BeEquivalentTo(seen, "of what " + person + " sees, asked in the application");

            foreach (var permission in GardenScenario.PermissionsAsked)
            {
                var held = gardens.HeldBy(person, permission).Select(id => id.Value).ToList();
                var because = "of what " + person + " holds " + permission + " on";

                (await session.ListAsync<Guid>("SELECT id FROM wardens.plots_where_i_hold($1) AS id", permission)).Should().BeEquivalentTo(held, because);
                (await gardens.AsAsync(person.Caller, async provider =>
                {
                    var reach = provider.GetRequiredService<MembershipAccessManager<Plot>>().Reach(permission);
                    return await provider.GetRequiredService<WardensContext>().Plots.Within(reach).Select(plot => plot.Id.Value).ToListAsync(Cancellation);
                })).Should().BeEquivalentTo(held, because + ", asked in the application");
            }
        }

        // And what the SQLite suite pins of it: through a role read from its row; from above, by somebody on
        // no plot; both ways at once; nothing through a role put away; and nobody for whom the host does not count.
        Task<MemberHold<PlotId>?> HoldAsync(GardenPerson person, PlotId plot, string permission)
            => gardens.AsAsync(person.Caller, provider => provider.GetRequiredService<MembershipAccessManager<Plot>>().HoldAsync(plot, permission, Cancellation));

        (await HoldAsync(data.Cy, data.Beans, PlotPermissions.Water))!.Via.Should().Be(MemberVia.Members);
        (await HoldAsync(gardens.Ranger, data.Leeks, PlotPermissions.Water))!.Via.Should().Be(MemberVia.Above);
        (await HoldAsync(gardens.Ranger, data.Beans, PlotPermissions.Plant))!.Via.Should().BeNull();
        (await HoldAsync(gardens.Ranger, data.Kale, PlotPermissions.See)).Should().BeNull("the kale is in the orchard");
        (await HoldAsync(data.Ben, data.Beans, PlotPermissions.Plant))!.Via.Should().Be(MemberVia.Members);
        (await HoldAsync(data.Ben, data.Beans, PlotPermissions.Fence))!.Via.Should().Be(MemberVia.Above);
        (await HoldAsync(data.Fay, data.Beans, PlotPermissions.Plant))!.Via.Should().BeNull();
        (await HoldAsync(data.Eve, data.Beans, PlotPermissions.Fence)).Should().BeNull();
    }

    private static string Column(IEntityType entity, string property) => RowAccessModel.Column(entity, property);

    /// <summary>A rule of each resource's for signed-in users: what the functions are granted to them for, written as no policy here.</summary>
    private static readonly IReadOnlyList<RowAccessRule> ForUsers =
    [
        RowAccessRule.For<Document>("Users read documents", RowOperations.Read, "true", RowAccessRoles.User),
        RowAccessRule.For<Plot>("Users read plots", RowOperations.Read, "true", RowAccessRoles.User),
    ];

    /// <summary>
    /// A new database of the calling test's own, copied from a template made once for the run: the schema's
    /// tables as the context's model says them, with every privilege for callers, and the access file written
    /// with the contributions.
    /// </summary>
    private static Task<FilingDatabase> DatabaseAsync(FilingPostgres server, string template, string schema, Func<DbContext> model, IReadOnlyList<IRowAccessContribution> contributions)
        => server.CreateDatabaseAsync(
            template,
            async (prepared, cancellationToken) =>
            {
                await FilingPostgres.ExecuteAsync(
                    prepared.SuperuserConnectionString,
                    $"CREATE SCHEMA ddd AUTHORIZATION {FilingPostgres.LoginRole}; CREATE SCHEMA {schema} AUTHORIZATION {FilingPostgres.LoginRole};",
                    cancellationToken);
                await FilingPostgres.ExecuteAsync(prepared.SuperuserConnectionString, PostgresRowAccess.SetupScript(new PostgresRowLevelSecurityOptions(), loginRole: FilingPostgres.LoginRole), cancellationToken);

                await using var context = model();
                await FilingPostgres.ExecuteAsync(prepared.ConnectionString, context.Database.GenerateCreateScript(), cancellationToken);
                await FilingPostgres.ExecuteAsync(
                    prepared.ConnectionString,
                    $"""
                    GRANT USAGE ON SCHEMA {schema} TO authenticated, anon, ddd_system_in;
                    GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {schema} TO authenticated, anon, ddd_system_in;
                    """,
                    cancellationToken);
                // No policy is written, and the functions are granted to signed-in users, as a rule of the resource's for them grants them.
                await FilingPostgres.ExecuteAsync(
                    prepared.ConnectionString,
                    PostgresRowAccess.Script(context, [], null, new RowAccessExport { Contributions = contributions, Rules = ForUsers }),
                    cancellationToken);
            },
            TestContext.Current.CancellationToken);

    // ------------------------------------------------------------------ documents of plain users, and their owners' deputies

    /// <summary>A permission somebody holds over everything a user owns: what a user gives a deputy.</summary>
    private sealed class Deputy
    {
        /// <summary>The user whose documents the permission is held over.</summary>
        public required UserId Of { get; init; }

        /// <summary>The user that holds it, as its token says who it is.</summary>
        public required Guid User { get; init; }

        /// <summary>The permission.</summary>
        public required string Key { get; init; }
    }

    /// <summary>Where a caller holds a permission over documents: at the owners that made it a deputy for that permission.</summary>
    private sealed class DeputiesOfOwners : IPlacesReached<DocumentId, UserId>
    {
        public IQueryable<UserId> PlacesReached(DbContext context, CurrentUserSnapshot caller, string permission)
        {
            var user = caller.Id;
            return context.Set<Deputy>().Where(deputy => deputy.User == user && deputy.Key == permission).Select(deputy => deputy.Of);
        }
    }

    /// <summary>The same answer in the database, under the logical name the documents' rules give it.</summary>
    private sealed class DeskOwnFunctions : IRowAccessContribution
    {
        public string Owner => "desk";

        public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export)
            => context.Model.FindEntityType(typeof(Deputy)) is not { } deputies
                ? null
                : new RowAccessContributionResult(
                    [
                        new ContributedFunction(
                            "owners_where_i_hold",
                            "text",
                            "SETOF uuid",
                            $"SELECT d.{Column(deputies, nameof(Deputy.Of))} FROM {RowAccessModel.Table(deputies)} d "
                            + $"WHERE d.{Column(deputies, nameof(Deputy.User))} = {{caller:uid}} AND d.{Column(deputies, nameof(Deputy.Key))} = $1",
                            SecurityDefiner: true),
                    ],
                    [],
                    []);
    }

    /// <summary>A context that keeps the documents where their owner is, and who is whose deputy.</summary>
    private sealed class DeskContext(DbContextOptions<DeskContext> options) : DbContext(options)
    {
        public DbSet<Document> Documents => Set<Document>();

        public DbSet<Deputy> Deputies => Set<Deputy>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("desk");
            modelBuilder.Entity<Document>(document =>
            {
                document.Property(row => row.Id).ValueGeneratedNever();
                document.HasMembers(row => row.Shares);
            });
            modelBuilder.Entity<Deputy>().HasKey(row => new { row.Of, row.User, row.Key });
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            DDDToolkit.EntityFramework.Conventions.ModelConfigurationBuilderExtensions.AddDDDToolkitConventions(configurationBuilder);
            DDDToolkit.Supporting.Membership.TestHost.Converters.ConverterExtensions.AddFilingConverters(configurationBuilder);
        }
    }

    /// <summary>
    /// The documents, their owners and the owners' deputies over one Postgres database. Ada owns the minutes,
    /// shared with Ben as an onlooker and with Dee, without a role, both until next week, and the agenda,
    /// shared with nobody. Ben owns the notes. Cy is Ada's deputy for seeing and changing, Dee for changing
    /// alone, and Eve is nobody's anything.
    /// </summary>
    private sealed class Desk : IAsyncDisposable
    {
        private static readonly MembershipRules Rules = MembershipRules.For<Document>(
            Roles.Fixed([new("contributor", [DocumentPermissions.View, DocumentPermissions.Edit]), new("onlooker", [DocumentPermissions.View])]),
            reachFromAbove: new DatabaseFunction("desk", "owners_where_i_hold"),
            systemScopes: []);

        private readonly ServiceProvider _provider;
        private readonly List<Document> _documents = [];
        private readonly List<Deputy> _deputies = [];
        private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

        private Desk(FilingDatabase database)
        {
            Database = database;
            var services = new ServiceCollection();
            services.AddSingleton(TimeProvider.System);
            services.AddDbContext<DeskContext>(options => options.UseNpgsql(database.ConnectionString));
            services.AddScoped<IPlacesReached<DocumentId, UserId>, DeputiesOfOwners>();
            services.AddMembership<DocumentShare, DocumentShareId, UserId, NamedRole, DeskContext, Document, DocumentId>(Rules);
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        public FilingDatabase Database { get; }

        public UserId Ada { get; } = UserId.CreateSequential();

        public UserId Ben { get; } = UserId.CreateSequential();

        public UserId Cy { get; } = UserId.CreateSequential();

        public UserId Dee { get; } = UserId.CreateSequential();

        public UserId Eve { get; } = UserId.CreateSequential();

        public DocumentId Minutes { get; } = DocumentId.CreateSequential();

        public DocumentId Agenda { get; } = DocumentId.CreateSequential();

        public DocumentId Notes { get; } = DocumentId.CreateSequential();

        public IReadOnlyList<(string Name, UserId User)> People => [("Ada", Ada), ("Ben", Ben), ("Cy", Cy), ("Dee", Dee), ("Eve", Eve)];

        public static async Task<Desk> CreateAsync(FilingPostgres server)
        {
            var database = await DatabaseAsync(
                server,
                "desk_above",
                "desk",
                () => new DeskContext(new DbContextOptionsBuilder<DeskContext>().UseNpgsql("Host=model-only").Options),
                [new MembershipRowAccessContribution<DocumentShare>(Rules), new DeskOwnFunctions()]);
            var desk = new Desk(database);

            // Started a while back, so the database's clock, which its functions ask, need not agree with this machine's to the second.
            var before = desk._now.AddDays(-10);
            var period = MemberPeriod.Between(before, desk._now.AddDays(7));
            var minutes = new Document(desk.Minutes, "Minutes", desk.Ada, before);
            minutes.ShareWith(desk.Ben, DocumentMembership.Onlooker, period, desk._now, by: desk.Ada);
            minutes.ShareWith(desk.Dee, period, desk._now, by: desk.Ada);
            desk._documents.AddRange(
                minutes,
                new Document(desk.Agenda, "Agenda", desk.Ada, before),
                new Document(desk.Notes, "Notes", desk.Ben, before));
            desk._deputies.AddRange(
                new Deputy { Of = desk.Ada, User = desk.Cy.Value, Key = DocumentPermissions.View },
                new Deputy { Of = desk.Ada, User = desk.Cy.Value, Key = DocumentPermissions.Edit },
                new Deputy { Of = desk.Ada, User = desk.Dee.Value, Key = DocumentPermissions.Edit });

            await desk.AsAsync(CurrentUserSnapshot.System, async provider =>
            {
                var context = provider.GetRequiredService<DeskContext>();
                context.Documents.AddRange(desk._documents);
                context.Deputies.AddRange(desk._deputies);
                await context.SaveChangesAsync(Cancellation);
                return true;
            });

            return desk;
        }

        /// <summary>The documents a user sees, read over the rows in memory: those it is on now, and those of an owner it holds the permission that sees over.</summary>
        public IReadOnlyList<DocumentId> SeenBy(UserId user)
            => [.. _documents.Where(document => OnNow(document, user) || IsDeputy(user, document, DocumentPermissions.View)).Select(document => document.Id)];

        /// <summary>
        /// The documents a permission is held on, read over the rows in memory: by owning one, for a permission of the
        /// document; by being on it, for the permission that sees; through a role held now that gives the permission; and
        /// from above, at the owner.
        /// </summary>
        public IReadOnlyList<DocumentId> HeldBy(UserId user, string permission)
            => [.. _documents
                .Where(document =>
                    (document.OwnerId == user && Rules.OwnerHolds(permission))
                    || (Rules.MembershipGives(permission) && OnNow(document, user))
                    || document.Shares.Any(share => share.MemberId == user && Rules.RolesWith(permission).Any(role => share.HoldsAt(role, _now)))
                    || IsDeputy(user, document, permission))
                .Select(document => document.Id)];

        public Task<MemberHold<DocumentId>?> HoldAsync(UserId user, DocumentId document, string permission)
            => AsAsync(TestCallers.User(user), provider => provider.GetRequiredService<MembershipAccessManager<Document>>().HoldAsync(document, permission, Cancellation));

        public async Task<T> AsAsync<T>(CurrentUserSnapshot caller, Func<IServiceProvider, Task<T>> ask)
        {
            using (TestUser.BeginAs(caller))
            {
                await using var scope = _provider.CreateAsyncScope();
                return await ask(scope.ServiceProvider);
            }
        }

        public ValueTask DisposeAsync() => _provider.DisposeAsync();

        private bool OnNow(Document document, UserId user) => document.Shares.Any(share => share.MemberId == user && share.AppliesAt(_now));

        private bool IsDeputy(UserId user, Document document, string permission)
            => _deputies.Any(deputy => deputy.User == user.Value && deputy.Of == document.OwnerId && deputy.Key == permission);
    }

    // ------------------------------------------------------------------ plots with kept roles, gardeners the host resolves, and wardens above

    /// <summary>A permission somebody holds at a garden, and so on every plot in it: the host's own rows.</summary>
    private sealed class GardenPermission
    {
        public Guid UserId { get; set; }

        public GardenId GardenId { get; set; }

        public string Permission { get; set; } = string.Empty;
    }

    /// <summary>Somebody the host no longer counts: nobody's gardener, whatever rows still name them.</summary>
    private sealed class LetGo
    {
        public Guid UserId { get; set; }
    }

    /// <summary>
    /// What a host answers for plots that stand beside something of its own: who the caller is as a gardener,
    /// which is nobody for somebody it let go, and at which gardens the caller holds a permission.
    /// </summary>
    private sealed class Wardens(UserId letGo) : ICallerMember<PlotId, UserId>, IPlacesReached<PlotId, GardenId>
    {
        public UserId? Find(CurrentUserSnapshot caller) => caller.Id is { } user && user != letGo.Value ? new UserId(user) : null;

        public IQueryable<GardenId> PlacesReached(DbContext context, CurrentUserSnapshot caller, string permission)
        {
            var user = caller.Id;
            var counted = user is not null && user != letGo.Value;
            return context.Set<GardenPermission>().Where(held => counted && held.UserId == user && held.Permission == permission).Select(held => held.GardenId);
        }
    }

    /// <summary>The same two answers in the database, under the logical names the plots' rules give them.</summary>
    private sealed class WardensOwnFunctions : IRowAccessContribution
    {
        public string Owner => "wardens";

        public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export)
        {
            if (context.Model.FindEntityType(typeof(GardenPermission)) is not { } permissions)
            {
                return null;
            }

            var gone = context.Model.FindEntityType(typeof(LetGo))!;
            return new RowAccessContributionResult(
                [
                    new ContributedFunction(
                        "caller_gardener",
                        "",
                        "uuid",
                        $"SELECT {{caller:uid}} WHERE NOT EXISTS (SELECT 1 FROM {RowAccessModel.Table(gone)} g WHERE g.{Column(gone, nameof(LetGo.UserId))} = {{caller:uid}})",
                        SecurityDefiner: true),
                    new ContributedFunction(
                        "gardens_where_i_hold",
                        "text",
                        "SETOF uuid",
                        $"SELECT k.{Column(permissions, nameof(GardenPermission.GardenId))} FROM {RowAccessModel.Table(permissions)} k "
                        + $"WHERE k.{Column(permissions, nameof(GardenPermission.UserId))} = (SELECT {{fn:wardens/caller_gardener}}()) AND k.{Column(permissions, nameof(GardenPermission.Permission))} = $1",
                        SecurityDefiner: true),
                ],
                [],
                []);
        }
    }

    /// <summary>A context that maps the plots as sitting in their garden, their role class, the permissions held at gardens, and who was let go.</summary>
    private sealed class WardensContext(DbContextOptions<WardensContext> options) : DbContext(options)
    {
        public DbSet<Plot> Plots => Set<Plot>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("wardens");
            modelBuilder.Entity<Plot>(plot =>
            {
                plot.Property(row => row.Id).ValueGeneratedNever();
                plot.HasMembers(row => row.Gardeners);
            });
            modelBuilder.Entity<PlotRole>().IsCustomRole();
            modelBuilder.Entity<GardenPermission>().HasKey(held => new { held.UserId, held.GardenId, held.Permission });
            modelBuilder.Entity<LetGo>().HasKey(row => row.UserId);
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            DDDToolkit.EntityFramework.Conventions.ModelConfigurationBuilderExtensions.AddDDDToolkitConventions(configurationBuilder);
            DDDToolkit.Supporting.Membership.TestHost.Converters.ConverterExtensions.AddFilingConverters(configurationBuilder);
        }
    }

    /// <summary>
    /// The gardens' plots and roles, as every suite seeds them, under rules that say all three things another
    /// way: the roles are kept for the plots, who a gardener is the host resolves, and a permission held at a plot's
    /// garden reaches the plot. A ranger sees and waters every plot of the meadow from above, Ben fences
    /// there, and Eve was let go.
    /// </summary>
    private sealed class WardenedGardens : IAsyncDisposable
    {
        private static readonly MembershipRules Rules = MembershipRules.Declare(
            typeof(Plot),
            "plots",
            PlotMembership.Rules.Permissions,
            Roles.Custom(starters: [new(PlotMembership.Tender, [PlotPermissions.See, PlotPermissions.Plant, PlotPermissions.Water]), new(PlotMembership.Waterer, [PlotPermissions.See, PlotPermissions.Water])]),
            members: MemberSource.Resolved(new DatabaseFunction("wardens", "caller_gardener")),
            reachFromAbove: new DatabaseFunction("wardens", "gardens_where_i_hold"),
            codes: PlotMembership.Codes);

        private readonly ServiceProvider _provider;
        private readonly List<GardenPermission> _keys;

        private WardenedGardens(FilingDatabase database)
        {
            Database = database;
            Data = new GardenScenario(DateTimeOffset.UtcNow);
            Ranger = new GardenPerson("Ranger", Guid.NewGuid(), Data.Meadow);
            _keys =
            [
                new GardenPermission { UserId = Ranger.User, GardenId = Data.Meadow, Permission = PlotPermissions.See },
                new GardenPermission { UserId = Ranger.User, GardenId = Data.Meadow, Permission = PlotPermissions.Water },
                new GardenPermission { UserId = Data.Ben.User, GardenId = Data.Meadow, Permission = PlotPermissions.Fence },

                // Held at the meadow by somebody the host let go: nothing reaches a plot through it.
                new GardenPermission { UserId = Data.Eve.User, GardenId = Data.Meadow, Permission = PlotPermissions.Water },
            ];

            var services = new ServiceCollection();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(new Wardens(letGo: Data.Eve.Member));
            services.AddDbContext<WardensContext>(options => options.UseNpgsql(database.ConnectionString));
            services.AddMembership<PlotGardener, PlotGardenerId, UserId, PlotRoleId, WardensContext, Plot, PlotId, Wardens>(Rules);
            _provider = services.BuildServiceProvider();
        }

        public FilingDatabase Database { get; }

        public GardenScenario Data { get; }

        public GardenPerson Ranger { get; }

        public IReadOnlyList<GardenPerson> People => [.. Data.People, Ranger];

        public static async Task<WardenedGardens> CreateAsync(FilingPostgres server)
        {
            var database = await DatabaseAsync(
                server,
                "wardens_above",
                "wardens",
                () => new WardensContext(new DbContextOptionsBuilder<WardensContext>().UseNpgsql("Host=model-only").Options),
                [new MembershipRowAccessContribution<PlotGardener>(Rules), new WardensOwnFunctions()]);
            var gardens = new WardenedGardens(database);

            await gardens.AsAsync(CurrentUserSnapshot.System, async provider =>
            {
                var context = provider.GetRequiredService<WardensContext>();
                context.AddRange(gardens.Data.Roles);
                context.AddRange(gardens.Data.Plots);
                context.AddRange(gardens._keys);
                context.Add(new LetGo { UserId = gardens.Data.Eve.User });
                await context.SaveChangesAsync(Cancellation);
                return true;
            });

            return gardens;
        }

        /// <summary>The plots somebody sees, read in memory: those it is on, and those of a garden it holds the permission that sees at. Nothing for somebody let go.</summary>
        public IReadOnlyList<PlotId> SeenBy(GardenPerson person)
            => Counts(person) ? [.. Data.PlotsSeenBy(person).Union(Above(person, PlotPermissions.See))] : [];

        /// <summary>The plots a permission is held on, read in memory: as the gardens' own rules say it, and from above. Nothing for somebody let go.</summary>
        public IReadOnlyList<PlotId> HeldBy(GardenPerson person, string permission)
            => Counts(person) ? [.. Data.PlotsHeldBy(person, permission).Union(Above(person, permission))] : [];

        public async Task<T> AsAsync<T>(CurrentUserSnapshot caller, Func<IServiceProvider, Task<T>> ask)
        {
            using (TestUser.BeginAs(caller))
            {
                await using var scope = _provider.CreateAsyncScope();
                return await ask(scope.ServiceProvider);
            }
        }

        public ValueTask DisposeAsync() => _provider.DisposeAsync();

        private bool Counts(GardenPerson person) => person.User != Data.Eve.User;

        private IEnumerable<PlotId> Above(GardenPerson person, string permission)
            => Data.Plots.Where(plot => _keys.Any(held => held.UserId == person.User && held.GardenId == plot.GardenId && held.Permission == permission)).Select(plot => plot.Id);
    }
}
