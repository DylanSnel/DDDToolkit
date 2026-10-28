using DDDToolkit.Abstractions.Access;
using DDDToolkit.Access;
using DDDToolkit.Auth.Supabase.AspNetCore;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The caller ASP.NET Core's Supabase scheme answers outside a request: the system, as it always was, or,
/// where the host requires explicit callers, nobody. No request and no database are needed to ask it.
/// </summary>
public sealed class SupabaseCurrentUserSourceTests
{
    private const string ProjectUrl = "http://127.0.0.1:54321";

    private const string JwtSecret = "super-secret-jwt-token-with-at-least-32-characters-long";

    [Fact]
    public void Outside_a_request_with_explicit_callers_required_the_accessor_throws()
    {
        using var strict = Host(requireExplicitCurrentUser: true);
        var callers = strict.GetRequiredService<ICurrentUser>();

        strict.GetRequiredService<IHttpContextAccessor>().HttpContext.Should().BeNull("this is work outside a request");
        ((Func<CurrentUserSnapshot>)(() => callers.Snapshot)).Should().Throw<NoCurrentUserException>();

        using (TestUser.BeginSystem("reports"))
        {
            callers.Snapshot.Should().BeSameAs(BegunUser, "a caller begun around the work is answered");
        }

        using var plain = Host(requireExplicitCurrentUser: false);
        plain.GetRequiredService<ICurrentUser>().Snapshot.Should().BeSameAs(CurrentUserSnapshot.System, "without the option, work outside a request is the system's, as before");
    }

    [Fact]
    public void Inside_a_request_the_accessor_answers_the_requests_caller_whatever_is_required()
    {
        using var strict = Host(requireExplicitCurrentUser: true);
        var requests = strict.GetRequiredService<IHttpContextAccessor>();
        requests.HttpContext = new DefaultHttpContext();

        strict.GetRequiredService<ICurrentUser>().Snapshot.Should().BeSameAs(CurrentUserSnapshot.Anonymous, "a request without a validated token is anon");
        requests.HttpContext.GetSupabaseUser().Should().BeSameAs(CurrentUserSnapshot.Anonymous);

        using (TestUser.BeginNone())
        {
            strict.GetRequiredService<ICurrentUser>().Snapshot.Should().BeSameAs(CurrentUserSnapshot.Anonymous, "BeginNone hides an ambient caller, not the request");
        }

        requests.HttpContext = null;
    }

    private static ServiceProvider Host(bool requireExplicitCurrentUser)
    {
        var services = new ServiceCollection();
        services.AddAuthentication().AddSupabaseJwtBearer(ProjectUrl, jwt => jwt.UseSupabaseJwtSecret(JwtSecret));
        if (requireExplicitCurrentUser)
        {
            services.RequireExplicitCurrentUser();
        }

        return services.BuildServiceProvider();
    }
}
