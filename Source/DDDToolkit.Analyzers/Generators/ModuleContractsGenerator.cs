using System.Text;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Analyzers;

/// <summary>
/// Writes <c>[assembly: ModuleContracts]</c> into a project whose <c>DDD_ModuleContracts</c> is <c>true</c>: the
/// project is its module's contracts, and every public type of it is published to the other modules. So a
/// <c>Directory.Build.props</c> can say it for the contracts projects of a folder, and none of their types needs a
/// <c>[ModuleContract]</c>.
/// <para>
/// The property reaches this generator the way <c>DDD_Module</c> does, declared by the props file of the
/// DDDToolkit.Analyzers package, and unlike <c>DDD_Module</c> it needs no build step that writes it into the project
/// first. That step is there because every generator of a project asks which module it is in, and a generator never
/// sees what another one writes. Whether a type is published matters to the other modules: the module boundary
/// analyzer of a project that names one of its types, and the registrations of a project that stores or binds its
/// ids, which read it from the compiled assembly, where this attribute is. The one generator of the project itself
/// that asks, the one that marks <c>{Module}EventNames</c> <c>[ModuleContract]</c>, reads the property as well,
/// through <see cref="ModuleBoundary.IsContractsByTheBuild"/>, so it writes the same class either way.
/// </para>
/// <para>
/// It is written only where the project declares no <c>[assembly: ModuleContracts]</c> itself, in a file of its own or
/// through an <c>AssemblyAttribute</c> item: a generator is handed both and the build is not, so the attribute is never
/// declared twice (CS0579). Only <c>true</c> counts, in any case and with spaces around it, as MSBuild compares it. And
/// only the property counts: a project is never taken for its module's contracts because of its name, since a module
/// may be about contracts of another kind, and a host that wants its <c>*.Contracts</c> projects to be its modules'
/// contracts says so in its own props, with a condition on the project's name.
/// </para>
/// <para>
/// Where the project cannot name the attribute, because it references a DDDToolkit.Abstractions older than it, or
/// none, the property is the project's explicit choice and cannot be carried out, so the same pass reports DDD00068
/// at the project file rather than drop it without a word.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ModuleContractsGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var said = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) => ModuleBoundary.SaysModuleContracts(provider.GlobalOptions));

        // Read off the compilation, so it runs on every edit; a plain value compares equal while nothing changes, and
        // the file stays cached. The property is asked first, so a project that does not set it reads no attribute.
        var outcome = context.CompilationProvider
            .Combine(said)
            .Select(static (pair, _) => pair.Right ? OutcomeFor(pair.Left) : Outcome.Nothing);

        context.RegisterSourceOutput(outcome.Combine(context.ProjectFile()), static (production, data) =>
        {
            switch (data.Left)
            {
                case Outcome.Write:
                    production.AddSource("ModuleContracts.g.cs", SourceText.From(Write(), Encoding.UTF8));
                    break;

                case Outcome.CannotWrite:
                    DiagnosticInfo.Create(DiagnosticDescriptors.ModuleContractsNotWritten, data.Right).Report(production);
                    break;
            }
        });
    }

    /// <summary>What becomes of a <c>DDD_ModuleContracts</c> that is true.</summary>
    private enum Outcome
    {
        /// <summary>Nothing to write: the property says nothing, or the project declares the attribute itself.</summary>
        Nothing,

        /// <summary>The attribute is the generator's to write.</summary>
        Write,

        /// <summary>The project cannot name the attribute, so the choice cannot be carried out: DDD00068.</summary>
        CannotWrite,
    }

    /// <summary>
    /// What to do where the property is true. An attribute the project declares itself is kept, whatever declares
    /// its type. Otherwise the generator writes it, where the project can name the attribute; a project with an older
    /// DDDToolkit.Abstractions, or none, cannot, and hears so.
    /// </summary>
    private static Outcome OutcomeFor(Compilation compilation)
    {
        if (ModuleBoundary.HasModuleContractsAttribute(compilation.Assembly))
        {
            return Outcome.Nothing;
        }

        return compilation.GetTypeByMetadataName(KnownTypes.ModuleContractsAttribute) is null ? Outcome.CannotWrite : Outcome.Write;
    }

    private static string Write()
    {
        var writer = new CodeWriter().Header();
        writer.Line("// This project is its module's contracts, as its DDD_ModuleContracts says: every public type of it is published");
        writer.Line("// to the other modules. An [assembly: ModuleContracts] of the project's own would have been kept instead, and");
        writer.Line("// nothing written here.");
        writer.Line("[assembly: global::DDDToolkit.Abstractions.Attributes.ModuleContractsAttribute]");
        return writer.ToString();
    }
}
