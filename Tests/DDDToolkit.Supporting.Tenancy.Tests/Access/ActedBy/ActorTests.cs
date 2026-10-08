using System.Text.Json;
using System.Text.Json.Serialization;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Interfaces;
using DDDToolkit.Testing;

namespace DDDToolkit.Supporting.Tenancy.Tests;

/// <summary>
/// Who a change is recorded as: every Tenancy caller that can change something carries its actor, a seat as
/// itself, system work as the system in its scope, and the work begun for an operator or for a link's token as
/// that operator or token. The actor maps onto the toolkit's own record of who acted, with ids only. Every
/// domain event of Tenancy's carries it as <c>By</c>, and every use case passes its caller's.
/// </summary>
public class ActorTests
{
    private static readonly TenantId Harbor = new(1);
    private static readonly SeatId Ada = SeatId.CreateSequential();
    private static readonly Guid Odette = Guid.NewGuid();

    /// <summary>Every domain event type of the package, open: found by reflection, so a new one is held to the rule.</summary>
    private static readonly Type[] EventTypes =
    [
        .. typeof(TenantAggregate<>).Assembly.GetTypes()
            .Where(type => typeof(IDomainEvent).IsAssignableFrom(type) && !type.IsAbstract)
            .OrderBy(type => type.Name, StringComparer.Ordinal),
    ];

    private static HostCaller Current => TenancyCallers.Current<TenantId, SeatId>();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>Who an event says made the change, whatever the event.</summary>
    private static TenancyActor<SeatId>? ByOf(IDomainEvent raised)
        => (TenancyActor<SeatId>?)raised.GetType().GetProperty("By")!.GetValue(raised);

    [Fact]
    public void A_seat_caller_is_recorded_as_its_seat()
    {
        var caller = HostCaller.InSeat(Harbor, Ada);

        caller.Actor.Should().Be(TenancyActor<SeatId>.OfSeat(Ada));
        caller.Actor!.Value.Should().Be(new TenancyActor<SeatId>(TenancyActorKind.Seat, Ada), "a seat is named by its id, with no scope and no operator");
        ((ITenancyCaller)caller).Actor!.SeatId.Should().Be(Ada);

        HostCaller.Nobody(TenancyRefusals.NotSeated).Actor.Should().BeNull("nobody changes nothing");
        ((ITenancyCaller)HostCaller.Nobody(TenancyRefusals.SeatSuspended)).Actor.Should().BeNull();
    }

    [Fact]
    public void System_work_is_recorded_with_its_scope()
    {
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            Current.Actor.Should().Be(TenancyActor<SeatId>.OfSystem(TenancyWork.SystemScope));
            Current.Should().Be(HostCaller.System);
        }

        using (TenancyWork.BeginSystemIn<TenantId, SeatId>(Harbor))
        {
            Current.Actor.Should().Be(TenancyActor<SeatId>.OfSystem("tenancy"));
        }

        // The seat the work is done for is recorded next to the scope: it is not who acted.
        using (TenancyWork.BeginSystemIn(Harbor, (SeatId?)Ada, scope: "projects"))
        {
            Current.Actor.Should().Be(new TenancyActor<SeatId>(TenancyActorKind.System, Ada, Scope: "projects"));
            Current.Seat.Should().Be(Ada);
            Current.Actor!.Value.ToActedBy().Should().Be(new ActedBy("system", "projects"));
            Callers.Ambient!.Scope.Should().Be("projects", "the scope of the record is the scope the work runs in");
        }

