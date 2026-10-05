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
/// <c>[IntegrationEvent]</c>.
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
/// project that sets it and no further, so on its own it can name a generated method but it cannot describe
/// a boundary to anybody else.
/// <para>
/// A folder of projects need not declare it file by file. With <c>DDD_DeclareModule</c> set to true beside
/// <c>DDD_Module</c>, in a <c>Directory.Build.props</c> for one, the build declares the module and the toolkit's
/// generator writes this attribute into every project below that declares none itself. One a project
/// declares always wins, and the attribute is never written twice.
/// </para>
/// <para>
/// The module's name is also the name in the code the generators write for the assembly:
/// <c>{Module}EventNames</c>, <c>Add{Module}Converters</c> and the other registrations. It wins over
/// <c>DDD_Module</c>, which names them in a project that declares no module.
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
