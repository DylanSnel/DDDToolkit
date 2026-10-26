using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Access;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Examples.Tenancy.Tests.Tenants.Roles;

/// <summary>
/// The host turns the sync of the role packs on with <c>SyncRolePacks()</c>: once it has started, every tenant's
/// roles made from a pack follow that pack as the catalogue has it now. Harbor's Surveyor is stored as a version of
/// the application made it whose surveyor's pack did not record inspections yet, and the next start of the
/// application brings the key to the role, with the event in the tenant's access history, where its administrators
/// read it.
/// </summary>
/// <remarks>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks: the sync runs as Tenancy's system work in each tenant, under those policies.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class RolePackSyncScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private const string RoleFollowedItsPack = "tenancy.role-followed-its-pack";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    [Fact]
    public async Task A_key_its_pack_gained_reaches_an_existing_tenants_role_when_the_host_starts_with_the_event_in_the_history()
    {
        await using var onPostgres = await sample.StartOnPostgresAsync();
        var surveyor = Harbor.Roles[SampleCatalogue.Surveyor].Value;
        string KeysOfTheSurveyor() => $"SELECT \"Keys\" FROM tenancy.\"Roles\" WHERE \"Id\" = '{surveyor}'";

        // The surveyor's role as the older version made it: without recording inspections, and remembering a pack
        // without it. What the tenant changed itself is not in the way: it changed nothing of this role.
        await onPostgres.AsOwnerAsync(
            $"""
            UPDATE tenancy."Roles"
            SET "Keys" = pg_catalog.array_remove("Keys", '{InspectionKeys.Record}'), "KeysFromPack" = pg_catalog.array_remove("KeysFromPack", '{InspectionKeys.Record}')
            WHERE "Id" = '{surveyor}'
            """,
            Cancellation);
        (await onPostgres.AsOwnerAsync<string[]>(KeysOfTheSurveyor(), Cancellation)).Should().Equal(ProjectKeys.View);

        // The next start of the application, on the same database: the sync runs once it has started.
        var next = await onPostgres.StartAnotherHostAsync(Cancellation);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (!(await onPostgres.AsOwnerAsync<string[]>(KeysOfTheSurveyor(), Cancellation)).Contains(InspectionKeys.Record) && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(100, Cancellation);
        }

        (await onPostgres.AsOwnerAsync<string[]>(KeysOfTheSurveyor(), Cancellation)).Should().Equal([InspectionKeys.Record, ProjectKeys.View], "the role follows its pack");

        // Maud administers access in harbor, and reads the history: the role followed its pack, as the application's
        // work, with the key that came in. It is the one role that changed.
        using var maud = await next.ClientAsync("maud", Harbor.Slug);
        var history = await maud.GetFromJsonAsync<JsonElement>("/tenancy/history?size=10", Cancellation);
        var followed = history.GetProperty("items").EnumerateArray().Where(row => row.Text("event") == RoleFollowedItsPack).ToList();
        var row = followed.Should().ContainSingle().Which;
        row.GetProperty("by").Text("kind").Should().Be(TenancyActorKinds.System);
        row.GetProperty("details").GetRawText().Should().Contain(InspectionKeys.Record).And.Contain(SampleCatalogue.Surveyor).And.Contain(surveyor.ToString());

        // A run of its own, as a deployment step would start one, finds nothing left to change.
        var again = await next.Services.GetRequiredService<IRolePackSync>().SyncAsync(Cancellation);
        again.Should().BeEquivalentTo(new { Tenants = DemoData.Tenants.Count, RolesChanged = 0, Succeeded = true });
        (await onPostgres.AsOwnerAsync<long>($"SELECT count(*) FROM tenancy.\"{TenantsContext.HistoryTable}\" WHERE \"EventName\" = '{RoleFollowedItsPack}'", Cancellation))
            .Should().Be(1);
    }
}
