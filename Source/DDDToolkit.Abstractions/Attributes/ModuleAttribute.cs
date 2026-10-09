namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Declares that this assembly is a module: a bounded piece of the system that owns its own types and
/// lets other modules in only through a published contract. Put it in any file of the project, or in
/// <c>AssemblyInfo.cs</c>.
/// <code>
/// [assembly: Module("Ordering")]
/// </code>
/// <para>
/// One assembly is one module. Everything the assembly declares belongs to it, and everything it
/// declares is internal to it unless the type carries <c>[ModuleContract]</c> or
/// <c>[IntegrationEvent]</c>, or is public in an assembly that is the module's contracts,
/// <c>[assembly: ModuleContracts]</c>.
/// </para>
/// <para>
/// The attribute only means something when both sides opt in. Another module that names one of this
/// module's unpublished types reports
/// <see href="https://github.com/DylanSnel/DDDToolkit/blob/main/docs/diagnostics.md#ddd00022">DDD00022</see>,
/// and it reports nothing at all against an assembly that does not carry this attribute. Framework
/// assemblies, NuGet packages and a shared kernel are therefore never in the way.
/// </para>
/// </summary>
/// <remarks>
/// This is an assembly attribute rather than an MSBuild property because the analyzer has to read the
/// module of an assembly it only sees through metadata. <c>DDD_Module</c> reaches the compiler of the
/// project that sets it and no further, so the build turns it into this attribute for anybody else to read.
/// <para>
/// A project need not write it. <c>DDD_Module</c>, set in the project file or for a whole folder of projects in a
/// <c>Directory.Build.props</c>, declares the module: the build writes it into the project and the toolkit's
/// generator writes this attribute from it, into every project that sets it and declares none itself. One a
/// project declares always wins, and the attribute is never written twice. A test project is not declared this
/// way, and neither is a project that sets <c>DDD_DeclareModule</c> to false.
/// </para>
/// <para>
/// The module's name is also the name in the code the generators write for the assembly:
/// <c>{Module}EventNames</c>, <c>Add{Module}Converters</c> and the other registrations. It wins over
/// <c>DDD_Module</c>, which names them in a project that is no module: a test project, or one that sets
/// <c>DDD_DeclareModule</c> to false.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class ModuleAttribute : Attribute
{
    /// <summary>Names the module.</summary>
    /// <param name="name">The module name, as it appears in diagnostics. Cannot be empty.</param>
    /// <exception cref="ArgumentException">The name is empty or white space.</exception>
    public ModuleAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A module name cannot be empty.", nameof(name));
        }

        Name = name;
    }

    /// <summary>The module name.</summary>
    public string Name { get; }
}
