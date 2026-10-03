namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests.Infrastructure;

/// <summary>Somebody a question about plots is asked as: a user, and the garden its requests are in.</summary>
/// <param name="Name">What the tests call them.</param>
/// <param name="User">The user's id: what its token carries, and what a gardener is known by.</param>
/// <param name="Garden">The garden the user's requests are in, as the host knows it.</param>
public sealed record GardenPerson(string Name, Guid User, GardenId Garden)
{
    /// <summary>The caller: a signed-in user, whose token says the garden its requests are in, as the host's database reads it.</summary>
    public Caller Caller => Callers.FromClaims(TestCallers.Claims(User, staff: null, garden: Garden.Value));

    /// <summary>Who the user is as a gardener.</summary>
    public UserId Member => new(User);

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>
/// The data the questions about plots and sheds are asked over: two gardens, each with roles of its own for
/// its plots, three plots, and one shed with the roles the whole application keeps for sheds. Every period is
/// days away from the moment it is made for, so a clock that is a little off, a database's own among them,
/// answers the same.
/// </summary>
/// <remarks>
/// <para>
/// The meadow began with the three starter roles, the owner's, the tender's (see, plant, water) and the
/// waterer's (see, water), and made two of its own since: the fencer (see, fence), and the seasonal hand
/// (plant, water), which it put away. The orchard began with the same three, calls its owner's role the
/// head gardener, and took watering out of its tender's.
/// </para>
/// <para>
/// In the meadow: the beans, owned by Ada, with Ben tending, Cy on it until next week and watering for three
/// days, Di on it with no role, Eve fencing, and Fay as a seasonal hand; and the leeks, owned by Ben, with
/// Ada watering and Eve on it as a tender until yesterday. In the orchard: the kale, owned by Gil, with Hal
/// tending. Jo is on no plot.
/// </para>
/// <para>
/// The sheds have one set of roles, in no garden's name: the keeper, their owner's role (open, stock), and
/// the lender, made since (open, and lending tools, a key of another module's). The tool shed is owned by
/// Ada, with Ben lending and Di in it with no role.
/// </para>
/// </remarks>
public sealed class GardenScenario
{
    /// <summary>The scenario, as it is at <paramref name="now"/>.</summary>
    public GardenScenario(DateTimeOffset now)
    {
        Now = now;
        var monthAgo = now.AddDays(-30);
        var before = now.AddDays(-10);
        var yesterday = now.AddDays(-1);
        NextWeek = now.AddDays(7);
        InThreeDays = now.AddDays(3);

        Ada = Person("Ada", Meadow);
        Ben = Person("Ben", Meadow);
        Cy = Person("Cy", Meadow);
        Di = Person("Di", Meadow);
        Eve = Person("Eve", Meadow);
        Fay = Person("Fay", Meadow);
        Gil = Person("Gil", Orchard);
        Hal = Person("Hal", Orchard);
        Jo = Person("Jo", Meadow);

        // Each garden's first roles, made as a host makes them: from the drafts of the starter roles.
        MeadowOwner = Starter(Meadow, MembershipRules.DefaultOwnerRole);
        MeadowTender = Starter(Meadow, PlotMembership.Tender);
        MeadowWaterer = Starter(Meadow, PlotMembership.Waterer);
        OrchardOwner = Starter(Orchard, MembershipRules.DefaultOwnerRole);
        OrchardTender = Starter(Orchard, PlotMembership.Tender);
        OrchardWaterer = Starter(Orchard, PlotMembership.Waterer);

        // And what each garden did with them since.
        Fencer = new PlotRole(PlotRoleId.CreateSequential(), Meadow, new KeptRoleDraft("Fencer", "Keeps the rabbits out.", [PlotKeys.See, PlotKeys.Fence]));
        Seasonal = new PlotRole(PlotRoleId.CreateSequential(), Meadow, new KeptRoleDraft("Seasonal hand", null, [PlotKeys.Plant, PlotKeys.Water]));
        Seasonal.PutAway();
        OrchardOwner.CallIt("Head gardener", "Whoever a plot of the orchard belongs to.");
        OrchardTender.HaveItGive(PlotKeys.See, PlotKeys.Plant);
        Roles = [MeadowOwner, MeadowTender, MeadowWaterer, Fencer, Seasonal, OrchardOwner, OrchardTender, OrchardWaterer];

        var beans = new Plot(Beans, Meadow, "Beans", Ada.Member, MeadowOwner.Id, monthAgo);
        beans.LetOn(Ben.Member, MeadowTender.Id, MemberPeriod.Open(before), now, by: Ada.Member);
        beans.LetOn(Cy.Member, MemberPeriod.Between(before, NextWeek), now, by: Ada.Member);
        beans.GiveRole(Cy.Member, MeadowWaterer.Id, MemberPeriod.Between(before, InThreeDays), now, by: Ada.Member);
        beans.LetOn(Di.Member, MemberPeriod.Open(before), now, by: Ada.Member);
        beans.LetOn(Eve.Member, Fencer.Id, MemberPeriod.Open(before), now, by: Ada.Member);
        beans.LetOn(Fay.Member, Seasonal.Id, MemberPeriod.Open(before), now, by: Ada.Member);

        var leeks = new Plot(Leeks, Meadow, "Leeks", Ben.Member, MeadowOwner.Id, monthAgo);
        leeks.LetOn(Ada.Member, MeadowWaterer.Id, MemberPeriod.Open(before), now, by: Ben.Member);
        leeks.LetOn(Eve.Member, MemberPeriod.Open(before), now, by: Ben.Member);
        leeks.GiveRole(Eve.Member, MeadowTender.Id, MemberPeriod.Between(before, yesterday), now, by: Ben.Member);

        var kale = new Plot(Kale, Orchard, "Kale", Gil.Member, OrchardOwner.Id, monthAgo);
        kale.LetOn(Hal.Member, OrchardTender.Id, MemberPeriod.Open(before), now, by: Gil.Member);
        Plots = [beans, leeks, kale];

        // The sheds' roles: the one starter role, and one made since.
        ShedKeeper = new ShedRole(ShedRoleId.CreateSequential(), StarterRoles.Missing<ShedRoleId>(ShedMembership.Rules, []).Single());
        Lender = new ShedRole(ShedRoleId.CreateSequential(), new KeptRoleDraft("Lender", "Lends the tools out.", [ShedKeys.Open, ShedKeys.LendTools]));
        ShedRoles = [ShedKeeper, Lender];

        var tools = new Shed(ToolShed, "Tools", Ada.Member, ShedKeeper.Id, monthAgo);
        tools.LetIn(Ben.Member, Lender.Id, MemberPeriod.Open(before), now, by: Ada.Member);
        tools.LetIn(Di.Member, MemberPeriod.Open(before), now, by: Ada.Member);
        Sheds = [tools];
    }

