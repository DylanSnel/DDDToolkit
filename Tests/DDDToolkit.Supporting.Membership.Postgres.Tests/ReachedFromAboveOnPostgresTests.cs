using DDDToolkit.EntityFramework.Postgres;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DDDToolkit.Supporting.Membership.Postgres.Tests;

/// <summary>
/// Two ways of saying a resource's rules that the other suites of this project run on SQLite alone, run here
/// against the database: plain users with the roles their rules declare, reached from above; and roles kept
/// for the resource, for members the host resolves, reached from above. For each, the four functions asked
/// past the application, the access questions asked in C#, and the rules read over the rows in memory have to
/// agree, for everybody and every key.
/// </summary>
public sealed class ReachedFromAboveOnPostgresTests(FilingPostgres postgres)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Users_with_the_roles_their_rules_declare_are_reached_from_above_in_the_database_as_in_the_application()
    {
        await using var desk = await Desk.CreateAsync(postgres);
        string[] keys = [DocumentKeys.View, DocumentKeys.Edit, DocumentKeys.Share, "documents.unheard-of"];

        foreach (var (name, user) in desk.People)
        {
            await using var session = await CallerSession.BeginAsync(desk.Database, TestCallers.User(user));

            var seen = desk.SeenBy(user).Select(id => id.Value).ToList();
            (await session.ListAsync<Guid>("SELECT id FROM desk.documents_i_see() AS id")).Should().BeEquivalentTo(seen, "of what " + name + " sees");
            (await desk.AsAsync(TestCallers.User(user), async provider =>
            {
                var see = provider.GetRequiredService<IMemberQuestions<DocumentId>>().KeyReach([]).See;
                return await provider.GetRequiredService<DeskContext>().Documents.Within(see).Select(document => document.Id.Value).ToListAsync(Cancellation);
            })).Should().BeEquivalentTo(seen, "of what " + name + " sees, asked in the application");

            foreach (var key in keys)
            {
                var held = desk.HeldBy(user, key).Select(id => id.Value).ToList();
                var because = "of what " + name + " holds " + key + " on";

                (await session.ListAsync<Guid>("SELECT id FROM desk.documents_where_i_hold($1) AS id", key)).Should().BeEquivalentTo(held, because);
                (await desk.AsAsync(TestCallers.User(user), async provider =>
                {
                    var reach = provider.GetRequiredService<IMemberQuestions<DocumentId>>().Reach(key);
                    return await provider.GetRequiredService<DeskContext>().Documents.Within(reach).Select(document => document.Id.Value).ToListAsync(Cancellation);
                })).Should().BeEquivalentTo(held, because + ", asked in the application");
            }
        }

        // And what the SQLite suite pins of it: Cy is Ada's deputy for seeing and changing, and a member of
        // nothing; Dee changes what Ada owns and sees only what is shared with her.
        (await desk.HoldAsync(desk.Cy, desk.Minutes, DocumentKeys.Edit))!.Via.Should().Be(MemberVia.Above);
        (await desk.HoldAsync(desk.Cy, desk.Minutes, DocumentKeys.Share))!.Via.Should().BeNull();
        (await desk.HoldAsync(desk.Cy, desk.Notes, DocumentKeys.View)).Should().BeNull();
        (await desk.HoldAsync(desk.Dee, desk.Agenda, DocumentKeys.Edit)).Should().BeNull("within the reach of the key, and not there for her");
        (await desk.HoldAsync(desk.Ben, desk.Minutes, DocumentKeys.View))!.Via.Should().Be(MemberVia.Members);
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
                var see = provider.GetRequiredService<IMemberQuestions<PlotId>>().KeyReach([]).See;
                return await provider.GetRequiredService<WardensContext>().Plots.Within(see).Select(plot => plot.Id.Value).ToListAsync(Cancellation);
            })).Should().BeEquivalentTo(seen, "of what " + person + " sees, asked in the application");

            foreach (var key in GardenScenario.KeysAsked)
            {
                var held = gardens.HeldBy(person, key).Select(id => id.Value).ToList();
                var because = "of what " + person + " holds " + key + " on";

                (await session.ListAsync<Guid>("SELECT id FROM wardens.plots_where_i_hold($1) AS id", key)).Should().BeEquivalentTo(held, because);
                (await gardens.AsAsync(person.Caller, async provider =>
                {
                    var reach = provider.GetRequiredService<IMemberQuestions<PlotId>>().Reach(key);
                    return await provider.GetRequiredService<WardensContext>().Plots.Within(reach).Select(plot => plot.Id.Value).ToListAsync(Cancellation);
                })).Should().BeEquivalentTo(held, because + ", asked in the application");
            }
        }

        // And what the SQLite suite pins of it: through a role read from its row; from above, by somebody on
        // no plot; both ways at once; nothing through a role put away; and nobody for whom the host does not count.
        Task<MemberHold<PlotId>?> HoldAsync(GardenPerson person, PlotId plot, string key)
            => gardens.AsAsync(person.Caller, provider => provider.GetRequiredService<IMemberQuestions<PlotId>>().HoldAsync(plot, key, Cancellation));

        (await HoldAsync(data.Cy, data.Beans, PlotKeys.Water))!.Via.Should().Be(MemberVia.Members);
        (await HoldAsync(gardens.Ranger, data.Leeks, PlotKeys.Water))!.Via.Should().Be(MemberVia.Above);
        (await HoldAsync(gardens.Ranger, data.Beans, PlotKeys.Plant))!.Via.Should().BeNull();
        (await HoldAsync(gardens.Ranger, data.Kale, PlotKeys.See)).Should().BeNull("the kale is in the orchard");
        (await HoldAsync(data.Ben, data.Beans, PlotKeys.Plant))!.Via.Should().Be(MemberVia.Members);
        (await HoldAsync(data.Ben, data.Beans, PlotKeys.Fence))!.Via.Should().Be(MemberVia.Above);
        (await HoldAsync(data.Fay, data.Beans, PlotKeys.Plant))!.Via.Should().BeNull();
        (await HoldAsync(data.Eve, data.Beans, PlotKeys.Fence)).Should().BeNull();
    }

    private static string Column(IEntityType entity, string property) => RowAccessModel.Column(entity, property);

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
                await FilingPostgres.ExecuteAsync(prepared.ConnectionString, PostgresRowAccess.Script(context, [], null, new RowAccessExport { Contributions = contributions }), cancellationToken);
            },
            TestContext.Current.CancellationToken);

    // ------------------------------------------------------------------ documents of plain users, and their owners' deputies

    /// <summary>A key somebody holds over everything a user owns: what a user gives a deputy.</summary>
    private sealed class Deputy
    {
        /// <summary>The user whose documents the key is held over.</summary>
        public required UserId Of { get; init; }

        /// <summary>The user that holds it, as its token says who it is.</summary>
        public required Guid User { get; init; }

        /// <summary>The key.</summary>
        public required string Key { get; init; }
    }

    /// <summary>Where a caller holds a key over documents: at the owners that made it a deputy for that key.</summary>
    private sealed class DeputiesOfOwners : IPlacesReached<DocumentId, UserId>
    {
        public IQueryable<UserId> PlacesReached(DbContext context, Caller caller, string key)
        {
            var user = caller.UserId;
            return context.Set<Deputy>().Where(deputy => deputy.User == user && deputy.Key == key).Select(deputy => deputy.Of);
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
                document.HasMembers(row => row.Shares, row => row.OwnerId, at: row => row.OwnerId);
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
        private static readonly MembershipRules Rules = new(
            "documents",
            keys: [DocumentKeys.View, DocumentKeys.Edit, DocumentKeys.Share],
            roles: [new("contributor", [DocumentKeys.View, DocumentKeys.Edit]), new("onlooker", [DocumentKeys.View])],
            seeKey: DocumentKeys.View,
            above: new("desk/owners_where_i_hold"));

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
            var minutes = new Document(desk.Minutes, "Minutes", desk.Ada, DocumentMembership.Owner, before);
            minutes.ShareWith(desk.Ben, DocumentMembership.Onlooker, period, desk._now, by: desk.Ada);
            minutes.ShareWith(desk.Dee, period, desk._now, by: desk.Ada);
            desk._documents.AddRange(
                minutes,
                new Document(desk.Agenda, "Agenda", desk.Ada, DocumentMembership.Owner, before),
                new Document(desk.Notes, "Notes", desk.Ben, DocumentMembership.Owner, before));
            desk._deputies.AddRange(
                new Deputy { Of = desk.Ada, User = desk.Cy.Value, Key = DocumentKeys.View },
                new Deputy { Of = desk.Ada, User = desk.Cy.Value, Key = DocumentKeys.Edit },
                new Deputy { Of = desk.Ada, User = desk.Dee.Value, Key = DocumentKeys.Edit });

            await desk.AsAsync(Caller.System, async provider =>
            {
                var context = provider.GetRequiredService<DeskContext>();
                context.Documents.AddRange(desk._documents);
                context.Deputies.AddRange(desk._deputies);
                await context.SaveChangesAsync(Cancellation);
                return true;
            });

            return desk;
        }

        /// <summary>The documents a user sees, read over the rows in memory: those it is on now, and those of an owner it holds the key that sees over.</summary>
        public IReadOnlyList<DocumentId> SeenBy(UserId user)
            => [.. _documents.Where(document => OnNow(document, user) || IsDeputy(user, document, DocumentKeys.View)).Select(document => document.Id)];

        /// <summary>
        /// The documents a key is held on, read over the rows in memory: by owning one, for a key of the
        /// document; by being on it, for the key that sees; through a role held now that gives the key; and
        /// from above, at the owner.
        /// </summary>
        public IReadOnlyList<DocumentId> HeldBy(UserId user, string key)
            => [.. _documents
                .Where(document =>
                    (document.OwnerId == user && Rules.OwnerHolds(key))
                    || (Rules.MembershipGives(key) && OnNow(document, user))
                    || document.Shares.Any(share => share.MemberId == user && Rules.RolesWith(key).Any(role => share.HoldsAt(role, _now)))
                    || IsDeputy(user, document, key))
                .Select(document => document.Id)];

        public Task<MemberHold<DocumentId>?> HoldAsync(UserId user, DocumentId document, string key)
            => AsAsync(TestCallers.User(user), provider => provider.GetRequiredService<IMemberQuestions<DocumentId>>().HoldAsync(document, key, Cancellation));

        public async Task<T> AsAsync<T>(Caller caller, Func<IServiceProvider, Task<T>> ask)
        {
            using (Callers.Begin(caller))
            {
                await using var scope = _provider.CreateAsyncScope();
                return await ask(scope.ServiceProvider);
            }
        }

        public ValueTask DisposeAsync() => _provider.DisposeAsync();

        private bool OnNow(Document document, UserId user) => document.Shares.Any(share => share.MemberId == user && share.AppliesAt(_now));

        private bool IsDeputy(UserId user, Document document, string key)
            => _deputies.Any(deputy => deputy.User == user.Value && deputy.Of == document.OwnerId && deputy.Key == key);
    }

    // ------------------------------------------------------------------ plots with kept roles, gardeners the host resolves, and wardens above

    /// <summary>A key somebody holds at a garden, and so on every plot in it: the host's own rows.</summary>
    private sealed class GardenKey
    {
        public Guid UserId { get; set; }

        public GardenId GardenId { get; set; }

        public string Key { get; set; } = string.Empty;
    }

    /// <summary>Somebody the host no longer counts: nobody's gardener, whatever rows still name them.</summary>
    private sealed class LetGo
    {
        public Guid UserId { get; set; }
    }

    /// <summary>
    /// What a host answers for plots that stand beside something of its own: who the caller is as a gardener,
    /// which is nobody for somebody it let go, and at which gardens the caller holds a key.
    /// </summary>
    private sealed class Wardens(UserId letGo) : ICallerMember<PlotId, UserId>, IPlacesReached<PlotId, GardenId>
    {
        public UserId? Find(Caller caller) => caller.UserId is { } user && user != letGo.Value ? new UserId(user) : null;

        public IQueryable<GardenId> PlacesReached(DbContext context, Caller caller, string key)
        {
            var user = caller.UserId;
            var counted = user is not null && user != letGo.Value;
            return context.Set<GardenKey>().Where(held => counted && held.UserId == user && held.Key == key).Select(held => held.GardenId);
        }
    }

    /// <summary>The same two answers in the database, under the logical names the plots' rules give them.</summary>
    private sealed class WardensOwnFunctions : IRowAccessContribution
    {
        public string Owner => "wardens";

        public RowAccessContributionResult? Contribute(DbContext context, RowAccessExport export)
        {
            if (context.Model.FindEntityType(typeof(GardenKey)) is not { } keys)
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
                        $"SELECT k.{Column(keys, nameof(GardenKey.GardenId))} FROM {RowAccessModel.Table(keys)} k "
                        + $"WHERE k.{Column(keys, nameof(GardenKey.UserId))} = (SELECT {{fn:wardens/caller_gardener}}()) AND k.{Column(keys, nameof(GardenKey.Key))} = $1",
                        SecurityDefiner: true),
                ],
                [],
                []);
        }
    }

    /// <summary>A context that maps the plots as sitting in their garden, their role class, the keys held at gardens, and who was let go.</summary>
    private sealed class WardensContext(DbContextOptions<WardensContext> options) : DbContext(options)
    {
        public DbSet<Plot> Plots => Set<Plot>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("wardens");
            modelBuilder.Entity<Plot>(plot =>
            {
                plot.Property(row => row.Id).ValueGeneratedNever();
                plot.HasMembers(row => row.Gardeners, row => row.OwnerId, at: row => row.GardenId);
            });
            modelBuilder.Entity<PlotRole>().IsKeptRole();
            modelBuilder.Entity<GardenKey>().HasKey(held => new { held.UserId, held.GardenId, held.Key });
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
    /// way: the roles are kept for the plots, who a gardener is the host resolves, and a key held at a plot's
    /// garden reaches the plot. A ranger sees and waters every plot of the meadow from above, Ben fences
    /// there, and Eve was let go.
    /// </summary>
    private sealed class WardenedGardens : IAsyncDisposable
    {
        private static readonly MembershipRules Rules = new(
            "plots",
            keys: [.. PlotMembership.Rules.Keys],
            roles: [new(PlotMembership.Tender, [PlotKeys.See, PlotKeys.Plant, PlotKeys.Water]), new(PlotMembership.Waterer, [PlotKeys.See, PlotKeys.Water])],
            members: MemberSource.Resolved("wardens/caller_gardener"),
            seeKey: PlotKeys.See,
            memberKeys: PlotMembership.Rules.MemberKeys,
            codes: PlotMembership.Codes,
            above: new("wardens/gardens_where_i_hold"),
            rolesKept: true);

        private readonly ServiceProvider _provider;
        private readonly List<GardenKey> _keys;

        private WardenedGardens(FilingDatabase database)
        {
            Database = database;
            Data = new GardenScenario(DateTimeOffset.UtcNow);
            Ranger = new GardenPerson("Ranger", Guid.NewGuid(), Data.Meadow);
            _keys =
            [
                new GardenKey { UserId = Ranger.User, GardenId = Data.Meadow, Key = PlotKeys.See },
                new GardenKey { UserId = Ranger.User, GardenId = Data.Meadow, Key = PlotKeys.Water },
                new GardenKey { UserId = Data.Ben.User, GardenId = Data.Meadow, Key = PlotKeys.Fence },

                // Held at the meadow by somebody the host let go: nothing reaches a plot through it.
                new GardenKey { UserId = Data.Eve.User, GardenId = Data.Meadow, Key = PlotKeys.Water },
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

            await gardens.AsAsync(Caller.System, async provider =>
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

        /// <summary>The plots somebody sees, read in memory: those it is on, and those of a garden it holds the key that sees at. Nothing for somebody let go.</summary>
        public IReadOnlyList<PlotId> SeenBy(GardenPerson person)
            => Counts(person) ? [.. Data.PlotsSeenBy(person).Union(Above(person, PlotKeys.See))] : [];

        /// <summary>The plots a key is held on, read in memory: as the gardens' own rules say it, and from above. Nothing for somebody let go.</summary>
        public IReadOnlyList<PlotId> HeldBy(GardenPerson person, string key)
            => Counts(person) ? [.. Data.PlotsHeldBy(person, key).Union(Above(person, key))] : [];

        public async Task<T> AsAsync<T>(Caller caller, Func<IServiceProvider, Task<T>> ask)
        {
            using (Callers.Begin(caller))
            {
                await using var scope = _provider.CreateAsyncScope();
                return await ask(scope.ServiceProvider);
            }
        }

        public ValueTask DisposeAsync() => _provider.DisposeAsync();

        private bool Counts(GardenPerson person) => person.User != Data.Eve.User;

        private IEnumerable<PlotId> Above(GardenPerson person, string key)
            => Data.Plots.Where(plot => _keys.Any(held => held.UserId == person.User && held.GardenId == plot.GardenId && held.Key == key)).Select(plot => plot.Id);
    }
}
