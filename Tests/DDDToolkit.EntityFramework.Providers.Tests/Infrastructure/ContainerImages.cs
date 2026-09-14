namespace DDDToolkit.EntityFramework.Providers.Tests.Infrastructure;

/// <summary>
/// The database images this suite runs against, and the reasoning behind each one, in one place so a
/// bump is a deliberate edit with the argument next to it.
/// <para>
/// <b>The rule.</b> Every tag is exact, down to the patch. A floating tag such as
/// <c>2022-latest</c> means somebody else's release can turn this build red on a day nobody touched
/// it, and then the first question about a red build is "is this us?" rather than "what did we
/// break?". The cost of pinning is that security updates need a commit; that is the right cost for a
/// throw-away container that holds nothing and listens to nobody.
/// </para>
/// <para>
/// Bumping is a normal chore: pick the newest patch of the same product line, run the suite, commit.
/// Changing the product line, PostgreSQL 17 to 18 or SQL Server 2022 to 2025, is a different decision
/// and belongs in its own commit with its own reasoning.
/// </para>
/// </summary>
public static class ContainerImages
{
    /// <summary>
    /// PostgreSQL. The major matches the pgmq image
    /// <c>ghcr.io/pgmq/pg17-pgmq</c> that <c>DDDToolkit.EntityFramework.Postgres</c> is tested against
    /// in <c>Tests/DDDToolkit.EntityFramework.Tests</c>, because the pgmq project publishes its
    /// extension on PostgreSQL 17. One major across the whole suite means a difference between two
    /// runs is a difference in our code, not a difference between two servers.
    /// <para>
    /// The Debian image rather than <c>-alpine</c>, for the same reason: alpine is musl, and musl and
    /// glibc do not collate text identically. Nothing here orders text, but a 250 MB saving is not
    /// worth introducing a second variable into the one suite whose job is to find differences.
    /// </para>
    /// </summary>
    public const string Postgres = "postgres:17.11";

    /// <summary>
    /// SQL Server. 2022 rather than 2025 because 2022 is the older release still in mainstream
    /// support, so it is the weaker of the two and the one a failure would show up on first; a 2025
    /// server runs everything a 2022 server does.
    /// <para>
    /// The exact tag is the newest 2022 cumulative update, GDR included. It is the expensive one:
    /// roughly 700 MB to pull and the better part of a minute to become ready, against about 150 MB
    /// and a few seconds for PostgreSQL. That is why the CI workflow gives each provider its own job
    /// rather than making one job wait for both.
    /// </para>
    /// </summary>
    public const string SqlServer = "mcr.microsoft.com/mssql/server:2022-CU26-GDR1-ubuntu-22.04";
}
