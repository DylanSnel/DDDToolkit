namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Declares this assembly a contributor of row level security: the Supabase export of every application that
/// references it, directly or through another project, writes what the class writes into that application's
/// migrations. SQL functions, policies and statements for the tables a package maps, written from each context's
/// Entity Framework model, next to the policies of the <c>[RowAccess]</c> rules:
/// <code>
/// [assembly: RowAccessContribution(typeof(AuditRowAccess))]
/// </code>
/// <para>
/// Referencing the package is the application's consent: a package such as Tenancy on Postgres only works with its
/// SQL in the database, so an application that uses it has nothing to choose. The application writes no line and no
/// class for it. The build makes the class in the project that runs the export, through a class it writes into
/// <c>DDDToolkit.RowAccessContributionsOfPackages.g.cs</c>, and an application that must not write a package's SQL,
/// or not for one context, says so with <see cref="LeaveOutRowAccessContributionAttribute"/>.
/// </para>
/// <para>
/// The class is public, not abstract, and implements <c>IRowAccessContribution</c>, from
/// <c>DDDToolkit.EntityFramework.Postgres</c>. Without a constructor that takes anything, it is made with
/// <c>new X()</c>. SQL that depends on what only the application knows, its catalogue or a resource's rules, takes
/// it in the constructor, whose every parameter says with <see cref="FromApplicationAttribute"/> which member the
/// application marks for it; a generic class is closed with the type argument of the attribute that marks it, once
/// for each member so marked. The build reports what it cannot find (DDD00054) and what it cannot use (DDD00072).
/// </para>
/// <para>
/// A package is an assembly that declares no module. In an assembly that declares one, with <c>DDD_Module</c> or
/// <c>[assembly: Module]</c>, the same attribute offers the class rather than writing it: a module's SQL is the
/// application's own, so the project that runs the export lists it with
/// <see cref="UseRowAccessContributionAttribute"/>, and the host says what its database gets. The offer is what
/// makes a forgotten line heard: the build warns about one that project does not list (DDD00069).
/// </para>
/// </summary>
/// <param name="contribution">The class that writes the SQL, open where it is generic: <c>typeof(MembershipRowAccessContribution&lt;&gt;)</c>.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class RowAccessContributionAttribute(Type contribution) : Attribute
{
    /// <summary>The class that writes the SQL.</summary>
    public Type Contribution { get; } = contribution;
}
