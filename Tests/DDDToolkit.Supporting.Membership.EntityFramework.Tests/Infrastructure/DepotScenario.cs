namespace DDDToolkit.Supporting.Membership.EntityFramework.Tests.Infrastructure;

/// <summary>Somebody a question about the depot's resources is asked as: a user, and the porter the depot knows it as, if any.</summary>
/// <param name="Name">What the tests call them.</param>
/// <param name="User">The user's id: what its token carries.</param>
/// <param name="Porter">The porter the depot knows the user as, or <see langword="null"/> for a user it never took on.</param>
/// <param name="Active">Whether the depot counts the porter.</param>
public sealed record DepotPerson(string Name, Guid User, PorterId? Porter, bool Active)
{
    /// <summary>The caller: a signed-in user, with nothing of the depot in its token.</summary>
    public Caller Caller => TestCallers.User(new UserId(User));

    /// <summary>The porter the depot counts the user as, or nobody.</summary>
    public PorterId? Member => Active ? Porter : null;

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>
/// The data the questions about the depot's resources are asked over: a depot with bays, porters, roles of its
/// own and keys held at bays, three pallets and three crates. Every period is days away from the moment it is
/// made for, so a clock that is a little off, a database's own among them, answers the same.
/// </summary>
/// <remarks>
/// <para>
/// The depot: the hall, with a left and a right aisle under it, and a bay under each (LeftBay, RightBay). Ana, Bo, Cas,
/// Dot, Eli and Flo are porters; Ned is one the depot let go; Ivo is a user it never took on. Its roles: the
/// crate lead (pack, weigh), the packer (pack, move, print labels, manage the depot), and the old hand, a role
/// no longer in use (pack, weigh).
/// </para>
/// <para>
/// The pallets, whose roles are their own: the pears, owned by Ana, with Bo loading, Cas checking until next
/// week, Dot on it with no role and Ned loading; the plums, owned by Bo, with Ana checking; the limes, owned
/// by Cas alone.
/// </para>
/// <para>
/// The crates, whose roles are the depot's: the tea, in LeftBay, owned by Ana, with Bo packing for three days, Cas
/// on it until next week as an old hand, Dot whose packing ended yesterday, and Ned packing; the salt, in RightBay,
/// owned by Bo; the rice, in the left aisle itself, owned by Cas. At bays: Eli sees and moves crates in the
/// left aisle; Flo sees and packs in the whole hall; Bo packs in the left aisle and moves in LeftBay; Ned sees
/// in the whole hall, for what that is worth to somebody the depot let go.
/// </para>
/// </remarks>
public sealed class DepotScenario
{
    /// <summary>The scenario, as it is at <paramref name="now"/>.</summary>
    public DepotScenario(DateTimeOffset now)
    {
        Now = now;
        var monthAgo = now.AddDays(-30);
        var before = now.AddDays(-10);
        var yesterday = now.AddDays(-1);
        NextWeek = now.AddDays(7);
        InThreeDays = now.AddDays(3);

        Porters = [.. People.Where(person => person.Porter is not null).Select(person => new Porter { Id = person.Porter!.Value, UserId = person.User, Active = person.Active })];

        Roles =
        [
            new DepotRole { Id = Lead, Name = CrateMembership.OwnerRole, InUse = true },
            new DepotRole { Id = Packer, Name = "packer", InUse = true },
            new DepotRole { Id = OldHand, Name = "old-hand", InUse = false },
        ];
        RoleKeys =
        [
            Gives(Lead, CrateKeys.Pack), Gives(Lead, CrateKeys.Weigh),
            Gives(Packer, CrateKeys.Pack), Gives(Packer, CrateKeys.Move), Gives(Packer, CrateKeys.PrintLabels), Gives(Packer, CrateKeys.ManageDepot),
            Gives(OldHand, CrateKeys.Pack), Gives(OldHand, CrateKeys.Weigh),
        ];

        Bays =
        [
            new Bay { Id = Hall },
            new Bay { Id = LeftAisle, PartOf = Hall },
            new Bay { Id = LeftBay, PartOf = LeftAisle },
            new Bay { Id = RightAisle, PartOf = Hall },
            new Bay { Id = RightBay, PartOf = RightAisle },
        ];
        BayPaths = [.. Bays.SelectMany(bay => Above(bay.Id).Select(above => new BayPath { AboveId = above, BayId = bay.Id }))];
        BayHolds =
        [
            Holds(Eli, LeftAisle, CrateKeys.See), Holds(Eli, LeftAisle, CrateKeys.Move),
            Holds(Flo, Hall, CrateKeys.See), Holds(Flo, Hall, CrateKeys.Pack),
            Holds(Bo, LeftAisle, CrateKeys.Pack), Holds(Bo, LeftBay, CrateKeys.Move),
            Holds(Ned, Hall, CrateKeys.See),
        ];

        var pears = new Pallet(Pears, "Pears", Ana.Porter!.Value, PalletMembership.Owner, monthAgo);
        pears.PutOn(Bo.Porter!.Value, PalletMembership.Loader, MemberPeriod.Open(before), now, by: Ana.Porter);
        pears.PutOn(Cas.Porter!.Value, PalletMembership.Checker, MemberPeriod.Between(before, NextWeek), now, by: Ana.Porter);
        pears.PutOn(Dot.Porter!.Value, MemberPeriod.Open(before), now, by: Ana.Porter);
        pears.PutOn(Ned.Porter!.Value, PalletMembership.Loader, MemberPeriod.Open(before), now, by: Ana.Porter);

        var plums = new Pallet(Plums, "Plums", Bo.Porter.Value, PalletMembership.Owner, monthAgo);
        plums.PutOn(Ana.Porter.Value, PalletMembership.Checker, MemberPeriod.Open(before), now, by: Bo.Porter);

        var limes = new Pallet(Limes, "Limes", Cas.Porter.Value, PalletMembership.Owner, monthAgo);
        Pallets = [pears, plums, limes];

        var tea = new Crate(Tea, "Tea", LeftBay, Ana.Porter.Value, Lead, monthAgo);
        tea.PutOn(Bo.Porter.Value, MemberPeriod.Open(before), now, by: Ana.Porter);
        tea.GiveRole(Bo.Porter.Value, Packer, MemberPeriod.Between(before, InThreeDays), now, by: Ana.Porter);
        tea.PutOn(Cas.Porter.Value, OldHand, MemberPeriod.Between(before, NextWeek), now, by: Ana.Porter);
        tea.PutOn(Dot.Porter.Value, MemberPeriod.Open(before), now, by: Ana.Porter);
        tea.GiveRole(Dot.Porter.Value, Packer, MemberPeriod.Between(before, yesterday), now, by: Ana.Porter);
        tea.PutOn(Ned.Porter.Value, Packer, MemberPeriod.Open(before), now, by: Ana.Porter);

        var salt = new Crate(Salt, "Salt", RightBay, Bo.Porter.Value, Lead, monthAgo);
        var rice = new Crate(Rice, "Rice", LeftAisle, Cas.Porter.Value, Lead, monthAgo);
        Crates = [tea, salt, rice];
    }

