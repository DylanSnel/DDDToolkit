using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Access;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using DDDToolkit.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>What every request about the apiary's hives implements: the generator writes what each requires into it.</summary>
[AccessRequests]
public partial interface IHivesRequest : IRequireAccess;

/// <summary>Only a hive's keeper smokes it: the owner alone, of the hive the request names.</summary>
[OwnOnly]
public sealed record SmokeHive([Own] HiveId Hive) : IHivesRequest;

/// <summary>
/// The owner of an aggregate, read as the caller on Postgres: the core's check of <c>[Own]</c> on a hive's id has the
/// Entity Framework package read the hive's keeper, which the hive marks <c>[Owner]</c>, in one statement on a
/// connection that runs as the caller, so the apiary's policies decide whether the row is there for the caller at all.
/// A hive the caller may not see is not found, one it sees and keeps not is somebody else's.
/// </summary>
public sealed class AggregateOwnerPostgresTests(ExplicitCallersPostgres postgres)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_hive_the_policies_hide_from_the_caller_is_not_found_and_the_keeper_s_own_lets_the_keeper_through()
    {
        // Keepers have their own hives, and a signed-in caller reads no other: Bob does not see Alice's hive.
        var database = await ApiaryDatabase.CreateAsync(postgres, rules: [ApiaryRules.Keepers, ApiaryRules.Visitors]);
        await using var host = database.BuildHost(services => services.AddAccessChecks<IHivesRequest>());
        var alices = await SettleAsync(host, ApiaryDatabase.Alice, ApiaryDatabase.AliceId);

        await AskAsync(host, ApiaryDatabase.Alice, new SmokeHive(alices));

        var hidden = await RefusedAsync(host, ApiaryDatabase.Bob, new SmokeHive(alices));
        hidden.Code.Should().Be(ToolkitRefusals.NotFound, "the statement ran as Bob, and the policies keep Alice's hive from him");
        hidden.Kind.Should().Be(RefusalKind.NotFound);

        (await RefusedAsync(host, ApiaryDatabase.Alice, new SmokeHive(HiveId.CreateSequential()))).Code.Should().Be(ToolkitRefusals.NotFound, "a hive nobody settled reads the same");
    }

    [Fact]
    public async Task A_hive_the_caller_sees_and_does_not_keep_is_somebody_else_s()
    {
        // Every signed-in caller reads every hive: Bob sees Alice's, and is no keeper of it.
        var database = await ApiaryDatabase.CreateAsync(postgres, rules: [ApiaryRules.Keepers, ApiaryRules.SignedIn, ApiaryRules.Visitors]);
        await using var host = database.BuildHost(services => services.AddAccessChecks<IHivesRequest>());
        var alices = await SettleAsync(host, ApiaryDatabase.Alice, ApiaryDatabase.AliceId);

        (await RefusedAsync(host, ApiaryDatabase.Bob, new SmokeHive(alices))).Code.Should().Be(ToolkitRefusals.NotOwn);
        await AskAsync(host, ApiaryDatabase.Alice, new SmokeHive(alices));

        // A visitor keeps no hive, and is refused before anything is sent: the hive is closed to visitors besides.
        (await RefusedAsync(host, Caller.Anonymous, new SmokeHive(alices))).Code.Should().Be(ToolkitRefusals.NotOwn);
    }

    /// <summary>Settles a hive kept by <paramref name="keeper"/>, as <paramref name="caller"/>, and answers its id.</summary>
    private static async Task<HiveId> SettleAsync(IServiceProvider host, Caller caller, Guid keeper)
    {
        var id = HiveId.CreateSequential();
        await ApiaryDatabase.AsAsync(host, caller, async context =>
        {
            context.Hives.Add(new Hive(id, number: 1, "By the hedge", keeper, isOpen: false));
            await context.SaveChangesAsync(Cancellation);
        });

        return id;
    }

    private static async Task AskAsync(IServiceProvider host, Caller caller, IHivesRequest request)
    {
        await using var scope = host.CreateAsyncScope();
        using var running = Callers.Begin(caller);
        await scope.ServiceProvider.GetRequiredService<AccessChecks<IHivesRequest>>().RequireAsync(request, Cancellation);
    }

    private static async Task<RefusalException> RefusedAsync(IServiceProvider host, Caller caller, IHivesRequest request)
        => (await FluentActions.Awaiting(() => AskAsync(host, caller, request)).Should().ThrowAsync<RefusalException>()).Which;
}
