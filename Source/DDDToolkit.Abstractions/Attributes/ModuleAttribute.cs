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
/// project that sets it and no further, so it can name a generated method but it cannot describe a
/// boundary to anybody else.
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