    /// <summary>The moment the scenario is made for.</summary>
    public DateTimeOffset Now { get; }

    /// <summary>When Cas's memberships end.</summary>
    public DateTimeOffset NextWeek { get; }

    /// <summary>When Bo's packing on the tea ends.</summary>
    public DateTimeOffset InThreeDays { get; }

    public DepotPerson Ana { get; } = Person("Ana");

    public DepotPerson Bo { get; } = Person("Bo");

    public DepotPerson Cas { get; } = Person("Cas");

    public DepotPerson Dot { get; } = Person("Dot");

    public DepotPerson Eli { get; } = Person("Eli");

    public DepotPerson Flo { get; } = Person("Flo");

    /// <summary>A porter the depot let go: still on a pallet and a crate, and nobody's member any more.</summary>
    public DepotPerson Ned { get; } = Person("Ned", active: false);

    /// <summary>A user the depot never took on.</summary>
    public DepotPerson Ivo { get; } = new("Ivo", Guid.CreateVersion7(), null, false);

    /// <summary>Everybody a question is asked as.</summary>
    public IReadOnlyList<DepotPerson> People => [Ana, Bo, Cas, Dot, Eli, Flo, Ned, Ivo];

    public DepotRoleId Lead { get; } = DepotRoleId.CreateSequential();

    public DepotRoleId Packer { get; } = DepotRoleId.CreateSequential();

    public DepotRoleId OldHand { get; } = DepotRoleId.CreateSequential();

