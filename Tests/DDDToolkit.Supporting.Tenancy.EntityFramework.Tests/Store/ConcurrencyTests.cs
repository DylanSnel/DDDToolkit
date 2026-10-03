using DDDToolkit.Exceptions;
using DDDToolkit.Supporting.Tenancy.Catalogue;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// Two commands that each decided on what the other changes cannot both commit. Each race runs command A as far
/// as the hook, commits command B there, and lets A go on: A fails on the tenant's access revision, which it read
/// before anything else, or on the organization's version for two edits of the tree. The database is left as B
/// left it.
/// <para>
/// Harbor: North and South under the root, Coast under North. Grace supervises North; Lin is placed at North
/// and at Coast with no role.
/// </para>
/// </summary>
public abstract class ConcurrencyTests(TestDatabases databases) : IAsyncLifetime
{
    private TestServices _services = null!;

    private HostTenancy.ProvisionedTenant _harbor = null!;
    private OrganizationUnitId _north;
    private OrganizationUnitId _south;
    private OrganizationUnitId _coast;
    private SeatId _grace;
    private SeatId _lin;

    public async ValueTask InitializeAsync() => _services = await databases.ServicesAsync();

    public ValueTask DisposeAsync()
    {
        _services?.Dispose();
        return ValueTask.CompletedTask;
    }

    private RoleId Operator => _harbor.RolesByPack[HostCatalogue.OperatorPack];

    /// <summary>The table of the access revisions, as the provider's SQL names it.</summary>
    private string Revisions => _services.Database.TenancyTable("TenancyAccessRevisions");

    private RoleId Supervisor => _harbor.RolesByPack[HostCatalogue.SupervisorPack];

    private RoleId Administrator => _harbor.AdministratorRole;

    [Fact]
    public async Task Two_revokes_of_the_last_two_administrators_cannot_both_commit()
    {
        await BuildHarborAsync();
        var ada = _harbor.AdminSeat;
        var beth = await _services.SeatAtAsync(_harbor, "Beth", _harbor.RootUnit, HostCatalogue.AdministratorPack);
        (await AdministratorsAsync()).Should().BeEquivalentTo([ada, beth]);

        // Each sees the other administrator, so each passes the last-administrator rule on its own.
        var conflict = await RaceAsync(
            interrupted: services => services.Seats().RevokeAsync(ada, _harbor.RootUnit, Administrator, TestContext.Current.CancellationToken),
            interrupting: services => services.Seats().RevokeAsync(beth, _harbor.RootUnit, Administrator, TestContext.Current.CancellationToken));

        conflict.AggregateType.Should().Be<TenancyAccessRevision<TenantId>>();
        (await AdministratorsAsync()).Should().Equal([ada], "one administrator is left");
    }

    [Fact]
    public async Task The_revision_is_read_before_any_check()
    {
        await BuildHarborAsync();
        await _services.GrantAsync(_harbor, _grace, _north, HostCatalogue.OperatorPack);

        // Right after Grace's command reads the revision, system work takes her operator role away. Her checks
        // still pass on what she holds as a supervisor, and read what was committed: nothing they see is wrong.
        // The save compares the revision read first, so it fails all the same.
        DbContext? grantorContext = null;
        await _services.BySeat(_harbor.Tenant, _grace, async services =>
        {
            grantorContext = services.Tenancy();
            _services.Commands.Reset();
            _services.Hook.AfterCommand(
                grantorContext,
                command => command.Contains("FROM " + Revisions),
                () => _services.BySystemIn(_harbor.Tenant, system => system.Seats().RevokeAsync(_grace, _north, Operator, TestContext.Current.CancellationToken)));

            var conflict = await FluentActions.Awaiting(() => services.Seats().GrantAsync(_lin, _north, Operator, until: null, reason: null, TestContext.Current.CancellationToken))
                .Should().ThrowAsync<ConcurrencyConflictException>();
            conflict.Which.AggregateType.Should().Be<TenancyAccessRevision<TenantId>>();
        });

        _services.Hook.Fired.Should().BeTrue();
        var sent = _services.Commands.Sent.Where(command => ReferenceEquals(command.Context, grantorContext)).ToList();
        sent[0].Text.Should().Contain("FROM " + Revisions, "the revision is the command's first read");
        var save = sent.FindIndex(command => command.Text.Contains("\"TenancyAccessRevisions\"") && command.Writes);
        save.Should().BePositive("the save writes the revision");
        sent.Take(save).Skip(1).Should().NotContain(command => command.Text.Contains("\"TenancyAccessRevisions\""), "and the command's only read of the revision");
        sent.Skip(save + 1).Should().ContainSingle("the save that found no row reads it once more, to tell a lost race from a policy's denial")
            .Which.Text.Should().Contain("FROM " + Revisions).And.Contain("\"Revision\"");

        (await GrantsAsync(_lin)).Should().BeEmpty();
        (await GrantsAsync(_grace)).Should().Equal([(_north, Supervisor)], "the racing revoke committed");
    }

