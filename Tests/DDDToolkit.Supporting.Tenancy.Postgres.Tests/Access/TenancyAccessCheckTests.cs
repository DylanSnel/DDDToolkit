using DDDToolkit.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using DDDToolkit.Supporting.Tenancy.TestHost.Persistence;
using DDDToolkit.Supporting.Tenancy.TestHost.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static DDDToolkit.Supporting.Tenancy.Postgres.Tests.Infrastructure.TenancySeed;

namespace DDDToolkit.Supporting.Tenancy.Postgres.Tests;

/// <summary>
/// Tenancy's access check where the database keeps tenants apart and answers about the rights itself: a key for
/// the whole tenant is asked as the caller, under the policies, in one statement. Over Tenancy's own context that
/// reads Tenancy's tables; over a module's own context it asks Tenancy's functions and names none of its tables.
/// What the other cases let through reads nothing, and is proven with the Entity Framework layer.
/// </summary>
public abstract class TenancyAccessCheckTests(TenancyPostgres postgres, TenancyNaming names)
{
    private static readonly TenancyRequirement.ForTheWholeTenant ManagesSeats = TenancyAccess.ForTheWholeTenant(TenancyKeys.SeatsManage);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>A request of the tenancy module that declares <paramref name="Requires"/>.</summary>
    private sealed record Declaring(AccessRequirement Requires) : IHostRequest
    {
        AccessRequirement IRequireAccess.RequiredAccess => Requires;
    }

    /// <summary>What a request of the widgets' module implements: a module of its own, with a context of its own.</summary>
    private interface IWidgetRequest : IRequireAccess;

    /// <summary>A request of the widgets' module that declares <paramref name="Requires"/>.</summary>
    private sealed record OfWidgetsDeclaring(AccessRequirement Requires) : IWidgetRequest
    {
        AccessRequirement IRequireAccess.RequiredAccess => Requires;
    }

    [Fact]
    public async Task A_key_for_the_whole_tenant_is_asked_as_the_caller_under_the_policies()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var recorder = new CommandRecorder();
        await using var services = new TenancyServices(database, contexts: options => options.AddInterceptors(recorder));
        var request = new Declaring(ManagesSeats);

