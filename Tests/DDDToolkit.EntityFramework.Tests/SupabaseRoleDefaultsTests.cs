using System.Reflection;
using System.Reflection.Emit;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;
using DDDToolkit.EntityFramework.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace DDDToolkit.EntityFramework.Tests;

/// <summary>
/// The roles are said once, in the project file: every kind of caller has a default, the build records
/// <c>SupabaseRowAccessRoles</c> in the application, <c>AddSupabaseRowLevelSecurity</c> switches to what it recorded or to
/// the defaults, and the host's own code wins over both. Every access file records the roles it was written for. None
/// of this opens a database; <see cref="SupabaseRolesMatchTests"/> asks one.
/// </summary>
public sealed class SupabaseRoleDefaultsTests : IDisposable
{
    private static readonly SupabaseMigrationSource Shelves = SupabaseMigrationSource.For(() => SupabaseShelfContext.Create());

    private static readonly RowAccessRule ShelvesByName = RowAccessRule.For<SupabaseShelf>(
        "Shelves by name", RowOperations.Read, "({col:Name} IS NOT DISTINCT FROM {caller:claim:shelf})", RowAccessRoles.User);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ddd-role-defaults-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Without_a_record_the_host_switches_to_the_roles_the_export_writes_for_by_default()
    {
        // This test assembly records nothing: the build leaves a test project alone.
        var options = Registered(services => services.AddSupabaseRowLevelSecurity());

        (options.UserRole, options.AnonymousRole, options.SystemInRole, options.SystemRole)
            .Should().Be(("authenticated", "anon", "ddd_system_in", SupabaseRowLevelSecurity.DefaultSystemRole), "the system caller does the toolkit's bookkeeping as ddd_system, which the access files make");
        options.TokenRoles.Should().BeEmpty();
        RowAccessRoleNames.Of(options).Should().Be(SupabaseRowLevelSecurity.DefaultRoles, "the host and the export take the same defaults");
        new SupabaseMigrationOptions().Roles.Should().Be(SupabaseRowLevelSecurity.DefaultRoles, "and so does an export by hand");

        Registered(services => services.AddSupabaseRowLevelSecurity<CallerOfTheTest>()).SystemRole.Should().Be("ddd_system", "the registration with an accessor of its own takes them too");
    }

    [Fact]
    public void The_hosts_own_code_wins_over_the_defaults()
    {
        var options = Registered(services => services.AddSupabaseRowLevelSecurity(options =>
        {
            options.SystemRole = null;
            options.TokenRoles["analyst"] = "desk_analyst";
        }));

        options.SystemRole.Should().BeNull("a host that runs its background work as the role it logs in as says so");
        options.TokenRoles.Should().Equal(new Dictionary<string, string> { ["analyst"] = "desk_analyst" });
    }

    [Fact]
    public void The_roles_the_build_recorded_in_the_registering_assembly_are_its_defaults_and_its_code_still_wins()
    {
        var recorded = RegisteredFrom("user=desk_user | system=none | Token: analyst = desk_analyst");

        (recorded.UserRole, recorded.AnonymousRole, recorded.SystemInRole, recorded.SystemRole)
            .Should().Be(("desk_user", "anon", "ddd_system_in", null), "a pair the project left out keeps its default, and system=none is the role the host logs in as");
        recorded.TokenRoles.Should().Equal(new Dictionary<string, string> { ["analyst"] = "desk_analyst" });

        var platform = RegisteredFrom("system=service_role");
        platform.SystemRole.Should().Be(SupabaseRowLevelSecurity.ServiceRole, "the system caller runs as the platform's role the project named");
        RowAccessRoleNames.Of(platform).System.Should().Be(SupabaseRowLevelSecurity.ServiceRole);

        var overridden = RegisteredFrom("system=none|token:analyst=desk_analyst", options =>
        {
            options.SystemRole = "desk_books";
            options.TokenRoles.Remove("analyst");
        });
        overridden.SystemRole.Should().Be("desk_books", "what the host's code sets comes after what the build recorded");
        overridden.TokenRoles.Should().BeEmpty();

        RegisteredFrom(recorded: null).SystemRole.Should().Be("ddd_system", "an application without a record takes the defaults");
    }