    [Theory]
    [InlineData("grant")]
    [InlineData("revoke")]
    [InlineData("withdraw")]
    [InlineData("suspend")]
    [InlineData("reactivate")]
    [InlineData("deactivate")]
    [InlineData("set-keys")]
    [InlineData("archive-role")]
    [InlineData("move")]
    [InlineData("place")]
    [InlineData("archive-unit")]
    public async Task Every_guarded_command_reads_the_revision_before_anything_else(string command)
    {
        await BuildHarborAsync();
        if (command == "reactivate")
        {
            await _services.BySystemIn(_harbor.Tenant, services => services.Seats().SuspendAsync(_lin, TestContext.Current.CancellationToken));
        }

        _services.Commands.Reset();
        await _services.BySeat(_harbor.Tenant, _harbor.AdminSeat, services => command switch
        {
            "grant" => services.Seats().GrantAsync(_lin, _coast, Operator, until: null, reason: null, TestContext.Current.CancellationToken),
            "revoke" => services.Seats().RevokeAsync(_grace, _north, Supervisor, TestContext.Current.CancellationToken),
            "withdraw" => services.Seats().WithdrawAsync(_lin, _coast, TestContext.Current.CancellationToken),
            "suspend" => services.Seats().SuspendAsync(_lin, TestContext.Current.CancellationToken),
            "reactivate" => services.Seats().ReactivateAsync(_lin, TestContext.Current.CancellationToken),
            "deactivate" => services.Seats().DeactivateAsync(_lin, TestContext.Current.CancellationToken),
            "set-keys" => services.Roles().SetKeysAsync(Operator, [HostCatalogue.WidgetChange], TestContext.Current.CancellationToken),
            "archive-role" => services.Roles().ArchiveAsync(Operator, TestContext.Current.CancellationToken),
            "move" => services.Organization().MoveUnitAsync(_coast, _south, TestContext.Current.CancellationToken),
            "place" => services.Seats().PlaceAsync(_lin, _south, primary: false, TestContext.Current.CancellationToken),
            "archive-unit" => services.Organization().ArchiveUnitAsync(_coast, TestContext.Current.CancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
        });

        _services.Commands.Commands[0].Should().Contain("FROM " + Revisions, "the revision is read before the key checks and the loads");
        _services.Commands.Commands.Should().Contain(sent => sent.StartsWith("UPDATE " + Revisions) || sent.Contains("\nUPDATE " + Revisions), "and bumped by the save");
    }

    [Fact]
    public async Task A_grant_racing_a_revoke_of_the_grantors_rights_fails_one()
    {
        await BuildHarborAsync();

        var conflict = await RaceAsync(
            interrupted: services => services.Seats().GrantAsync(_lin, _north, Operator, until: null, reason: null, TestContext.Current.CancellationToken),
            interrupting: services => services.Seats().RevokeAsync(_grace, _north, Supervisor, TestContext.Current.CancellationToken),
            interruptedSeat: _grace);

        conflict.AggregateType.Should().Be<TenancyAccessRevision<TenantId>>();
        (await GrantsAsync(_lin)).Should().BeEmpty("Grace granted on rights she no longer had when she saved");
        (await _services.StoredRightsAsync(_grace)).Should().BeEmpty();

        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => _services.BySeat(_harbor.Tenant, _grace, services =>
            services.Seats().GrantAsync(_lin, _north, Operator, until: null, reason: null, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task A_role_edit_racing_a_grant_of_that_role_fails_one()
    {
        await BuildHarborAsync();

        // The role gains a key Grace does not hold while she grants it.
        var conflict = await RaceAsync(
            interrupted: services => services.Seats().GrantAsync(_lin, _north, Operator, until: null, reason: null, TestContext.Current.CancellationToken),
            interrupting: services => services.Roles().SetKeysAsync(Operator, [HostCatalogue.WidgetChange, HostCatalogue.WidgetCreate, TenancyKeys.RolesManage], TestContext.Current.CancellationToken),
            interruptedSeat: _grace);

        conflict.AggregateType.Should().Be<TenancyAccessRevision<TenantId>>();
        (await GrantsAsync(_lin)).Should().BeEmpty();

        var refusal = await Refused.WithCodeAsync(TenancyRefusals.GrantExceedsOwn, () => _services.BySeat(_harbor.Tenant, _grace, services =>
            services.Seats().GrantAsync(_lin, _north, Operator, until: null, reason: null, TestContext.Current.CancellationToken)));
        refusal.Arguments["Missing"].Should().Be(TenancyKeys.RolesManage);
    }

    [Fact]
    public async Task A_move_racing_a_grant_below_it_fails_one()
    {
        await BuildHarborAsync();

        // Coast moves out from under North, where Grace's keys are, while she grants there.
        var conflict = await RaceAsync(
            interrupted: services => services.Seats().GrantAsync(_lin, _coast, Operator, until: null, reason: null, TestContext.Current.CancellationToken),
            interrupting: services => services.Organization().MoveUnitAsync(_coast, _south, TestContext.Current.CancellationToken),
            interruptedSeat: _grace);

        conflict.AggregateType.Should().Be<TenancyAccessRevision<TenantId>>();
        (await GrantsAsync(_lin)).Should().BeEmpty();

        await Refused.WithCodeAsync(TenancyRefusals.NotPermitted, () => _services.BySeat(_harbor.Tenant, _grace, services =>
            services.Seats().GrantAsync(_lin, _coast, Operator, until: null, reason: null, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task An_archive_racing_a_grant_there_fails_one()
    {
        await BuildHarborAsync();

        // The grant finds Coast active; the archive commits before the grant saves. The grant never touches the
        // organization, so its version cannot keep them apart; the revision both take does.
        var conflict = await RaceAsync(
            interrupted: services => services.Seats().GrantAsync(_lin, _coast, Operator, until: null, reason: null, TestContext.Current.CancellationToken),
            interrupting: services => services.Organization().ArchiveUnitAsync(_coast, TestContext.Current.CancellationToken));

        conflict.AggregateType.Should().Be<TenancyAccessRevision<TenantId>>();
        (await GrantsAsync(_lin)).Should().BeEmpty("nothing new is granted at an archived unit");
        await Refused.WithCodeAsync(TenancyRefusals.UnitNotActive, () => _services.BySystemIn(_harbor.Tenant, services =>
            services.Seats().GrantAsync(_lin, _coast, Operator, until: null, reason: null, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task An_archive_racing_a_placement_there_fails_one()
    {
        await BuildHarborAsync();

        var conflict = await RaceAsync(
            interrupted: services => services.Seats().PlaceAsync(_grace, _south, primary: false, TestContext.Current.CancellationToken),
            interrupting: services => services.Organization().ArchiveUnitAsync(_south, TestContext.Current.CancellationToken));

        conflict.AggregateType.Should().Be<TenancyAccessRevision<TenantId>>();
        (await PlacementsAsync(_grace)).Should().Equal([_north], "nobody is newly placed at an archived unit");
        await Refused.WithCodeAsync(TenancyRefusals.UnitNotActive, () => _services.BySystemIn(_harbor.Tenant, services =>
            services.Seats().PlaceAsync(_grace, _south, primary: false, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Two_tree_edits_race_on_the_organization_version()
    {
        await BuildHarborAsync();
        OrganizationUnitId west = default;

        var conflict = await RaceAsync(
            interrupted: services => services.Organization().RenameUnitAsync(_north, "North Shore", TestContext.Current.CancellationToken),
            interrupting: async services => west = await services.Organization().AddUnitAsync(_harbor.RootUnit, "West", "region", TestContext.Current.CancellationToken));

        conflict.AggregateType.Should().Be<HostOrganization>("a rename does not take the access revision; the organization's version keeps the edits apart");
        await _services.BySystemIn(_harbor.Tenant, async services =>
        {
            var organization = await services.Tenancy().Set<HostOrganization>().SingleAsync(TestContext.Current.CancellationToken);
            organization.FindUnit(_north)!.Name.Should().Be("North");
            organization.FindUnit(west).Should().NotBeNull();
        });

        (await _services.StoredPathsAsync(_harbor.Tenant)).Should().Contain(path => path.AncestorId == _harbor.RootUnit && path.DescendantId == west);
    }

    /// <summary>
    /// Runs <paramref name="interrupted"/> until it saves, commits <paramref name="interrupting"/> as system work
    /// there, and expects the first command's save to fail. It runs as <paramref name="interruptedSeat"/>, or as
    /// system work.
    /// </summary>
    private async Task<ConcurrencyConflictException> RaceAsync(Func<IServiceProvider, Task> interrupted, Func<IServiceProvider, Task> interrupting, SeatId? interruptedSeat = null)
    {
        var caller = interruptedSeat is { } seat ? HostCaller.InSeat(_harbor.Tenant, seat) : HostCaller.SystemIn(_harbor.Tenant);
        var conflict = await _services.RunAsync(caller, async services =>
        {
            _services.Hook.BeforeSave(services.Tenancy(), () => _services.BySystemIn(_harbor.Tenant, interrupting));
            return (await FluentActions.Awaiting(() => interrupted(services)).Should().ThrowAsync<ConcurrencyConflictException>()).Which;
        });

        _services.Hook.Fired.Should().BeTrue("the second command committed while the first was saving");
        return conflict;
    }

    private async Task BuildHarborAsync()
    {
        _harbor = await _services.ProvisionAsync("harbor");
        _north = await _services.AddUnitAsync(_harbor.Tenant, _harbor.RootUnit, "North");
        _south = await _services.AddUnitAsync(_harbor.Tenant, _harbor.RootUnit, "South");
        _coast = await _services.AddUnitAsync(_harbor.Tenant, _north, "Coast", "site");
        _grace = await _services.SeatAtAsync(_harbor, "Grace", _north, HostCatalogue.SupervisorPack);
        _lin = await _services.SeatAtAsync(_harbor, "Lin", _north);
        await _services.BySystemIn(_harbor.Tenant, services => services.Seats().PlaceAsync(_lin, _coast, primary: false, TestContext.Current.CancellationToken));
    }

    /// <summary>The seats holding the administrator key at the root, from the stored rights.</summary>
    private async Task<List<SeatId>> AdministratorsAsync()
        => [.. (await _services.StoredRightsAsync())
            .Where(right => right.UnitId == _harbor.RootUnit && right.Key == TenancyKeys.AdministratorKey)
            .Select(right => right.SeatId)
            .Distinct()];

    /// <summary>The units a seat is placed in, as saved.</summary>
    private Task<List<OrganizationUnitId>> PlacementsAsync(SeatId seat)
        => _services.BySystemIn(_harbor.Tenant, async services =>
            (await services.Tenancy().Set<HostSeat>().SingleAsync(row => row.Id == seat, TestContext.Current.CancellationToken)).Placements
                .Select(placement => placement.UnitId)
                .ToList());

    /// <summary>A seat's grants as saved, as (unit, role).</summary>
    private Task<List<(OrganizationUnitId Unit, RoleId Role)>> GrantsAsync(SeatId seat)
        => _services.BySystemIn(_harbor.Tenant, async services =>
            (await services.Tenancy().Set<HostSeat>().SingleAsync(row => row.Id == seat, TestContext.Current.CancellationToken)).Placements
                .SelectMany(placement => placement.Grants.Select(grant => (placement.UnitId, grant.RoleId)))
                .ToList());
}

/// <summary>Racing commands, on SQLite in memory.</summary>
public sealed class ConcurrencyTestsOnSqlite() : ConcurrencyTests(TestDatabases.Sqlite);

/// <summary>Racing commands, on Postgres, as the tables' owner with no row level security: the Entity Framework layer on Npgsql, not the policies.</summary>
public sealed class ConcurrencyTestsOnPostgres(PostgresDatabases postgres) : ConcurrencyTests(postgres);
