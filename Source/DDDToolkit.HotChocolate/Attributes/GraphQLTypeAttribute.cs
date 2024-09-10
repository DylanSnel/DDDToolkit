namespace DDDToolkit.HotChocolate.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class GraphQLTypeAttribute<TSchemaType> : Attribute where TSchemaType : INamedType
{
}
