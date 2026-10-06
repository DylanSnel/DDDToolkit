using System.Reflection;
using System.Runtime.CompilerServices;
using DDDToolkit.EntityFramework;
using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.Startup;
using DDDToolkit.Supporting.Tenancy;
using DDDToolkit.Supporting.Tenancy.Catalogue;
using DDDToolkit.Supporting.Tenancy.EntityFramework;
using FluentAssertions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Examples.Tenancy.Tests.Host;

/// <summary>
/// What the host checks before it takes a request, the catalogue and how every context is wired, and how it
/// registers its modules' contexts: each pooled, the request's own and the one a read takes wired alike.
/// </summary>
[Trait("Category", "Samples")]
[Trait("Sample", "Tenancy.Supabase")]
public sealed class StartupTests(SampleHosts sample) : IClassFixture<SampleHosts>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_bad_catalogue_refuses_start_up()
    {
        // A module that declares a key under Tenancy's own prefix: the catalogue refuses it when it is built.
        await using var host = await sample.NotStartedAsync(services => services.AddTenancyPermissions(
        [
            new Permission("tenancy.everything", "Rogue", "Claims a key that is Tenancy's to declare"),
        ]));

        // Read from what the host logged as it gave up, as well as from what the start threw: the host fails on a
        // thread of its own, and a test that asks for it a moment too late is only told that it is disposed.
        var refused = host.RefusedStart();

        refused.OfType<TenancyCatalogueException>().Should().NotBeEmpty("the catalogue is what refuses the start, and the host gave up with {0}", string.Join(" / ", refused.Select(failure => failure.Message)))
            .And.OnlyContain(catalogue => catalogue.Problems.Count(problem => problem.Contains("tenancy.everything", StringComparison.Ordinal)) == 1);
    }

    [Fact]
    public async Task A_fresh_start_and_a_walk_through_the_crew_log_no_warning()
    {
        // Everything a first start does, on a database with the exported files applied and nothing in it: the
        // checks, then the seeding. Then what the screens do with a project: list, open, and change its crew.
        // None of it is refused, so nothing of it should be logged as a warning: not a check that found
        // something to mention, nor a query whose shape nobody chose.
        var logs = new RecordingLoggerProvider();
        await using var host = await sample.StartAsync(services => services.AddSingleton<ILoggerProvider>(logs), seeded: false);
        var harbor = DemoData.Harbor;
        var pier = harbor.ProjectNamed("Pier 7");
        using var leo = await host.ClientAsync("leo", harbor.Slug);

        (await leo.VisibleProjectsAsync()).Names().Should().Equal("Pier 7");
        (await leo.ProjectDetailAsync(pier)).GetProperty("crew").GetArrayLength().Should().Be(3);
        using (var given = await leo.GiveCrewRoleAsync(pier, harbor.SeatOf(DemoPeople.Vic), harbor.ProjectRoles[SampleCatalogue.Surveyor]))
        {
            given.IsSuccessStatusCode.Should().BeTrue();
        }

        using (var taken = await leo.TakeCrewRoleAsync(pier, harbor.SeatOf(DemoPeople.Vic), harbor.ProjectRoles[SampleCatalogue.Surveyor]))
        {
            taken.IsSuccessStatusCode.Should().BeTrue();
        }

        (await leo.GetAsync("/tenancy/roles", TestContext.Current.CancellationToken)).IsSuccessStatusCode.Should().BeTrue();

        logs.Entries.Where(entry => entry.Level >= LogLevel.Warning).Select(entry => entry.Category + ": " + entry.Message)
            .Should().BeEmpty("a first start, and a crew changed as its lead may, give nothing to warn about");
        logs.Entries.Should().Contain(entry => entry.Category == typeof(DemoSeeder).FullName, "the host's logs are the ones read here");

        // Each module's context was given what the host registered by UseDDDToolkit alone, and the log said so, once
        // per context, as information.
        var wired = logs.Entries.Where(entry => entry.Category == "DDDToolkit.EntityFramework.ContextParts").ToList();
        wired.Should().OnlyContain(entry => entry.Level == LogLevel.Information);
        foreach (var context in new[] { nameof(TenantsContext), nameof(ProjectsContext), nameof(InspectionsContext) })
        {
            wired.Should().ContainSingle(entry => entry.Message.StartsWith($"UseDDDToolkit gave '{context}' ", StringComparison.Ordinal))
                .Which.Message.Should().Contain(": postgres.row-level-security, tenancy.save-check.");
        }

        // The host has no start-up class of its own: every check its registrations brought ran, in their order,
        // and passed, before it served anything.
        var checks = host.Services.GetRequiredService<StartupChecks>().InOrder().Select(check => check.Name).ToList();
        logs.Entries.Should().ContainSingle(entry => entry.Category == "DDDToolkit.Startup.StartupCheckRunner" && entry.Level == LogLevel.Information)
            .Which.Message.Should().EndWith(": " + string.Join(", ", checks) + ".", "the runner names every check that passed, in the order it ran them");
    }

    [Fact]
    public async Task Every_context_is_wired_by_the_one_call_with_what_the_host_registered()
    {
        var host = await sample.SharedAsync();
        await using var scope = host.Services.CreateAsyncScope();

        var contexts = EntityFrameworkChecks.RegisteredContexts(scope.ServiceProvider);

        contexts.Should().Contain([typeof(TenantsContext), typeof(ProjectsContext), typeof(InspectionsContext)]);
        foreach (var contextType in contexts)
        {
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
            var toolkit = () => EntityFrameworkChecks.EnsureToolkitWired(context);
            var tenancy = () => TenancyChecks.EnsureWired(context);
            var asCaller = () => PostgresRowAccessChecks.EnsureRowLevelSecurityWired(context);

            toolkit.Should().NotThrow("{0} saves through the toolkit's interceptors, and its outbox and the history it keeps are in its model", contextType.Name);
            tenancy.Should().NotThrow("{0} keeps rows to a tenant, and UseDDDToolkit gave it Tenancy's save check after the toolkit's interceptors", contextType.Name);
            asCaller.Should().NotThrow("{0} runs as its caller: the host registered row level security, and UseDDDToolkit put it on the context", contextType.Name);

            // Each module wrote UseDDDToolkit and nothing more: the toolkit's interceptors, then the caller on every
            // connection, then Tenancy's save check, each once, in that order.
            Interceptors(context).Select(interceptor => interceptor is TenancySaveInterceptor ? typeof(TenancySaveInterceptor) : interceptor.GetType()).Take(6).Should().Equal(
                [
                    typeof(PublishDomainEventsInterceptor), typeof(InvariantInterceptor), typeof(AggregateVersionInterceptor), typeof(DatabaseRefusalInterceptor),
                    typeof(PostgresRowLevelSecurityInterceptor), typeof(TenancySaveInterceptor),
                ],
                "{0} is wired by the one call",
                contextType.Name);
        }
    }

    [Fact]
    public async Task The_catalogue_the_policies_are_exported_from_is_the_one_the_host_runs_with()
    {
        // The host builds its catalogue from its services: the application's part, and the modules' lists, which
        // Tenancy's generator collected into the host. The program that exports has no host, and Tenancy's package
        // builds it there from what the sample marks: the part marked [TenancyCatalogue] and every list marked
        // [TenancyPermissions]. Policies written from another catalogue than the host's would let other grants
        // through than the application gives, so the two are held to the same keys and packs.
        var running = (await sample.SharedAsync()).Services.GetRequiredService<TenancyCatalogue>();
        var exported = ExportedMarks.Tenancy().Catalogue;

        exported.Permissions.Select(permission => (permission.Key, permission.ManagesAccess, permission.Retired))
            .Should().BeEquivalentTo(running.Permissions.Select(permission => (permission.Key, permission.ManagesAccess, permission.Retired)));
        exported.Packs.Select(pack => (pack.Key, Keys: string.Join(",", pack.Keys.Order(StringComparer.Ordinal)), pack.Administers))
            .Should().BeEquivalentTo(running.Packs.Select(pack => (pack.Key, Keys: string.Join(",", pack.Keys.Order(StringComparer.Ordinal)), pack.Administers)));
    }

    [Fact]
    public async Task A_context_a_query_takes_from_the_factory_is_wired_like_the_request_s()
    {
        var host = await sample.SharedAsync();
        await using var scope = host.Services.CreateAsyncScope();

        // Every module's contexts come from a pool: a read takes one from the factory for its one query, and the
        // request's own is taken from the same pool. Both must be the same context in every respect but who holds
        // it: the tenant filter, the save check and the toolkit's interceptors, each once.
        await using var tenancy = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<TenantsContext>>().CreateDbContextAsync(Cancellation);
        await using var projects = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<ProjectsContext>>().CreateDbContextAsync(Cancellation);
        await using var inspections = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<InspectionsContext>>().CreateDbContextAsync(Cancellation);

        foreach (var (made, ofTheScope) in new (DbContext, DbContext)[]
        {
            (tenancy, scope.ServiceProvider.GetRequiredService<TenantsContext>()),
            (projects, scope.ServiceProvider.GetRequiredService<ProjectsContext>()),
            (inspections, scope.ServiceProvider.GetRequiredService<InspectionsContext>()),
        })
        {
            var check = () => TenancyChecks.EnsureWired(made);

            made.Should().NotBeSameAs(ofTheScope, "the factory hands out a context of its own");
            check.Should().NotThrow("the factory's {0} is configured as the scope's is", made.GetType().Name);
            Interceptors(made).Select(interceptor => interceptor.GetType())
                .Should().Equal(Interceptors(ofTheScope).Select(interceptor => interceptor.GetType()), "and with the same interceptors: both share the pool's options");
            Interceptors(made).Select(interceptor => interceptor.GetType())
                .Should().OnlyHaveUniqueItems("the context's options are configured once, for the pool");
        }
    }

    [Fact]
    public void A_pooled_context_class_keeps_no_state_of_its_own()
    {
        // A pool hands one instance to one caller after another, and gives back to each only what Entity Framework
        // resets: what the instance tracks, and its own settings. A field a context class declares would be carried
        // from one request into the next.
        var contexts = SampleLayout.Projects
            .Select(project => project.Anchor.Assembly)
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(DbContext).IsAssignableFrom(type) && !type.IsAbstract)
            .ToList();

        contexts.Should().BeEquivalentTo([typeof(TenantsContext), typeof(ProjectsContext), typeof(InspectionsContext)]);
        foreach (var context in contexts)
        {
            // The one shape Entity Framework pools: a single public constructor, taking the options and nothing else.
            var constructor = context.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Should().ContainSingle("{0} is pooled", context.Name).Which;
            constructor.GetParameters().Select(parameter => parameter.ParameterType)
                .Should().Equal([typeof(DbContextOptions<>).MakeGenericType(context)], "{0} takes its options and nothing of a request", context.Name);

            var fields = new List<FieldInfo>();
            for (var type = context; type != typeof(DbContext); type = type.BaseType!)
            {
                fields.AddRange(type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
            }

            fields.Where(field => !IsBackingFieldOfASet(field)).Select(field => field.Name)
                .Should().BeEmpty("{0} keeps nothing between the callers it serves", context.Name);
        }

        // A set declared as an auto-property is filled by Entity Framework when the instance is made, with a set
        // that asks the context each time: the one field a context class may have.
        static bool IsBackingFieldOfASet(FieldInfo field)
            => field.IsDefined(typeof(CompilerGeneratedAttribute))
               && field.FieldType.IsGenericType
               && field.FieldType.GetGenericTypeDefinition() == typeof(DbSet<>);
    }

    [Fact]
    public async Task A_configuration_added_for_a_test_reaches_a_pooled_context()
    {
        // What the tests of this suite do to watch a module's statements: add an interceptor to its context's
        // options from outside, after the host registered the pool, with the lifetime Entity Framework gives a
        // configuration by default. A pool builds its options once, from the application's services, and the host
        // validates its scopes: this is the proof that such a configuration still applies there.
        var added = new CommandCounter();
        await using var host = await sample.StartAsync(services =>
        {
            services.ConfigureDbContext<TenantsContext>(options => options.AddInterceptors(added));
            services.ConfigureDbContext<ProjectsContext>(options => options.AddInterceptors(added));
            services.ConfigureDbContext<InspectionsContext>(options => options.AddInterceptors(added));
        });

        var fromTheRoot = () => host.Services.GetRequiredService<ISender>();
        fromTheRoot.Should().Throw<InvalidOperationException>("the host validates its scopes").WithMessage("*scoped*root*");

        await using var scope = host.Services.CreateAsyncScope();
        await using var tenancy = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<TenantsContext>>().CreateDbContextAsync(Cancellation);
        await using var projects = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<ProjectsContext>>().CreateDbContextAsync(Cancellation);
        await using var inspections = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<InspectionsContext>>().CreateDbContextAsync(Cancellation);

        foreach (var context in new DbContext[]
        {
            tenancy,
            projects,
            inspections,
            scope.ServiceProvider.GetRequiredService<TenantsContext>(),
            scope.ServiceProvider.GetRequiredService<ProjectsContext>(),
            scope.ServiceProvider.GetRequiredService<InspectionsContext>(),
        })
        {
            context.IsPooled().Should().BeTrue();
            Interceptors(context).Should().ContainSingle(interceptor => ReferenceEquals(interceptor, added), "the test's interceptor is in the options of {0}, once", context.GetType().Name);
            TenancyChecks.EnsureWired(context);
        }

        // And it is not only listed: a statement of a request passes through it.
        using (SampleCallers.BeginSeatOf(DemoPeople.Ada, DemoData.Harbor))
        {
            added.WatchThisFlow();
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new VisibleProjects(), Cancellation);
            added.Commands.Should().NotBeEmpty();
        }
    }

    /// <summary>The interceptors a context was configured with, in order.</summary>
    private static IEnumerable<IInterceptor> Interceptors(DbContext context)
        => context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.Interceptors!;
}
