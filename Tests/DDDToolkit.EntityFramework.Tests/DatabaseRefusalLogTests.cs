using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// What a save the database refused is logged as, on SQLite, where a trigger leaves the statement no row as a policy
/// does on Postgres. The request's access check is asked again: refusing now, the caller's rights changed between
/// the check and the save, and that is an information line; letting the caller through still, C# and the database
/// disagree, and that is a warning. With no check to ask, or no answer from it, the warning stays.
/// </summary>
public sealed class DatabaseRefusalLogTests : IDisposable
{
    private const string Disagree = "The database refused a save the application allowed: depot.Pallets. C# and the policies disagree.";

    private static readonly DepotId North = new(1);

    private static readonly Guid BobId = Guid.Parse("b0b00000-0000-4000-8000-000000000012");

    private static readonly Caller Bob = Callers.FromClaims($$"""{"sub":"{{BobId}}","role":"authenticated"}""");

    private readonly SqliteDatabase _db = new();

    private readonly KeptLogLines _logs = new();

    private readonly PalletId _pallet = PalletId.CreateSequential();

    public DatabaseRefusalLogTests()
    {
        _db.EnsureCreated(() => Context());
        using (var context = Context())
        {
            context.Pallets.Add(new Pallet(_pallet, North, 1, "Alice's", owner: null));
            context.SaveChanges();
        }

        // What a policy does on Postgres, a trigger does here: the statement changes no row and says nothing.
        using var command = _db.Connection.CreateCommand();
        command.CommandText = "CREATE TRIGGER \"Pallets_stay_as_they_are\" BEFORE UPDATE ON \"Pallets\" BEGIN SELECT RAISE(IGNORE); END";
        command.ExecuteNonQuery();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _logs.Dispose();
        _db.Dispose();
    }

    /// <summary>A context that saves the way <c>UseDDDToolkit</c> makes one save, logging to the test's lines.</summary>
    private PalletContext Context() => Context(LogLevel.Information);

    private PalletContext Context(LogLevel level)
        => new(_db.Options<PalletContext>(options => options
            .UseLoggerFactory(_logs.Factory(level))
            .AddInterceptors(new AggregateVersionInterceptor(), new DatabaseRefusalInterceptor())));

    /// <summary>The handler of <see cref="RetitlePallet"/>: loads the pallet, retitles it, and saves, with whatever happens in between.</summary>
    private async Task RetitleAsync(RetitlePallet command, Action? meanwhile = null, LogLevel level = LogLevel.Information)
    {
        await using var context = Context(level);
        var pallet = await context.Pallets.SingleAsync(row => row.Id == command.Pallet, Cancellation);
        pallet.Retitle(command.Label);
        meanwhile?.Invoke();

        var refusal = (await FluentActions.Awaiting(() => context.SaveChangesAsync(Cancellation)).Should().ThrowAsync<RefusalException>()).Which;
        refusal.Code.Should().Be(ToolkitRefusals.Refused, "the caller gets the same refusal however it is logged");

        // A save without await is answered, and logged, the same way.
        FluentActions.Invoking(() => context.SaveChanges()).Should().Throw<RefusalException>().Which.Code.Should().Be(ToolkitRefusals.Refused);
    }

    [Fact]
    public async Task A_refusal_after_the_callers_rights_changed_is_an_information_line_and_no_warning()
    {
        var keepers = new KeepersInMemory().Keeps(BobId, North);
        var checks = new AccessChecks<IDepotRequest>([new KeeperCheck(Bob, keepers)]);
        var command = new RetitlePallet(_pallet, North, "Bob's now");

        // Bob keeps the north depot when his request is checked, and no longer when its handler saves.
        await KeeperCheck.SendAsync(checks, command, () => RetitleAsync(command, meanwhile: () => keepers.StopsKeeping(BobId, North)), Cancellation);

        var lines = _logs.Of<DatabaseRefusalInterceptor>();
        lines.Should().HaveCount(2, "one for the save with await and one for the save without").And.OnlyContain(line => line.Level == LogLevel.Information);
        lines[0].Message.Should().Be(
            "The database refused a save to depot.Pallets after the access check of RetitlePallet (KeepsDepot) let the caller through. Asked again, the check refuses as well: "
            + "the caller's rights changed between the check and the save, and the caller is refused.");
        lines[0].Exception.Should().BeNull("a change of rights is said in a line, with no stack trace of what the save threw");
        lines[1].Message.Should().Be(lines[0].Message);
        keepers.Asked.Should().Be(3, "once to pass, and once again for each refused save");
    }

