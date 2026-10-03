using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Examples.Tenancy.Tests.Host;

/// <summary>
/// Making the demonstration people users of Supabase Auth, all with one password, is for a developer's machine:
/// the host refuses to start with it anywhere else. That it makes them, under the ids their seats are found by,
/// is proven against Supabase's own Auth server, in <c>Supabase/SampleOnSupabaseTests</c>.
/// </summary>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class DemoAuthUsersTests(SampleHosts sample) : IClassFixture<SampleHosts>
{
    [Fact]
    public async Task Outside_development_or_without_its_settings_the_host_does_not_start_and_says_everything_that_is_wrong()
    {
        await using var production = await sample.NotStartedAsync(
            settings: new Dictionary<string, string>
            {
                ["Supabase:Url"] = "https://tenancy.example.test",
                [DemoAuthUsers.Setting] = "true",
                [DemoAuthUsers.PasswordSetting] = "too-short",
            },
            environment: Environments.Production);

        var start = () => production.CreateClient();

        start.Should().Throw<InvalidOperationException>().WithMessage(
            "*Sample:SeedAuthUsers*Production, not Development*Supabase:SecretKey is not set*Sample:DemoPassword is not set, or shorter than 12 characters*");
    }

    [Fact]
    public async Task Without_the_setting_nothing_of_it_is_registered()
    {
        // A host that is not told to make them: no Auth server is asked for, and none is reached.
        var host = await sample.SharedAsync();

        host.Services.GetService(typeof(DDDToolkit.Auth.Supabase.SupabaseAuthAdmin)).Should().BeNull();
        host.Services.GetServices<IHostedService>().Should().NotContain(service => service is DemoAuthUsers);
    }
}
