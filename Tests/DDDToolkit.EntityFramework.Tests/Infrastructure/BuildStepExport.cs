using System.Runtime.CompilerServices;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.EntityFramework.Postgres;
using DDDToolkit.EntityFramework.Supabase;

namespace DDDToolkit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// What the generator writes into a host that turns the Supabase export on, written by hand into this test
/// assembly, so the build step's target can start this assembly the way it starts a host:
/// <c>SupabaseMigrationBuildTargetTests</c> runs the targets file against it through MSBuild.
/// <para>
/// It exports only when that test asked for it through <see cref="Variable"/> as well, so no other start of
/// this assembly, the test run's own included, ever exports anything or ends early.
/// </para>
/// </summary>
internal static class BuildStepExport
{
    /// <summary>Set to <c>1</c> by the test that runs the build step against this assembly.</summary>
    public const string Variable = "DDDTOOLKIT_TESTS_BUILD_STEP";

    /// <summary>
    /// The rules the build step writes: two for the user and the anonymous caller, which ask the caller's id and
    /// claims, one for the scoped system role, which asks its role, and one for a token role, which only a build
    /// that maps it can write.
    /// </summary>
    public static readonly RowAccessRule[] Rules =
    [
        DeskRules.Owners,
        DeskRules.Teammates,
        RowAccessRule.For<Ticket>("Scoped work reads by role", RowOperations.Read, "({caller:role} IS NOT NULL)", RowAccessRoles.SystemIn),
        RowAccessRule.For<Ticket>("Analysts read every ticket", RowOperations.Read, "TRUE", RowAccessRoles.Token("analyst")),
    ];

    // The contribution it hands over, as a host that lists it with [assembly: UseRowAccessContribution] does:
    // DutyRowAccess, whose functions ask the caller's claims and are granted to the user's role.
    [ModuleInitializer]
#pragma warning disable CA2255 // The ModuleInitializer attribute should not be used in libraries: this assembly is started as the application.
    internal static void ExportWhenTheTestAsks()
#pragma warning restore CA2255
    {
        if (Environment.GetEnvironmentVariable(Variable) != "1")
        {
            return;
        }

        SupabaseMigrationBuild.RunIfRequested(
            static () => [SupabaseMigrationSource.For(static () => DeskContext.Create(), "desk")],
            static () => Rules,
            static () => [],
            static () => [new DutyRowAccess()]);
    }
}
