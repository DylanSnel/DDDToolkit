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
public sealed class SupabaseCallerAccessorTests
{
    private const string ProjectUrl = "http://127.0.0.1:54321";

    private const string JwtSecret = "super-secret-jwt-token-with-at-least-32-characters-long";

    [Fact]
    public void Outside_a_request_with_explicit_callers_required_the_accessor_throws()
    {
        using var strict = Host(requireExplicitCallers: true);
        var callers = strict.GetRequiredService<ICallerAccessor>();

        strict.GetRequiredService<IHttpContextAccessor>().HttpContext.Should().BeNull("this is work outside a request");
        ((Func<Caller>)(() => callers.Current)).Should().Throw<NoCallerException>();

        using (Callers.Begin(Caller.SystemIn("reports")))
        {
            callers.Current.Should().BeSameAs(Callers.Ambient, "a caller begun around the work is answered");
        }

        using var plain = Host(requireExplicitCallers: false);
        plain.GetRequiredService<ICallerAccessor>().Current.Should().BeSameAs(Caller.System, "without the option, work outside a request is the system's, as before");
    }

    [Fact]
    public void Inside_a_request_the_accessor_answers_the_requests_caller_whatever_is_required()
    {
        using var strict = Host(requireExplicitCallers: true);
        var requests = strict.GetRequiredService<IHttpContextAccessor>();
        requests.HttpContext = new DefaultHttpContext();

        strict.GetRequiredService<ICallerAccessor>().Current.Should().BeSameAs(Caller.Anonymous, "a request without a validated token is anon");
        requests.HttpContext.SupabaseCaller().Should().BeSameAs(Caller.Anonymous);

        using (Callers.BeginNone())
        {
            strict.GetRequiredService<ICallerAccessor>().Current.Should().BeSameAs(Caller.Anonymous, "BeginNone hides an ambient caller, not the request");
        }

        requests.HttpContext = null;
    }

    private static ServiceProvider Host(bool requireExplicitCallers)
    {
        var services = new ServiceCollection();
        services.AddAuthentication().AddSupabaseJwtBearer(ProjectUrl, jwt => jwt.UseSupabaseJwtSecret(JwtSecret));
        if (requireExplicitCallers)
        {
            services.RequireExplicitCallers();
        }

        return services.BuildServiceProvider();
    }
}
