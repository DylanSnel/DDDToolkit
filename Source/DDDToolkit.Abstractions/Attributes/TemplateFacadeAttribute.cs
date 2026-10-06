namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Gives an application one name for a generic class this assembly ships, closed over the application's own
/// classes, so no project of the application writes the class's type arguments. A package whose use cases are
/// nested in one class generic over the application's classes declares
/// <code>
/// [assembly: TemplateFacade(typeof(TenancyUseCases&lt;,,,,,,,,&gt;), "{Module}Tenancy")]
///
/// public abstract partial class TenancyUseCases&lt;
///     [TemplateType(typeof(TenantAggregateAttribute&lt;&gt;), Take = TemplateArgumentKind.Type)] TTenant,
///     [TemplateType(typeof(TenantAggregateAttribute&lt;&gt;))] TTenantId, ...&gt;
/// </code>
/// and the project that declares the classes of the module Shop with those templates gets
/// <code>
/// public abstract class ShopTenancy : global::Acme.Tenancy.TenancyUseCases&lt;global::Shop.Domain.ShopTenant, global::Shop.Contracts.TenantId, ...&gt;
/// {
///     private ShopTenancy() { }
/// }
/// </code>
/// in the global namespace, so every project that references it, the module's application and API projects and the
/// host among them, takes a <c>ShopTenancy.SeatCommands</c> and answers a <c>ShopTenancy.SeatOverview</c>.
/// <para>
/// <b>A name, not a second type.</b> C# finds a type nested in a class through every class that derives from it,
/// so <c>ShopTenancy.SeatOverview</c> is the package's own <c>TenancyUseCases&lt;...&gt;.SeatOverview</c>: what the
/// container registered, what reflection sees and what the compiler reports, with the package's documentation.
/// The class itself is never made, and nothing derives from it. C# finds a static member the same way, so what is
/// generic over the classes and is called rather than named goes on the same class: Tenancy's system work is
/// <c>ShopTenancy.BeginSystem()</c>, a call the compiler binds even in the project the class is written into.
/// </para>
/// <para>
/// <b>Why a class, and not a global alias.</b> An alias holds in the project that declares it and no further, so it
/// would have to be written into every project, and a generator's alias is seen by the compiler but not by the
/// other generators of the project: HotChocolate's, reading <c>[ObjectType&lt;ShopTenancy.SeatOverview&gt;]</c>,
/// would not know the name. A class is written once, where the classes are declared, and reaches every project
/// above it, and the generators there, through the reference. In the project that declares the classes the class
/// is a generator's output too, so the other generators there do not see it: a module of one project that names the
/// records in another library's attributes or in a GraphQL resolver keeps an alias of exactly that name there,
/// <c>global using ShopTenancy = ...;</c>, and gets no class.
/// </para>
/// <para>
/// <b>Which classes.</b> A project gets the class when it declares a class with one of the type's templates. Each
/// type parameter is filled as a <see cref="TemplateTypeAttribute"/> fills one of a registration: with the class of
/// its template the project declares, or, when it declares none, the one the projects of its module that it
/// references declare. A template with no class or several leaves the project without one, as does a class that
/// does not meet the type's constraints; the project hears why, DDD00065, as information, since a module whose
/// classes are split over two projects gets the class in the second.
/// </para>
/// <para>
/// <b>The application may name it otherwise.</b> The project that declares the classes declares this attribute too,
/// for the same type, and its name stands instead of the package's: a module called Tenancy, which would get
/// <c>TenancyTenancy</c>, writes
/// <code>
/// [assembly: TemplateFacade(typeof(TenancyUseCases&lt;,,,,,,,,&gt;), "ShopTenancy")]
/// </code>
/// A package asks for a class of the types it declares itself; the attribute another project of the application
/// declares is that project's alone.
/// </para>
/// <para>
/// <b>What the application wrote stays.</b> A project that has a type or a namespace of the name in the global
/// namespace, its own or one it references, or an alias of the name at the top of one of its files, gets nothing.
/// A type of the name the project declares in a namespace keeps the class out as well, and DDD00065 says so on that
/// type: the class would hide it in every file that imports its namespace. A project above the one that declares
/// the classes is not looked at: a type of the name it imports with a using is hidden by the class there, so it
/// qualifies that type, or the application names the class otherwise.
/// </para>
/// </summary>
/// <param name="type">
/// The open generic class, <c>typeof(TenancyUseCases&lt;,,,,,,,,&gt;)</c>, not nested in a generic type. Every one
/// of its type parameters carries <see cref="TemplateTypeAttribute"/>, since the class the application gets leaves
/// none open, and it is abstract rather than static, with a protected constructor, so that class can derive from it.
/// </param>
/// <param name="name">
/// What the application's class is called: a name in which <c>{Module}</c> stands for the module, named as the
/// generators name it everywhere: after <see cref="ModuleAttribute"/>, otherwise <c>DDD_Module</c>, otherwise the
/// assembly without the dots, so <c>order-management</c> is <c>OrderManagement</c>.
/// </param>
/// <remarks>
/// A type this attribute names, or a name it gives, that the generator cannot use is passed over without a word:
/// that is the package's mistake, and its own tests of a project that names the class are what show it.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class TemplateFacadeAttribute(Type type, string name) : Attribute
{
    /// <summary>The open generic class the application's class derives from, closed over its classes.</summary>
    public Type Type { get; } = type;

    /// <summary>What the application's class is called, with <c>{Module}</c> for the module the classes are declared in.</summary>
    public string Name { get; } = name;
}
