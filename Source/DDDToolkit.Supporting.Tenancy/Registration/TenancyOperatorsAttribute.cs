using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Supporting.Tenancy;

/// <summary>
/// Marks the token roles of the application's operators, the ones it adds to <c>TenancyOptions.OperatorTokenRoles</c>,
/// so the program that exports the database's policies writes them for the same roles:
/// <code>
/// public static class ShopOperators
/// {
///     [TenancyOperators]
///     public static IReadOnlyList&lt;string&gt; TokenRoles { get; } = ["operator"];
/// }
///
/// // Where Tenancy is registered
/// options.OperatorTokenRoles.UnionWith(ShopOperators.TokenRoles);
/// </code>
/// <para>
/// Tenancy on Postgres gives the database role of each a policy that reads every tenant and writes nothing, and the
/// host's start-up check stops a host that runs with other operators than its database was written for. An
/// application without operators marks nothing, and nothing is written for them.
/// </para>
/// <para>
/// The member is a static property or field of a collection of <see cref="string"/>, readable from the project that
/// runs the export: in a library it is public, in public types, which that library's own build checks (DDD00070).
/// One member is marked in all the projects it references: the export reports two, and one of another type (DDD00072).
/// </para>
/// </summary>
[ApplicationMark]
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class TenancyOperatorsAttribute : Attribute;
