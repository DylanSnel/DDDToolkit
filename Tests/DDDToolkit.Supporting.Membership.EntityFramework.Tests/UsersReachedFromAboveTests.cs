using DDDToolkit.Abstractions.Access;
using Microsoft.Data.Sqlite;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests;

/// <summary>
/// Reach from above with nothing else of an organization: documents whose members are plain users, known by
/// the id of their token, with the roles their rules declare, as a document always had them. Of the three
/// things rules say, only one is said another way: a document sits at its owner, and a user's deputy, who
/// holds a permission over everything that user owns, holds it on the document without it being shared with them.
/// So where a resource sits is whatever its row keeps, and reach from above needs neither members the
/// application resolves nor roles kept elsewhere.
/// </summary>
public sealed class UsersReachedFromAboveTests
{
    private static readonly MembershipRules Rules = MembershipRules.For<Document>(
        Roles.Fixed([new("contributor", [DocumentPermissions.View, DocumentPermissions.Edit]), new("onlooker", [DocumentPermissions.View])]),
        reachFromAbove: true);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_document_of_plain_users_with_roles_of_its_own_is_held_from_above_by_its_owners_deputy()
    {
        await using var desk = await Desk.SeededAsync();

        // Cy is no member of anything. Ada made him her deputy for seeing and changing what she owns: he holds
        // both on her minutes, from above, with no end known here, and not the permission nobody gave him.
        var view = await desk.HoldAsync(desk.Cy, desk.Minutes, DocumentPermissions.View);
        (view!.Via, view.Until).Should().Be((MemberVia.Above, null));
        (await desk.HoldAsync(desk.Cy, desk.Minutes, DocumentPermissions.Edit))!.Via.Should().Be(MemberVia.Above);
        (await desk.HoldAsync(desk.Cy, desk.Minutes, DocumentPermissions.Share))!.Via.Should().BeNull("he sees the minutes, and nobody gave him that permission");

        // What Ben owns is not Ada's: it sits at another owner, and is not there for her deputy.
        (await desk.HoldAsync(desk.Cy, desk.Notes, DocumentPermissions.View)).Should().BeNull();

        // The members hold what they always held: the owner every permission by owning, a member what its role gives.
        var share = await desk.HoldAsync(desk.Ada, desk.Minutes, DocumentPermissions.Share);
        (share!.Via, share.Until).Should().Be((MemberVia.Members, null));
        var read = await desk.HoldAsync(desk.Ben, desk.Minutes, DocumentPermissions.View);
        (read!.Via, read.Until).Should().Be((MemberVia.Members, desk.NextWeek));
        (await desk.HoldAsync(desk.Ben, desk.Minutes, DocumentPermissions.Edit))!.Via.Should().BeNull("an onlooker reads");

        // Somebody who is nobody's member and nobody's deputy reaches nothing.
        (await desk.HoldAsync(desk.Eve, desk.Minutes, DocumentPermissions.View)).Should().BeNull();
    }

    [Fact]
    public async Task A_deputy_that_does_not_hold_the_permission_that_sees_holds_a_permission_only_on_what_is_shared_with_it_and_only_that_long()
    {
        await using var desk = await Desk.SeededAsync();

        // Dee changes what Ada owns, and holds the permission that sees over nobody. The minutes are shared with her
        // until next week, so they are there for her until then, and so is what she holds on them from above.
        var edit = await desk.HoldAsync(desk.Dee, desk.Minutes, DocumentPermissions.Edit);
        (edit!.Via, edit.Until).Should().Be((MemberVia.Above, desk.NextWeek));

        // The agenda is Ada's as well, and shared with nobody: within the reach of the permission, and not there for her.
        (await desk.HoldAsync(desk.Dee, desk.Agenda, DocumentPermissions.Edit)).Should().BeNull();
        (await desk.WithinAsync(desk.Dee, DocumentPermissions.Edit)).Should().BeEquivalentTo([desk.Minutes, desk.Agenda]);
        (await desk.WithinAsync(desk.Dee, DocumentPermissions.View)).Should().BeEquivalentTo([desk.Minutes]);
        (await desk.PermissionsOnAsync(desk.Dee)).Should().ContainSingle().Which.Should().Be((desk.Minutes, "documents.edit documents.view"));

        // At the moment her membership ends, the minutes are gone for her with everything she held on them.
        desk.Clock.Advance(desk.NextWeek - desk.Clock.Now - TimeSpan.FromTicks(1));
        (await desk.HoldAsync(desk.Dee, desk.Minutes, DocumentPermissions.Edit))!.Via.Should().Be(MemberVia.Above, "one tick before the end the hold named");
        desk.Clock.Advance(TimeSpan.FromTicks(1));
        (await desk.HoldAsync(desk.Dee, desk.Minutes, DocumentPermissions.Edit)).Should().BeNull("at the end the hold named");
        (await desk.PermissionsOnAsync(desk.Dee)).Should().BeEmpty();
    }

