
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

    /// <summary>
    /// The id that makes a new one of itself, <c>TId.Create()</c>: what the generator implements for an id over a
    /// <c>Guid</c>, and for one that declares its own <c>Create()</c>. Only the .NET 10 build of the abstractions has it.
    /// </summary>
    public const string CreatableEntityIdInterface = "DDDToolkit.Abstractions.Interfaces.ICreatableEntityId`1";

    public const string SingleValueObjectAttribute = AttributesNamespace + ".SingleValueObjectAttribute`1";
    public const string ValueObjectAttribute = AttributesNamespace + ".ValueObjectAttribute";
    public const string EntityAttribute = AttributesNamespace + ".EntityAttribute`1";
    public const string AggregateRootAttribute = AttributesNamespace + ".AggregateRootAttribute`1";
    public const string InternalAttribute = AttributesNamespace + ".InternalAttribute";
    public const string DontCompareAttribute = AttributesNamespace + ".DontCompareAttribute";
    public const string KeyPartAttribute = AttributesNamespace + ".KeyPartAttribute";
    /// <summary>The property of an aggregate root that names its owner: <c>[Owner]</c>.</summary>
    public const string OwnerAttribute = AttributesNamespace + ".OwnerAttribute";

    /// <summary>An abstract generic parent a package ships for aggregate roots declared elsewhere: <c>[AggregateRootBase]</c>.</summary>
    public const string AggregateRootBaseAttribute = AttributesNamespace + ".AggregateRootBaseAttribute";

    /// <summary>An abstract generic parent a package ships for child entities declared elsewhere: <c>[EntityBase]</c>.</summary>
    public const string EntityBaseAttribute = AttributesNamespace + ".EntityBaseAttribute";

    /// <summary>On a package's attribute class: a class declared with it is an aggregate root deriving from the named parent.</summary>
    public const string AggregateRootTemplateAttribute = AttributesNamespace + ".AggregateRootTemplateAttribute";

    /// <summary>On a package's attribute class: a class declared with it is a child entity deriving from the named parent.</summary>
    public const string EntityTemplateAttribute = AttributesNamespace + ".EntityTemplateAttribute";

    /// <summary>On a template attribute class: fills one type parameter of the parent from another template class of the project.</summary>
    public const string TemplateArgumentAttribute = AttributesNamespace + ".TemplateArgumentAttribute";

    /// <summary>On a package's generic registration method: the application gets it closed over its template classes.</summary>
    public const string TemplateRegistrationAttribute = AttributesNamespace + ".TemplateRegistrationAttribute";

    /// <summary>On a type parameter of a registration method: filled from the class declared with a template.</summary>
    public const string TemplateTypeAttribute = AttributesNamespace + ".TemplateTypeAttribute";

    /// <summary>Assembly attribute naming a type that declares registration methods, so a referencing project finds them cheaply.</summary>
    public const string TemplateRegistrationsAttribute = AttributesNamespace + ".TemplateRegistrationsAttribute";

    /// <summary>Assembly attribute naming a generic class the project that declares the application's template classes gets a class of its own of, closed over them.</summary>
    public const string TemplateFacadeAttribute = AttributesNamespace + ".TemplateFacadeAttribute";

    /// <summary>Assembly attribute of the application's, giving the class a <see cref="TemplateFacadeAttribute"/> asks for a name of its own.</summary>
    public const string TemplateFacadeNameAttribute = AttributesNamespace + ".TemplateFacadeNameAttribute";

    /// <summary>Marks a package's assembly attribute as the switch that has the generator write the classes of the templates it names, and their ids, where a project leaves them out.</summary>
    public const string TemplateDefaultsAttribute = AttributesNamespace + ".TemplateDefaultsAttribute";

    /// <summary>Marks a package's generic assembly attribute as the line that says which of a project's ids are the ids of the package's classes.</summary>
    public const string TemplateIdsAttribute = AttributesNamespace + ".TemplateIdsAttribute";

    /// <summary>Assembly attribute that declares the assembly a module.</summary>
    public const string ModuleAttribute = AttributesNamespace + ".ModuleAttribute";

    /// <summary>Type attribute that puts a type in its module's published contract.</summary>
    public const string ModuleContractAttribute = AttributesNamespace + ".ModuleContractAttribute";

    /// <summary>Assembly attribute that puts every public type of the assembly in its module's published contract.</summary>
    public const string ModuleContractsAttribute = AttributesNamespace + ".ModuleContractsAttribute";

    /// <summary>Type attribute that names a published message. A published message is part of the contract too.</summary>
    public const string IntegrationEventAttribute = AttributesNamespace + ".IntegrationEventAttribute";

    /// <summary>A rule about who may do what with an aggregate's rows: <c>[RowAccess&lt;TAggregate&gt;]</c>.</summary>
    public const string RowAccessAttribute = AttributesNamespace + ".RowAccessAttribute`1";

    /// <summary>A question rules ask that reads an aggregate's entities, made one SQL function: <c>[AccessFunction&lt;TAggregate&gt;]</c>.</summary>
    public const string AccessFunctionAttribute = AttributesNamespace + ".AccessFunctionAttribute`1";

    /// <summary>An access function published in a module's contracts, asked by key: <c>[AccessFunctionContract&lt;TKey&gt;]</c>.</summary>
    public const string AccessFunctionContractAttribute = AttributesNamespace + ".AccessFunctionContractAttribute`1";

    /// <summary>
    /// A set the access to a resource answers, published in a module's contracts and asked by the resource's id:
    /// <c>[ResourceAccessContract&lt;TKey&gt;]</c>.
    /// </summary>
    public const string ResourceAccessContractAttribute = AttributesNamespace + ".ResourceAccessContractAttribute`1";

    /// <summary>A class of questions only the database answers, and the owner of the functions it names: <c>[AccessFunctions]</c>.</summary>
    public const string AccessFunctionsAttribute = AttributesNamespace + ".AccessFunctionsAttribute";

    /// <summary>A set-shaped question of an <c>[AccessFunctions]</c> class: <c>[AccessSet("name")]</c>.</summary>
    public const string AccessSetAttribute = AttributesNamespace + ".AccessSetAttribute";

    /// <summary>A question of an <c>[AccessFunctions]</c> class answered with one value: <c>[AccessScalar("name")]</c>.</summary>
    public const string AccessScalarAttribute = AttributesNamespace + ".AccessScalarAttribute";

    /// <summary>A question of an <c>[AccessFunctions]</c> class answered with one row of a mapped table: <c>[AccessRow("name")]</c>.</summary>
    public const string AccessRowAttribute = AttributesNamespace + ".AccessRowAttribute";

    /// <summary>What a set-shaped question answers with, which a rule asks <c>Contains</c> of.</summary>
    public const string AccessSet = "DDDToolkit.Abstractions.Access.AccessSet`1";

    /// <summary>Who is asking, as a row access rule sees them.</summary>
    public const string CurrentUser = "DDDToolkit.Abstractions.Access.ICurrentUser";

    /// <summary>SQL written into a row access rule as it is: <c>Sql.Call</c> and <c>Sql.Raw</c>.</summary>
    public const string SqlEscape = "DDDToolkit.Abstractions.Access.Sql";

    /// <summary>The constant the core generator writes into a row access rule: its SQL, columns still to fill in.</summary>
    public const string RowAccessSqlField = "RowAccessSql";

    /// <summary>
    /// What a column rule's <see cref="RowAccessSqlField"/> starts with. An export that does not know column rules
    /// stops at it, as at any placeholder it does not know, rather than write the rule as a policy for the whole
    /// row, which would let whoever it allows change every column.
    /// </summary>
    public const string ColumnRuleSqlMarker = "{columns}";

    /// <summary>
    /// What a restrictive rule's <see cref="RowAccessSqlField"/> starts with. An export that does not know
    /// restrictive rules stops at it, as at any placeholder it does not know, rather than write a rule meant to
    /// narrow as a policy that allows more.
    /// </summary>
    public const string RestrictiveRuleSqlMarker = "{restrictive}";

    /// <summary>On a module's request interface: the generator writes the pipeline behavior that holds its requests to what they declare.</summary>
    public const string AccessRequestsAttribute = AttributesNamespace + ".AccessRequestsAttribute";

    /// <summary>What a request implements to say what it requires of its caller; a module's request interface derives from it.</summary>
    public const string RequireAccessInterface = "DDDToolkit.Access.IRequireAccess";

    /// <summary>The access checks of one module, closed over its request interface: what a generated behavior asks.</summary>
    public const string AccessChecksUsage = "global::DDDToolkit.Access.AccessChecks";

    /// <summary>
    /// On an attribute a package ships: it states a request's requirement, and names the static call that makes it.
    /// The core's <c>[SignedIn]</c>, Tenancy's <c>[InTenant]</c> and Membership's <c>[SeenWith&lt;T&gt;]</c> are marked so.
    /// </summary>
    public const string StatesRequirementAttribute = "DDDToolkit.Access.StatesRequirementAttribute";

    /// <summary>On a request: the key its caller holds, where a marker on one of its members says, or for the whole tenant.</summary>
    public const string RequiresPermissionAttribute = "DDDToolkit.Access.RequiresPermissionAttribute";

    /// <summary>On an attribute a package ships: it marks where the key of <c>[RequiresPermission]</c> is held, and names the call.</summary>
    public const string PermissionMarkerAttribute = "DDDToolkit.Access.PermissionMarkerAttribute";

    /// <summary>On an attribute a package ships: it marks one more argument of a key marker's call, such as the version read.</summary>
    public const string PermissionMarkerArgumentAttribute = "DDDToolkit.Access.PermissionMarkerArgumentAttribute";

    /// <summary>On an attribute a package ships: it marks one more argument of a requirement attribute's call, such as the tenant an operator's command acts in.</summary>
    public const string RequirementArgumentAttribute = "DDDToolkit.Access.RequirementArgumentAttribute";

    /// <summary>On a package's assembly: what <c>[RequiresPermission]</c> means on a request that marks no member.</summary>
    public const string PermissionWithoutMarkerAttribute = "DDDToolkit.Access.PermissionWithoutMarkerAttribute";

    /// <summary>On a request: anyone may send it. The core's, which goes beside no other requirement attribute.</summary>
    public const string AllowAnonymousAttribute = "DDDToolkit.Access.AllowAnonymousAttribute";

    /// <summary>On a request: it is about the caller's own alone, the member marked <c>[Own]</c>.</summary>
    public const string OwnOnlyAttribute = "DDDToolkit.Access.OwnOnlyAttribute";

    /// <summary>On a member of an <c>[OwnOnly]</c> request: the value compared with the caller's own.</summary>
    public const string OwnAttribute = "DDDToolkit.Access.OwnAttribute";

    /// <summary>On a member of a request that requires a key: the caller's own, which the key is the alternative of.</summary>
    public const string OrOwnAttribute = "DDDToolkit.Access.OrOwnAttribute";

    /// <summary>On a package's assembly: a type its handle says the caller's own of, or a template whose classes' ids it is.</summary>
    public const string CallersOwnAttribute = "DDDToolkit.Access.CallersOwnAttribute";

    /// <summary>What says the caller's own value of a type: an application's handle is found by it.</summary>
    public const string CallersOwnInterface = "DDDToolkit.Access.ICallersOwn`1";

    /// <summary>The core's requirement, whose static methods combine cases and make the caller's own.</summary>
    public const string AccessRequirementUsage = "global::DDDToolkit.Access.AccessRequirement";

    /// <summary>What every command of the Mediator library implements: a requirement for queries only is not stated on one.</summary>
    public const string MediatorBaseCommand = "Mediator.IBaseCommand";

    /// <summary>What every command of the Mediator library answered with a stream implements.</summary>
    public const string MediatorBaseStreamCommand = "Mediator.IBaseStreamCommand";

    /// <summary>What every query of the Mediator library implements: the one kind of its messages a requirement for queries only is stated on.</summary>
    public const string MediatorBaseQuery = "Mediator.IBaseQuery";

    /// <summary>What every query of the Mediator library answered with a stream implements.</summary>
    public const string MediatorBaseStreamQuery = "Mediator.IBaseStreamQuery";

    /// <summary>What every message of the Mediator library answered once implements: a command, a query, a plain request.</summary>
    public const string MediatorMessage = "Mediator.IMessage";

    /// <summary>What every message of the Mediator library answered with a stream implements.</summary>
    public const string MediatorStreamMessage = "Mediator.IStreamMessage";

    /// <summary>Where the set of a module's access checks is registered, which a generated behavior's registration calls.</summary>
    public const string AccessCheckRegistration = "DDDToolkit.Access.AccessCheckServiceCollectionExtensions";

    /// <summary>
    /// The assembly attribute that says which behavior asks the checks of a request interface, which the start-up
    /// check that the behavior is in the pipeline reads. Written beside a behavior where the project can see it.
    /// </summary>
    public const string AccessBehaviorAttribute = "DDDToolkit.Access.AccessBehaviorAttribute";

    /// <summary>The assembly attribute that says the id of an aggregate whose owner a project's requests ask about.</summary>
    public const string AsksOwnerOfAttribute = "DDDToolkit.Access.AsksOwnerOfAttribute";

    /// <summary>The core's requirement record, whose <c>OwnerOf</c> a request's line or a project's own code calls.</summary>
    public const string AccessRequirement = "DDDToolkit.Access.AccessRequirement";

    /// <summary>
    /// The pipeline behavior of the Mediator library (assembly Mediator.Abstractions). A project that can see it
    /// uses the library, and gets a behavior written for each of its <c>[AccessRequests]</c> interfaces.
    /// </summary>
    public const string MediatorPipelineBehavior = "Mediator.IPipelineBehavior`2";

    /// <summary>
    /// The Mediator library's pipeline behavior for the messages that are answered with a stream, which pass no
    /// <see cref="MediatorPipelineBehavior"/>. Where the library has it, an <c>[AccessRequests]</c> interface
    /// gets a second behavior, so a stream query of the module is held to what it declares as well.
    /// </summary>
    public const string MediatorStreamPipelineBehavior = "Mediator.IStreamPipelineBehavior`2";

    /// <summary>
    /// The Mediator library's notification, which is published to its handlers through neither pipeline: a
    /// notification that implements an <c>[AccessRequests]</c> interface is asked about by nothing.
    /// </summary>
    public const string MediatorNotification = "Mediator.INotification";

    /// <summary>
    /// The Mediator library's sender, through which a command or a query passes the pipeline on its way to its handler.
    /// A project that can see it uses the library, and a handler of its called directly is DDD00061.
    /// </summary>
    public const string MediatorSender = "Mediator.ISender";

    /// <summary>The service collection a generated registration extends (assembly Microsoft.Extensions.DependencyInjection.Abstractions).</summary>
    public const string ServiceCollection = "Microsoft.Extensions.DependencyInjection.IServiceCollection";

    /// <summary>Where <c>TryAddEnumerable</c> lives, which a generated registration adds a behavior with.</summary>
    public const string ServiceCollectionDescriptorExtensions = "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions";

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

    /// <summary>
    /// A package's declaration that it contributes row level security of its own, which every application that
    /// references it writes into its migrations: <c>[assembly: RowAccessContribution(typeof(X))]</c>.
    /// </summary>
    public const string RowAccessContributionAttribute = AttributesNamespace + ".RowAccessContributionAttribute";

    /// <summary>A host's own contribution, or a module's, written into its migrations: <c>[assembly: UseRowAccessContribution(typeof(X))]</c>.</summary>
    public const string UseRowAccessContributionAttribute = AttributesNamespace + ".UseRowAccessContributionAttribute";

    /// <summary>A host's choice not to write a package's contribution, or not for one context: <c>[assembly: LeaveOutRowAccessContribution(typeof(X))]</c>.</summary>
    public const string LeaveOutRowAccessContributionAttribute = AttributesNamespace + ".LeaveOutRowAccessContributionAttribute";

    /// <summary>Where a constructor parameter of a package's contribution comes from: <c>[FromApplication(typeof(TMarker))]</c>.</summary>
    public const string FromApplicationAttribute = AttributesNamespace + ".FromApplicationAttribute";

    /// <summary>What a row access contribution implements.</summary>
    public const string RowAccessContributionInterface = PostgresNamespace + ".IRowAccessContribution";

    /// <summary>What the class the Supabase build writes for a package's contribution implements, which the export names the package's class by.</summary>
    public const string PackageRowAccessContributionInterface = PostgresNamespace + ".IPackageRowAccessContribution";

    /// <summary>The attribute on an attribute an application marks a member with for a package's contribution: <c>[ApplicationMark]</c>.</summary>
    public const string ApplicationMarkAttribute = AttributesNamespace + ".ApplicationMarkAttribute";

    /// <summary>Entity Framework's context, the only thing a contribution can be left out of (assembly Microsoft.EntityFrameworkCore).</summary>
    public const string DbContext = "Microsoft.EntityFrameworkCore.DbContext";

    /// <summary>Entity Framework's design-time factory (assembly Microsoft.EntityFrameworkCore).</summary>
    public const string DesignTimeDbContextFactory = "Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory`1";

    public const string StjJsonConverterAttribute = "System.Text.Json.Serialization.JsonConverterAttribute";

    /// <summary>HotChocolate's own opt-out, for members its convention-based binding would publish.</summary>
    public const string GraphQLIgnoreAttribute = "HotChocolate.GraphQLIgnoreAttribute";
    public const string StjJsonConstructorAttribute = "System.Text.Json.Serialization.JsonConstructorAttribute";
    public const string IParsable = "System.IParsable`1";

    /// <summary>
    /// What every generated id, single value object and always-valid twin implements when the project can see it:
    /// <c>DDDToolkit.Interfaces.ISingleValue&lt;TSelf, TValue&gt;</c>, the way back from a stored value that needs no
    /// Entity Framework in the declaring project.
    /// </summary>
    public const string SingleValueInterface = "DDDToolkit.Interfaces.ISingleValue`2";
    public const string ReadOnlySet = "System.Collections.ObjectModel.ReadOnlySet`1";

    // Fully qualified names used in generated code.
    public const string BaseTypesNamespace = "global::DDDToolkit.BaseTypes";
    public const string InterfacesNamespace = "global::DDDToolkit.Abstractions.Interfaces";
    public const string HasKeyPartsInterface = "global::DDDToolkit.Interfaces.IHasKeyParts";
    public const string SingleValueInterfaceUsage = "global::DDDToolkit.Interfaces.ISingleValue";
    public const string ValidationNamespace = "global::DDDToolkit.Validation";
    public const string InvariantInterface = "global::DDDToolkit.Invariants.IInvariant";
    public const string InvariantViolation = "global::DDDToolkit.Invariants.InvariantViolation";
    public const string InvariantViolationException = "global::DDDToolkit.Exceptions.InvariantViolationException";
    public const string InternalAttributeUsage = "[global::DDDToolkit.Abstractions.Attributes.Internal]";
}
