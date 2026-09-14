using DDDToolkit.Interfaces;

namespace DDDToolkit.HotChocolate.Types;

/// <summary>
/// The GraphQL interface every domain event implements, printed as <c>interface DomainEvent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Fields: <c>eventId</c> (the stable identity of the occurrence, for idempotent handling),
/// <c>occurredAt</c> (when it happened, UTC) and <c>eventType</c> — the runtime type name, resolved
/// per object so a client can discriminate without a fragment per concrete type.
/// </para>
/// <para>
/// Registered by <see cref="DDDToolkit.HotChocolate.DependencyInjection.AddDDDToolkitTypes"/>.
/// Declaring it as a static
/// partial class with <c>[InterfaceType&lt;T&gt;]</c> lets HotChocolate.Types.Analyzers emit the
/// registration; that pattern is unchanged in HotChocolate 16.
/// </para>
/// </remarks>
[InterfaceType<IDomainEvent>]
public static partial class DomainEventInterfaceType
{
    static partial void Configure(IInterfaceTypeDescriptor<IDomainEvent> descriptor)
    {
        descriptor.Name("DomainEvent");
        descriptor.Description("Something that happened in the domain.");

        descriptor.Field(e => e.EventId)
            .Name("eventId")
            .Type<NonNullType<UuidType>>()
            .Description("Unique, stable identifier of this occurrence. Use it for idempotent handling.");

        descriptor.Field(e => e.OccurredAt)
            .Name("occurredAt")
            .Type<NonNullType<DateTimeType>>()
            .Description("When the event occurred (UTC).");

        descriptor.Field("eventType")
            .Type<NonNullType<StringType>>()
            .Description("The runtime type name of the event.")
            .Resolve(context => context.Parent<IDomainEvent>().GetType().Name);
    }
}
