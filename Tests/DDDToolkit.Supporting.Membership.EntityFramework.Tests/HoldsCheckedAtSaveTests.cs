using DDDToolkit.EntityFramework;
using DDDToolkit.Exceptions;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests;

/// <summary>
/// The expert hold: with <c>UseMemberHolds</c> on a context, every save that changes a resource with members is held
/// to what the access check of the request in hand read of it, and the handlers write nothing for it. Without it,
/// the default path: the request's check before the handler, the version the caller named at the load, the
/// version loaded at the save.
/// </summary>
public sealed class HoldsCheckedAtSaveTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>The one line that switches the hold on, as a host writes it after <c>UseDDDToolkit</c>.</summary>
    private static readonly Action<DbContextOptionsBuilder, IServiceProvider> Holds = (options, provider) => options.UseMemberHolds(provider);

    private static Task<SqliteFiling> FilingAsync(bool holds, bool ownContexts) => SqliteFiling.SeededAsync(ownContexts: ownContexts, wiring: holds ? Holds : null);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_request_sent_through_its_checks_is_saved_with_nothing_written_in_its_handler(bool ownContexts)
    {
        using var filing = await FilingAsync(holds: true, ownContexts);
        var data = filing.Scenario;

        await filing.Services.SendAsync(TestCallers.User(data.Ada), new ShareDocument(data.Minutes, data.Hal));
        await filing.Services.SendAsync(TestCallers.Staff(data.Keeper), new AdmitStaff(data.Cabinet, new StaffCode("N-100"), FolderMembership.Clerk));

        (await filing.Services.ReadAsync(data.Minutes)).Shares.Should().Contain(share => share.MemberId == data.Hal);
        (await filing.Services.ReadAsync(data.Cabinet)).Staff.Should().Contain(member => member.MemberId == new StaffCode("N-100"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_change_between_the_check_and_the_save_is_a_lost_race(bool ownContexts)
    {
        using var filing = await FilingAsync(holds: true, ownContexts);
        var data = filing.Scenario;

        // No version named: the check read the minutes at one version, and the hold holds the save to it.
        var lost = await FluentActions.Awaiting(() => ShareWithAChangeInBetweenAsync(filing, new ShareDocument(data.Minutes, data.Hal)))
            .Should().ThrowAsync<ConcurrencyConflictException>();
        lost.Which.AggregateType.Should().Be<Document>();
        lost.Which.AggregateId.Should().Be(data.Minutes);

        var minutes = await filing.Services.ReadAsync(data.Minutes);
        minutes.Shares.Should().NotContain(share => share.MemberId == data.Hal, "the request that lost the race changed nothing");
        minutes.Shares.Should().NotContain(share => share.MemberId == data.Dee, "and what was changed in between stays");
    }

    [Fact]
    public async Task Without_the_hold_a_change_between_the_check_and_the_load_is_the_last_write_that_wins()
    {
        // The default path, for the same race: a request that named no version changes the minutes as they are when
        // its handler loads them, which is what a client that sends no version asks for.
        using var filing = await FilingAsync(holds: false, ownContexts: false);
        var data = filing.Scenario;

        await ShareWithAChangeInBetweenAsync(filing, new ShareDocument(data.Minutes, data.Hal));

        var minutes = await filing.Services.ReadAsync(data.Minutes);
        minutes.Shares.Should().Contain(share => share.MemberId == data.Hal);
        minutes.Shares.Should().NotContain(share => share.MemberId == data.Dee);
    }

    [Fact]
    public async Task Without_the_hold_a_version_the_caller_named_is_held_at_the_load()
    {
        // The default path's If-Match: the version the caller read, compared by the check and again where the handler
        // loads, so the change in between is a lost race here too.
        using var filing = await FilingAsync(holds: false, ownContexts: false);
        var data = filing.Scenario;
        var read = (await filing.Services.ReadAsync(data.Minutes)).Version;

        await FluentActions.Awaiting(() => ShareWithAChangeInBetweenAsync(filing, new ShareDocument(data.Minutes, data.Hal, ExpectedVersion: read)))
            .Should().ThrowAsync<ConcurrencyConflictException>();

        (await filing.Services.ReadAsync(data.Minutes)).Shares.Should().NotContain(share => share.MemberId == data.Hal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_handler_called_without_its_checks_saves_nothing(bool ownContexts)
    {
        using var filing = await FilingAsync(holds: true, ownContexts);
        var data = filing.Scenario;

        // Ada owns the minutes and may share them: what is refused is the way round the check, not the caller.
        var refused = await FluentActions.Awaiting(() => filing.Services.AsAsync(TestCallers.User(data.Ada), provider =>
                provider.GetRequiredService<ShareDocumentHandler>().HandleAsync(new ShareDocument(data.Minutes, data.Hal), Cancellation)))
            .Should().ThrowAsync<InvalidOperationException>();
        refused.WithMessage($"Document {data.Minutes} was changed with no request in hand*Nothing was saved.");

        (await filing.Services.ReadAsync(data.Minutes)).Shares.Should().NotContain(share => share.MemberId == data.Hal);
    }

    [Fact]
    public async Task Without_the_hold_a_handler_called_without_its_checks_saves_what_its_caller_may()
    {
        // The default path leaves the way round the check to whatever checks every row, a database with row level
        // security: here there is none, and the handler shares as it would have behind the check.
        using var filing = await FilingAsync(holds: false, ownContexts: false);
        var data = filing.Scenario;

        await filing.Services.AsAsync(TestCallers.User(data.Ada), provider =>
            provider.GetRequiredService<ShareDocumentHandler>().HandleAsync(new ShareDocument(data.Minutes, data.Hal), Cancellation));

        (await filing.Services.ReadAsync(data.Minutes)).Shares.Should().Contain(share => share.MemberId == data.Hal);
    }

    [Fact]
    public async Task A_handler_that_changes_another_resource_than_its_request_names_is_refused()
    {
        using var filing = await FilingAsync(holds: true, ownContexts: false);
        var data = filing.Scenario;

        // Ada owns the minutes, and holds the key that shares there: the request checked the minutes, and its
        // handler, by mistake, shares the budget, which she only looks on.
        var refused = await FluentActions.Awaiting(() => filing.Services.AsAsync(TestCallers.User(data.Ada), async provider =>
            {
                var command = new ShareDocument(data.Minutes, data.Hal);
                await provider.GetRequiredService<AccessChecks<IFilingRequest>>().RequireAsync(command, Cancellation);

                var context = provider.GetRequiredService<FilingContext>();
                var budget = await context.Documents.AsTracking().SingleAsync(document => document.Id == data.Budget, Cancellation);
                budget.ShareWith(command.With, MemberPeriod.Open(data.Now), data.Now, by: data.Ada);
                await context.SaveChangesAsync(Cancellation);
            }))
            .Should().ThrowAsync<InvalidOperationException>();
        refused.WithMessage($"Document {data.Budget} was changed in the handling of ShareDocument, whose access check read Document {data.Minutes}*");

        (await filing.Services.ReadAsync(data.Budget)).Shares.Should().NotContain(share => share.MemberId == data.Hal);
    }

    [Fact]
    public async Task The_application_s_own_work_needs_no_check_and_a_flow_nobody_began_a_caller_for_is_not_it()
    {
        using var filing = await FilingAsync(holds: true, ownContexts: false);
        var data = filing.Scenario;

        // Begun by trusted code: saved.
        await filing.Services.ChangeAsync(data.Minutes, minutes => minutes.Unshare(data.Dee));
        (await filing.Services.ReadAsync(data.Minutes)).Shares.Should().NotContain(share => share.MemberId == data.Dee);

        // Nobody began a caller: the host answers the system for it, which is no proof the application did this.
        var refused = await FluentActions.Awaiting(async () =>
            {
                await using var scope = filing.Services.Provider.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<FilingContext>();
                (await context.Documents.AsTracking().SingleAsync(document => document.Id == data.Minutes, Cancellation)).Unshare(data.Fay);
                await context.SaveChangesAsync(Cancellation);
            })
            .Should().ThrowAsync<InvalidOperationException>();
        refused.WithMessage("*no request in hand*");
    }

    [Fact]
    public async Task A_new_resource_needs_no_hold()
    {
        using var filing = await FilingAsync(holds: true, ownContexts: false);
        var data = filing.Scenario;
        var written = DocumentId.CreateSequential();

        // Nothing was there to check: writing a document is held to whatever its own request requires, and to nothing here.
        await filing.Services.AsAsync(TestCallers.User(data.Hal), async provider =>
        {
            var context = provider.GetRequiredService<FilingContext>();
            context.Documents.Add(new Document(written, "Notes", data.Hal, DocumentMembership.Owner, data.Now));
            await context.SaveChangesAsync(Cancellation);
        });

        (await filing.Services.ReadAsync(written)).OwnerId.Should().Be(data.Hal);
    }

    [Fact]
    public async Task A_handling_that_saves_the_resource_twice_is_held_to_the_check_once()
    {
        using var filing = await FilingAsync(holds: true, ownContexts: false);
        var data = filing.Scenario;

        await filing.Services.AsAsync(TestCallers.User(data.Ada), async provider =>
        {
            var command = new ShareDocument(data.Minutes, data.Hal);
            await provider.GetRequiredService<AccessChecks<IFilingRequest>>().RequireAsync(command, Cancellation);

            var context = provider.GetRequiredService<FilingContext>();
            var minutes = await context.Documents.AsTracking().SingleAsync(document => document.Id == data.Minutes, Cancellation);
            minutes.ShareWith(data.Hal, MemberPeriod.Open(data.Now), data.Now, by: data.Ada);
            await context.SaveChangesAsync(Cancellation);

            // The first save moved the version on; the second compares the one the first left, as every save does.
            minutes.Unshare(data.Dee);
            await context.SaveChangesAsync(Cancellation);
        });

        var shares = (await filing.Services.ReadAsync(data.Minutes)).Shares;
        shares.Should().Contain(share => share.MemberId == data.Hal);
        shares.Should().NotContain(share => share.MemberId == data.Dee);
    }

    [Fact]
    public async Task A_save_tried_again_after_it_lost_the_race_loses_again()
    {
        using var filing = await FilingAsync(holds: true, ownContexts: false);
        var data = filing.Scenario;

        // The handler loads the minutes at the version the check read, and somebody changes them before the save: the
        // save loses the race. Tried again the way Entity Framework resolves a conflict, with the stored values taken
        // as the loaded ones, it is at a version the check never read, and loses again.
        var lost = await FluentActions.Awaiting(() => filing.Services.AsAsync(TestCallers.User(data.Ada), async provider =>
            {
                var command = new ShareDocument(data.Minutes, data.Hal);
                await provider.GetRequiredService<AccessChecks<IFilingRequest>>().RequireAsync(command, Cancellation);

                var context = provider.GetRequiredService<FilingContext>();
                var minutes = await context.Documents.AsTracking().SingleAsync(document => document.Id == data.Minutes, Cancellation);
                await ChangeInBetweenAsync(filing);
                minutes.ShareWith(command.With, MemberPeriod.Open(data.Now), data.Now, by: data.Ada);

                await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<ConcurrencyConflictException>("the first save lost the race");

                var entry = context.Entry(minutes);
                entry.OriginalValues.SetValues((await entry.GetDatabaseValuesAsync(Cancellation))!);
                await context.SaveChangesAsync(Cancellation);
            }))
            .Should().ThrowAsync<ConcurrencyConflictException>();
        lost.Which.AggregateId.Should().Be(data.Minutes);

        (await filing.Services.ReadAsync(data.Minutes)).Shares.Should().NotContain(share => share.MemberId == data.Hal, "neither save changed anything");
    }

    [Fact]
    public async Task A_save_after_the_request_s_handling_returned_is_refused()
    {
        using var filing = await FilingAsync(holds: true, ownContexts: false);
        var data = filing.Scenario;

        // A unit of work around the dispatcher: the dispatcher asks the checks and runs the handler, which changes the
        // minutes and leaves the save to whatever sent it. When that saves, the request's handling has returned.
        var refused = await FluentActions.Awaiting(() => filing.Services.AsAsync(TestCallers.User(data.Ada), async provider =>
            {
                var context = provider.GetRequiredService<FilingContext>();
                await DispatchAsync(provider, context, new ShareDocument(data.Minutes, data.Hal));
                await context.SaveChangesAsync(Cancellation);
            }))
            .Should().ThrowAsync<InvalidOperationException>();
        refused.WithMessage($"Document {data.Minutes} was changed with no request in hand*or the save ran after the request's handling returned*");

        (await filing.Services.ReadAsync(data.Minutes)).Shares.Should().NotContain(share => share.MemberId == data.Hal);

        async Task DispatchAsync(IServiceProvider provider, FilingContext context, ShareDocument command)
        {
            await provider.GetRequiredService<AccessChecks<IFilingRequest>>().RequireAsync(command, Cancellation);
            var minutes = await context.Documents.AsTracking().SingleAsync(document => document.Id == command.Document, Cancellation);
            minutes.ShareWith(command.With, MemberPeriod.Open(data.Now), data.Now, by: data.Ada);
        }
    }

    [Fact]
    public async Task A_model_that_would_not_compare_the_version_is_refused_at_the_first_held_save()
    {
        using var filing = await FilingAsync(holds: false, ownContexts: false);
        var data = filing.Scenario;

        var refused = await FluentActions.Awaiting(() => filing.Services.AsAsync(TestCallers.User(data.Ada), async provider =>
            {
                var command = new ShareDocument(data.Minutes, data.Hal);
                await provider.GetRequiredService<AccessChecks<IFilingRequest>>().RequireAsync(command, Cancellation);

                // A context over the same database whose model leaves the version out of what the save compares.
                var options = new DbContextOptionsBuilder<UncomparedContext>().UseSqlite(provider.GetRequiredService<FilingContext>().Database.GetDbConnection());
                options.UseDDDToolkit(provider).UseMemberHolds(provider);
                await using var context = new UncomparedContext(options.Options);
                var minutes = await context.Documents.AsTracking().SingleAsync(document => document.Id == data.Minutes, Cancellation);
                minutes.ShareWith(command.With, MemberPeriod.Open(data.Now), data.Now, by: data.Ada);
                await context.SaveChangesAsync(Cancellation);
            }))
            .Should().ThrowAsync<InvalidOperationException>();
        refused.WithMessage("The model of UncomparedContext does not map Document.Version as a concurrency token*AddDDDToolkitConventions*");

        (await filing.Services.ReadAsync(data.Minutes)).Shares.Should().NotContain(share => share.MemberId == data.Hal);
    }

    [Fact]
    public async Task A_context_asks_for_the_hold_after_the_toolkit_s_interceptors()
    {
        using var filing = await FilingAsync(holds: false, ownContexts: false);
        var provider = filing.Services.Provider;

        // Before UseDDDToolkit, the hold would run before the domain event handlers, and hold nothing they change.
        FluentActions.Invoking(() => new DbContextOptionsBuilder<FilingContext>().UseMemberHolds(provider))
            .Should().Throw<InvalidOperationException>().WithMessage("UseMemberHolds comes after UseDDDToolkit*options.UseDDDToolkit(serviceProvider).UseMemberHolds(serviceProvider)*");

        new DbContextOptionsBuilder<FilingContext>().UseDDDToolkit(provider).UseMemberHolds(provider)
            .Options.FindExtension<Microsoft.EntityFrameworkCore.Infrastructure.CoreOptionsExtension>()!.Interceptors!
            .Select(interceptor => interceptor.GetType().Name)
            .Should().EndWith("MemberHoldInterceptor");
    }

    [Fact]
    public void A_context_that_asks_for_the_hold_without_a_resource_registered_is_refused()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();

        FluentActions.Invoking(() => new DbContextOptionsBuilder().UseMemberHolds(provider))
            .Should().Throw<InvalidOperationException>().WithMessage("UseMemberHolds found no resource with members*");
    }

    /// <summary>
    /// Shares the minutes as Ada through the request's checks and its handler, with somebody else's change to the
    /// minutes made between the two.
    /// </summary>
    private static Task ShareWithAChangeInBetweenAsync(SqliteFiling filing, ShareDocument command)
        => filing.Services.AsAsync(TestCallers.User(filing.Scenario.Ada), async provider =>
        {
            await provider.GetRequiredService<AccessChecks<IFilingRequest>>().RequireAsync(command, Cancellation);
            await ChangeInBetweenAsync(filing);
            await provider.GetRequiredService<ShareDocumentHandler>().HandleAsync(command, Cancellation);
        });

    /// <summary>
    /// Somebody else's change to the minutes, taking Dee off: in a flow of its own, as another request arriving in
    /// between is, so nothing of the request in hand comes with it.
    /// </summary>
    private static async Task ChangeInBetweenAsync(SqliteFiling filing)
    {
        Task meanwhile;
        using (ExecutionContext.SuppressFlow())
        {
            meanwhile = Task.Run(() => filing.Services.ChangeAsync(filing.Scenario.Minutes, minutes => minutes.Unshare(filing.Scenario.Dee)), Cancellation);
        }

        await meanwhile;
    }

    /// <summary>The documents mapped as the host's context maps them, with their version left out of what the save compares.</summary>
    private sealed class UncomparedContext(DbContextOptions<UncomparedContext> options) : DbContext(options)
    {
        public DbSet<Document> Documents => Set<Document>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema(FilingContext.Schema);
            modelBuilder.Entity<Document>(document =>
            {
                document.Property(row => row.Id).ValueGeneratedNever();
                document.Property(row => row.Title).HasMaxLength(200);
                document.HasMembers(row => row.Shares, row => row.OwnerId);
                document.Property(row => row.Version).IsConcurrencyToken(false);
            });
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            DDDToolkit.EntityFramework.Conventions.ModelConfigurationBuilderExtensions.AddDDDToolkitConventions(configurationBuilder);
            DDDToolkit.Supporting.Membership.TestHost.Converters.ConverterExtensions.AddFilingConverters(configurationBuilder);
        }
    }
}
