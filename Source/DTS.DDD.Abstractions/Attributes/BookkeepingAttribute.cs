namespace DTS.DDD.Abstractions.Attributes;

/// <summary>
/// Marks a stored property or field that a package keeps for its own work, and that is not part of what the class
/// means to the application. Tenancy's role keeps <c>PermissionsFromStarterRole</c> to tell a permission its starter
/// role gained from one the tenant added. A fact about the member, said where the member is declared.
/// <code>
/// public abstract partial class RoleAggregate&lt;TRoleId, TTenantId&gt;
/// {
///     [Bookkeeping] public IReadOnlyList&lt;string&gt;? PermissionsFromStarterRole { get; private set; }
/// }
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// The member is stored and mapped as any other, so <see cref="InternalAttribute"/> does not fit it.
/// <see cref="InternalAttribute"/> marks a member that is never stored or serialized.
/// </para>
/// <para>
/// Each API layer decides what it does with the mark. <c>DTS.DDD.HotChocolate</c> leaves the member out of a type
/// that binds its fields by convention. A type that lists the member with <c>BindFieldsExplicitly()</c> keeps it, and so
/// does a field with a resolver of its own.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true)]
public sealed class BookkeepingAttribute : Attribute;
