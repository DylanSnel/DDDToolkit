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
    public const string KeyPartAttribute = AttributesNamespace + ".KeyPartAttribute";

    /// <summary>Assembly attribute that declares the assembly a module.</summary>
    public const string ModuleAttribute = AttributesNamespace + ".ModuleAttribute";

    /// <summary>Type attribute that puts a type in its module's published contract.</summary>
    public const string ModuleContractAttribute = AttributesNamespace + ".ModuleContractAttribute";

    /// <summary>Type attribute that names a published message. A published message is part of the contract too.</summary>
    public const string IntegrationEventAttribute = AttributesNamespace + ".IntegrationEventAttribute";

    /// <summary>A rule about who may do what with an aggregate's rows: <c>[RowAccess&lt;TAggregate&gt;]</c>.</summary>
    public const string RowAccessAttribute = AttributesNamespace + ".RowAccessAttribute`1";

    /// <summary>A question rules ask that reads an aggregate's entities, made one SQL function: <c>[AccessFunction&lt;TAggregate&gt;]</c>.</summary>
    public const string AccessFunctionAttribute = AttributesNamespace + ".AccessFunctionAttribute`1";

    /// <summary>An access function published in a module's contracts, asked by key: <c>[AccessFunctionContract&lt;TKey&gt;]</c>.</summary>
    public const string AccessFunctionContractAttribute = AttributesNamespace + ".AccessFunctionContractAttribute`1";

    /// <summary>Who is asking, as a row access rule sees them.</summary>
    public const string Caller = "DDDToolkit.Abstractions.Access.Caller";

    /// <summary>SQL written into a row access rule as it is: <c>Sql.Call</c> and <c>Sql.Raw</c>.</summary>
    public const string SqlEscape = "DDDToolkit.Abstractions.Access.Sql";

    /// <summary>The constant the core generator writes into a row access rule: its SQL, columns still to fill in.</summary>
    public const string RowAccessSqlField = "RowAccessSql";

    /// <summary>Namespace of the invariant interface, matched by name like everything else here.</summary>
    public const string InvariantsNamespace = "DDDToolkit.Invariants";

    /// <summary>Simple name of the interface one rule implements. It is generic, so the arity is checked separately.</summary>
    public const string InvariantInterfaceName = "IInvariant";

    public const string GraphQLTypeAttribute = "DDDToolkit.HotChocolate.Attributes.GraphQLTypeAttribute`1";

    /// <summary>Entity Framework's attribute that names the backing field of a property (assembly Microsoft.EntityFrameworkCore.Abstractions).</summary>
    public const string EfBackingFieldAttribute = "Microsoft.EntityFrameworkCore.BackingFieldAttribute";
    public const string EfBackingFieldAttributeUsage = "global::Microsoft.EntityFrameworkCore.BackingField";

    /// <summary>The marker on a design-time factory whose migrations the build exports for Supabase.</summary>
    public const string SupabaseNamespace = "DDDToolkit.EntityFramework.Supabase";
    public const string SupabaseMigrationsAttributeName = "SupabaseMigrationsAttribute";

    /// <summary>The package a module references when it has a marked factory; only those assemblies are searched.</summary>
    public const string SupabaseAssemblyName = "DDDToolkit.EntityFramework.Supabase";

    /// <summary>Where <c>RowAccessRule</c> lives, which the host's generated list of rules builds.</summary>
    public const string PostgresNamespace = "DDDToolkit.EntityFramework.Postgres";

    /// <summary>Entity Framework's design-time factory (assembly Microsoft.EntityFrameworkCore).</summary>
    public const string DesignTimeDbContextFactory = "Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory`1";

    public const string StjJsonConverterAttribute = "System.Text.Json.Serialization.JsonConverterAttribute";

    /// <summary>HotChocolate's own opt-out, for members its convention-based binding would publish.</summary>
    public const string GraphQLIgnoreAttribute = "HotChocolate.GraphQLIgnoreAttribute";
    public const string StjJsonConstructorAttribute = "System.Text.Json.Serialization.JsonConstructorAttribute";
    public const string IParsable = "System.IParsable`1";
    public const string ReadOnlySet = "System.Collections.ObjectModel.ReadOnlySet`1";

    // Fully qualified names used in generated code.
    public const string BaseTypesNamespace = "global::DDDToolkit.BaseTypes";
    public const string InterfacesNamespace = "global::DDDToolkit.Abstractions.Interfaces";
    public const string HasKeyPartsInterface = "global::DDDToolkit.Interfaces.IHasKeyParts";
    public const string ValidationNamespace = "global::DDDToolkit.Validation";
    public const string InvariantInterface = "global::DDDToolkit.Invariants.IInvariant";
    public const string InvariantViolation = "global::DDDToolkit.Invariants.InvariantViolation";
    public const string InvariantViolationException = "global::DDDToolkit.Exceptions.InvariantViolationException";
    public const string InternalAttributeUsage = "[global::DDDToolkit.Abstractions.Attributes.Internal]";
}
