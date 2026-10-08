using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using FluentAssertions;

namespace Examples.Tenancy.Tests.Tenants.Roles;

/// <summary>
/// One rule, held where roles are read: a role's keys are for whoever manages the tenant's roles. Everybody who
/// works in the tenant reads what a role is called; a seat without <c>tenancy.roles.manage</c> gets the roles and
/// no keys from every route, and from the GraphQL field with the refusal a refused request would give.
/// </summary>
/// <remarks>
/// The class's hosts run on Supabase's own Postgres image, with the exported policies and privileges under the
/// application's own checks.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class RoleKeysScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private const string RolesWithKeys = "{ roles { id name keys } }";

    private static DemoTenant Harbor => DemoData.Harbor;

    [Fact]
    public async Task Who_manages_the_tenants_roles_reads_their_keys_and_is_asked_about_once()
    {
        var sent = new SentRequests();
        await using var host = await sample.StartAsync(sent.AddTo);

        // Ada administers access in harbor, which brings the key for the whole tenant.
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        var roles = (await ada.GraphQLDataAsync(RolesWithKeys)).GetProperty("roles").EnumerateArray().ToList();

        roles.Should().HaveCount(Harbor.Roles.Count);
        roles.Should().OnlyContain(role => role.GetProperty("keys").ValueKind == JsonValueKind.Array, "she manages the tenant's roles");
        roles.Single(role => role.GetProperty("id").GetGuid() == Harbor.Roles[SampleCatalogue.Observer].Value)
            .GetProperty("keys").EnumerateArray().Select(key => key.GetString()).Should().Contain(ProjectKeys.View);

        // Every role of the list asked whether she holds the key, and the question was put once.
        sent.OfType<UnitsWhereIHold>().Should().ContainSingle().Which.Key.Should().Be(TenancyKeys.RolesManage);
    }

    [Theory]
    [InlineData("leo", false, "he leads a crew, and holds nothing in the organization")]
    [InlineData("rhea", false, "an area manager runs the work, not the tenant's roles")]
    [InlineData("hana", false, "the people office gives roles, and does not say what they bring")]
    [InlineData("ada", true, "she administers access in harbor, which brings the key for the whole tenant")]
    public async Task The_routes_answer_a_roles_keys_to_who_manages_the_tenants_roles_and_to_nobody_else(string person, bool manages, string because)
    {
        using var client = await sample.ClientAsync(person, Harbor.Slug);
        var observer = Harbor.Roles[SampleCatalogue.Observer].Value;

        // The list of the tenant's roles, and the directory's answer about some of them: two routes, one rule.
        var listed = (await client.GetFromJsonAsync<JsonElement>("/tenancy/roles", TestContext.Current.CancellationToken)).EnumerateArray().ToList();
        using var asked = await client.PostAsJsonAsync("/tenancy/directory/roles", new { ids = new[] { observer } }, TestContext.Current.CancellationToken);
        var named = (await asked.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).EnumerateArray().ToList();

        listed.Should().HaveCount(Harbor.Roles.Count, "anybody who works in the tenant reads which roles it has");
        named.Should().ContainSingle().Which.GetProperty("name").GetString().Should().Be("Observer");

        foreach (var role in listed.Concat(named))
        {
            role.GetProperty("keys").ValueKind.Should().Be(manages ? JsonValueKind.Array : JsonValueKind.Null, because);
            role.GetProperty("name").ValueKind.Should().Be(JsonValueKind.String, "what a role is called is everybody's to read");
        }

        if (manages)
        {
            listed.Single(role => role.GetProperty("id").GetGuid() == observer)
                .GetProperty("keys").EnumerateArray().Select(key => key.GetString()).Should().Equal(ProjectKeys.View);
        }
        else
        {
            string.Concat(listed.Concat(named).Select(role => role.GetRawText())).Should().NotContain(ProjectKeys.View, "no key of any role is in the answer");
        }
    }

    [Fact]
    public async Task The_roles_a_seat_holds_itself_it_reads_with_their_keys()
    {
        // Rhea does not manage the tenant's roles, and holds one: an area manager's. What she holds is hers to read.
        using var rhea = await sample.ClientAsync("rhea", Harbor.Slug);

        var me = await rhea.GetFromJsonAsync<JsonElement>("/me", TestContext.Current.CancellationToken);

        me.GetProperty("roles").EnumerateArray().Should().ContainSingle().Which
            .GetProperty("keys").EnumerateArray().Select(key => key.GetString()).Should().Contain(ProjectKeys.ManageCrew);
    }

    [Fact]
    public async Task A_seat_that_does_not_manage_roles_gets_the_roles_without_their_keys_and_the_refusal_per_field()
    {
        // Leo leads a crew, and holds nothing in the organization.
        using var leo = await sample.ClientAsync("leo", Harbor.Slug);
        var answer = await leo.GraphQLAsync(RolesWithKeys);

        // The roles stay, with everything but what was refused.
        var roles = answer.GetProperty("data").GetProperty("roles").EnumerateArray().ToList();
        roles.Should().HaveCount(Harbor.Roles.Count);
        roles.Should().OnlyContain(role => role.GetProperty("keys").ValueKind == JsonValueKind.Null, "the keys are for whoever manages the roles");
        roles.Should().OnlyContain(role => role.GetProperty("name").ValueKind == JsonValueKind.String);

        // One error for each refused field, at that field, with the code a refused request has.
        var errors = answer.GetProperty("errors").EnumerateArray().ToList();
        errors.Should().HaveCount(roles.Count);
        errors.Should().OnlyContain(error => error.Code() == TenancyRefusals.NotPermitted && error.Kind() == "not_permitted");
        errors.Select(error => string.Join('/', error.GetProperty("path").EnumerateArray().Select(part => part.ToString())))
            .Should().BeEquivalentTo(Enumerable.Range(0, roles.Count).Select(index => $"roles/{index}/keys"));

        // Without the field under the rule, nothing is refused.
        (await leo.GraphQLDataAsync("{ roles { name } }")).GetProperty("roles").GetArrayLength().Should().Be(Harbor.Roles.Count);
    }
}
