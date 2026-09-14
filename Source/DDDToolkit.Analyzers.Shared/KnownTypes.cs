namespace DDDToolkit.Analyzers.Common;

/// <summary>
/// Metadata names of the types the generators look for. The generators never reference the
/// assemblies that declare these types; everything is matched by name so the generator DLLs
/// have no run-time dependencies.
/// </summary>
internal static class KnownTypes
{
    public const string AttributesNamespace = "DDDToolkit.Abstractions.Attributes";

    public const string EntityIdAttribute = AttributesNamespace + ".EntityIdAttribute`1";

    /// <summary>Simple name of the entity id attribute, for matching it on a type the generator did not start from.</summary>
    public const string EntityIdAttributeName = "EntityIdAttribute";

    /// <summary>The marker interface every strongly typed id implements (metadata name of the non-generic one).</summary>
    public const string EntityIdInterface = "DDDToolkit.Abstractions.Interfaces.IEntityId";

    public const string SingleValueObjectAttribute = AttributesNamespace + ".SingleValueObjectAttribute`1";
    public const string ValueObjectAttribute = AttributesNamespace + ".ValueObjectAttribute";
    public const string EntityAttribute = AttributesNamespace + ".EntityAttribute`1";
    public const string AggregateRootAttribute = AttributesNamespace + ".AggregateRootAttribute`1";
    public const string InternalAttribute = AttributesNamespace + ".InternalAttribute";
    public const string DontCompareAttribute = AttributesNamespace + ".DontCompareAttribute";

    /// <summary>Assembly attribute that declares the assembly a module.</summary>
    public const string ModuleAttribute = AttributesNamespace + ".ModuleAttribute";

    /// <summary>Type attribute that puts a type in its module's published contract.</summary>
    public const string ModuleContractAttribute = AttributesNamespace + ".ModuleContractAttribute";

    /// <summary>Type attribute that names a published message. A published message is part of the contract too.</summary>
    public const string IntegrationEventAttribute = AttributesNamespace + ".IntegrationEventAttribute";

    /// <summary>Namespace of the invariant interface, matched by name like everything else here.</summary>
    public const string InvariantsNamespace = "DDDToolkit.Invariants";

    /// <summary>Simple name of the interface one rule implements. It is generic, so the arity is checked separately.</summary>
    public const string InvariantInterfaceName = "IInvariant";

    public const string GraphQLTypeAttribute = "DDDToolkit.HotChocolate.Attributes.GraphQLTypeAttribute`1";

    /// <summary>Entity Framework's attribute that names the backing field of a property (assembly Microsoft.EntityFrameworkCore.Abstractions).</summary>
    public const string EfBackingFieldAttribute = "Microsoft.EntityFrameworkCore.BackingFieldAttribute";
    public const string EfBackingFieldAttributeUsage = "global::Microsoft.EntityFrameworkCore.BackingField";

    public const string StjJsonConverterAttribute = "System.Text.Json.Serialization.JsonConverterAttribute";
    public const string StjJsonConstructorAttribute = "System.Text.Json.Serialization.JsonConstructorAttribute";
    public const string IParsable = "System.IParsable`1";
    public const string ReadOnlySet = "System.Collections.ObjectModel.ReadOnlySet`1";

    // Fully qualified names used in generated code.
    public const string BaseTypesNamespace = "global::DDDToolkit.BaseTypes";
    public const string InterfacesNamespace = "global::DDDToolkit.Abstractions.Interfaces";
    public const string ValidationNamespace = "global::DDDToolkit.Validation";
    public const string InvariantInterface = "global::DDDToolkit.Invariants.IInvariant";
    public const string InvariantViolation = "global::DDDToolkit.Invariants.InvariantViolation";
    public const string InvariantViolationException = "global::DDDToolkit.Exceptions.InvariantViolationException";
    public const string InternalAttributeUsage = "[global::DDDToolkit.Abstractions.Attributes.Internal]";
}