    [Fact]
    public async Task The_permissions_held_on_many_documents_and_a_list_of_the_hosts_own_are_one_statement_each_with_the_deputies_inside()
    {
        await using var desk = await Desk.SeededAsync();

        // Cy sees what Ada owns from above, and nothing of Ben's.
        desk.Commands.Reset();
        (await desk.PermissionsOnAsync(desk.Cy)).Should().BeEquivalentTo(
            [(desk.Minutes, "documents.edit documents.view"), (desk.Agenda, "documents.edit documents.view")]);
        desk.Commands.Count.Should().Be(1);

        desk.Commands.Reset();
        (await desk.WithinAsync(desk.Cy, DocumentPermissions.View)).Should().BeEquivalentTo([desk.Minutes, desk.Agenda]);
        desk.Commands.Commands.Should().ContainSingle().Which.Should().Contain("\"Deputies\"", "where a permission is held above is a subquery of the statement, and no list fetched first");

        // The roles are values of the rules, as ever: a member's role is asked of no table but the document's own.
        desk.Commands.Reset();
        (await desk.HoldAsync(desk.Ben, desk.Minutes, DocumentPermissions.Edit))!.Via.Should().BeNull();
        desk.Commands.Commands.Should().ContainSingle().Which.Should().Contain("\"DocumentShareRoles\"").And.Contain("\"Deputies\"");

        // A document handed to another owner sits at that owner from then on: reached from where it sits now.
        await desk.ChangeAsync(async context =>
            (await context.Documents.SingleAsync(document => document.Id == desk.Agenda, Cancellation)).HandOver(desk.Ben, FormerOwner.StaysAs(DocumentMembership.Onlooker), desk.Clock.Now));
        (await desk.HoldAsync(desk.Cy, desk.Agenda, DocumentPermissions.View)).Should().BeNull();
        (await desk.HoldAsync(desk.Cy, desk.Minutes, DocumentPermissions.View))!.Via.Should().Be(MemberVia.Above);
    }

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

    /// <summary>A context that keeps the documents where their owner is, and who is whose deputy.</summary>
    private sealed class DeskContext(DbContextOptions<DeskContext> options) : DbContext(options)
    {
        public DbSet<Document> Documents => Set<Document>();

        public DbSet<Deputy> Deputies => Set<Deputy>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
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
    /// The documents, their owners and the owners' deputies over one SQLite database in memory. Ada owns the
    /// minutes, shared with Ben as an onlooker and with Dee, without a role, both until next week, and the
    /// agenda, shared with nobody. Ben owns the notes. Cy is Ada's deputy for seeing and changing, Dee for
    /// changing alone, and Eve is nobody's anything.
    /// </summary>
    private sealed class Desk : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("DataSource=:memory:");
        private readonly ServiceProvider _provider;

        private Desk()
        {
            _connection.Open();
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton(Commands);
            services.AddDbContext<DeskContext>((provider, options) => options.UseSqlite(_connection).AddInterceptors(provider.GetRequiredService<CommandCounter>()));

            // Only what the rules say otherwise needs an answer: where a permission is held above. Who a member is,
            // is the caller's own id, and the roles are the rules' own.
            services.AddScoped<IPlacesReached<DocumentId, UserId>, DeputiesOfOwners>();
            services.AddMembership<DocumentShare, DocumentShareId, UserId, NamedRole, DeskContext, Document, DocumentId>(Rules);
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            NextWeek = Clock.Now.AddDays(7);
        }

        public FixedClock Clock { get; } = new();

