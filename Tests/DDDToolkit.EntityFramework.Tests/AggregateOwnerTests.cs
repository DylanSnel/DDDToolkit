using System.Data.Common;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Conventions;
using DDDToolkit.EntityFramework.Tests.Converters;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.Exceptions;
using DDDToolkit.Startup;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Tests;

// A sorting hub of letters, each carried by one postman or by nobody yet: the postman is the letter's owner, a fact of the
// domain, and only a letter's postman delivers it, a policy of the request. A hub names no owner.

[EntityId<Guid>]
public readonly partial record struct LetterId;

[EntityId<Guid>]
public readonly partial record struct PostmanId;

[EntityId<Guid>]
public readonly partial record struct HubId;

[AggregateRoot<LetterId>]
public partial class Letter
{
    public Letter(LetterId id, string label, PostmanId? postman) : base(id)
    {
        Label = label;
        Postman = postman;
    }

    public string Label { get; private set; }

    /// <summary>Who carries the letter, or nobody yet.</summary>
    [Owner]
    public PostmanId? Postman { get; private set; }
}

[AggregateRoot<HubId>]
public partial class Hub
{
    public Hub(HubId id, string name) : base(id) => Name = name;

    public string Name { get; private set; }
}

/// <summary>The application's handle: the postman a caller is, by the id its token names.</summary>
public sealed class CallersPostman : ICallersOwn<PostmanId>
{
    public PostmanId? Find(ICurrentUser user) => user.Id is { } id ? new PostmanId(id) : null;
}

/// <summary>What every request of the hub implements: the generator writes what each requires into it.</summary>
[AccessRequests]
public partial interface ILettersRequest : IRequireAccess;

/// <summary>Only the letter's postman delivers it: the owner alone, of the letter the request names.</summary>
[OwnOnly]
public sealed record DeliverLetter([Own] LetterId Letter) : ILettersRequest;

/// <summary>A request about the owner of a hub, which names none: what a hand-written requirement could say.</summary>
public sealed record CloseHub(HubId Hub) : ILettersRequest
{
    AccessRequirement IRequireAccess.RequiredAccess => AccessRequirement.OwnerOf(Hub);
}

public sealed class LettersContext(DbContextOptions<LettersContext> options) : DbContext(options)
{
    public DbSet<Letter> Letters => Set<Letter>();

    public DbSet<Hub> Hubs => Set<Hub>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddEfTestsConverters();
    }
}

/// <summary>A second context that maps the letters as well, the way a read model of another context might.</summary>
public sealed class LettersReadContext(DbContextOptions<LettersReadContext> options) : DbContext(options)
{
    public DbSet<Letter> Letters => Set<Letter>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddDDDToolkitConventions();
        configurationBuilder.AddEfTestsConverters();
    }
}

/// <summary>
/// The owner of an aggregate, read by the Entity Framework package for the core's check of <c>[Own]</c> on the aggregate's
/// id: <c>AddDDDToolkitEntityFramework</c> registers the reader for every id, it finds the context that maps the aggregate
/// among the registered ones, and reads the column the aggregate marks <c>[Owner]</c>, of one row, in one statement. On
/// SQLite nothing runs as the caller; what row level security hides is the Postgres tests'.
/// </summary>
public sealed class AggregateOwnerTests : IDisposable
{
    private static readonly Guid Ann = Guid.Parse("a11ce000-0000-4000-8000-000000000031");

    private static readonly Guid Bob = Guid.Parse("b0b00000-0000-4000-8000-000000000032");

    private static readonly LetterId AnnsLetter = new(Guid.Parse("9a7c0000-0000-4000-8000-000000000001"));

    private static readonly LetterId BobsLetter = new(Guid.Parse("9a7c0000-0000-4000-8000-000000000002"));

    private static readonly LetterId NobodysLetter = new(Guid.Parse("9a7c0000-0000-4000-8000-000000000003"));

    private static readonly LetterId NoLetter = new(Guid.Parse("9a7c0000-0000-4000-8000-000000000004"));

