namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Gives an application one name for a generic class this assembly ships, closed over the application's own
/// classes, so no project of the application writes the class's type arguments. A package whose manager is generic
/// over the application's classes declares
/// <code>
/// [assembly: TemplateFacade(typeof(SeatManager&lt;,,,,,,,,&gt;))]
///
/// public class SeatManager&lt;
///     [TemplateType(typeof(TenantAggregateAttribute&lt;&gt;), Take = TemplateArgumentKind.Type)] TTenant,
///     [TemplateType(typeof(TenantAggregateAttribute&lt;&gt;))] TTenantId, ...&gt;
/// </code>
/// and the project that declares the application's classes with those templates gets
/// <code>
/// public sealed class SeatManager : global::Acme.Tenancy.SeatManager&lt;global::Shop.Domain.ShopTenant, global::Shop.Contracts.TenantId, ...&gt;
/// {
///     public SeatManager(...) : base(...) { }
/// }
/// </code>
/// in the global namespace, so every project that references it, the module's application and API projects and the
/// host among them, takes a <c>SeatManager</c> and answers a <c>SeatManager.SeatOverview</c>.
/// <para>
/// <b>Named as your class is.</b> The application's class has the name of the generic class, without its type
/// parameters: the one name the application reads in your documentation, and no clash with your class, which C# tells
/// apart by its type parameters. Name the generic class for what it holds, then, as Tenancy's <c>SeatManager</c>
/// is. An application with two modules that each declare your classes gets two classes of that name, and is told so
/// (DDD00075): it names one of them with <see cref="TemplateFacadeNameAttribute"/>, in the project that declares
/// that module's classes.
/// </para>
/// <para>
/// <b>The package's own types.</b> C# finds a type nested in a class through every class that derives from it,
/// so <c>SeatManager.SeatOverview</c> is the package's own <c>SeatManager&lt;...&gt;.SeatOverview</c>: what reflection
/// sees and what the compiler reports, with the package's documentation. C# finds a static member the same way.
/// </para>
/// <para>
/// <b>Three kinds of class.</b> What the application gets follows the package's class.
/// <list type="bullet">
/// <item>A class that is not abstract is a service: the application's class is sealed, and has each public
/// constructor of the package's class, so the container makes it. The package's registration registers it: a type
/// parameter of a <see cref="TemplateRegistrationAttribute"/> method that carries this attribute,
/// <c>[TemplateFacade(typeof(SeatManager&lt;,,,,,,,,&gt;))] TSeatManager</c>, is filled with the class the project gets
/// for that type, or sees in a project of its module below it, closed over the same classes and ids as the method.
/// Where there is none, it is filled with the package's class itself, closed over them.</item>
/// <item>A record that is not sealed is a record your code makes and hands to the package's methods, as Tenancy's
/// <c>TenantToProvision</c> is: the application's record is sealed, and has each public constructor of the package's
/// record, so <c>new TenantToProvision(...)</c> needs no type argument.</item>
/// <item>An abstract class is a name alone: the application's class is abstract too, with a private constructor, and
/// nothing makes one or derives from it. Its nested types and static members are named through it.</item>
/// </list>
/// </para>
/// <para>
/// <b>An interface.</b> An interface your storage package implements, such as Tenancy's <c>ITenancyStore&lt;...&gt;</c>,
/// has no class to stand for it: the container hands out your interface, closed over the application's classes. The
/// application gets a global alias of it instead, <c>global using TenancyStore = ...;</c>, named as your interface is
/// without its leading <c>I</c>. An alias holds in its own project only, so it is written into the project that declares
/// the classes and into every project above it that sees the classes written there, each closed over the same classes.
/// A project that sees two modules' classes gets neither alias of one name, and is told so (DDD00075).
/// </para>
/// <para>
/// <b>Why a class, and not a global alias.</b> An alias holds in the project that declares it and no further, so it
/// would have to be written into every project, and a generator's alias is seen by the compiler but not by the
/// other generators of the project: HotChocolate's, reading <c>[ObjectType&lt;SeatManager.SeatOverview&gt;]</c>,
/// would not know the name. A class is written once, where the classes are declared, and reaches every project
/// above it, and the generators there, through the reference. In the project that declares the classes the class
/// is a generator's output too, so the other generators there do not see it: a module of one project that names the
/// nested records in another library's attributes or in a GraphQL resolver keeps an alias of exactly that name there,
/// <c>global using SeatManager = ...;</c>, and gets no class.
/// </para>
/// <para>
/// <b>Which classes.</b> A project gets the class when it declares a class with one of the type's templates. Each
/// type parameter is filled as a <see cref="TemplateTypeAttribute"/> fills one of a registration: with the class of
/// its template the project declares, or, when it declares none, the one the projects of its module that it
/// references declare. A template with no class or several leaves the project without one, as does a class that
/// does not meet the type's constraints; the project hears why, DDD00065, as information, since a module whose
/// classes are split over two projects gets the class in the second. A class over a template an application may
/// leave out says so with <see cref="OnlyWith"/>.
/// </para>
/// <para>
/// <b>What the application wrote stays.</b> A project that has a type or a namespace of the name in the global
/// namespace, its own or one it references, or an alias of the name at the top of one of its files, gets nothing.
/// A type of the name the project declares in a namespace keeps the class out as well, and DDD00065 says so on that
/// type: the class would hide it in every file that imports its namespace. A project above the one that declares
/// the classes is not looked at: a type of the name it imports with a using is hidden by the class there, so it
/// qualifies that type, or the application names the class otherwise. A class of the name the generator wrote for
/// another module is no such thing: the two would meet in the projects that see both, so it is DDD00075.
/// </para>
/// <para>
/// <b>For a module that sees only the ids.</b> A class whose type parameters all take ids is written as well into a
/// project that says which of its ids are the package's with a line marked <see cref="TemplateIdsAttribute"/>,
/// <c>[assembly: TenancyIds&lt;TenantId, SeatId, OrganizationUnitId, RoleId&gt;]</c>. It is written into that project's root
/// namespace rather than the global one: it is the module's own name for the package's class, and in the global
/// namespace it would meet the class of the module that declares the classes in every project that sees both, the host
/// first. The module's code under that namespace names it without a using.
/// </para>
/// </summary>
/// <param name="type">
/// The open generic class, <c>typeof(SeatManager&lt;,,,,,,,,&gt;)</c>, not nested in a generic type. Every one of its
/// type parameters carries <see cref="TemplateTypeAttribute"/>, since the class the application gets leaves none open.
/// It is neither abstract nor sealed, with public constructors, for a class the container makes or a record your code
/// makes; or it is abstract rather than static, with a protected constructor, for a name alone. Or it is an open
/// generic interface, <c>typeof(ITenancyStore&lt;,,,,,,,,&gt;)</c>, for an alias.
/// </param>
/// <remarks>
/// A type this attribute names that the generator cannot use is passed over without a word: that is the package's
/// mistake, and its own tests of a project that names the class are what show it. The attribute asks for a class of
/// the type the assembly that declares it declares: one an application's project declares for a type of a package
/// asks nothing.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.GenericParameter, AllowMultiple = true, Inherited = false)]
public sealed class TemplateFacadeAttribute(Type type) : Attribute
{
    /// <summary>The open generic class the application's class derives from, closed over its classes.</summary>
    public Type Type { get; } = type;

    /// <summary>
    /// The template of a class an application may leave out, such as Tenancy's invitation,
    /// <c>typeof(InvitationAggregateAttribute&lt;&gt;)</c>. The class is written only where a class is declared with that
    /// template, in the project or in a project of its module it references. A project without one gets no class and
    /// hears nothing. One of the type's <see cref="TemplateTypeAttribute"/>s names the same template. Left out, the class
    /// is written wherever one of its templates has a class, and a missing one is DDD00065. Only the assembly attribute
    /// reads it.
    /// </summary>
    public Type? OnlyWith { get; set; }
}
