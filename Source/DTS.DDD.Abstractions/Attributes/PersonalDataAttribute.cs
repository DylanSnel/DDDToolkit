namespace DTS.DDD.Abstractions.Attributes;

/// <summary>
/// Marks a property or field that holds data about a person that no reader of the object needs, such as the id of a
/// person's account at the identity provider. A fact about the data, said where the data is declared.
/// <code>
/// public abstract partial class SeatAggregate&lt;TSeatId, TTenantId, TUnitId, TRoleId&gt;
/// {
///     [PersonalData] public Guid Identity { get; private set; }
/// }
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// The member is stored and mapped as any other. Each API layer decides what it does with the mark.
/// <c>DTS.DDD.HotChocolate</c> leaves the member out of a type that binds its fields by convention. A type that
/// lists the member with <c>BindFieldsExplicitly()</c> keeps it, and so does a field with a resolver of its own.
/// </para>
/// <para>
/// Data that a reader needs for the object's purpose is not marked. The address of an invitation is an example.
/// Whoever may read invitations needs it, and the access check of the query decides who that is.
/// </para>
/// <para>
/// ASP.NET Core Identity has an attribute with the same simple name, <c>Microsoft.AspNetCore.Identity.PersonalDataAttribute</c>.
/// A file that imports both namespaces needs an alias for one of them.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true)]
public sealed class PersonalDataAttribute : Attribute;