    /// <summary>The moment the scenario is made for.</summary>
    public DateTimeOffset Now { get; }

    /// <summary>When Cy's place on the beans ends.</summary>
    public DateTimeOffset NextWeek { get; }

    /// <summary>When Cy's watering of the beans ends.</summary>
    public DateTimeOffset InThreeDays { get; }

    public GardenId Meadow { get; } = GardenId.CreateSequential();

    public GardenId Orchard { get; } = GardenId.CreateSequential();

    public GardenPerson Ada { get; }

    public GardenPerson Ben { get; }

    public GardenPerson Cy { get; }

    public GardenPerson Di { get; }

    public GardenPerson Eve { get; }

    public GardenPerson Fay { get; }

    public GardenPerson Gil { get; }

    public GardenPerson Hal { get; }

    /// <summary>A user that is on no plot.</summary>
    public GardenPerson Jo { get; }

    /// <summary>Everybody a question is asked as.</summary>
    public IReadOnlyList<GardenPerson> People => [Ada, Ben, Cy, Di, Eve, Fay, Gil, Hal, Jo];

    public PlotRole MeadowOwner { get; }

    public PlotRole MeadowTender { get; }

    public PlotRole MeadowWaterer { get; }

    /// <summary>A role the meadow made for itself: see and fence.</summary>
    public PlotRole Fencer { get; }

    /// <summary>A role the meadow made for itself and put away: it held plant and water.</summary>
    public PlotRole Seasonal { get; }

    /// <summary>The orchard's role for owners, which it calls the head gardener.</summary>
    public PlotRole OrchardOwner { get; }

    /// <summary>The orchard's tender, which no longer waters.</summary>
    public PlotRole OrchardTender { get; }

    public PlotRole OrchardWaterer { get; }

    public PlotId Beans { get; } = PlotId.CreateSequential();

    public PlotId Leeks { get; } = PlotId.CreateSequential();

    public PlotId Kale { get; } = PlotId.CreateSequential();

    /// <summary>The sheds' role for owners, made from the starter role their rules declare.</summary>
    public ShedRole ShedKeeper { get; }

    /// <summary>A role of sheds made since: open, and lend tools, which is another module's key.</summary>
    public ShedRole Lender { get; }

    public ShedId ToolShed { get; } = ShedId.CreateSequential();

    /// <summary>The roles of sheds, as their aggregates.</summary>
    public IReadOnlyList<ShedRole> ShedRoles { get; }

    /// <summary>The sheds, as their aggregates.</summary>
    public IReadOnlyList<Shed> Sheds { get; }

    /// <summary>The roles of both gardens, as their aggregates: what the questions are compared with.</summary>
    public IReadOnlyList<PlotRole> Roles { get; }

    /// <summary>The plots, as their aggregates.</summary>
    public IReadOnlyList<Plot> Plots { get; }

