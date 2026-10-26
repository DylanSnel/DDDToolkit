namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Says that an attribute is a mark an application puts on a static member for a package's row access contribution:
/// the attribute a constructor parameter of the contribution names with <see cref="FromApplicationAttribute"/>.
/// <code>
/// [ApplicationMark]
/// [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
/// public sealed class PlansAttribute : Attribute;
/// </code>
/// <para>
/// The contribution is made in the project that runs the Supabase export, which finds the marks in the projects it
/// references. Of a library it sees what is public and nothing else, so a member a library marks that is internal,
/// or declared in a type that is, is not there for it at all: the contribution would be made as if nothing were
/// marked, with a parameter's default or not at all, and that project could not say why. With this attribute on
/// the mark, the toolkit's analyzer reports such a member where it is declared, DDD00070. Tenancy's
/// <c>[TenancyCatalogue]</c> and <c>[TenancyOperators]</c> and Membership's <c>[MembershipRules&lt;TMember&gt;]</c>
/// carry it; a package that declares a mark of its own puts it on that.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ApplicationMarkAttribute : Attribute;