    public BayId Hall { get; } = BayId.CreateSequential();

    public BayId LeftAisle { get; } = BayId.CreateSequential();

    public BayId LeftBay { get; } = BayId.CreateSequential();

    public BayId RightAisle { get; } = BayId.CreateSequential();

    public BayId RightBay { get; } = BayId.CreateSequential();

    public PalletId Pears { get; } = PalletId.CreateSequential();

    public PalletId Plums { get; } = PalletId.CreateSequential();

    public PalletId Limes { get; } = PalletId.CreateSequential();

    public CrateId Tea { get; } = CrateId.CreateSequential();

    public CrateId Salt { get; } = CrateId.CreateSequential();

    public CrateId Rice { get; } = CrateId.CreateSequential();

    /// <summary>The depot's porters, as rows.</summary>
    public IReadOnlyList<Porter> Porters { get; }

    /// <summary>The depot's roles, as rows.</summary>
    public IReadOnlyList<DepotRole> Roles { get; }

    /// <summary>The keys the depot's roles give, as rows.</summary>
    public IReadOnlyList<DepotRoleKey> RoleKeys { get; }

    /// <summary>The bays, as rows.</summary>
    public IReadOnlyList<Bay> Bays { get; }

    /// <summary>Every bay with each bay above it, and with itself.</summary>
    public IReadOnlyList<BayPath> BayPaths { get; }

    /// <summary>The keys porters hold at bays.</summary>
    public IReadOnlyList<BayHold> BayHolds { get; }

    /// <summary>The pallets, as their aggregates: what the questions are compared with.</summary>
    public IReadOnlyList<Pallet> Pallets { get; }

    /// <summary>The crates, as their aggregates.</summary>
    public IReadOnlyList<Crate> Crates { get; }

    /// <summary>Every key a question about pallets is asked with, and one nobody declared.</summary>
    public static IReadOnlyList<string> PalletKeysAsked => [PalletKeys.See, PalletKeys.Load, PalletKeys.Strap, "pallets.unheard-of"];

    /// <summary>Every key a question about crates is asked with, another module's and the depot's own among them, and one nobody declared.</summary>
    public static IReadOnlyList<string> CrateKeysAsked
        => [CrateKeys.See, CrateKeys.Pack, CrateKeys.Weigh, CrateKeys.Move, CrateKeys.Scrap, CrateKeys.PrintLabels, CrateKeys.ManageDepot, "crates.unheard-of"];