    [Fact]
    public void A_library_that_registers_for_the_host_gets_the_roles_recorded_in_the_application_the_hosts_environment_names()
    {
        // A layer of the application's own, an AddInfrastructure() for one, makes the call and records nothing, as this
        // test assembly does. The host's builder put its environment in the services first, naming the application, the
        // host's assembly also where WebApplicationFactory runs it in a test, whose entry assembly is the test's.
        var application = Application("token:analyst=desk_analyst|system=none").GetName().Name!;

        var options = Registered(services => services
            .AddSingleton<IHostEnvironment>(new HostingEnvironment { ApplicationName = application })
            .AddSupabaseRowLevelSecurity());

        options.SystemRole.Should().BeNull("the host's project said system=none, and the library registered for it");
        options.TokenRoles.Should().Equal(new Dictionary<string, string> { ["analyst"] = "desk_analyst" });

        // A name of the host's own choosing that is no assembly, and no environment at all, leave the defaults.
        Registered(services => services
            .AddSingleton<IHostEnvironment>(new HostingEnvironment { ApplicationName = "No.Such.Application" })
            .AddSupabaseRowLevelSecurity()).SystemRole.Should().Be("ddd_system");
        Registered(services => services
            .AddKeyedSingleton<IHostEnvironment>("other", new HostingEnvironment { ApplicationName = application })
            .AddSupabaseRowLevelSecurity()).SystemRole.Should().Be("ddd_system", "a keyed environment is no host's");
    }

    [Theory]
    [InlineData("system=anon", "SupabaseRowAccessRoles has 'system=anon'. 'anon' is the role the application's own bookkeeping runs as, and the role of an anonymous caller as well.")]
    [InlineData("user=authenticated|User=members", "SupabaseRowAccessRoles has the key 'User' twice.")]
    [InlineData("anonymous=ddd_system", "SupabaseRowAccessRoles has 'anonymous=ddd_system' and no 'system' pair, so the bookkeeping runs as its default, ddd_system. 'ddd_system' is the role the application's own bookkeeping runs as, and the role of an anonymous caller as well.")]
    [InlineData("User=ddd_system", "SupabaseRowAccessRoles has 'User=ddd_system' and no 'system' pair, so the bookkeeping runs as its default, ddd_system. 'ddd_system' is the role the application's own bookkeeping runs as, and the role of a signed-in user as well.")]
    [InlineData("system-in=ddd_system", "SupabaseRowAccessRoles has 'system-in=ddd_system' and no 'system' pair, so the bookkeeping runs as its default, ddd_system. 'ddd_system' is the role the application's own bookkeeping runs as, and the scoped system role as well.")]
    [InlineData("token:analyst=ddd_system", "SupabaseRowAccessRoles has 'token:analyst=ddd_system' and no 'system' pair, so the bookkeeping runs as its default, ddd_system. 'ddd_system' is the role the application's own bookkeeping runs as, and the role of the token role 'analyst' as well.")]
    public void A_record_the_export_would_refuse_stops_the_registration_naming_the_assembly_and_the_property(string recorded, string problem)
    {
        var registering = () => RegisteredFrom(recorded);

        registering.Should().Throw<TargetInvocationException>().WithInnerException<InvalidOperationException>()
            .WithMessage($"The build recorded the roles of SupabaseRowAccessRoles in Recorded*, and they cannot be used: {problem}*Fix the property in that project's file, and build it again.");
    }