        // Ada administers Harbor: every key, at its root.
        recorder.Clear();
        await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => RequireAsync<IHostRequest>(scoped, request));
        var asked = recorder.Sent.Should().ContainSingle("the key is asked in one statement").Which;
        asked.Caller!.UserId.Should().Be(Ada.Identity, "the statement runs as the person, under the policies");
        asked.TenancyCaller.Should().Be(HostCaller.InSeat(Harbor, Ada.Seat));

        // Seth manages seats at North, which is not the whole tenant.
        var refusal = await RefusedWithAsync(TenancyRefusals.NotPermitted, () => services.BySeat(Seth.Identity, Harbor, Seth.Seat, scoped => RequireAsync<IHostRequest>(scoped, request)));
        refusal.Arguments["Key"].Should().Be(TenancyKeys.SeatsManage);

        // Oli has a seat in Orchard too, and administers neither: his seat there says nothing about Harbor.
        await RefusedWithAsync(TenancyRefusals.NotPermitted, () => services.BySeat(Oli.Identity, Harbor, Oli.Seat, scoped => RequireAsync<IHostRequest>(scoped, request)));

        // Odette administers Orchard, and passes there.
        await services.BySeat(Odette.Identity, Orchard, Odette.Seat, scoped => RequireAsync<IHostRequest>(scoped, request));

        // System work in the tenant holds every key there.
        await services.BySystemIn(Harbor, scoped => RequireAsync<IHostRequest>(scoped, request));
    }

    [Fact]
    public async Task A_module_s_check_asks_tenancy_s_functions_over_the_module_s_own_context()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var recorder = new CommandRecorder();
        await using var services = new TenancyServices(
            database,

            // A module that only asks Tenancy declares none of its classes, and writes the four ids out.
            configure: registered => registered.AddTenancyAccess<TenantId, SeatId, OrganizationUnitId, RoleId, IWidgetRequest, WidgetContext>(),
            contexts: options => options.AddInterceptors(recorder));
        var request = new OfWidgetsDeclaring(ManagesSeats);

        recorder.Clear();
        await services.BySeat(Ada.Identity, Harbor, Ada.Seat, scoped => RequireAsync<IWidgetRequest>(scoped, request));

        var statement = recorder.Sent.Should().ContainSingle("the key is asked in one statement").Which.Text;
        statement.Should().Contain("tenancy." + TenancyFunctionNames.CallerRights + "(", "a module reads the caller's rights from the function that answers them");
        statement.Should().NotContain(names.Of("SeatRights"), "and names no table of Tenancy's");

        var refusal = await RefusedWithAsync(TenancyRefusals.NotPermitted, () => services.BySeat(Seth.Identity, Harbor, Seth.Seat, scoped => RequireAsync<IWidgetRequest>(scoped, request)));
        refusal.Arguments["Key"].Should().Be(TenancyKeys.SeatsManage);
        await services.BySystemIn(Harbor, scoped => RequireAsync<IWidgetRequest>(scoped, request));
    }

    [Fact]
    public async Task With_a_pool_the_key_is_asked_on_a_context_of_its_own_as_the_same_caller()
    {
        var database = await postgres.CreateDatabaseAsync(TenancyPostgres.Template.Secured, Cancellation, names);
        var recorder = new CommandRecorder();
        await using var services = new TenancyServices(database, contexts: options => options.AddInterceptors(recorder), pooled: true);
        var request = new Declaring(ManagesSeats);

        await services.BySeat(Ada.Identity, Harbor, Ada.Seat, async scoped =>
        {
            // The scope's own context, the unit of work of its commands, rented before the checks run.
            var ofTheScope = scoped.Tenancy();
            recorder.Clear();

            await Task.WhenAll(RequireAsync<IHostRequest>(scoped, request), RequireAsync<IHostRequest>(scoped, request));

            recorder.Sent.Should().HaveCount(2, "two requests of one scope are checked side by side, each in one statement");
            recorder.Sent.Should().OnlyContain(sent => sent.Caller!.UserId == Ada.Identity && Equals(sent.TenancyCaller, HostCaller.InSeat(Harbor, Ada.Seat)));
            ofTheScope.ChangeTracker.Entries().Should().BeEmpty("a check never reads on the request's unit of work");
        });

        // The contexts the checks took went back to the pool: Seth, next, is asked about as Seth.
        await RefusedWithAsync(TenancyRefusals.NotPermitted, () => services.BySeat(Seth.Identity, Harbor, Seth.Seat, scoped => RequireAsync<IHostRequest>(scoped, request)));
    }

    private static Task RequireAsync<TRequests>(IServiceProvider scoped, TRequests request)
        where TRequests : class, IRequireAccess
        => scoped.GetRequiredService<AccessChecks<TRequests>>().RequireAsync(request, Cancellation).AsTask();

    /// <summary>Asserts that <paramref name="act"/> is refused with <paramref name="code"/>, and hands the refusal back for more.</summary>
    private static async Task<DDDToolkit.Exceptions.RefusalException> RefusedWithAsync(string code, Func<Task> act)
    {
        var refusal = (await FluentActions.Awaiting(act).Should().ThrowAsync<DDDToolkit.Exceptions.RefusalException>()).Which;
        refusal.Code.Should().Be(code, refusal.Message);
        return refusal;
    }
}

/// <summary>Tenancy's access check under the policies, under the names Entity Framework gives the tables and columns.</summary>
public sealed class TenancyAccessCheckTestsOnDefaultNames(TenancyPostgres postgres) : TenancyAccessCheckTests(postgres, TenancyNaming.Default);

/// <summary>Tenancy's access check under the policies, under snake_case names with enums stored as snake_case text.</summary>
public sealed class TenancyAccessCheckTestsOnSnakeCase(TenancyPostgres postgres) : TenancyAccessCheckTests(postgres, TenancyNaming.SnakeCase);