    [Fact]
    public async Task A_refusal_the_check_still_allows_is_a_warning_that_names_the_request()
    {
        var keepers = new KeepersInMemory().Keeps(BobId, North);
        var checks = new AccessChecks<IDepotRequest>([new KeeperCheck(Bob, keepers)]);
        var command = new RetitlePallet(_pallet, North, "Bob's now");

        // C# says Bob keeps the depot, before the save and after it; the database will never let the row change.
        await KeeperCheck.SendAsync(checks, command, () => RetitleAsync(command), Cancellation);

        var lines = _logs.Of<DatabaseRefusalInterceptor>();
        lines.Should().HaveCount(2).And.OnlyContain(line => line.Level == LogLevel.Warning);
        lines[0].Message.Should().Be(
            "The database refused a save the application allowed: depot.Pallets. C# and the policies disagree: asked again, the access check of RetitlePallet (KeepsDepot) still lets the caller through.");
        lines[0].Exception.Should().BeOfType<ConcurrencyConflictException>("a warning carries what the save threw");
    }

    [Fact]
    public async Task A_refusal_in_a_flow_that_passed_no_check_keeps_the_warning_it_had()
    {
        // The handler called directly: nothing was checked, so nothing can be asked again.
        await RetitleAsync(new RetitlePallet(_pallet, North, "Bob's now"));

        // A request that requires nothing, sent from the handler of one that passed: it is not that request.
        var keepers = new KeepersInMemory().Keeps(BobId, North);
        var checks = new AccessChecks<IDepotRequest>([new KeeperCheck(Bob, keepers)]);
        await KeeperCheck.SendAsync(checks, new RetitlePallet(_pallet, North, "Unused"), () =>
            KeeperCheck.SendAsync(checks, new LookAtDepot(North), () => RetitleAsync(new RetitlePallet(_pallet, North, "Looked at")), Cancellation), Cancellation);

        _logs.Of<DatabaseRefusalInterceptor>().Should().HaveCount(4)
            .And.OnlyContain(line => line.Level == LogLevel.Warning && line.Message == Disagree);
        keepers.Asked.Should().Be(1, "the request that passed is not asked about a save of the one it sent");
    }

    [Fact]
    public async Task A_check_that_fails_when_asked_again_leaves_the_warning_and_says_why()
    {
        var keepers = new KeepersInMemory { FailsAfterTheFirst = new InvalidOperationException("The keepers could not be read.") }.Keeps(BobId, North);
        var checks = new AccessChecks<IDepotRequest>([new KeeperCheck(Bob, keepers)]);
        var command = new RetitlePallet(_pallet, North, "Bob's now");

        await KeeperCheck.SendAsync(checks, command, () => RetitleAsync(command), Cancellation);

        var lines = _logs.Of<DatabaseRefusalInterceptor>();
        lines.Should().HaveCount(2).And.OnlyContain(line => line.Level == LogLevel.Warning);
        lines[0].Message.Should().Be(
            "The database refused a save the application allowed: depot.Pallets. C# and the policies disagree, unless the caller's rights changed since the access check of RetitlePallet (KeepsDepot), "
            + "which failed when it was asked again: InvalidOperationException: The keepers could not be read.");
        lines[0].Exception.Should().BeOfType<ConcurrencyConflictException>("the line carries what the save threw, not what the check did");
    }

    [Fact]
    public async Task A_conflict_when_the_check_is_asked_again_says_nothing_of_the_callers_rights_and_leaves_the_warning()
    {
        // As a check that holds a request to the version it names, asked again after the handler saved the thing once
        // already: it finds the key still held, then the version moved on. That is no refusal of the caller.
        var conflict = new ConcurrencyConflictException(typeof(Pallet), _pallet);
        var keepers = new KeepersInMemory { FailsAfterTheFirst = conflict }.Keeps(BobId, North);
        var checks = new AccessChecks<IDepotRequest>([new KeeperCheck(Bob, keepers)]);
        var command = new RetitlePallet(_pallet, North, "Bob's now");

        await KeeperCheck.SendAsync(checks, command, () => RetitleAsync(command), Cancellation);

        var lines = _logs.Of<DatabaseRefusalInterceptor>();
        lines.Should().HaveCount(2).And.OnlyContain(line => line.Level == LogLevel.Warning, "C# allowed what the database refuses, as far as anything says");
        lines[0].Message.Should().Be(
            "The database refused a save the application allowed: depot.Pallets. C# and the policies disagree, unless the caller's rights changed since the access check of RetitlePallet (KeepsDepot), "
            + "which failed when it was asked again: ConcurrencyConflictException: " + conflict.Message);
        keepers.Asked.Should().Be(3);
    }

    [Fact]
    public async Task With_nothing_listening_the_check_is_not_asked_again()
    {
        var keepers = new KeepersInMemory().Keeps(BobId, North);
        var checks = new AccessChecks<IDepotRequest>([new KeeperCheck(Bob, keepers)]);
        var command = new RetitlePallet(_pallet, North, "Bob's now");

        await KeeperCheck.SendAsync(checks, command, () => RetitleAsync(command, level: LogLevel.Error), Cancellation);

        _logs.Of<DatabaseRefusalInterceptor>().Should().BeEmpty();
        keepers.Asked.Should().Be(1, "a check is asked again only for a line somebody reads");
    }
}
