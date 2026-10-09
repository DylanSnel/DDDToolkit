namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Gives the class a package's use cases are named through, the template facade the package asks for with
/// <see cref="TemplateFacadeAttribute"/>, a name of the application's own, in the project that declares the classes it
/// is closed over. Put it beside the module's other declarations, <c>[assembly: Module]</c> or a package's switch; it
/// is in <c>DDDToolkit.Abstractions.Attributes</c>, as <c>[assembly: Module]</c> is:
/// <code>
/// using DDDToolkit.Abstractions.Attributes;
///
/// [assembly: TemplateFacadeName("TenancyUseCases", "CustomersTenancy")]
/// </code>
/// and the class the generator writes there is <c>CustomersTenancy</c> instead of <c>TenancyUseCases</c>, the name of
/// the package's generic class it is called after otherwise. Every project above it names the use cases by that
/// name, <c>CustomersTenancy.SeatCommands</c>, and writes nothing.
/// <para>
/// <b>When.</b> Rarely: the package's name is the one its documentation uses. Two modules of one application that
/// each declare a package's classes get two classes of that name, which meet in every project that sees both, the
/// host first; the toolkit says so, DDD00075, with this line for one of the modules, written out. A name of your own
/// that reads better in your code is just as welcome.
/// </para>
/// <para>
/// <b>Where.</b> In the project that declares the classes, where the class is written; the projects above it see the
/// name through the reference, and a line there has nothing to name. A module whose classes are split over two
/// projects may say it in either: the project that writes the class reads the line in the projects of its module it
/// takes classes from. A line that names no class the project gets, a class it names a second time, a name no class
/// can have, or a name a namespace or a type the project sees in the global namespace has already, the module's root
/// namespace say, changes nothing, and is a warning, DDD00076, at the line.
/// </para>
/// </summary>
/// <param name="facade">
/// The class to name, as the package calls it: the name of its generic class without the type parameters,
/// <c>"TenancyUseCases"</c>. Where two packages' classes of one name meet in the project, the one with its namespace,
/// <c>"DDDToolkit.Supporting.Tenancy.UseCases.TenancyUseCases"</c>, tells them apart.
/// </param>
/// <param name="name">The name the class gets: a name a class can have, in the global namespace, that no namespace or type there has.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class TemplateFacadeNameAttribute(string facade, string name) : Attribute
{
    /// <summary>The class to name, as the package calls it: its generic class's name, without the type parameters.</summary>
    public string Facade { get; } = facade;

    /// <summary>The name the class gets.</summary>
    public string Name { get; } = name;
}