        // A caller made by hand, as a test makes one, is Tenancy's own work.
        HostCaller.SystemIn(Harbor).Actor.Should().Be(TenancyActor<SeatId>.OfSystem(TenancyWork.SystemScope));
        HostCaller.SystemIn(Harbor, Ada).Actor.Should().Be(TenancyActor<SeatId>.OfSystem(TenancyWork.SystemScope, Ada));
    }

    [Fact]
    public void Begin_operator_names_the_operator_and_no_seat()
    {
        using (TenancyWork.BeginOperator<TenantId, SeatId>(Odette))
        {
            Current.Kind.Should().Be(TenancyCallerKind.System, "it provisions, as system work outside any tenant does");
            Current.Actor.Should().Be(TenancyActor<SeatId>.OfOperator(Odette, TenancyWork.SystemScope));
            Current.Seat.Should().BeNull();
            TenancyCallers.CurrentTenantOrNull().Should().BeNull();
            Callers.Ambient!.Kind.Should().Be(CallerKind.SystemIn);
            Callers.Ambient.Scope.Should().Be(TenancyWork.SystemScope);
            Callers.Ambient.UserId.Should().BeNull("the operator is who the work is recorded as, never who it runs as");
        }

        using (TenancyWork.BeginOperatorIn<TenantId, SeatId>(Harbor, Odette))
        {
            Current.Kind.Should().Be(TenancyCallerKind.SystemInTenant);
            Current.Tenant.Should().Be(Harbor);
            Current.Seat.Should().BeNull("an operator holds no seat, and none is recorded for it");
            Current.Actor.Should().Be(new TenancyActor<SeatId>(TenancyActorKind.Operator, Operator: Odette, Scope: "tenancy"));
            Callers.Ambient!.Scope.Should().Be(TenancyWork.SystemScope);
        }

        using (TenancyWork.BeginOperatorIn<TenantId, SeatId>(Harbor, Odette, scope: "operations"))
        {
            Current.Actor!.Value.Scope.Should().Be("operations");
            Callers.Ambient!.Scope.Should().Be("operations");
        }

        Callers.Ambient.Should().BeNull();
        TenancyCallers.Ambient.Should().BeNull();

        // An operator is somebody: the empty identity is refused, and so is a scope that is none, before anything begins.
        FluentActions.Invoking(() => TenancyWork.BeginOperator<TenantId, SeatId>(Guid.Empty)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => TenancyWork.BeginOperatorIn<TenantId, SeatId>(Harbor, Guid.Empty)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => TenancyWork.BeginOperatorIn<TenantId, SeatId>(Harbor, Odette, scope: "Not A Scope")).Should().Throw<ArgumentException>();
        Callers.Ambient.Should().BeNull("a refused beginning leaves no caller behind");
        TenancyCallers.Ambient.Should().BeNull();
    }

    [Fact]
    public void Begin_token_names_the_seat_the_token_stands_for()
    {
        using (TenancyWork.BeginTokenIn(Harbor, Ada, scope: "projects"))
        {
            Current.Kind.Should().Be(TenancyCallerKind.SystemInTenant, "it is system work in the tenant, not the seat");
            Current.Tenant.Should().Be(Harbor);
            Current.Seat.Should().Be(Ada);
            Current.Actor.Should().Be(TenancyActor<SeatId>.OfToken(Ada, "projects"));
            Callers.Ambient!.Scope.Should().Be("projects");
        }

        FluentActions.Invoking(() => TenancyWork.BeginTokenIn(Harbor, Ada, scope: "")).Should().Throw<ArgumentException>();
        TenancyCallers.Ambient.Should().BeNull();
    }

    [Fact]
    public void System_work_is_never_recorded_as_a_seat()
    {
        var seat = TenancyActor<SeatId>.OfSeat(Ada);

        FluentActions.Invoking(() => HostCaller.SystemIn(Harbor, seat)).Should().Throw<ArgumentException>().WithMessage("*never as a seat*");
        FluentActions.Invoking(() => HostCaller.SystemBy(seat)).Should().Throw<ArgumentException>();
        HostCaller.SystemBy(TenancyActor<SeatId>.OfOperator(Odette, "tenancy")).Kind.Should().Be(TenancyCallerKind.System);
    }

    [Fact]
    public void System_work_is_recorded_as_somebody()
    {
        // An actor is a value anybody can put together. One that names nobody would go into records that are kept
        // for good, so the caller takes only what the factories make: every kind with what it is named by.
        TenancyActor<SeatId>[] nobody =
        [
            default,
            new(TenancyActorKind.Operator),
            new(TenancyActorKind.Operator, Scope: "tenancy"),
            new(TenancyActorKind.Operator, Operator: Guid.Empty, Scope: "tenancy"),
            new(TenancyActorKind.Operator, Operator: Odette),
            new(TenancyActorKind.Token, Scope: "projects"),
            new(TenancyActorKind.Token, Ada),
            new(TenancyActorKind.System),
            new(TenancyActorKind.System, Scope: " "),
            new((TenancyActorKind)99, Ada, Odette, "tenancy"),
            TenancyActor<SeatId>.OfOperator(Odette, "tenancy") with { Operator = null },
        ];
        foreach (var actor in nobody)
        {
            FluentActions.Invoking(() => HostCaller.SystemIn(Harbor, actor)).Should().Throw<ArgumentException>($"{actor} names nobody");
            FluentActions.Invoking(() => HostCaller.SystemBy(actor)).Should().Throw<ArgumentException>($"{actor} names nobody");
        }

        // What the factories make is taken as it is.
        TenancyActor<SeatId>[] somebody =
        [
            TenancyActor<SeatId>.OfSystem("projects"),
            TenancyActor<SeatId>.OfSystem("projects", Ada),
            TenancyActor<SeatId>.OfOperator(Odette, "operations"),
            TenancyActor<SeatId>.OfToken(Ada, "projects"),
        ];
        foreach (var actor in somebody)
        {
            HostCaller.SystemIn(Harbor, actor).Actor.Should().Be(actor);
            HostCaller.SystemBy(actor).Actor.Should().Be(actor);
        }
    }

    [Fact]
    public void The_actor_maps_onto_the_cores_acted_by()
    {
        var seat = new SeatId(Guid.Parse("0f3c1d2e-4b5a-4c6d-8e7f-9a0b1c2d3e4f"));
        var identity = Guid.Parse("A1B2C3D4-E5F6-4789-8ABC-DEF012345678");

        // A kind, and one id: a seat's and an operator's as a database writes the same value as text.
        TenancyActor<SeatId>.OfSeat(seat).ToActedBy().Should().Be(new ActedBy("seat", "0f3c1d2e-4b5a-4c6d-8e7f-9a0b1c2d3e4f"));
        TenancyActor<SeatId>.OfOperator(identity, "tenancy").ToActedBy().Should().Be(new ActedBy("operator", "a1b2c3d4-e5f6-4789-8abc-def012345678"));
        TenancyActor<SeatId>.OfSystem("projects").ToActedBy().Should().Be(new ActedBy("system", "projects"));
        TenancyActor<SeatId>.OfSystem("projects", seat).ToActedBy().Should().Be(new ActedBy("system", "projects"), "the seat the work is done for is not who acted");
        TenancyActor<SeatId>.OfToken(seat, "projects").ToActedBy().Should().Be(new ActedBy("token", "0f3c1d2e-4b5a-4c6d-8e7f-9a0b1c2d3e4f"));

        // An id that wraps a number is written in digits, whatever the culture of the thread.
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("nl-NL");
            TenancyActor<TenantId>.OfSeat(new TenantId(1234567)).ToActedBy().Id.Should().Be("1234567");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }

        // The system's kind is the toolkit's own; the other three are Tenancy's.
        TenancyActorKinds.System.Should().Be(ActedByKinds.System);
        Enum.GetValues<TenancyActorKind>().Select(TenancyActorKinds.From).Should().Equal("seat", "operator", "system", "token");
        TenancyActor<SeatId>.OfSystem("tenancy").ToActedBy().Should().Be(ActedBy.From(Caller.SystemIn("tenancy")), "system work is recorded as the toolkit records it");
    }

    [Fact]
    public void An_actor_is_stored_with_its_kind_as_a_word_whatever_the_serializer_writes_enums_as()
    {
        (TenancyActor<SeatId> Actor, string Word)[] actors =
        [
            (TenancyActor<SeatId>.OfSeat(Ada), "seat"),
            (TenancyActor<SeatId>.OfOperator(Odette, "tenancy"), "operator"),
            (TenancyActor<SeatId>.OfSystem("tenancy", Ada), "system"),
            (TenancyActor<SeatId>.OfToken(Ada, "projects"), "token"),
        ];

        // As a serializer writes an enum by default, as a number, and as one told to write every enum by its member's
        // name: an event is stored for good, so the kind on it is the word a record keeps, under either.
        var byName = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        JsonSerializer.Serialize(TenantShape.Flat, byName).Should().Be("\"Flat\"", "these options do write an enum by name");

        foreach (var options in new[] { new JsonSerializerOptions(), byName })
        {
            foreach (var (actor, word) in actors)
            {
                var json = JsonSerializer.Serialize(actor, options);

                using var stored = JsonDocument.Parse(json);
                stored.RootElement.GetProperty("Kind").GetString().Should().Be(word);
                stored.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(["Kind", "Seat", "Operator", "Scope"]);
                JsonSerializer.Deserialize<TenancyActor<SeatId>>(json, options).Should().Be(actor, "it is read back as it was written");
            }
        }

        // The words are the ones a row that keeps who acted has, and a kind on its own is written the same way.
        actors.Select(pair => pair.Word).Should().Equal(actors.Select(pair => pair.Actor.ToActedBy().Kind));
        JsonSerializer.Serialize(TenancyActorKind.Token).Should().Be("\"token\"");

        // Anything else is refused, and not read as some kind: a number, a member's name, nothing.
        foreach (var kind in new[] { "1", "\"Seat\"", "\"person\"", "null" })
        {
            FluentActions.Invoking(() => JsonSerializer.Deserialize<TenancyActor<SeatId>>("{\"Kind\":" + kind + "}"))
                .Should().Throw<JsonException>(kind);
        }
    }

    [Fact]
    public void Every_tenancy_event_type_carries_who_made_the_change()
    {
        EventTypes.Should().HaveCount(28, "an event that is added is registered with the outbox and, when it changes access, kept in the history");

        foreach (var type in EventTypes)
        {
            var constructor = type.GetConstructors().Should().ContainSingle($"{type.Name} is a positional record").Which;
            var by = constructor.GetParameters()[^1];

            by.Name.Should().Be("By", $"{type.Name} ends with who made the change");
            var actor = Nullable.GetUnderlyingType(by.ParameterType);
            actor.Should().NotBeNull($"{type.Name} may be raised with nobody named");
            actor!.GetGenericTypeDefinition().Should().Be(typeof(TenancyActor<>));

            // The actor is closed over the seat id of the event itself, so an event about a tenant, a unit or a role names it too.
            var seat = actor.GetGenericArguments()[0];
            seat.IsGenericParameter.Should().BeTrue();
            seat.Name.Should().Be("TSeatId");
            type.GetGenericArguments().Should().Contain(seat);

            type.GetProperty("TenantId").Should().NotBeNull($"{type.Name} says which tenant it is about, which the access history files it under");
        }
    }

    [Fact]
    public async Task Every_event_a_use_case_raises_names_who_made_it()
    {
        var harness = Harness.OfHarbor();
        var harbor = harness.Harbor;
        var ada = harness.Administrator;
        var watcher = harness.RoleFromPack(HostCatalogue.WatcherPack);

        // Every command a seat gives, as Harbor's administrator.
        await harness.As(ada, use => use.Tenants.RenameOrganizationAsync("Harbor Yards", Cancellation));
        var east = await harness.As(ada, use => use.Organization.AddUnitAsync(harbor.Root, "East", Cancellation));
        await harness.As(ada, use => use.Organization.RenameUnitAsync(east, "Far East", Cancellation));
        await harness.As(ada, use => use.Organization.MoveUnitAsync(east, harbor.North, Cancellation));
        await harness.As(ada, use => use.Organization.ArchiveUnitAsync(east, Cancellation));

        var polisher = await harness.As(ada, use => use.Roles.CreateAsync("Polisher", "Polishes widgets", [HostCatalogue.WidgetRead], Cancellation));
        await harness.As(ada, use => use.Roles.RenameAsync(polisher, "Buffer", "Buffs widgets", Cancellation));
        await harness.As(ada, use => use.Roles.SetKeysAsync(polisher, [HostCatalogue.WidgetCreate], Cancellation));
        await harness.As(ada, use => use.Roles.ArchiveAsync(polisher, Cancellation));

        var ben = await harness.As(ada, use => use.Seats.AddSeatAsync(Guid.NewGuid(), Cancellation, configure: added => added.Rename("Ben")));
        await harness.As(ada, use => use.Seats.PlaceAsync(ben, harbor.North, primary: true, Cancellation));
        await harness.As(ada, use => use.Seats.PlaceAsync(ben, harbor.South, primary: false, Cancellation));
        await harness.As(ada, use => use.Seats.MakePrimaryAsync(ben, harbor.South, Cancellation));
        await harness.As(ada, use => use.Seats.GrantAsync(ben, harbor.South, watcher, until: null, reason: null, Cancellation));
        await harness.As(ada, use => use.Seats.RevokeAsync(ben, harbor.South, watcher, Cancellation));
        await harness.As(ada, use => use.Seats.GrantAsync(ben, harbor.North, watcher, until: null, reason: null, Cancellation));
        await harness.As(ada, use => use.Seats.WithdrawAsync(ben, harbor.North, Cancellation));
        await harness.As(ada, use => use.Seats.SuspendAsync(ben, Cancellation));
        await harness.As(ada, use => use.Seats.ReactivateAsync(ben, Cancellation));
        await harness.As(ada, use => use.Seats.DeactivateAsync(ben, Cancellation));

        var withdrawn = await harness.As(ada, use => use.Invitations.IssueAsync("lark@example.test", harbor.North, watcher, null, null, Cancellation));
        await harness.As(ada, use => use.Invitations.CancelAsync(withdrawn.Id, Cancellation));
        var invited = await harness.As(ada, use => use.Invitations.IssueAsync("wren@example.test", harbor.North, watcher, null, null, Cancellation));

        var bySeat = harness.Store.SavedEvents.ToList();
        bySeat.Should().HaveCountGreaterThan(20).And.OnlyContain(raised => ByOf(raised) == TenancyActor<SeatId>.OfSeat(ada));

        // An invitation is accepted by somebody who has no seat yet: the application's work, for the seat that issued it.
        await harness.Accept(Guid.NewGuid(), invited.Token);

        var forTheIssuer = harness.Store.SavedEvents.Skip(bySeat.Count).ToList();
        forTheIssuer.Should().HaveCount(4).And.OnlyContain(raised => ByOf(raised) == TenancyActor<SeatId>.OfSystem(TenancyWork.SystemScope, ada));
        var before = bySeat.Count + forTheIssuer.Count;

        // A tenant's own life is the application's work: provisioned by the system, ...
        HostTenancy.ProvisionedTenant quay;
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            harness.Store.BeginUnitOfWork();
            quay = await harness.Tenants.ProvisionAsync(
                new HostTenancy.TenantToProvision("quay", "Quay Works", TenantShape.Flat, "Quay", Guid.NewGuid()),
                Cancellation);
        }

        var bySystem = harness.Store.SavedEvents.Skip(before).ToList();
        bySystem.Should().NotBeEmpty().And.OnlyContain(raised => ByOf(raised) == TenancyActor<SeatId>.OfSystem(TenancyWork.SystemScope));

        // ... Harbor's roles follow a pack the application changed, as its work too, ...
        var later = TenancyCatalogue.Build(
            HostCatalogue.Application with
            {
                Packs = [.. HostCatalogue.Application.Packs.Select(pack => pack.Key == HostCatalogue.WatcherPack ? pack with { Keys = [HostCatalogue.WidgetCreate] } : pack)],
            },
            []);
        await harness.BySystemWork(use => new HostTenancy.RoleCommands(use.Store, later, use.Clock).FollowPacksAsync(Cancellation));
        var followed = harness.Store.SavedEvents.Skip(before + bySystem.Count).ToList();
        followed.Should().ContainSingle().Which.Should().BeOfType<RoleFollowedItsPack<TenantId, RoleId, SeatId>>();
        followed.Should().OnlyContain(raised => ByOf(raised) == TenancyActor<SeatId>.OfSystem(TenancyWork.SystemScope));
        bySystem.AddRange(followed);

        // ... made a tree by its administrator, with the roles of the packs seeded for the new shape, ...
        await harness.Run(HostCaller.InSeat(quay.Tenant, quay.AdminSeat), use => use.Tenants.ChangeShapeAsync(TenantShape.Hierarchical, roleIds: null, language: null, Cancellation));

        var byQuin = harness.Store.SavedEvents.Skip(before + bySystem.Count).ToList();
        byQuin.Select(raised => raised.GetType().GetGenericTypeDefinition()).Should().Contain([typeof(TenantShapeChanged<,>), typeof(RoleCreated<,,>)]);
        byQuin.Should().OnlyContain(raised => ByOf(raised) == TenancyActor<SeatId>.OfSeat(quay.AdminSeat));

        // ... and suspended, reactivated and closed for an operator.
        using (TenancyWork.BeginOperatorIn<TenantId, SeatId>(quay.Tenant, Odette))
        {
            harness.Store.BeginUnitOfWork();
            await harness.Tenants.SuspendAsync("unpaid", Cancellation);
            harness.Store.BeginUnitOfWork();
            await harness.Tenants.ReactivateAsync(Cancellation);
            harness.Store.BeginUnitOfWork();
            await harness.Tenants.CloseAsync("wound up", Cancellation);
        }

        var forTheOperator = harness.Store.SavedEvents.Skip(before + bySystem.Count + byQuin.Count).ToList();
        forTheOperator.Should().HaveCount(3).And.OnlyContain(raised => ByOf(raised) == TenancyActor<SeatId>.OfOperator(Odette, TenancyWork.SystemScope));

        // The script gave every command, so every event there is was raised by one, and none of them unnamed.
        harness.Store.SavedEvents.Select(raised => raised.GetType().GetGenericTypeDefinition()).Distinct()
            .Should().BeEquivalentTo(EventTypes);
    }

    [Fact]
    public async Task A_grant_by_a_seat_names_the_seat()
    {
        var harness = Harness.OfHarbor();
        var ben = await harness.SeatAt("Ben", harness.Harbor.North);
        var before = harness.Store.SavedEvents.Count;

        await harness.As(harness.Administrator, use => use.Seats.GrantAsync(
            ben, harness.Harbor.North, harness.RoleFromPack(HostCatalogue.WatcherPack), until: null, reason: null, Cancellation));

        var granted = harness.Store.SavedEvents.Skip(before).Should().ContainSingle()
            .Which.Should().BeOfType<OrganizationRoleGranted<TenantId, SeatId, OrganizationUnitId, RoleId>>().Which;
        granted.SeatId.Should().Be(ben);
        granted.By.Should().Be(TenancyActor<SeatId>.OfSeat(harness.Administrator));
        granted.GrantedBy.Should().Be(harness.Administrator, "the grant keeps the same seat as its granter");
    }

    [Fact]
    public async Task System_work_for_a_seat_names_the_system_and_the_seat()
    {
        var harness = Harness.OfHarbor();
        var ada = harness.Administrator;
        var ben = await harness.SeatAt("Ben", harness.Harbor.North);
        var watcher = harness.RoleFromPack(HostCatalogue.WatcherPack);
        var before = harness.Store.SavedEvents.Count;

        // An import that acts for Ada. The system did it: Ada is who it was done for, and who the grant keeps.
        using (TenancyWork.BeginSystemIn(harness.Tenant, (SeatId?)ada))
        {
            harness.Store.BeginUnitOfWork();
            await harness.Seats.GrantAsync(ben, harness.Harbor.North, watcher, until: null, reason: null, Cancellation);

            // On the very seat it acts for, the grant keeps nobody, and the event still says whom the work was for.
            harness.Store.BeginUnitOfWork();
            await harness.Seats.GrantAsync(ada, harness.Harbor.Root, watcher, until: null, reason: null, Cancellation);
        }

        var granted = harness.Store.SavedEvents.Skip(before).Cast<OrganizationRoleGranted<TenantId, SeatId, OrganizationUnitId, RoleId>>().ToList();
        granted.Should().HaveCount(2).And.OnlyContain(grant => grant.By == TenancyActor<SeatId>.OfSystem(TenancyWork.SystemScope, ada));
        granted[0].By!.Value.Should().Be(new TenancyActor<SeatId>(TenancyActorKind.System, ada, Scope: "tenancy"));
        granted[0].By!.Value.ToActedBy().Should().Be(new ActedBy("system", "tenancy"), "the seat the work is done for is not who acted");
        granted[0].GrantedBy.Should().Be(ada);
        granted[1].GrantedBy.Should().BeNull("system work that acts on the seat it acts for records no granter");
    }

    [Fact]
    public async Task An_operators_suspension_names_the_operators_identity()
    {
        var harness = Harness.OfHarbor();

        using (TenancyWork.BeginOperatorIn<TenantId, SeatId>(harness.Tenant, Odette))
        {
            harness.Store.BeginUnitOfWork();
            await harness.Tenants.SuspendAsync("Asked for by the owner", Cancellation);
        }

        var suspended = harness.Store.SavedEvents.Should().ContainSingle().Which.Should().BeOfType<TenantSuspended<TenantId, SeatId>>().Which;
        suspended.By.Should().Be(TenancyActor<SeatId>.OfOperator(Odette, TenancyWork.SystemScope));
        suspended.By!.Value.Seat.Should().BeNull("an operator holds no seat, in any tenant");
        suspended.By.Value.ToActedBy().Should().Be(new ActedBy("operator", Odette.ToString("D")));
    }

    [Fact]
    public async Task Provisioning_by_the_seeder_names_the_system()
    {
        var harness = new Harness(New.Catalogue());

        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            harness.Store.BeginUnitOfWork();
            await harness.Tenants.ProvisionAsync(
                new HostTenancy.TenantToProvision("harbor", "Harbor Works", TenantShape.Hierarchical, "Harbor Works", Guid.NewGuid()),
                Cancellation);
        }

        // The tenant, its root, a role per pack, the first seat with its placement and its role, and the activation.
        harness.Store.SavedEvents.Select(raised => raised.GetType().GetGenericTypeDefinition()).Distinct().Should().BeEquivalentTo(
        [
            typeof(TenantProvisioned<,>), typeof(OrganizationUnitAdded<,,>), typeof(RoleCreated<,,>), typeof(SeatAdded<,>),
            typeof(SeatPlaced<,,>), typeof(OrganizationRoleGranted<,,,>), typeof(TenantActivated<,>),
        ]);
        harness.Store.SavedEvents.Should().OnlyContain(raised => ByOf(raised) == TenancyActor<SeatId>.OfSystem("tenancy"));
        harness.Store.SavedEvents.OfType<OrganizationRoleGranted<TenantId, SeatId, OrganizationUnitId, RoleId>>().Single().GrantedBy.Should().BeNull();
    }

    [Fact]
    public async Task A_role_keys_change_names_the_keys_added_and_taken_out()
    {
        var catalogue = New.Catalogue();
        var role = New.Role(catalogue, "Operator", HostCatalogue.WidgetRead, HostCatalogue.WidgetCreate);
        role.DrainEvents();
        TenancyActor<SeatId>? by = TenancyActor<SeatId>.OfSeat(Ada);

        // Changing implies reading, so the key that was named before stays, as an implied one now.
        var first = role.AsScenario().When(changed => changed.SetKeys([HostCatalogue.WidgetChange], catalogue, by))
            .SingleEvent<RoleKeysChanged<TenantId, RoleId, SeatId>>();
        first.Added.Should().Equal(HostCatalogue.WidgetChange);
        first.Removed.Should().Equal(HostCatalogue.WidgetCreate);
        first.By.Should().Be(by);
        role.DrainEvents();

        // Taking out the key that implied another takes both out, and both are named, in order.
        var second = role.AsScenario().When(changed => changed.SetKeys<SeatId>([HostCatalogue.WidgetCreate], catalogue))
            .SingleEvent<RoleKeysChanged<TenantId, RoleId, SeatId>>();
        second.Added.Should().Equal(HostCatalogue.WidgetCreate);
        second.Removed.Should().Equal(HostCatalogue.WidgetChange, HostCatalogue.WidgetRead);
        second.By.Should().BeNull("the role was changed with nobody named");

        // Through the use case the keys are the same, and the caller is who changed them.
        var harness = Harness.OfHarbor();
        var watcher = harness.RoleFromPack(HostCatalogue.WatcherPack);
        await harness.As(harness.Administrator, use => use.Roles.SetKeysAsync(watcher, [HostCatalogue.WidgetChange, TenancyKeys.SeatsManage], Cancellation));

        var changedByAda = harness.Store.SavedEvents.Should().ContainSingle().Which.Should().BeOfType<RoleKeysChanged<TenantId, RoleId, SeatId>>().Which;
        changedByAda.RoleId.Should().Be(watcher);
        changedByAda.Added.Should().Equal(TenancyKeys.SeatsManage, HostCatalogue.WidgetChange);
        changedByAda.Removed.Should().BeEmpty("reading is implied by changing, and stays");
        changedByAda.By.Should().Be(TenancyActor<SeatId>.OfSeat(harness.Administrator));
    }

    [Fact]
    public async Task Provisioning_for_an_operator_is_recorded_as_that_operator()
    {
        var harness = new Harness(New.Catalogue());
        var command = new HostTenancy.TenantToProvision("harbor", "Harbor Works", TenantShape.Hierarchical, "Harbor Works", Guid.NewGuid());

        HostTenancy.ProvisionedTenant provisioned;
        using (TenancyWork.BeginOperator<TenantId, SeatId>(Odette))
        {
            harness.Store.BeginUnitOfWork();
            provisioned = await harness.Tenants.ProvisionAsync(command, TestContext.Current.CancellationToken);
        }

        // It saves as system work in the tenant it makes, as any provisioning does, and still as the operator's act.
        var save = harness.Store.Observed.Single(call => call.Name == nameof(HostTenancy.IStore.SaveAsync));
        save.Tenancy!.Kind.Should().Be(TenancyCallerKind.SystemInTenant);
        save.Tenancy.TenantId.Should().Be(provisioned.Tenant);
        save.Tenancy.Actor!.ToActedBy().Should().Be(new ActedBy("operator", Odette.ToString("D")));
        save.Core!.Scope.Should().Be(TenancyWork.SystemScope);

        // Every event of the new tenant says the same: the operator, and no seat.
        harness.Store.SavedEvents.Should().NotBeEmpty().And.OnlyContain(raised => ByOf(raised) == TenancyActor<SeatId>.OfOperator(Odette, TenancyWork.SystemScope));

        // Provisioned by the system itself, it is the system's.
        var other = new Harness(New.Catalogue());
        using (TenancyWork.BeginSystem<TenantId, SeatId>())
        {
            other.Store.BeginUnitOfWork();
            await other.Tenants.ProvisionAsync(command, TestContext.Current.CancellationToken);
        }

        other.Store.Observed.Single(call => call.Name == nameof(HostTenancy.IStore.SaveAsync)).Tenancy!.Actor!.ToActedBy()
            .Should().Be(new ActedBy("system", "tenancy"));
    }
}