        public CommandCounter Commands { get; } = new();

        public DateTimeOffset NextWeek { get; }

        public UserId Ada { get; } = UserId.CreateSequential();

        public UserId Ben { get; } = UserId.CreateSequential();

        public UserId Cy { get; } = UserId.CreateSequential();

        public UserId Dee { get; } = UserId.CreateSequential();

        public UserId Eve { get; } = UserId.CreateSequential();

        public DocumentId Minutes { get; } = DocumentId.CreateSequential();

        public DocumentId Agenda { get; } = DocumentId.CreateSequential();

        public DocumentId Notes { get; } = DocumentId.CreateSequential();

        public static async Task<Desk> SeededAsync()
        {
            var desk = new Desk();
            var before = desk.Clock.Now.AddDays(-10);
            var period = MemberPeriod.Between(before, desk.NextWeek);

            var minutes = new Document(desk.Minutes, "Minutes", desk.Ada, before);
            minutes.ShareWith(desk.Ben, DocumentMembership.Onlooker, period, desk.Clock.Now, by: desk.Ada);
            minutes.ShareWith(desk.Dee, period, desk.Clock.Now, by: desk.Ada);
            var agenda = new Document(desk.Agenda, "Agenda", desk.Ada, before);
            var notes = new Document(desk.Notes, "Notes", desk.Ben, before);

            await desk.ChangeAsync(async context =>
            {
                await context.Database.EnsureCreatedAsync(Cancellation);
                context.Documents.AddRange(minutes, agenda, notes);
                context.Deputies.AddRange(
                    new Deputy { Of = desk.Ada, User = desk.Cy.Value, Key = DocumentPermissions.View },
                    new Deputy { Of = desk.Ada, User = desk.Cy.Value, Key = DocumentPermissions.Edit },
                    new Deputy { Of = desk.Ada, User = desk.Dee.Value, Key = DocumentPermissions.Edit });
            });
            desk.Commands.Reset();
            return desk;
        }

        /// <summary>What <paramref name="user"/> holds of <paramref name="permission"/> on a document, as the access questions answer it.</summary>
        public Task<MemberHold<DocumentId>?> HoldAsync(UserId user, DocumentId document, string permission)
            => AsAsync(TestUsers.User(user), provider => provider.GetRequiredService<MembershipAccessManager<Document>>().HoldAsync(document, permission, Cancellation));

        /// <summary>The documents within the reach of <paramref name="permission"/>, in a statement of the host's own.</summary>
        public Task<List<DocumentId>> WithinAsync(UserId user, string permission)
            => AsAsync(TestUsers.User(user), provider =>
            {
                var reach = provider.GetRequiredService<MembershipAccessManager<Document>>().Reach(permission);
                return provider.GetRequiredService<DeskContext>().Documents.Within(reach).Select(document => document.Id).ToListAsync(Cancellation);
            });

        /// <summary>The permissions <paramref name="user"/> holds on each of the three documents it sees, in the order of their text.</summary>
        public async Task<List<(DocumentId Document, string Keys)>> PermissionsOnAsync(UserId user)
        {
            var held = await AsAsync(TestUsers.User(user), provider => provider.GetRequiredService<MembershipAccessManager<Document>>()
                .PermissionsOnAsync([Minutes, Agenda, Notes], [DocumentPermissions.View, DocumentPermissions.Edit, DocumentPermissions.Share], Cancellation));

            return [.. held.Select(pair => (pair.Key, string.Join(' ', pair.Value.Order(StringComparer.Ordinal))))];
        }

        /// <summary>Changes the rows as the application's own work, and saves.</summary>
        public Task ChangeAsync(Func<DeskContext, Task> change)
            => AsAsync(CurrentUserSnapshot.System, async provider =>
            {
                var context = provider.GetRequiredService<DeskContext>();
                await change(context);
                await context.SaveChangesAsync(Cancellation);
                return true;
            });

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private async Task<T> AsAsync<T>(CurrentUserSnapshot caller, Func<IServiceProvider, Task<T>> ask)
        {
            using (TestAccess.BeginAs(caller))
            {
                await using var scope = _provider.CreateAsyncScope();
                return await ask(scope.ServiceProvider);
            }
        }
    }
}
