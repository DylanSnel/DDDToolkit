using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DDDToolkit.Supporting.Tenancy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Examples.Tenancy.Tests.Tenants.Invitations;

/// <summary>
/// An invitation stays open for a time, a week unless the host says otherwise, and nothing is written when that
/// time is over: whether it has run out is decided from the clock when somebody lists or accepts it. So here
/// time passes.
/// </summary>
/// <remarks>
/// Time passes for real: no test moves the clock an invitation is held against, and the moment one ends cannot be
/// changed afterwards, by anyone. So the scenario's host keeps an invitation open for a few seconds, which is a
/// setting of Tenancy's the host otherwise leaves at its week (<see cref="TenancyInvitationOptions{TInvitationId}"/>),
/// and the scenario waits until the invitation's own end is past for the database and for the host.
/// </remarks>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class InvitationOverTimeScenarios(SampleHosts sample) : IClassFixture<SampleHosts>
{
    /// <summary>
    /// How long the scenario's host keeps an invitation open: time for the one request that has to be answered
    /// while it is, and no longer than a test can wait.
    /// </summary>
    private static readonly TimeSpan StaysOpenFor = TimeSpan.FromSeconds(8);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static DemoTenant Meadow => DemoData.Meadow;

    [Fact]
    public async Task An_invitation_whose_time_is_over_is_no_longer_listed_and_is_refused_when_it_is_accepted()
    {
        // The host's own registration, with the two lifetimes in seconds: the shortest an invitation may stay
        // open is no longer than the time it stays open by default, or the first invitation is refused.
        await using var onPostgres = await sample.StartOnPostgresAsync(services => services.Replace(ServiceDescriptor.Singleton(
            new TenancyInvitationOptions<InvitationId> { MinLifetime = StaysOpenFor, DefaultLifetime = StaysOpenFor })));
        var host = onPostgres.Host;
        using var tove = await host.ClientAsync("tove", Meadow.Slug);
        using var juno = await host.ClientAsync("juno", tenant: null);

        using var invited = await tove.PostAsJsonAsync(
            "/tenancy/invitations",
            new { address = DemoPeople.Juno.Email, unitId = Meadow.Root.Value, roleId = Meadow.Roles[SampleCatalogue.Observer].Value },
            Cancellation);
        invited.StatusCode.Should().Be(HttpStatusCode.OK);
        var issued = await invited.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        var (token, expiresAt) = (issued.Text("token"), issued.GetProperty("expiresAt").GetDateTimeOffset());

        // Listed while it is open: asked first, and judged once the clock has said it still was.
        var whileOpen = await tove.GetFromJsonAsync<JsonElement>("/tenancy/invitations", Cancellation);
        host.Services.GetRequiredService<TimeProvider>().GetUtcNow()
            .Should().BeBefore(expiresAt, "the list has to be answered while the invitation is open, which gives it {0} seconds", StaysOpenFor.TotalSeconds);
        whileOpen.GetArrayLength().Should().Be(1);

        await onPostgres.WaitUntilItIsPastAsync(expiresAt, Cancellation);

        (await tove.GetFromJsonAsync<JsonElement>("/tenancy/invitations", Cancellation)).GetArrayLength().Should().Be(0);
        using var tooLate = await juno.PostAsJsonAsync("/invitations/accept", new { token }, Cancellation);
        await tooLate.ShouldBeRefusedAsync(HttpStatusCode.Conflict, TenancyRefusals.InvitationLapsed);
        (await juno.GetFromJsonAsync<JsonElement>("/me/seats", Cancellation)).EnumerateArray().Should().ContainSingle("she got no seat in meadow");
    }
}
