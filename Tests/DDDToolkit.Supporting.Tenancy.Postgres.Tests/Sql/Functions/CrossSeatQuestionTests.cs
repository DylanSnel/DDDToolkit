using DDDToolkit.Abstractions.Access;
using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// What a seat may learn of other seats' rights, which its own reads no longer show: the tenant's administrators, the
/// rights a move of a unit changes, and who holds a key at a unit. The database answers each through a function that
/// runs as its owner, to a seat its guard lets ask, as ids, keys and dates and nothing else; the use cases ask the
/// first two through the store, and refuse as they do in C#; and the third answers as the C# question does.
/// </summary>
public abstract class CrossSeatQuestionTests(TenancyPostgres postgres, TenancyNaming names)
{
    private const string Administrators = "SELECT \"SeatId\" || ' ' || \"RoleId\" FROM tenancy.tenant_administrators()";

    private const string Reaches =
        "SELECT \"UnitId\" || ' ' || \"Key\" || ' ' || coalesce(pg_catalog.to_char(\"EndsAt\" AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS.US'), 'no end') || ' ' || \"Parent\" || ' ' || \"OfCaller\" FROM tenancy.rights_a_move_changes($1, $2)";

    private const string Holders = "SELECT \"SeatId\" FROM tenancy.seats_holding_at($1, $2)";

    /// <summary>Who asks, in which tenant, and whether anything is answered them at all: a seat that holds a key somewhere.</summary>
    private static readonly Dictionary<string, (Guid Identity, TenantId Tenant, string Slug, bool Answered)> Callers = new(StringComparer.Ordinal)
    {
        ["Ada in harbor, who administers it"] = (Ada.Identity, Harbor, "harbor", true),
        ["Hiro in harbor, who gives roles at North"] = (Hiro.Identity, Harbor, "harbor", true),
        ["Seth in harbor, who supervises North"] = (Seth.Identity, Harbor, "harbor", true),
        ["Oli in harbor, who manages nothing"] = (Oli.Identity, Harbor, "harbor", true),
        ["Oli in orchard, who watches"] = (Oli.Identity, Orchard, "orchard", true),
        ["Sue in harbor, suspended"] = (Sue.Identity, Harbor, "harbor", false),
        ["Eve in harbor, with an ended and a future grant"] = (Eve.Identity, Harbor, "harbor", false),
        ["Odette in orchard, who administers it"] = (Odette.Identity, Orchard, "orchard", true),
        ["Quin in quay, a suspended tenant"] = (Quin.Identity, Quay, "quay", false),
        ["Ada in orchard, where she has no seat"] = (Ada.Identity, Orchard, "orchard", false),
        ["A stranger in harbor"] = (Stranger, Harbor, "harbor", false),
    };

    private static readonly OrganizationUnitId[] Units = [HarborRoot, North, South, NorthPier, OrchardRoot, QuayRoot];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string> Seats => [.. Callers.Keys];

    [Fact]
    public async Task The_administrators_are_answered_to_a_manager_and_to_nobody_else()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var ada = Ada.Seat.Value + " " + HarborRoles.Administrator.Value;

        // Ada administers Harbor, Seth manages units, seats and grants at North, Hiro gives roles there: each may read
        // every grant of Harbor, and is answered who administers it.
        foreach (var manager in new[] { Ada, Seth, Hiro })
        {
            await using var caller = await AsCaller.PersonAsync(database, manager.Identity, Harbor, Cancellation);
            (await caller.ListAsync<string>(Administrators, Cancellation)).Should().Equal([ada], "{0} manages access in Harbor", manager.Name);
        }

        // Oli manages nothing, Eve holds nothing now, Sue is suspended, and a stranger has no seat: each is answered nobody.
        foreach (var identity in new[] { Oli.Identity, Eve.Identity, Sue.Identity, Stranger })
        {
            await using var caller = await AsCaller.PersonAsync(database, identity, Harbor, Cancellation);
            (await caller.ListAsync<string>(Administrators, Cancellation)).Should().BeEmpty();
        }

        // The tenant is the calling seat's: Odette is answered Orchard's, Oli, who watches there, nobody, and Ada, who has
        // no seat there, nobody either.
        await using (var odette = await AsCaller.PersonAsync(database, Odette.Identity, Orchard, Cancellation))
        {
            (await odette.ListAsync<string>(Administrators, Cancellation)).Should().Equal(Odette.Seat.Value + " " + OrchardRoles.Administrator.Value);
        }