    private readonly SqliteDatabase _database = new();

    private readonly Statements _statements = new();

    public AggregateOwnerTests()
    {
        _database.EnsureCreated(() => new LettersContext(_database.Options<LettersContext>()));
        using var seed = new LettersContext(_database.Options<LettersContext>());
        seed.Letters.AddRange(
            new Letter(AnnsLetter, "Books", new PostmanId(Ann)),
            new Letter(BobsLetter, "Plates", new PostmanId(Bob)),
            new Letter(NobodysLetter, "Lamp", postman: null));
        seed.Hubs.Add(new Hub(HubId.Create(), "North"));
        seed.SaveChanges();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void The_request_s_line_is_the_owner_of_the_letter_it_names()
    {
        // Written by the generator from [OwnOnly] and [Own] LetterId: the letter's id has no own, and the letter marks its owner.
        ((IRequireAccess)new DeliverLetter(AnnsLetter)).RequiredAccess.Should().Be(AccessRequirement.OwnerOf(AnnsLetter));
    }

    [Fact]
    public async Task The_postman_of_a_letter_delivers_it_and_nobody_else_does()
    {
        await using var host = Host();

        await AskAsync(host, CurrentUserSnapshot.User(Ann), new DeliverLetter(AnnsLetter));

        (await RefusedAsync(host, CurrentUserSnapshot.User(Ann), new DeliverLetter(BobsLetter))).Code.Should().Be(ToolkitRefusals.NotOwn, "Bob carries it");
        (await RefusedAsync(host, CurrentUserSnapshot.User(Ann), new DeliverLetter(NobodysLetter))).Code.Should().Be(ToolkitRefusals.NotOwn, "a letter nobody carries is nobody's");
        var missing = await RefusedAsync(host, CurrentUserSnapshot.User(Ann), new DeliverLetter(NoLetter));
        missing.Code.Should().Be(ToolkitRefusals.NotFound);
        missing.Kind.Should().Be(RefusalKind.NotFound);
    }

    [Fact]
    public async Task One_statement_reads_the_owner_s_column_of_one_row_and_nothing_else()
    {
        await using var host = Host();

        await AskAsync(host, CurrentUserSnapshot.User(Ann), new DeliverLetter(AnnsLetter));

        var statement = _statements.Read.Should().ContainSingle("one row is read, once").Subject;
        statement.Should().Contain("\"Postman\"").And.Contain("FROM \"Letters\"").And.Contain("LIMIT");
        statement.Should().NotContain("\"Label\"").And.NotContain("\"Version\"", "only the owner's column is read");
    }

    [Fact]
    public async Task A_caller_who_is_no_postman_is_refused_before_anything_is_read()
    {
        await using var host = Host();

        (await RefusedAsync(host, CurrentUserSnapshot.Anonymous, new DeliverLetter(AnnsLetter))).Code.Should().Be(ToolkitRefusals.NotOwn);
        (await RefusedAsync(host, CurrentUserSnapshot.System, new DeliverLetter(AnnsLetter))).Code.Should().Be(ToolkitRefusals.NotOwn, "the application's own work carries no letter");
        _statements.Read.Should().BeEmpty("nothing is sent for a caller who owns nothing");
    }

    [Fact]
    public async Task A_context_of_a_factory_is_read_on_as_well_as_one_of_the_scope()
    {
        var services = Services();
        services.AddDbContextFactory<LettersContext>((provider, options) => Configure(provider, options));
        await using var host = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await AskAsync(host, CurrentUserSnapshot.User(Ann), new DeliverLetter(AnnsLetter));
        (await RefusedAsync(host, CurrentUserSnapshot.User(Bob), new DeliverLetter(AnnsLetter))).Code.Should().Be(ToolkitRefusals.NotOwn);
    }

    [Fact]
    public async Task An_aggregate_that_marks_no_owner_stops_the_request_and_says_what_to_mark()
    {
        await using var host = Host();

        var hub = HubId.Create();
        (await FailedAsync(host, CurrentUserSnapshot.User(Ann), new CloseHub(hub)))
            .WithMessage("'Hub', whose id is HubId, marks no property [Owner]*Mark the property that names its owner [Owner].");
    }

    [Fact]
    public async Task An_aggregate_no_registered_context_maps_stops_the_request()
    {
        var services = Services();
        services.AddDbContext<LettersReadContext>((provider, options) => options.UseSqlite(_database.Connection).UseDDDToolkit(provider));
        await using var host = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        (await FailedAsync(host, CurrentUserSnapshot.User(Ann), new CloseHub(HubId.Create())))
            .WithMessage("No context the host registered maps an aggregate whose id is HubId*register an IAggregateOwners<HubId>*");
    }

    [Fact]
    public async Task An_aggregate_two_contexts_map_stops_the_request_rather_than_guess()
    {
        var services = Services();
        services.AddDbContext<LettersContext>(Configure);
        services.AddDbContext<LettersReadContext>((provider, options) => options.UseSqlite(_database.Connection).UseDDDToolkit(provider));
        await using var host = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        (await FailedAsync(host, CurrentUserSnapshot.User(Ann), new DeliverLetter(AnnsLetter)))
            .WithMessage("An aggregate whose id is LetterId is mapped more than once, 'Letter' by 'LettersContext' and 'Letter' by 'LettersReadContext'*");
    }

    [Fact]
    public async Task A_reader_the_host_registers_for_an_id_is_asked_instead_of_the_package_s()
    {
        var services = Services();
        services.AddDbContext<LettersContext>(Configure);
        services.AddScoped<IAggregateOwners<LetterId>, EverythingIsBobs>();
        await using var host = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await AskAsync(host, CurrentUserSnapshot.User(Bob), new DeliverLetter(AnnsLetter));
        _statements.Read.Should().BeEmpty("the host's own reader read it");
    }

    [Fact]
    public void The_assembly_says_the_aggregates_its_requests_ask_the_owner_of()
    {
        // The line of [Own] on a letter's id, and the call CloseHub writes itself; the hive's is SmokeHive's.
        typeof(ILettersRequest).Assembly.GetCustomAttributes(typeof(AsksOwnerOfAttribute), inherit: false).Cast<AsksOwnerOfAttribute>()
            .Select(static asked => asked.AggregateId).Should().BeEquivalentTo([typeof(HiveId), typeof(HubId), typeof(LetterId)]);
    }

    [Fact]
    public async Task The_start_up_check_finds_what_cannot_be_read_before_the_first_request_and_names_each()
    {
        // This assembly's requests ask about a letter's owner, a hub's, which marks none, and a hive's, which no context of
        // this host maps: two of the three stop the start, each with what its first request would have been told.
        var services = Services();
        services.AddDbContext<LettersContext>(Configure);
        await using var host = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        (await FluentActions.Awaiting(() => OwnersMapped(services).RunAsync(host, Cancellation)).Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("The owners of 2 aggregates the host's requests ask about cannot be read:*"
                + "- 'Hub', whose id is HubId, marks no property [Owner]*"
                + "- No context the host registered maps an aggregate whose id is HiveId*");
        _statements.Read.Should().BeEmpty("the models are read, and no row");
    }

    [Fact]
    public async Task An_id_the_host_reads_the_owner_of_itself_is_its_reader_s_and_the_rest_are_found_at_start_up()
    {
        var services = Services();
        services.AddDbContext<LettersContext>(Configure);
        services.AddScoped<IAggregateOwners<HiveId>, NobodysHives>();
        await using var host = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        (await FluentActions.Awaiting(() => OwnersMapped(services).RunAsync(host, Cancellation)).Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("'Hub', whose id is HubId, marks no property [Owner], so a request about its owner cannot be checked. Mark the property that names its owner [Owner].");

        services.AddScoped<IAggregateOwners<HubId>, NobodysHubs>();
        await using var passing = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await OwnersMapped(services).RunAsync(passing, Cancellation);

        // What the check found is what the requests use: the letter's owner is read, and nothing more is looked for.
        await AskAsync(passing, CurrentUserSnapshot.User(Ann), new DeliverLetter(AnnsLetter));
        _statements.Read.Should().ContainSingle();
    }

    public void Dispose() => _database.Dispose();

    /// <summary>The check <c>AddDDDToolkitEntityFramework</c> brought to <paramref name="services"/> for the owners.</summary>
    private static StartupCheck OwnersMapped(IServiceCollection services)
        => services.GetStartupChecks().Registered.Single(static each => each.Name == EntityFrameworkChecks.OwnersMappedCheck);

    private ServiceProvider Host()
    {
        var services = Services();
        services.AddDbContext<LettersContext>(Configure);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>The hub's host, without its context: the toolkit's integration, the postman's handle and the request's checks.</summary>
    private ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddDDDToolkitEntityFramework();
        services.AddCallersOwn<PostmanId, CallersPostman>();
        services.AddAccessChecks<ILettersRequest>();
        return services;
    }

    private void Configure(IServiceProvider provider, DbContextOptionsBuilder options)
        => options.UseSqlite(_database.Connection).UseDDDToolkit(provider).AddInterceptors(_statements);

    private static async Task AskAsync(IServiceProvider host, CurrentUserSnapshot caller, ILettersRequest request)
    {
        await using var scope = host.CreateAsyncScope();
        using var running = TestUser.BeginAs(caller);
        await scope.ServiceProvider.GetRequiredService<AccessChecks<ILettersRequest>>().RequireAsync(request, Cancellation);
    }

    private static async Task<RefusalException> RefusedAsync(IServiceProvider host, CurrentUserSnapshot caller, ILettersRequest request)
        => (await FluentActions.Awaiting(() => AskAsync(host, caller, request)).Should().ThrowAsync<RefusalException>()).Which;

    private static async Task<FluentAssertions.Specialized.ExceptionAssertions<InvalidOperationException>> FailedAsync(IServiceProvider host, CurrentUserSnapshot caller, ILettersRequest request)
        => await FluentActions.Awaiting(() => AskAsync(host, caller, request)).Should().ThrowAsync<InvalidOperationException>("a request whose owner cannot be read lets nobody through");

    /// <summary>A reader of the host's own: every hive is nobody's.</summary>
    private sealed class NobodysHives : IAggregateOwners<HiveId>
    {
        public Type OwnerType => typeof(Guid);

        public ValueTask<AggregateOwner?> ReadAsync(HiveId aggregate, CancellationToken cancellationToken) => ValueTask.FromResult<AggregateOwner?>(AggregateOwner.Nobody);
    }

    /// <summary>A reader of the host's own: every hub is nobody's.</summary>
    private sealed class NobodysHubs : IAggregateOwners<HubId>
    {
        public Type OwnerType => typeof(PostmanId);

        public ValueTask<AggregateOwner?> ReadAsync(HubId aggregate, CancellationToken cancellationToken) => ValueTask.FromResult<AggregateOwner?>(AggregateOwner.Nobody);
    }

    /// <summary>A reader of the host's own: every letter is Bob's.</summary>
    private sealed class EverythingIsBobs : IAggregateOwners<LetterId>
    {
        public Type OwnerType => typeof(PostmanId);

        public ValueTask<AggregateOwner?> ReadAsync(LetterId aggregate, CancellationToken cancellationToken)
            => ValueTask.FromResult<AggregateOwner?>(new AggregateOwner(new PostmanId(Bob)));
    }

    /// <summary>The text of every reading the contexts sent.</summary>
    private sealed class Statements : DbCommandInterceptor
    {
        private readonly List<string> _read = [];

        public IReadOnlyList<string> Read
        {
            get
            {
                lock (_read)
                {
                    return [.. _read];
                }
            }
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            lock (_read)
            {
                _read.Add(command.CommandText);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
