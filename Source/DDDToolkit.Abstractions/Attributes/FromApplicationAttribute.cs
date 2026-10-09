namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Says where a constructor parameter of a package's row access contribution comes from: the static property or
/// field the application marks with <see cref="Marker"/>. The build makes the contribution in the project that runs
/// the Supabase export, before any host exists, so what its SQL depends on is what the application declares, and
/// it finds that by the mark:
/// <code>
/// // In the package
/// public class PlanRowAccess([FromApplication(typeof(PlansAttribute))] IReadOnlyList&lt;string&gt; plans) : IRowAccessContribution { ... }
///
/// // In the application
/// [Plans]
/// public static IReadOnlyList&lt;string&gt; All { get; } = ["free", "pro"];
/// </code>
/// <para>
/// Searched are the project that runs the export and every project it references that references the marker's
/// assembly, the one that declares it excepted. A marked member is static, readable from that project, and of a type
/// the parameter takes; one that is not is DDD00071. Of a library, that project sees what is public and nothing
/// else, so a member a library marks that is not public is not found at all: put <see cref="ApplicationMarkAttribute"/>
/// on the marker, and the library's own build reports it where it is declared (DDD00070). It is found in one of
/// three ways:
/// </para>
/// <list type="bullet">
/// <item><b>One.</b> Exactly one member is marked. Two are DDD00071, since the build cannot choose. None is the
/// parameter's default value where it has one, so a package gives what it can do without one, and otherwise
/// DDD00054: the contribution is not written.</item>
/// <item><b>Every</b> (<see cref="Every"/>). Every member so marked, in the order of their names, as an array of
/// the parameter's element type: a parameter of <c>IEnumerable&lt;IEnumerable&lt;Permission&gt;&gt;</c> takes
/// every marked list of permissions, and none is an empty array.</item>
/// <item><b>Once for each.</b> A generic marker, <c>typeof(MembershipRulesAttribute&lt;&gt;)</c>, on a parameter of
/// a generic contribution: the contribution is made once for every member so marked, closed with the type
/// arguments the mark is written with, so <c>[MembershipRules&lt;DocumentShare&gt;]</c> makes
/// <c>MembershipRowAccessContribution&lt;DocumentShare&gt;</c> with that member. A contribution has one such
/// parameter at most, and none marked is DDD00054.</item>
/// </list>
/// <para>
/// The build uses the public constructor whose every parameter carries this attribute, and of several the one with
/// the most parameters; a class without one is made with its public parameterless constructor.
/// </para>
/// </summary>
/// <param name="marker">The attribute the application marks the member with, open where it is generic.</param>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class FromApplicationAttribute(Type marker) : Attribute
{
    /// <summary>The attribute the application marks the member with.</summary>
    public Type Marker { get; } = marker;

    /// <summary>Whether the parameter takes every member so marked, as an array, rather than exactly one.</summary>
    public bool Every { get; set; }
}
