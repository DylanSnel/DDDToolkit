using HotChocolate.Types;

namespace DDDToolkit.HotChocolate.Attributes;

/// <summary>
/// Declares which GraphQL schema type a strongly typed id or single value object binds to.
/// </summary>
/// <remarks>
/// <para>
/// Read by the DDDToolkit HotChocolate source generator, not by HotChocolate itself: the generator
/// emits <c>BindRuntimeType&lt;TRuntime, TSchemaType&gt;()</c> for the annotated type into the
/// assembly's <c>Add{Module}GraphQlRuntimeBindings</c> extension. Without it the generator falls back
/// to the scalar that matches the wrapped CLR type (<c>Guid</c> to <c>UuidType</c>, <c>string</c> to
/// <c>StringType</c>, and so on).
/// </para>
/// <para>
/// Use it when the default scalar is not specific enough, for example an e-mail address that should
/// appear in the schema as <c>EmailAddress</c> rather than <c>String</c>:
/// </para>
/// <code>
/// [GraphQLType&lt;EmailAddressType&gt;]
/// [SingleValueObject&lt;string&gt;]
/// public partial record EmailAddress;
/// </code>
/// <para>
/// This is distinct from HotChocolate's own <c>HotChocolate.GraphQLTypeAttribute</c>, which annotates
/// individual members and arguments. The constraint was <c>INamedType</c> before HotChocolate 15;
/// that interface is gone and <see cref="ITypeDefinition"/> replaces it.
/// </para>
/// </remarks>
/// <typeparam name="TSchemaType">The HotChocolate type to bind to, for example <c>UuidType</c>.</typeparam>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class GraphQLTypeAttribute<TSchemaType> : Attribute
    where TSchemaType : ITypeDefinition
{
}