    [Fact]
    public void Every_access_file_records_the_roles_it_was_written_for_and_a_change_of_roles_writes_it_again()
    {
        Build("token:analyst=desk_analyst | token:Analyst=desk_examiner").ExitCode.Should().Be(0);
        var first = AccessFiles().Should().ContainSingle().Subject;

        File.ReadAllText(first).Should().EndWith(
            "\n-- The roles the policies and the privileges above are written for, as the project that exports says them in\n" +
            "-- SupabaseRowAccessRoles, recorded on the ddd schema: the application compares the roles it switches to with\n" +
            "-- them when it starts, in the start-up check supabase.roles-match-access-files.\n" +
            "DO $ddd$\n" +
            "DECLARE\n" +
            "    recorded constant text := 'DDDToolkit row access roles: {\"user\":\"authenticated\",\"anonymous\":\"anon\",\"system-in\":\"ddd_system_in\",\"system\":\"ddd_system\",\"token\":{\"Analyst\":\"desk_examiner\",\"analyst\":\"desk_analyst\"}}';\n" +
            "BEGIN\n" +
            "    IF pg_catalog.to_regnamespace('ddd') IS NULL THEN\n" +
            "        CREATE SCHEMA ddd;\n" +
            "    END IF;\n" +
            "    IF pg_catalog.obj_description(pg_catalog.to_regnamespace('ddd')::pg_catalog.oid, 'pg_namespace') IS DISTINCT FROM recorded THEN\n" +
            "        EXECUTE 'COMMENT ON SCHEMA ddd IS ' || pg_catalog.quote_literal(recorded);\n" +
            "    END IF;\n" +
            "END\n" +
            "$ddd$;\n");

        // The same roles in other words write nothing; other roles write the file again, with what they are now.
        Build("Token:Analyst=desk_examiner|token:analyst=desk_analyst|system=ddd_system").Output.Should().Contain("Unchanged").And.NotContain("Created");
        Build("token:analyst=desk_analyst|token:Analyst=desk_examiner|system=none").ExitCode.Should().Be(0);
        AccessFiles().Should().HaveCount(2);
        File.ReadAllText(AccessFiles()[^1]).Should().Contain("\"system\":null,", "the newest file records that it made no bookkeeping role");
    }

    /// <summary>The options the services <paramref name="register"/> registers resolve to.</summary>
    private static PostgresRowLevelSecurityOptions Registered(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<PostgresRowLevelSecurityOptions>();
    }

    /// <summary>
    /// The options <c>AddSupabaseRowLevelSecurity</c> registers when an application whose build recorded
    /// <paramref name="recorded"/> calls it, with <paramref name="configure"/>, as a host's <c>Program</c> does.
    /// </summary>
    private static PostgresRowLevelSecurityOptions RegisteredFrom(string? recorded, Action<PostgresRowLevelSecurityOptions>? configure = null)
    {
        var services = new ServiceCollection();
        Application(recorded).GetType("Program")!.GetMethod("Register")!.Invoke(null, [services, configure]);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<PostgresRowLevelSecurityOptions>();
    }

    /// <summary>
    /// An application made here, with the attribute the build writes where it recorded <paramref name="recorded"/>, and a
    /// class <c>Program</c> whose one method, <c>Register(services, configure)</c>, calls <c>AddSupabaseRowLevelSecurity</c>.
    /// </summary>
    private static Assembly Application(string? recorded)
    {
        var name = new AssemblyName("Recorded" + Guid.NewGuid().ToString("N"));
        var assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        if (recorded is not null)
        {
            assembly.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!,
                ["SupabaseRowAccessRoles", recorded]));
        }

        var program = assembly.DefineDynamicModule(name.Name!).DefineType("Program", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var register = program.DefineMethod(
            "Register",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(IServiceCollection),
            [typeof(IServiceCollection), typeof(Action<PostgresRowLevelSecurityOptions>)]);
        var il = register.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(Supabase.DependencyInjection).GetMethods().Single(method =>
            method.Name == nameof(Supabase.DependencyInjection.AddSupabaseRowLevelSecurity) && !method.IsGenericMethodDefinition));
        il.Emit(OpCodes.Ret);
        program.CreateType();

        return assembly;
    }

    private string[] AccessFiles()
        => Directory.Exists(_directory) ? [.. Directory.GetFiles(_directory, "*_access.*.ddd.sql").Order(StringComparer.Ordinal)] : [];

    private (int ExitCode, string Output) Build(string? roles)
    {
        using var output = new StringWriter();
        var exitCode = SupabaseMigrationBuild.Run("Write", [Shelves], [ShelvesByName], [], [], _directory, start: null, roles, callerFunctions: null, grants: null, force: null, output);
        return (exitCode, output.ToString());
    }
}
