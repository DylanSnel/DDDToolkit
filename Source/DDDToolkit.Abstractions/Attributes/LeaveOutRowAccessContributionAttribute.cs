namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Leaves a package's row access contribution out of this application's exported migrations, or out of one
/// context's access file. Put it in the project that runs the Supabase export:
/// <code>
/// // None of Tenancy's SQL: the application writes Tenancy's policies itself.
/// [assembly: LeaveOutRowAccessContribution(typeof(TenancyRowAccessContribution))]
///
/// // Membership's functions everywhere but in the archive's access file.
/// [assembly: LeaveOutRowAccessContribution(typeof(MembershipRowAccessContribution&lt;&gt;), Context = typeof(ArchiveContext))]
/// </code>
/// <para>
/// A package that declares itself a contributor (<see cref="RowAccessContributionAttribute"/>) writes its SQL into
/// every application that references it, because a package such as Tenancy on Postgres only works with it in the
/// database. This is for the rare application that must not have it: one that writes those policies itself, with a
/// class of its own it then lists with <see cref="UseRowAccessContributionAttribute"/>, or one with a context whose
/// database is not the package's to write. What is left out is not checked by anything else: the package's start-up
/// checks still expect what it writes wherever the application uses it.
/// </para>
/// <para>
/// A generic contribution is left out with its open type, every closing of it, or with one closing,
/// <c>typeof(MembershipRowAccessContribution&lt;DocumentShare&gt;)</c>, that one alone. A line that leaves nothing
/// out, because no referenced package declares what it names, nothing the application marks makes that closing,
/// or its <see cref="Context"/> is no class derived from <c>DbContext</c>, is a warning (DDD00068), and nothing is
/// left out for it.
/// </para>
/// </summary>
/// <param name="contribution">The package's contribution, as it declares it, or one closing of a generic one.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class LeaveOutRowAccessContributionAttribute(Type contribution) : Attribute
{
    /// <summary>The package's contribution that is left out.</summary>
    public Type Contribution { get; } = contribution;

    /// <summary>
    /// The context it is left out of, its access file only; the contribution still writes for every other one.
    /// Not set, it writes for none.
    /// </summary>
    public Type? Context { get; set; }
}
