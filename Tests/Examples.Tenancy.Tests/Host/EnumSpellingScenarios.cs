using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Exceptions;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Examples.Tenancy.Tests.Host;

/// <summary>
/// Enum values travel by name in lower snake case, in every answer and every body: the host's choice, made once in
/// its JSON options, so no route spells a value itself.
/// </summary>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class EnumSpellingScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Harbor => DemoData.Harbor;

    [Fact]
    public async Task Rest_spells_status_shape_state_and_via_in_lower_snake_case()
    {
        await using var host = await sample.StartAsync();
        using var ada = await host.ClientAsync("ada", Harbor.Slug);
        var observer = Harbor.ProjectRoles[SampleCatalogue.Observer].Value;

        // The policy itself, on a value of two words: every route writes with these options.
        var options = host.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        JsonSerializer.Serialize(RefusalKind.NotPermitted, options).Should().Be("\"not_permitted\"");

        var me = await ada.GetFromJsonAsync<JsonElement>("/me", Cancellation);
        me.GetProperty("tenant").GetProperty("shape").GetString().Should().Be("hierarchical");
        me.GetProperty("tenant").GetProperty("status").GetString().Should().Be("active");
        me.GetProperty("seat").GetProperty("status").GetString().Should().Be("active");

        var project = await ada.ProjectDetailAsync(Harbor.ProjectNamed("Bay bridge"));
        project.GetProperty("state").GetString().Should().Be("open");
        project.GetProperty("via").GetString().Should().Be("crew");

        // A body is read in the same spelling, and what it changed is answered in it: meadow turns hierarchical.
        using var tove = await host.ClientAsync("tove", DemoData.Meadow.Slug);
        using (var changed = await tove.PostAsJsonAsync("/tenancy/shape", new { shape = "hierarchical" }, Cancellation))
        {
            changed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await tove.GetFromJsonAsync<JsonElement>("/me", Cancellation)).GetProperty("tenant").GetProperty("shape").GetString().Should().Be("hierarchical");

        // And a project role's status, of the Membership package's, the same way.
        using (var archived = await ada.PostAsync($"/project-roles/{observer}/archive", content: null, Cancellation))
        {
            archived.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await ada.ProjectRolesAsync()).Single(role => role.GetProperty("id").GetGuid() == observer).GetProperty("status").GetString().Should().Be("archived");

        // A number is no name: no route reads one for an enum value.
        using var numbered = await ada.PostAsJsonAsync("/tenancy/shape", new { shape = 1 }, Cancellation);
        await numbered.ShouldBeRefusedAsync(HttpStatusCode.BadRequest, RefusalProblems.InvalidRequest);
    }
}
