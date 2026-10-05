using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Tenants.Tenant;

/// <summary>
/// A flat tenant next to a hierarchical one. Meadow is flat: its root is its only unit until an administrator
/// makes it hierarchical, and a tenant never goes back.
/// </summary>
/// <remarks>
/// A test that changes the shape makes a host of its own; the one that is only refused shares the class's.
/// <para>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </para>
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class FlatAndHierarchicalScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Meadow => DemoData.Meadow;

    [Fact]
    public async Task Meadow_refuses_a_unit_below_its_root()
    {
        using var tove = await sample.ClientAsync("tove", Meadow.Slug);

        using var response = await tove.PostAsJsonAsync("/tenancy/units", Greenhouse, Cancellation);

        await response.ShouldBeRefusedAsync(HttpStatusCode.Conflict, TenancyRefusals.FlatTenant);
    }

    [Fact]
    public async Task After_changing_shape_meadow_accepts_units()
    {
        await using var host = await sample.StartAsync();
        using var tove = await host.ClientAsync("tove", Meadow.Slug);

        using var reshaped = await tove.PostAsJsonAsync("/tenancy/shape", new { shape = "hierarchical" }, Cancellation);
        using var added = await tove.PostAsJsonAsync("/tenancy/units", Greenhouse, Cancellation);

        reshaped.StatusCode.Should().Be(HttpStatusCode.NoContent);
        added.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await added.Content.ReadFromJsonAsync<JsonElement>(Cancellation)).GetProperty("id").GetGuid();

        var units = await tove.GetFromJsonAsync<JsonElement>("/tenancy/units", Cancellation);
        var greenhouse = units.EnumerateArray().Should().ContainSingle(unit => unit.GetProperty("id").GetGuid() == id).Which;
        greenhouse.GetProperty("path").GetString().Should().Be("Meadow Gardens / Greenhouse");
        greenhouse.GetProperty("kind").GetString().Should().Be("site", "the kind the request gave is the application's own field, saved with the unit");
        units.EnumerateArray().Should().ContainSingle(unit => unit.GetProperty("id").GetGuid() == Meadow.Root.Value)
            .Which.GetProperty("kind").GetString().Should().Be("company", "the root's kind was set when the tenant was provisioned");

        var me = await tove.GetFromJsonAsync<JsonElement>("/me", Cancellation);
        me.GetProperty("tenant").GetProperty("shape").GetString().Should().Be("hierarchical");
    }

    [Fact]
    public async Task After_changing_shape_meadow_has_the_roles_of_a_hierarchical_tenant()
    {
        await using var host = await sample.StartAsync();
        using var tove = await host.ClientAsync("tove", Meadow.Slug);

        var before = await tove.GetFromJsonAsync<JsonElement>("/tenancy/roles", Cancellation);
        using var reshaped = await tove.PostAsJsonAsync("/tenancy/shape", new { shape = "hierarchical" }, Cancellation);
        var after = await tove.GetFromJsonAsync<JsonElement>("/tenancy/roles", Cancellation);

        // The area manager's pack and the access admin's are only for a hierarchical tenant: a flat one has only
        // its root to manage, and one administrator's role that holds every key.
        before.EnumerateArray().Select(role => role.GetProperty("fromPack").GetString())
            .Should().BeEquivalentTo(SampleCatalogue.TenantAdmin, SampleCatalogue.CrewLead, SampleCatalogue.Surveyor, SampleCatalogue.Observer, SampleCatalogue.PeopleOffice);
        reshaped.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var manager = after.EnumerateArray().Should().ContainSingle(role => role.GetProperty("fromPack").GetString() == SampleCatalogue.AreaManager).Which;
        manager.GetProperty("name").GetString().Should().Be("Area manager");
        manager.GetProperty("status").GetString().Should().Be("active");
        manager.GetProperty("keys").EnumerateArray().Select(key => key.GetString()).Should().Contain([ProjectKeys.Open, ProjectKeys.ChangeOwner]);

        // The administrators' role of the new shape is there too, with the keys its pack lists, and nobody holds
        // it: Tove keeps the flat tenant's role, which holds every key, and gives the new one to whom she chooses.
        var accessAdmin = after.EnumerateArray().Should().ContainSingle(role => role.GetProperty("fromPack").GetString() == SampleCatalogue.AccessAdmin).Which;
        accessAdmin.GetProperty("keys").EnumerateArray().Select(key => key.GetString())
            .Should().Contain([TenancyKeys.RolesManage, ProjectKeys.ManageCrew, ProjectKeys.View]).And.NotContain([ProjectKeys.Edit, ProjectKeys.Close]);
        var me = await tove.GetFromJsonAsync<JsonElement>("/me", Cancellation);
        me.GetProperty("placements").EnumerateArray().Single().GetProperty("grants").EnumerateArray().Select(grant => grant.GetProperty("roleId").GetGuid())
            .Should().Equal(Meadow.Roles[SampleCatalogue.TenantAdmin].Value);
    }

    [Fact]
    public async Task Changing_back_to_flat_is_refused()
    {
        await using var host = await sample.StartAsync();
        using var tove = await host.ClientAsync("tove", Meadow.Slug);

        using var reshaped = await tove.PostAsJsonAsync("/tenancy/shape", new { shape = "hierarchical" }, Cancellation);
        using var back = await tove.PostAsJsonAsync("/tenancy/shape", new { shape = "flat" }, Cancellation);

        reshaped.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await back.ShouldBeRefusedAsync(HttpStatusCode.Conflict, TenancyRefusals.ShapeChange);
    }

    /// <summary>A site right below meadow's root.</summary>
    private static object Greenhouse => new { parentId = Meadow.Root.Value, name = "Greenhouse", kind = "site" };
}
