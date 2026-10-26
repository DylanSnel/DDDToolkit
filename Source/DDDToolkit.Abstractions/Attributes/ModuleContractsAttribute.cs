namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Says that this assembly is its module's contracts: every public type it declares is part of the module's
/// published contract, so other modules may name it, without a <c>[ModuleContract]</c> on each. Only means something
/// in an assembly that is a module, as <c>[ModuleContract]</c> does.
/// <code>
/// [assembly: ModuleContracts]
/// </code>
/// <para>
/// For a module's contracts project, which holds nothing but what the other modules may name: its ids, its read
/// models, its keys and its interfaces. Marking each of them says the same thing once per type, and a type added to
/// the project without the mark is a warning in every module that names it (DDD00022). A type the project keeps to
/// itself is <c>internal</c>, which C# already holds every other assembly to. A <c>[ModuleContract]</c> on a type
/// keeps meaning what it means, here and in any other project of the module.
/// </para>
/// </summary>
/// <remarks>
/// It is always the project's own choice. The toolkit never takes a project for its module's contracts because of
/// its name: a project called <c>Legal.Contracts</c> may well be a module's domain, about contracts of another kind.
/// <para>
/// A project need not write it. <c>DDD_ModuleContracts</c> set to <c>true</c>, in the project file or for a folder of
/// projects in a <c>Directory.Build.props</c>, has the toolkit's generator write this attribute into the project,
/// unless the project declares it itself, so it is never declared twice. A folder that wants every project whose
/// name ends in <c>.Contracts</c> to be one says so itself, in its own props, with a condition on the project's name.
/// </para>
/// <para>
/// Publishing a type is not the same as making it safe to hold. An entity or aggregate root of another module stays
/// off limits as stored state even when its assembly publishes it, which reports
/// <see href="https://github.com/DylanSnel/DDDToolkit/blob/main/docs/diagnostics.md#ddd00023">DDD00023</see>. A
/// contracts project holds ids, value objects, integration events and read models instead.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class ModuleContractsAttribute : Attribute
{
}