    /// <summary>Every key a question about plots is asked with, and one nobody declared.</summary>
    public static IReadOnlyList<string> KeysAsked => [PlotKeys.See, PlotKeys.Plant, PlotKeys.Water, PlotKeys.Fence, PlotKeys.Sell, "plots.unheard-of"];

    /// <summary>Every key a question about sheds is asked with, another module's among them, and one nobody declared.</summary>
    public static IReadOnlyList<string> ShedKeysAsked => [ShedKeys.Open, ShedKeys.Stock, ShedKeys.Sell, ShedKeys.LendTools, "sheds.unheard-of"];

    /// <summary>Saves the scenario as the application's own work, in no garden.</summary>
    public async Task SaveAsync(GardenServices services)
        => await services.AsAsync(Caller.System, async provider =>
        {
            var context = provider.GetRequiredService<GardenContext>();
            context.PlotRoles.AddRange(Roles);
            context.Plots.AddRange(Plots);
            context.ShedRoles.AddRange(ShedRoles);
            context.Sheds.AddRange(Sheds);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

    /// <summary>The plots somebody sees: those it owns, and those it is a gardener on now.</summary>
    public IReadOnlyList<PlotId> PlotsSeenBy(GardenPerson person)
        => [.. Plots.Where(plot => plot.OwnerId == person.Member || plot.Gardeners.Any(row => row.MemberId == person.Member && row.AppliesAt(Now))).Select(plot => plot.Id).Order()];

    /// <summary>The plots somebody is a gardener on now.</summary>
    public IReadOnlyList<PlotId> PlotsAsMember(GardenPerson person)
        => [.. Plots.Where(plot => plot.Gardeners.Any(row => row.MemberId == person.Member && row.AppliesAt(Now))).Select(plot => plot.Id).Order()];

    /// <summary>
    /// The plots where somebody is a gardener now and holds a role now that gives <paramref name="key"/>: a
    /// role in use that holds the key, for a key the plots' rules let a member's role give.
    /// </summary>
    public IReadOnlyList<PlotId> PlotsWithARoleFor(GardenPerson person, string key)
        => [.. Plots
            .Where(plot => plot.Gardeners.Any(row => row.MemberId == person.Member
                && Roles.Any(role => role.Gives(key, PlotMembership.Rules) && row.HoldsAt(role.Id, Now))))
            .Select(plot => plot.Id)
            .Order()];

    /// <summary>
    /// The plots somebody holds <paramref name="key"/> on, worked out from the aggregates in memory with the
    /// rules, the way a reader of the rules would: by owning it, by being on it, or through a role of plots.
    /// </summary>
    public IReadOnlyList<PlotId> PlotsHeldBy(GardenPerson person, string key)
    {
        var rules = PlotMembership.Rules;
        var owned = Plots.Where(plot => rules.OwnerHolds(key) && plot.OwnerId == person.Member).Select(plot => plot.Id);
        var joined = rules.MembershipGives(key) ? PlotsAsMember(person) : [];

        return [.. owned.Union(joined).Union(PlotsWithARoleFor(person, key)).Order()];
    }

    /// <summary>The sheds somebody sees: those it owns, and those it is a hand in now.</summary>
    public IReadOnlyList<ShedId> ShedsSeenBy(GardenPerson person)
        => [.. Sheds.Where(shed => shed.OwnerId == person.Member || shed.Hands.Any(row => row.MemberId == person.Member && row.AppliesAt(Now))).Select(shed => shed.Id).Order()];

    /// <summary>The sheds where somebody is a hand now and holds a role of sheds now that gives <paramref name="key"/>.</summary>
    public IReadOnlyList<ShedId> ShedsWithARoleFor(GardenPerson person, string key)
        => [.. Sheds
            .Where(shed => shed.Hands.Any(row => row.MemberId == person.Member
                && ShedRoles.Any(role => role.Gives(key, ShedMembership.Rules) && row.HoldsAt(role.Id, Now))))
            .Select(shed => shed.Id)
            .Order()];

    /// <summary>The sheds somebody holds <paramref name="key"/> on: by owning it, or through a role of sheds. Being a hand gives no key.</summary>
    public IReadOnlyList<ShedId> ShedsHeldBy(GardenPerson person, string key)
        => [.. Sheds.Where(shed => ShedMembership.Rules.OwnerHolds(key) && shed.OwnerId == person.Member).Select(shed => shed.Id)
            .Union(ShedsWithARoleFor(person, key))
            .Order()];

    private static GardenPerson Person(string name, GardenId garden) => new(name, Guid.CreateVersion7(), garden);

    /// <summary>A garden's role made from the starter role <paramref name="name"/>, as the rules' drafts say it.</summary>
    private static PlotRole Starter(GardenId garden, string name)
        => new(PlotRoleId.CreateSequential(), garden, StarterRoles.Missing<PlotRoleId>(PlotMembership.Rules, []).Single(draft => draft.MadeFrom == name));
}