    /// <summary>Saves the scenario as the application's own work, and has the depot's desk know its porters.</summary>
    public async Task SaveAsync(DepotServices services)
        => await services.AsAsync(Caller.System, async provider =>
        {
            var desk = provider.GetRequiredService<DepotDesk>();
            foreach (var porter in Porters)
            {
                desk.TakeOn(porter);
            }

            var context = provider.GetRequiredService<DepotContext>();
            context.Porters.AddRange(Porters);
            context.Roles.AddRange(Roles);
            context.RoleKeys.AddRange(RoleKeys);
            context.Bays.AddRange(Bays);
            context.BayPaths.AddRange(BayPaths);
            context.BayHolds.AddRange(BayHolds);
            context.Pallets.AddRange(Pallets);
            context.Crates.AddRange(Crates);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

    /// <summary>
    /// The pallets somebody holds <paramref name="key"/> on, worked out from the aggregates in memory with the
    /// rules, the way a reader of the rules would: what every storage has to agree with.
    /// </summary>
    public IReadOnlyList<PalletId> PalletsHeldBy(DepotPerson person, string key)
    {
        var rules = PalletMembership.Rules;
        return [.. Pallets
            .Where(pallet => person.Member is { } member
                && ((rules.OwnerHolds(key) && pallet.OwnerId == member)
                    || pallet.Porters.Any(row => row.MemberId == member && row.AppliesAt(Now)
                        && (rules.MembershipGives(key) || rules.RolesWith(key).Any(role => row.HoldsAt(role, Now))))))
            .Select(pallet => pallet.Id)
            .Order()];
    }

    /// <summary>The pallets somebody sees: those the porter the depot counts it as owns, or is on now.</summary>
    public IReadOnlyList<PalletId> PalletsSeenBy(DepotPerson person)
        => [.. Pallets.Where(pallet => person.Member is { } member && (pallet.OwnerId == member || pallet.Porters.Any(row => row.MemberId == member && row.AppliesAt(Now)))).Select(pallet => pallet.Id).Order()];

    /// <summary>The pallets where somebody is on it now and holds a role now that gives <paramref name="key"/>.</summary>
    public IReadOnlyList<PalletId> PalletsWithARoleFor(DepotPerson person, string key)
        => [.. Pallets
            .Where(pallet => person.Member is { } member
                && pallet.Porters.Any(row => row.MemberId == member && PalletMembership.Rules.RolesWith(key).Any(role => row.HoldsAt(role, Now))))
            .Select(pallet => pallet.Id)
            .Order()];

    /// <summary>The crates somebody is on now.</summary>
    public IReadOnlyList<CrateId> CratesAsMember(DepotPerson person)
        => [.. Crates.Where(crate => person.Member is { } member && crate.Porters.Any(row => row.MemberId == member && row.AppliesAt(Now))).Select(crate => crate.Id).Order()];

    /// <summary>
    /// The crates where somebody is on it now and holds a role of the depot's now that gives
    /// <paramref name="key"/>: a role in use, and a key the crate's rules let a member's role give.
    /// </summary>
    public IReadOnlyList<CrateId> CratesWithARoleFor(DepotPerson person, string key)
        => [.. Crates
            .Where(crate => person.Member is { } member
                && CrateMembership.Rules.MemberKeys.Allows(key)
                && crate.Porters.Any(row => row.MemberId == member && RolesGiving(key).Any(role => row.HoldsAt(role, Now))))
            .Select(crate => crate.Id)
            .Order()];

    /// <summary>The crates that stand where somebody holds <paramref name="key"/>, or below: reached from above.</summary>
    public IReadOnlyList<CrateId> CratesReachedFromAbove(DepotPerson person, string key)
        => [.. Crates
            .Where(crate => person.Member is { } member
                && BayHolds.Any(hold => hold.PorterId == member && hold.Key == key && Above(crate.BayId).Contains(hold.BayId)))
            .Select(crate => crate.Id)
            .Order()];

    /// <summary>The crates somebody sees: those it owns, those it is on now, and those that stand where it holds the key that sees.</summary>
    public IReadOnlyList<CrateId> CratesSeenBy(DepotPerson person)
        => [.. Crates.Where(crate => person.Member is { } member && crate.OwnerId == member).Select(crate => crate.Id)
            .Union(CratesAsMember(person)).Union(CratesReachedFromAbove(person, CrateKeys.See)).Order()];

    /// <summary>
    /// The crates somebody holds <paramref name="key"/> on, worked out in memory with the rules and the depot's
    /// rows: by owning it, by being on it, through a role of the depot's, or from above.
    /// </summary>
    public IReadOnlyList<CrateId> CratesHeldBy(DepotPerson person, string key)
    {
        var rules = CrateMembership.Rules;
        var owned = Crates.Where(crate => person.Member is { } member && rules.OwnerHolds(key) && crate.OwnerId == member).Select(crate => crate.Id);
        var joined = rules.MembershipGives(key) ? CratesAsMember(person) : [];

        return [.. owned.Union(joined).Union(CratesWithARoleFor(person, key)).Union(CratesReachedFromAbove(person, key)).Order()];
    }

    /// <summary>The depot's roles in use that give <paramref name="key"/>.</summary>
    public IReadOnlyList<DepotRoleId> RolesGiving(string key)
        => [.. RoleKeys.Where(given => given.Key == key && Roles.Single(role => role.Id == given.RoleId).InUse).Select(given => given.RoleId)];

    /// <summary>A bay, and every bay above it.</summary>
    private IEnumerable<BayId> Above(BayId bay)
    {
        for (BayId? at = bay; at is { } here; at = Bays.Single(candidate => candidate.Id == here).PartOf)
        {
            yield return here;
        }
    }

    private static DepotPerson Person(string name, bool active = true) => new(name, Guid.CreateVersion7(), PorterId.CreateSequential(), active);

    private static DepotRoleKey Gives(DepotRoleId role, string key) => new() { RoleId = role, Key = key };

    private static BayHold Holds(DepotPerson person, BayId bay, string key) => new() { PorterId = person.Porter!.Value, BayId = bay, Key = key };
}
