using System.Data.Common;
using System.Text.RegularExpressions;
using System.Transactions;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Supporting.Tenancy.EntityFramework.Tests;

/// <summary>
/// A read across tenants that Tenancy makes runs as a caller begun around that one query, on another context and
/// connection than the caller's: whatever the caller holds open cannot narrow it, and it does not reach into the
/// caller's work. Where nothing but the save keeps the rights, as on SQLite, that caller is the application itself,
/// <see cref="Caller.System"/>, reading the tables; where the database keeps them, it is scoped system work in no
/// tenant, asking the database's function.
/// </summary>
public sealed class SystemReadTests
{
    private const string PolishKey = "widget.polish";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_system_read_inside_a_user_transaction_runs_on_its_own_connection()
    {
        // In a file, where every context connects on its own, as on a server. Meadow's role holds a key a later
        // version of the application removed from its code.
        using var before = new TestServices(inFile: true, configure: services => services.AddTenancyPermissions([new Permission(PolishKey, "Widgets", "Polish widgets")]));
        var harbor = await before.ProvisionAsync("harbor");
        var meadow = await before.ProvisionAsync("meadow");
        await before.BySystemIn(meadow.Tenant, services =>
            services.Roles().CreateAsync("Polisher", "Polishes widgets", [PolishKey, HostCatalogue.WidgetRead], Cancellation));

        using var after = new TestServices(database: before.Database);

        await after.BySeat(harbor.Tenant, harbor.AdminSeat, async services =>
        {
            // The caller, a seat of harbor, holds a transaction with a change of its own in it, not yet committed.
            var context = services.Tenancy();
            await using var transaction = await context.Database.BeginTransactionAsync(Cancellation);
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE \"Roles\" SET \"Keys\" = '[\"widget.buff\"]' WHERE \"TenantId\" = {0}", [harbor.Tenant.Value], Cancellation);
            var callers = context.Database.GetDbConnection();
            after.Commands.Reset();

            var unknown = await TenancyChecks.UnknownStoredKeysAsync<HostRole, RoleId, TenantId>(
                context, services.GetRequiredService<TenancyCatalogue>(), Cancellation);

            // Across tenants, as the application; and not what the caller's open transaction holds.
            unknown.Should().Equal([PolishKey], "meadow's key is read, and harbor's uncommitted change is not");

            var read = after.Commands.Sent.Should().ContainSingle("the read is one query").Which;
            read.Caller.Should().BeSameAs(Caller.System, "the read runs as the application itself");
            read.Context.Should().NotBeNull().And.NotBeSameAs(context, "it runs on a context of its own");
            read.Connection.Should().NotBeSameAs(callers, "and on another connection than the caller's");
            read.Transaction.Should().BeNull("outside the caller's transaction");

            // The caller's work is as it was: its transaction open, and nobody begun around it.
            Callers.Ambient.Should().BeNull();
            context.Database.CurrentTransaction.Should().BeSameAs(transaction);
            await transaction.RollbackAsync(Cancellation);
        });
    }

    [Fact]
    public async Task A_system_read_inside_a_transaction_scope_is_not_enlisted_in_it()
    {
        using var services = new TestServices(inFile: true);
        await services.ProvisionAsync("harbor");

        await services.BySystemIn((await services.ProvisionAsync("meadow")).Tenant, async scoped =>
        {
            // The caller's own unit of work, in an ambient transaction it began.
            using var ambient = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
            services.Commands.Reset();

            var unknown = await TenancyChecks.UnknownStoredKeysAsync<HostRole, RoleId, TenantId>(
                scoped.Tenancy(), scoped.GetRequiredService<TenancyCatalogue>(), Cancellation);

            unknown.Should().BeEmpty();
            var read = services.Commands.Sent.Should().ContainSingle().Which;
            read.Caller.Should().BeSameAs(Caller.System);
            read.InAmbientTransaction.Should().BeFalse("a read of its own does not join the caller's transaction");
            System.Transactions.Transaction.Current.Should().NotBeNull("and the caller's transaction is still the caller's");
        });
    }

    [Fact]
    public async Task The_tenants_to_sweep_are_the_active_and_the_suspended_ones_read_as_the_application_itself()
    {
        using var services = new TestServices(inFile: true);
        var harbor = await services.ProvisionAsync("harbor");
        var meadow = await services.ProvisionAsync("meadow");
        var quarry = await services.ProvisionAsync("quarry");
        await services.BySystemIn(meadow.Tenant, scoped => scoped.Tenants().SuspendAsync("A pause", Cancellation));
        await services.BySystemIn(quarry.Tenant, scoped => scoped.Tenants().CloseAsync("Wound up", Cancellation));

        // Asked in the middle of a seat's work in harbor: the tenants of every round, harbor's or not.
        await services.BySeat(harbor.Tenant, harbor.AdminSeat, async scoped =>
        {
            services.Commands.Reset();
            var tenants = await TenancySystemReads.TenantsToSweepAsync<HostTenant, TenantId>(scoped.Tenancy(), "widgets", Cancellation);

            tenants.Should().BeEquivalentTo([harbor.Tenant, meadow.Tenant], "a suspended tenant is still visited, and a closed one is not");
            var read = services.Commands.Sent.Should().ContainSingle("the read is one query").Which;
            read.Caller.Should().BeSameAs(Caller.System, "nothing holds anyone to a tenant here but the filter, which the application itself reads past");
            read.Context.Should().NotBeNull().And.NotBeSameAs(scoped.Tenancy(), "it runs on a context of its own");
            Regex.Match(read.Text, @"^SELECT (.+?)\s+FROM", RegexOptions.Singleline).Groups[1].Value.Trim()
                .Should().MatchRegex(@"^""\w+""\.""Id""$", "it projects the ids and nothing else of any tenant");

            // A scope that is none is refused the same here, where the scope travels nowhere; and so is a module's own
            // context, which maps no tenants.
            await FluentActions.Awaiting(() => TenancySystemReads.TenantsToSweepAsync<HostTenant, TenantId>(scoped.Tenancy(), "Not a scope", Cancellation))
                .Should().ThrowAsync<ArgumentException>();
            await FluentActions.Awaiting(() => TenancySystemReads.TenantsToSweepAsync<HostTenant, TenantId>(scoped.Widgets(), "widgets", Cancellation))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("'TestWidgetContext' does not map HostTenant*modelBuilder.AddTenancy()*");
            services.Commands.Count.Should().Be(1);
        });
    }

    [Fact]
    public async Task Where_the_database_keeps_the_rights_a_read_across_tenants_asks_its_function_as_scoped_system_work_in_no_tenant()
    {
        // The option a package for one database turns on, with the functions it writes. SQLite has none, so each read
        // fails here; what it sent, and as whom, is what a database that has them is asked.
        using var services = new TestServices(inFile: true, configure: collection => collection.Configure<TenancyStoreOptions>(options => options.DatabaseKeepsRights = true));
        var harbor = await services.ProvisionAsync("harbor");

        await services.BySeat(harbor.Tenant, harbor.AdminSeat, async scoped =>
        {
            services.Commands.Reset();
            await FluentActions.Awaiting(() => TenancyChecks.UnknownStoredKeysAsync<HostRole, RoleId, TenantId>(scoped.Tenancy(), scoped.GetRequiredService<TenancyCatalogue>(), Cancellation))
                .Should().ThrowAsync<DbException>();
            await FluentActions.Awaiting(() => TenancySystemReads.TenantsToSweepAsync<HostTenant, TenantId>(scoped.Tenancy(), "widgets", Cancellation))
                .Should().ThrowAsync<DbException>();

            services.Commands.Sent.Select(command => (Text: command.Text.Trim(), Caller: command.Caller!.ToString())).Should().Equal(
                ("SELECT f.v AS \"Value\" FROM \"" + TenancyFunctionNames.RoleKeysInUse + "\"() AS f(v)", "system in " + TenancyWork.SystemScope),
                ("SELECT f.v AS \"TenantId\" FROM \"" + TenancyFunctionNames.TenantsToSweep + "\"() AS f(v)", "system in widgets"));
            services.Commands.Sent.Should().OnlyContain(
                command => command.TenancyCaller == null && !ReferenceEquals(command.Context, scoped.Tenancy()) && !command.InAmbientTransaction,
                "in no tenant, though the work around it is a seat's in harbor, and on a context of its own");

            // The seat's own work goes on as it was.
            TenancyCallers.Ambient.Should().Be(HostCaller.InSeat(harbor.Tenant, harbor.AdminSeat));
            Callers.Ambient.Should().BeNull();
        });
    }
}
