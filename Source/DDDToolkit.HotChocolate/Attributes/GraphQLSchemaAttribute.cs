using HotChocolate.Language;

namespace DDDToolkit.HotChocolate.Attributes;

/// <summary>
/// Says that the fields of a class belong to one GraphQL schema, named <see cref="Name"/>, and to no other: an
/// administration schema beside the one every user is offered, for instance.
/// </summary>
/// <remarks>
/// <para>
/// HotChocolate's own generator registers everything a project marks, its <c>[Query]</c> methods, its
/// <c>[QueryType]</c> and <c>[ObjectType&lt;T&gt;]</c> classes, in one method per project, and that method goes into
/// every schema the project is added to. So a field meant for one schema cannot be one HotChocolate's generator
/// finds. A class marked with this attribute is not: its public static methods carry no <c>[Query]</c> or
/// <c>[Mutation]</c>, the attribute says which operation type they are fields of, and the toolkit's generator
/// registers them in the bindings it writes for the module, <c>Add{Module}GraphQlRuntimeBindings()</c>, for the
/// schema whose name the builder has, and for no other. Every schema calls the bindings already, so a class marked
/// for one asks nothing more of the host.
/// </para>
/// <code>
/// [GraphQLSchema("admin", OperationType.Query)]
/// internal static class SeatsAdminQueries
/// {
///     public static Task&lt;IReadOnlyList&lt;SeatGrant&gt;&gt; GetSeatGrantsAsync(SeatId seat, [Service] ISender sender, CancellationToken cancellationToken)
///         =&gt; sender.Send(new SeatGrants(seat), cancellationToken).AsTask();
/// }
///
/// services.AddGraphQLServer("admin").AddTenantsGraphQlRuntimeBindings().AddTenantsTypes();   // has seatGrants
/// services.AddGraphQLServer("user").AddTenantsGraphQlRuntimeBindings().AddTenantsTypes();    // has not
/// </code>
/// <para>
/// A class without the attribute is in every schema its project is added to, as it always was: the types, and the
/// fields every caller is offered, need no mark. Only what one schema has and the others have not is marked.
/// A class that belongs to two schemas carries the attribute twice. What is marked are fields of the operation
/// types, <c>Query</c>, <c>Mutation</c> and <c>Subscription</c>: a field of one schema on a type every schema shows
/// is registered by hand, in the schema that has it.
/// </para>
/// <para>
/// Each method is bound the way HotChocolate binds a <c>[Query]</c> method its generator finds: by reflection over
/// the method, when the schema is built, so a parameter marked <c>[Service]</c>, a <c>CancellationToken</c> and the
/// attributes HotChocolate reads off a method work as they do there. The class carries nothing HotChocolate's
/// generator registers, so no <c>[QueryType]</c>, no <c>[ExtendObjectType]</c> and no method marked <c>[Query]</c>,
/// <c>[Mutation]</c> or <c>[Subscription]</c>: DDD00062 says so where it does, and where a class has no field, an
/// instance method, or two methods that would be one field. A method marked <c>[GraphQLIgnore]</c> or
/// <c>[DataLoader]</c> is no field, and neither is the stream a subscription names with
/// <c>[Subscribe(With = ...)]</c>.
/// </para>
/// <para>
/// The name is the one the schema is registered under, <c>AddGraphQLServer("admin")</c>; HotChocolate's default
/// schema, <c>AddGraphQLServer()</c>, is called <c>_Default</c>. It is a name the classes and the host agree on, as
/// the name of a Fusion source schema is.
/// </para>
/// </remarks>
/// <param name="name">The name of the schema the class belongs to.</param>
/// <param name="operation">The operation type its public static methods are fields of.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class GraphQLSchemaAttribute(string name, OperationType operation) : Attribute
{
    /// <summary>The name of the schema the class belongs to, as the schema is registered.</summary>
    public string Name { get; } = name;

    /// <summary>The operation type the class's public static methods are fields of.</summary>
    public OperationType Operation { get; } = operation;
}
