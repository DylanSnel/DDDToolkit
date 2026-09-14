namespace DDDToolkit.EntityFramework.Providers.Tests.Infrastructure;

/// <summary>
/// The trait every test class in this project carries, so a run can ask for one provider.
/// <para>
/// CI uses it to give each provider its own job: <c>dotnet test --filter "Provider=Postgres"</c>.
/// That matters because xunit creates a collection fixture only when something in its collection is
/// going to run, so filtering to one provider starts one container instead of two. A job that only
/// needs PostgreSQL should not wait on a 700 MB SQL Server pull.
/// </para>
/// <para>
/// The values have no spaces on purpose. They end up inside a quoted filter expression in a YAML
/// file in a shell, and each of those three layers has an opinion about spaces.
/// </para>
/// </summary>
public static class ProviderTraits
{
    /// <summary>The trait name.</summary>
    public const string Key = "Provider";

    /// <summary>The value on every PostgreSQL test class.</summary>
    public const string Postgres = "Postgres";

    /// <summary>The value on every SQL Server test class.</summary>
    public const string SqlServer = "SqlServer";
}
