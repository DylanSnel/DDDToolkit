using DDDToolkit.EntityFramework;
using DDDToolkit.Supporting.Membership.EntityFramework;
using Microsoft.Data.Sqlite;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests;

/// <summary>
/// The statement of a check on one resource, or on a list of them, compiled once for each shape it takes: one statement
/// per check, with the caller's values sent as parameters, and the query filters of the context it runs on.
/// </summary>
public sealed class CompiledHoldsTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_callers_asking_for_one_permission_send_the_same_statement_with_their_own_values()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        // Ian manages the minutes, and Ben only contributes: each answer is the caller's own.
        var ian = await filing.DocumentsAsync(TestUsers.User(data.Ian), access => access.HoldAsync(data.Minutes, DocumentPermissions.Share, Cancellation));
        var ben = await filing.DocumentsAsync(TestUsers.User(data.Ben), access => access.HoldAsync(data.Minutes, DocumentPermissions.Share, Cancellation));
        var ada = await filing.DocumentsAsync(TestUsers.User(data.Ada), access => access.HoldAsync(data.Minutes, DocumentPermissions.Share, Cancellation));

        ian!.Via.Should().Be(MemberVia.Members);
        ben!.Via.Should().BeNull("Ben sees the document, and a contributor's role does not give the permission that shares");
        ada!.Via.Should().Be(MemberVia.Members, "the owner holds it by owning the document");
        ada.Until.Should().BeNull("an owner holds a permission of the resource with no end");

        var statements = filing.Commands.Commands;
        statements.Should().HaveCount(3, "each check is one statement");
        statements.Distinct().Should().ContainSingle("the statement has one shape for every caller, and the callers' values are its parameters");
    }

    [Fact]
    public async Task A_list_of_resources_is_read_in_one_compiled_statement_with_a_row_for_each_resource_the_caller_sees()
    {
        using var filing = await SqliteFiling.SeededAsync();
        var data = filing.Scenario;

        var version = (await filing.Services.ReadAsync(data.Minutes)).Version;
        filing.Commands.Reset();

        var held = await filing.DocumentsAsync(TestUsers.User(data.Ada), access => access.HoldEachAsync([data.Minutes, data.Budget, data.Outline], DocumentPermissions.View, Cancellation));

        held.Keys.Should().BeEquivalentTo([data.Minutes, data.Budget], "Ada owns the minutes, looks on at the budget, and does not see the outline");
        held[data.Minutes].Version.Should().Be(version);
        filing.Commands.Commands.Should().ContainSingle();
    }

    [Fact]
    public async Task The_compiled_statement_reads_the_query_filter_of_the_context_it_runs_on_for_every_check()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(Cancellation);

        var services = new ServiceCollection();
        services.AddDDDToolkitEntityFramework(options => options.DispatchInProcess((_, _, _) => Task.CompletedTask));
        services.AddMembership<DocumentShare, DocumentShareId, UserId, NamedRole, ArchiveHidingContext, Document, DocumentId>(DocumentMembership.Rules);
        services.AddScoped<ArchiveView>();
        services.AddDbContext<ArchiveHidingContext>((provider, options) => options.UseSqlite(connection).UseDDDToolkit(provider));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var owner = UserId.Create();
        var archived = DocumentId.Create();
        using (TestAccess.BeginAs(CurrentUserSnapshot.System))
        {
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ArchiveHidingContext>();
            await context.Database.EnsureCreatedAsync(Cancellation);
            var document = new Document(archived, "Old minutes", owner, DateTimeOffset.UtcNow.AddDays(-1));
            document.MoveToArchive();
            context.Documents.Add(document);
            await context.SaveChangesAsync(Cancellation);
        }

        async Task<MemberHold<DocumentId>?> AskAsync(bool hideArchived)
        {
            using (TestAccess.BeginAs(TestUsers.User(owner)))
            {
                await using var scope = provider.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ArchiveView>().HideArchived = hideArchived;
                return await scope.ServiceProvider.GetRequiredService<MembershipAccessManager<Document>>().HoldAsync(archived, DocumentPermissions.Edit, Cancellation);
            }
        }

        // The filter reads a member of the context, which each scope sets: the same compiled statement follows it each time.
        (await AskAsync(hideArchived: true)).Should().BeNull("the context of this scope hides archived documents");
        (await AskAsync(hideArchived: false))!.Via.Should().Be(MemberVia.Members);
        (await AskAsync(hideArchived: true)).Should().BeNull();
    }

    /// <summary>Whether the documents of a scope's context hide the archived ones.</summary>
    private sealed class ArchiveView
    {
        public bool HideArchived { get; set; }
    }

    /// <summary>The documents, mapped as the host's context maps them, with a filter that reads a member of the context.</summary>
    private sealed class ArchiveHidingContext(DbContextOptions<ArchiveHidingContext> options, ArchiveView view) : DbContext(options)
    {
        public DbSet<Document> Documents => Set<Document>();

        /// <summary>Whether this context hides the archived documents: what the filter reads.</summary>
        public bool HideArchived => view.HideArchived;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Document>(document =>
            {
                document.Property(row => row.Id).ValueGeneratedNever();
                document.Property(row => row.Title).HasMaxLength(200);
                document.HasMembers(row => row.Shares);
                document.HasQueryFilter(row => !HideArchived || !row.Archived);
            });
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            DDDToolkit.EntityFramework.Conventions.ModelConfigurationBuilderExtensions.AddDDDToolkitConventions(configurationBuilder);
            DDDToolkit.Supporting.Membership.TestHost.Converters.ConverterExtensions.AddFilingConverters(configurationBuilder);
        }
    }
}