        foreach (var identity in new[] { Oli.Identity, Ada.Identity })
        {
            await using var caller = await AsCaller.PersonAsync(database, identity, Orchard, Cancellation);
            (await caller.ListAsync<string>(Administrators, Cancellation)).Should().BeEmpty();
        }

        // A second administrator is answered too; one whose role ends is none, though she may ask while it lasts.
        await HoldAtAsync(database, Hiro, HarborRoot, keys: null, Cancellation);
        await HoldUntilAsync(database, Eve, HarborRoot, HarborRoles.Administrator, days: 7);
        foreach (var asking in new[] { Ada, Eve })
        {
            await using var caller = await AsCaller.PersonAsync(database, asking.Identity, Harbor, Cancellation);
            (await caller.ListAsync<string>(Administrators, Cancellation)).Should().BeEquivalentTo([ada, Hiro.Seat.Value + " " + HarborRoles.Administrator.Value]);
        }

        // System work reads the rights of its tenant itself, and neither it nor an anonymous caller may ask.
        await using (var system = await AsCaller.SystemInAsync(database, Harbor, TenancyWork.SystemScope, Cancellation))
        {
            await NotExecutableAsync(system, Administrators);
        }

        await using var anonymous = await AsCaller.AnonymousAsync(database, Cancellation);
        await NotExecutableAsync(anonymous, Administrators);
    }

    [Fact]
    public async Task The_last_administrator_check_holds_for_a_unit_level_seats_manager()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // Oli keeps the seats of Harbor, through a role at the root that holds the seats key alone: no administrator, and
        // no manager of roles or grants. He reads no rights but his own, and every grant.
        await HoldAtAsync(database, Oli, HarborRoot, [TenancyKeys.SeatsManage], Cancellation);
        var recorder = new CommandRecorder();
        await using var services = new TenancyServices(database, contexts: options => options.AddInterceptors(recorder));
        Task As(Person person, Func<IServiceProvider, Task> act) => services.BySeat(person.Identity, Harbor, person.Seat, act);

        await using (var oli = await AsCaller.PersonAsync(database, Oli.Identity, Harbor, Cancellation))
        {
            (await oli.ListAsync<Guid>("SELECT DISTINCT \"SeatId\" FROM tenancy.\"SeatRights\"", Cancellation)).Should().Equal(Oli.Seat.Value);
            (await oli.ListAsync<string>(Administrators, Cancellation)).Should().Equal(Ada.Seat.Value + " " + HarborRoles.Administrator.Value);
        }

        // He stops Eve's seat. The command asks who administers Harbor before it lets a seat stop counting, and the
        // database answers him, though the rights it reads are not his to read: Ada stays, so Eve's seat is stopped.
        await As(Oli, scoped => scoped.Seats().SuspendAsync(Eve.Seat, Cancellation));
        recorder.Sent.Should().Contain(command => command.Text.Contains("tenant_administrators", StringComparison.Ordinal) && command.Caller!.Kind == CallerKind.User,
            "the store asks the database's function, as the signed-in user");
        var rights = "tenancy." + names.Shown("SeatRights");
        recorder.Sent.Should().Contain(command => command.Text.Contains("UPDATE tenancy." + names.Shown("Seats"), StringComparison.Ordinal), "the save is among what was recorded, under this naming");
        recorder.Sent.Should().NotContain(
            command => command.Text.Contains("INSERT INTO " + rights, StringComparison.Ordinal)
                       || command.Text.Contains("UPDATE " + rights, StringComparison.Ordinal)
                       || command.Text.Contains("DELETE FROM " + rights, StringComparison.Ordinal),
            "and the save writes no right");

        // Seth holds a role that manages access, and Ada the administrators': neither is his to stop, for the keys he
        // lacks, which he is told since he reads their grants.
        var beyond = await RefusedAsync(TenancyRefusals.GrantExceedsOwn, () => As(Oli, scoped => scoped.Seats().SuspendAsync(Seth.Seat, Cancellation)));
        beyond.Arguments["Missing"].Should().Be(TenancyKeys.GrantsManage + ", " + TenancyKeys.UnitsManage);
        await RefusedAsync(TenancyRefusals.GrantExceedsOwn, () => As(Oli, scoped => scoped.Seats().DeactivateAsync(Ada.Seat, Cancellation)));

        // Ada, the one administrator, cannot stop her own seat: the use case refuses it, from what the database answered,
        // before anything is written for the trigger to refuse.
        var last = await RefusedAsync(TenancyRefusals.LastAdmin, () => As(Ada, scoped => scoped.Seats().SuspendAsync(Ada.Seat, Cancellation)));
        last.InnerException.Should().BeNull("the use case refused it, not the database");
        await RefusedAsync(TenancyRefusals.LastAdmin, () => As(Ada, scoped => scoped.Seats().RevokeAsync(Ada.Seat, HarborRoot, HarborRoles.Administrator, Cancellation)));
        await RefusedAsync(TenancyRefusals.LastAdmin, () => As(Ada, scoped => scoped.Roles().ArchiveAsync(HarborRoles.Administrator, Cancellation)));

        // With a second administrator she may.
        await HoldAtAsync(database, Hiro, HarborRoot, keys: null, Cancellation);
        await As(Ada, scoped => scoped.Seats().SuspendAsync(Ada.Seat, Cancellation));

        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ListAsync<string>("SELECT \"DisplayName\" FROM tenancy.\"Seats\" WHERE \"TenantId\" = 1 AND \"Status\" = 'Suspended' ORDER BY 1", Cancellation)).Should().Equal("Ada", "Eve", "Sue");
        (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = ANY ($1)", Cancellation, new[] { Ada.Seat.Value, Eve.Seat.Value })).Should().Be(0);
        (await owner.ScalarAsync<long>("SELECT count(*) FROM tenancy.\"SeatRights\" WHERE \"SeatId\" = $1", Cancellation, Seth.Seat.Value)).Should().BePositive("the refused commands wrote nothing");
    }

    [Fact]
    public async Task A_move_is_refused_through_the_database_as_it_is_in_csharp()
    {
        // Seth supervises North for good, and South for a week.
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await HoldUntilAsync(database, Seth, South, HarborRoles.Supervisor, days: 7);
        var recorder = new CommandRecorder();
        await using (var services = new TenancyServices(database, contexts: options => options.AddInterceptors(recorder)))
        {
            Task AsSeth(Func<IServiceProvider, Task> act) => services.BySeat(Seth.Identity, Harbor, Seth.Seat, act);
            var quay = OrganizationUnitId.CreateSequential();
            await AsSeth(scoped => scoped.Organization().AddUnitAsync(South, "South Quay", "site", Cancellation, quay));

            // A unit of South moved under North would be his for good.
            var longer = await RefusedAsync(TenancyRefusals.GrantExceedsOwn, () => AsSeth(scoped => scoped.Organization().MoveUnitAsync(quay, North, Cancellation)));
            longer.Arguments["Missing"].As<string>().Should().Contain(TenancyKeys.UnitsManage, "Seth holds South's keys for a week, and North's for good");
            recorder.Sent.Should().Contain(command => command.Text.Contains("rights_a_move_changes", StringComparison.Ordinal) && command.Caller!.Kind == CallerKind.User,
                "the store asks the database's function, as the signed-in user");

            // The other way round his hold only gets shorter.
            await AsSeth(scoped => scoped.Organization().MoveUnitAsync(NorthPier, South, Cancellation));
        }

        await using (var seth = await AsCaller.PersonAsync(database, Seth.Identity, Harbor, Cancellation))
        {
            (await seth.ListAsync<Guid>("SELECT tenancy.units_where_i_hold('tenancy.units.manage')", Cancellation)).Should().Contain(NorthPier.Value);
        }

        // Oli administers Harbor for a week. North Pier moved away from North would take from Seth, and from Hiro, what
        // they hold there for good, which he could not take away grant by grant.
        var other = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await HoldUntilAsync(other, Oli, HarborRoot, HarborRoles.Administrator, days: 7);
        await using (var services = new TenancyServices(other))
        {
            var taking = await RefusedAsync(TenancyRefusals.GrantExceedsOwn, () => services.BySeat(Oli.Identity, Harbor, Oli.Seat, scoped =>
                scoped.Organization().MoveUnitAsync(NorthPier, South, Cancellation)));
            taking.Arguments["Missing"].Should().Be(TenancyKeys.GrantsManage + ", " + TenancyKeys.SeatsManage + ", " + TenancyKeys.UnitsManage,
                "Seth supervises North for good, and Oli runs the tenant for a week");
            taking.Arguments["Role"].Should().BeNull();

            // Eve administers it for a week as well, and holds a role at South that is still to start: the move would
            // give her its keys at North Pier for good, so she is refused for those too.
            await HoldUntilAsync(other, Eve, HarborRoot, HarborRoles.Administrator, days: 7);
            var gaining = await RefusedAsync(TenancyRefusals.GrantExceedsOwn, () => services.BySeat(Eve.Identity, Harbor, Eve.Seat, scoped =>
                scoped.Organization().MoveUnitAsync(NorthPier, South, Cancellation)));
            gaining.Arguments["Missing"].Should().Be(TenancyKeys.GrantsManage + ", " + TenancyKeys.SeatsManage + ", " + TenancyKeys.UnitsManage + ", widget.change, widget.create, widget.read");

            await using (var seth = await AsCaller.PersonAsync(other, Seth.Identity, Harbor, Cancellation))
            {
                (await seth.ListAsync<Guid>("SELECT tenancy.units_where_i_hold('tenancy.units.manage')", Cancellation)).Should().Contain(NorthPier.Value, "the refused move wrote nothing");
            }

            // Ada holds every key for good, and moves it.
            await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.Organization().MoveUnitAsync(NorthPier, South, Cancellation));
        }

        await using var after = await AsCaller.PersonAsync(other, Seth.Identity, Harbor, Cancellation);
        (await after.ListAsync<Guid>("SELECT tenancy.units_where_i_hold('tenancy.units.manage')", Cancellation)).Should().Equal(North.Value);
    }

    [Fact]
    public async Task A_move_is_not_let_through_where_the_database_does_not_see_the_calling_seat()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var yard = OrganizationUnitId.CreateSequential();

        // A host that registered Tenancy for Postgres and left row level security off its contexts: every command runs
        // as the application, which owns the tables, and the database is told of no signed-in user. Seth supervises North,
        // so the use case finds him to manage units at North and at North Pier, and asks what a move between them changes.
        await using (var unsecured = new TenancyServices(database, rowLevelSecurity: false))
        {
            await unsecured.BySeat(Seth.Identity, Harbor, Seth.Seat, scoped => scoped.Organization().AddUnitAsync(North, "North Yard", "site", Cancellation, yard));

            // The database answers nobody nothing. Read as "the move changes nothing", that would let every move through,
            // with nothing in the database to check it afterwards: the store takes it for no answer instead.
            var unanswered = await FluentActions.Awaiting(() => unsecured.BySeat(Seth.Identity, Harbor, Seth.Seat, scoped => scoped.Organization().MoveUnitAsync(yard, NorthPier, Cancellation)))
                .Should().ThrowAsync<InvalidOperationException>();
            unanswered.Which.Message.Should().Contain("does not see the seat the application acts as").And.Contain("UsePostgresRowLevelSecurity");
        }

        await using (var owner = await AsCaller.OwnerAsync(database, Cancellation))
        {
            (await owner.ScalarAsync<Guid>("SELECT \"ParentId\" FROM tenancy.\"OrganizationUnits\" WHERE \"Id\" = $1", Cancellation, yard.Value)).Should().Be(North.Value, "nothing moved");
        }

        // Under row level security the database sees Seth, answers him his own rights there, and the move, which changes
        // nothing he holds, goes through.
        await using var services = new TenancyServices(database);
        await services.BySeat(Seth.Identity, Harbor, Seth.Seat, scoped => scoped.Organization().MoveUnitAsync(yard, NorthPier, Cancellation));

        await using var after = await AsCaller.OwnerAsync(database, Cancellation);
        (await after.ScalarAsync<Guid>("SELECT \"ParentId\" FROM tenancy.\"OrganizationUnits\" WHERE \"Id\" = $1", Cancellation, yard.Value)).Should().Be(NorthPier.Value);
    }

    [Fact]
    public async Task A_move_checked_while_the_movers_own_grant_ended_is_a_conflict_to_read_again()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var now = await DatabaseNowAsync(database.ConnectionString, Cancellation);

        // Oli managed units for all of Harbor until an hour ago, by the database's clock.
        await using (var seeding = new TenancyServices(database))
        {
            var desk = await seeding.BySystemIn(Harbor, scoped => scoped.Roles().CreateAsync("Units desk", "Keeps the tree", [TenancyKeys.UnitsManage], Cancellation));
            await seeding.BySystemIn(Harbor, scoped => scoped.Seats().PlaceAsync(Oli.Seat, HarborRoot, primary: false, Cancellation));
            await seeding.BySystemIn(Harbor, scoped => scoped.Seats().GrantAsync(Oli.Seat, HarborRoot, desk, now.AddHours(-1), reason: null, Cancellation, now.AddDays(-3)));
        }

        // The application's clock is two hours behind: what a grant that ends between the use case's check and the
        // database's answer looks like, or two clocks that differ. The use case finds him to manage units at both
        // parents; the database, which sees his seat, no longer does, and answers nothing. That is a race he lost,
        // to read again, and neither a move that changes nothing nor a fault of the host's.
        await using (var behind = new TenancyServices(database, configure: collection => collection.AddSingleton<TimeProvider>(new StoppedClock(now.AddHours(-2)))))
        {
            var conflict = await FluentActions.Awaiting(() => behind.BySeat(Oli.Identity, Harbor, Oli.Seat, scoped => scoped.Organization().MoveUnitAsync(NorthPier, South, Cancellation)))
                .Should().ThrowAsync<ConcurrencyConflictException>();
            conflict.Which.Message.Should().Contain("Read again");
        }

        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ScalarAsync<Guid>("SELECT \"ParentId\" FROM tenancy.\"OrganizationUnits\" WHERE \"Id\" = $1", Cancellation, NorthPier.Value)).Should().Be(North.Value, "nothing moved");
    }

    [Fact]
    public async Task The_rights_a_move_changes_are_answered_only_to_a_units_manager_at_both_parents()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await HoldUntilAsync(database, Eve, HarborRoot, HarborRoles.Administrator, days: 7);
        var now = await DatabaseNowAsync(database.ConnectionString, Cancellation);

        // Ada manages units everywhere. What the function answers her about a move from under North to under South is
        // what the store reads from the rights themselves where the save writes them, as on any other database: her own
        // rights that have not ended, and every seat's of a key that manages access, at North, at South and above them.
        List<string> expected;
        await using (var composed = new TenancyServices(
                         database,
                         configure: collection => collection.AddSingleton<TimeProvider>(new StoppedClock(now)),
                         rowLevelSecurity: false,
                         databaseKeepsRights: false))
        {
            var catalogue = composed.Provider.GetRequiredService<TenancyCatalogue>();
            var reaches = await composed.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => scoped.GetRequiredService<HostTenancy.IStore>()
                .RightsAMoveChangesAsync(Harbor, Ada.Seat, North, South, catalogue.AccessManagingKeys, now, Cancellation));
            expected = [.. reaches.Select(reach =>
                $"{reach.UnitId.Value} {reach.Key} {reach.EndsAt?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff", System.Globalization.CultureInfo.InvariantCulture) ?? "no end"} {reach.Parent.Value} {(reach.OfCaller ? "true" : "false")}")];
        }

        expected.Should().Contain(reach => reach.EndsWith(" true", StringComparison.Ordinal)).And.Contain(reach => reach.EndsWith(" false", StringComparison.Ordinal))
            .And.Contain(reach => !reach.Contains("no end", StringComparison.Ordinal), "Eve's rights end, and are among them");
        expected.Should().NotContain(reach => reach.Contains(" widget.", StringComparison.Ordinal) && reach.EndsWith(" false", StringComparison.Ordinal), "of other seats, only the keys that manage access");

        await using (var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation))
        {
            (await ada.ListAsync<string>(Reaches, Cancellation, North.Value, South.Value)).Should().BeEquivalentTo(expected);
            (await ada.ListAsync<string>(Reaches, Cancellation, North.Value, OrchardRoot.Value)).Should().BeEmpty("a parent of another tenant is none she manages");
        }

        // Eve manages units everywhere too, for a week: she may ask, and is answered her own rights where Ada was
        // answered hers, the Operator role at South she holds from the day after tomorrow among them, which has not ended.
        await using (var eve = await AsCaller.PersonAsync(database, Eve.Identity, Harbor, Cancellation))
        {
            var hers = await eve.ListAsync<string>(Reaches, Cancellation, North.Value, South.Value);
            hers.Where(reach => reach.EndsWith(" false", StringComparison.Ordinal) && reach.Contains(" tenancy.", StringComparison.Ordinal))
                .Should().NotBeEmpty("the keys that manage access of the other seats, Ada's now among them");
            hers.Where(reach => reach.EndsWith(" true", StringComparison.Ordinal)).Should().HaveCount(expected.Count(reach => reach.EndsWith(" true", StringComparison.Ordinal)) + 3);
        }

        // Seth manages units at North and below, and nowhere else: a move within them he is answered, one that leaves them
        // or comes into them he is not.
        await using (var seth = await AsCaller.PersonAsync(database, Seth.Identity, Harbor, Cancellation))
        {
            (await seth.ListAsync<string>(Reaches, Cancellation, North.Value, NorthPier.Value)).Should().NotBeEmpty();
            (await seth.ListAsync<string>(Reaches, Cancellation, North.Value, South.Value)).Should().BeEmpty();
            (await seth.ListAsync<string>(Reaches, Cancellation, South.Value, North.Value)).Should().BeEmpty();
            (await seth.ListAsync<string>(Reaches, Cancellation, HarborRoot.Value, North.Value)).Should().BeEmpty();
        }

        // Hiro gives roles at North and Oli operates widgets: neither manages units, and neither is answered anything.
        foreach (var person in new[] { Hiro, Oli, Sue })
        {
            await using var caller = await AsCaller.PersonAsync(database, person.Identity, Harbor, Cancellation);
            (await caller.ListAsync<string>(Reaches, Cancellation, North.Value, NorthPier.Value)).Should().BeEmpty();
        }

        await using (var system = await AsCaller.SystemInAsync(database, Harbor, TenancyWork.SystemScope, Cancellation))
        {
            await NotExecutableAsync(system, Reaches, North.Value, South.Value);
        }

        await using var anonymous = await AsCaller.AnonymousAsync(database, Cancellation);
        await NotExecutableAsync(anonymous, Reaches, North.Value, South.Value);
    }

    [Theory]
    [MemberData(nameof(Seats))]
    public async Task Seats_holding_at_answers_as_the_csharp_question_for_every_seat_key_and_unit(string who)
    {
        var (identity, tenant, slug, holds) = Callers[who];
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var now = await DatabaseNowAsync(database.ConnectionString, Cancellation);
        var keys = TenancyPostgres.Catalogue.LiveKeys;

        // In C#, over the rights themselves, as the application reads them past the policies, at the moment the
        // database's clock said: the question works the answer out, and applies what a seat may learn itself.
        var csharp = new Dictionary<(string Key, OrganizationUnitId Unit), List<Guid>>();
        await using (var composed = new TenancyServices(
                         database,
                         configure: collection => collection.AddSingleton<TimeProvider>(new StoppedClock(now)),
                         rowLevelSecurity: false,
                         databaseKeepsRights: false))
        {
            await using var scope = composed.Scope();
            var caller = await scope.ServiceProvider.GetRequiredService<TenantSelection<TenantId, SeatId>>().ResolveAsync(Caller.User(identity), slug, Cancellation);
            using (TenancyCallers.Begin(caller))
            {
                var questions = scope.ServiceProvider.Answers().Over(scope.ServiceProvider.Tenancy());
                foreach (var key in keys)
                {
                    foreach (var unit in Units)
                    {
                        csharp[(key, unit)] = [.. (await questions.SeatsHoldingAt(key, unit).ToListAsync(Cancellation)).Select(seat => seat.Value)];
                    }
                }
            }
        }

        // In SQL, as that person, with that tenant in the setting.
        await using var person = await AsCaller.PersonAsync(database, identity, tenant, Cancellation);
        var answered = 0;
        foreach (var key in keys)
        {
            foreach (var unit in Units)
            {
                var sql = await person.ListAsync<Guid>(Holders, Cancellation, key, unit.Value);
                sql.Should().BeEquivalentTo(csharp[(key, unit)], "seats_holding_at('{0}', {1}) is SeatsHoldingAt", key, unit);
                answered += sql.Count;
            }
        }

        (answered > 0).Should().Be(holds, "{0} holds a key somewhere, or holds nothing that applies", who);
    }

    [Fact]
    public async Task Seats_holding_at_tells_a_plain_member_only_about_itself()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        // Ada holds every key at the root, Seth the widgets' at North, and Oli at North Pier: Ada, who manages access, is
        // answered all three for North Pier, and the two above it for North.
        await using (var ada = await AsCaller.PersonAsync(database, Ada.Identity, Harbor, Cancellation))
        {
            (await ada.ListAsync<Guid>(Holders, Cancellation, "widget.read", NorthPier.Value)).Should().BeEquivalentTo([Ada.Seat.Value, Seth.Seat.Value, Oli.Seat.Value]);
            (await ada.ListAsync<Guid>(Holders, Cancellation, "widget.read", North.Value)).Should().BeEquivalentTo([Ada.Seat.Value, Seth.Seat.Value]);
            (await ada.ListAsync<Guid>(Holders, Cancellation, "widget.read", South.Value)).Should().Equal([Ada.Seat.Value], "Sue is suspended, and Eve's grants there apply at no moment near now");
            (await ada.ListAsync<Guid>(Holders, Cancellation, "widget.polish", North.Value)).Should().BeEmpty("a key the catalogue does not have is held by nobody");
        }

        // Oli manages nothing: where he holds the key he is answered himself, and where he does not, nobody, whoever does.
        await using (var oli = await AsCaller.PersonAsync(database, Oli.Identity, Harbor, Cancellation))
        {
            (await oli.ListAsync<Guid>(Holders, Cancellation, "widget.read", NorthPier.Value)).Should().Equal(Oli.Seat.Value);
            (await oli.ListAsync<Guid>(Holders, Cancellation, "widget.read", North.Value)).Should().BeEmpty();
            (await oli.ListAsync<Guid>(Holders, Cancellation, TenancyKeys.UnitsManage, NorthPier.Value)).Should().BeEmpty();
        }

        // Through the question, under the policies, it is the same answer: for a seat the database's function gives it,
        // in Tenancy's own context and in a module's, as a query that composes into one statement; for system work in
        // the tenant, which reads every right of it, the question works it out.
        var recorder = new CommandRecorder();
        await using var services = new TenancyServices(database, contexts: options => options.AddInterceptors(recorder));
        async Task<List<SeatId>> ActiveHoldersAsync(IServiceProvider scoped, DbContext context)
        {
            var holders = scoped.Answers().Over(context).SeatsHoldingAt(HostCatalogue.WidgetRead, NorthPier);
            return await context.Set<SeatRow<TenantId, SeatId>>().Where(seat => holders.Contains(seat.Id) && seat.Status == SeatStatus.Active).Select(seat => seat.Id).ToListAsync(Cancellation);
        }

        foreach (var module in new[] { false, true })
        {
            DbContext Context(IServiceProvider scoped) => module ? scoped.Widgets() : scoped.Tenancy();

            (await services.BySeat(Oli.Identity, Harbor, Oli.Seat, scoped => ActiveHoldersAsync(scoped, Context(scoped)))).Should().Equal(Oli.Seat);
            recorder.Sent.Should().ContainSingle("one statement answers it").Which.Text.Should().Contain("seats_holding_at");
            (await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => ActiveHoldersAsync(scoped, Context(scoped)))).Should().BeEquivalentTo([Ada.Seat, Oli.Seat, Seth.Seat]);
            (await services.BySystemIn(Harbor, scoped => ActiveHoldersAsync(scoped, Context(scoped)))).Should().BeEquivalentTo([Ada.Seat, Oli.Seat, Seth.Seat]);
            recorder.Sent.Should().HaveCount(3).And.Subject.Last().Text.Should().NotContain("seats_holding_at", "system work reads the rights");
            recorder.Clear();
        }
    }

    [Fact]
    public async Task Seats_holding_at_is_executable_by_the_user_role_alone()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);

        await using (var system = await AsCaller.SystemInAsync(database, Harbor, TenancyWork.SystemScope, Cancellation))
        {
            await NotExecutableAsync(system, Holders, "widget.read", North.Value);
        }

        await using (var anonymous = await AsCaller.AnonymousAsync(database, Cancellation))
        {
            await NotExecutableAsync(anonymous, Holders, "widget.read", North.Value);
        }

        // And so are the other two that answer about other seats' rights: the signed-in user's, and no other role's.
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        (await owner.ListAsync<string>(
                """
                SELECT p.proname || ' ' || pg_catalog.has_function_privilege('authenticated', p.oid, 'EXECUTE')
                       || ' ' || pg_catalog.has_function_privilege('ddd_system_in', p.oid, 'EXECUTE')
                       || ' ' || pg_catalog.has_function_privilege('anon', p.oid, 'EXECUTE')
                       || ' ' || pg_catalog.has_function_privilege('public', p.oid, 'EXECUTE')
                FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                WHERE n.nspname = 'tenancy' AND p.proname = ANY ($1) ORDER BY 1
                """,
                Cancellation,
                (object)new[] { TenancyFunctionNames.TenantAdministrators, TenancyFunctionNames.RightsAMoveChanges, TenancyFunctionNames.SeatsHoldingAt }))
            .Should().Equal(
                "rights_a_move_changes true false false false",
                "seats_holding_at true false false false",
                "tenant_administrators true false false false");
    }

    [Fact]
    public async Task Every_cross_seat_function_returns_ids_keys_and_dates_only()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        await using var owner = await AsCaller.OwnerAsync(database, Cancellation);
        var functions = new[] { TenancyFunctionNames.TenantAdministrators, TenancyFunctionNames.RightsAMoveChanges, TenancyFunctionNames.SeatsHoldingAt };

        // What each returns, as the catalog has it: a seat and its role; a unit, a key, an end, a parent and whether the
        // right is the caller's; a seat.
        (await owner.ListAsync<string>(
                """
                SELECT p.proname || '(' || pg_catalog.pg_get_function_identity_arguments(p.oid) || ') ' || pg_catalog.pg_get_function_result(p.oid)
                FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                WHERE n.nspname = 'tenancy' AND p.proname = ANY ($1) ORDER BY 1
                """,
                Cancellation,
                (object)functions))
            .Should().Equal(
                "rights_a_move_changes(parent uuid, new_parent uuid) TABLE(\"UnitId\" uuid, \"Key\" text, \"EndsAt\" timestamp with time zone, \"Parent\" uuid, \"OfCaller\" boolean)",
                "seats_holding_at(key text, unit uuid) TABLE(\"SeatId\" uuid)",
                "tenant_administrators() TABLE(\"SeatId\" uuid, \"RoleId\" uuid)");

        // No name, no identity, no reason and no text of anyone: every column is an id, a key, a moment or a yes or no.
        (await owner.ListAsync<string>(
                """
                SELECT DISTINCT pg_catalog.format_type(returned.type, NULL)
                FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                CROSS JOIN LATERAL ROWS FROM (pg_catalog.unnest(p.proallargtypes), pg_catalog.unnest(p.proargmodes)) AS returned(type, mode)
                WHERE n.nspname = 'tenancy' AND p.proname = ANY ($1) AND returned.mode = 't' ORDER BY 1
                """,
                Cancellation,
                (object)functions))
            .Should().Equal("boolean", "text", "timestamp with time zone", "uuid");

        // And each answers about the calling seat's tenant alone, found as every question of Tenancy's finds it.
        (await owner.ListAsync<string>(
                "SELECT p.proname FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'tenancy' AND p.proname = ANY ($1) AND p.prosrc LIKE '%tenancy.caller_tenant()%' ORDER BY 1",
                Cancellation,
                (object)functions))
            .Should().HaveCount(3);
    }

    /// <summary>
    /// Gives <paramref name="person"/> a role in Harbor at <paramref name="unit"/> that ends <paramref name="days"/> days
    /// from now by the database's clock, placing them there first where they are not: system work through the use cases.
    /// </summary>
    private static async Task HoldUntilAsync(TestDatabase database, Person person, OrganizationUnitId unit, RoleId role, int days)
    {
        var until = (await DatabaseNowAsync(database.ConnectionString, Cancellation)).AddDays(days);
        await using var services = new TenancyServices(database);
        await services.BySystemIn(Harbor, async scoped =>
        {
            var seat = (await scoped.GetRequiredService<HostTenancy.IStore>().FindSeatAsync(person.Seat, Cancellation))!;
            if (seat.Placements.All(placement => placement.UnitId != unit))
            {
                await scoped.Seats().PlaceAsync(person.Seat, unit, primary: false, Cancellation);
            }
        });
        await services.BySystemIn(Harbor, scoped => scoped.Seats().GrantAsync(person.Seat, unit, role, until, reason: null, Cancellation));
    }

    /// <summary>Runs <paramref name="command"/>, expecting the use case to refuse it with <paramref name="code"/>.</summary>
    private static async Task<RefusalException> RefusedAsync(string code, Func<Task> command)
    {
        var refusal = (await command.Should().ThrowAsync<RefusalException>()).Which;
        refusal.Code.Should().Be(code);
        return refusal;
    }

    /// <summary>Asks <paramref name="sql"/> as <paramref name="caller"/>, expecting Postgres to refuse the function to its role.</summary>
    private static async Task NotExecutableAsync(AsCaller caller, string sql, params object[] parameters)
    {
        var refusal = await FluentActions.Awaiting(() => caller.ListAsync<string>(sql, Cancellation, parameters)).Should().ThrowAsync<PostgresException>();
        refusal.Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        refusal.Which.MessageText.Should().Contain("permission denied for function");
    }
}

/// <summary>What a seat may learn of other seats' rights, under the names Entity Framework gives the tables and columns.</summary>
public sealed class CrossSeatQuestionTestsOnDefaultNames(TenancyPostgres postgres) : CrossSeatQuestionTests(postgres, TenancyNaming.Default);

/// <summary>What a seat may learn of other seats' rights, under snake_case names with enums stored as snake_case text.</summary>
public sealed class CrossSeatQuestionTestsOnSnakeCase(TenancyPostgres postgres) : CrossSeatQuestionTests(postgres, TenancyNaming.SnakeCase);
