using DDDToolkit.Interfaces;

namespace DDDToolkit.HotChocolate.Types;
[InterfaceType<IDomainEvent>]
public static partial class DomainEventInterfaceType
{
    static partial void Configure(IInterfaceTypeDescriptor<IDomainEvent> descriptor)
    {
        descriptor.Name("DomainEvent");
        descriptor.Field("eventType").Type<NonNullType<StringType>>().Resolve(context =>
            context.Parent<IDomainEvent>().GetType().Name);
    }
}
