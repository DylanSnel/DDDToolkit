namespace DDDToolkit.EntityFramework.Conventions;

/// <summary>
/// Names a model convention a package ships for the classes an application declares with its templates, so a context
/// maps them without a call of the package's own: the context calls <c>AddDDDToolkitConventions()</c>, as it does for
/// ordinary aggregates, and every convention named by an assembly the context's assembly references, directly or
/// through another, is applied to its model.
/// <code>
/// [assembly: ModelConvention(typeof(MembershipModelConvention))]
/// </code>
/// <para>
/// The convention runs when the model is finished, after <c>OnModelCreating</c>, so what the context says by hand wins:
/// a convention maps only what was not mapped yet. It implements <c>IModelFinalizingConvention</c> and has a public
/// constructor without parameters. The same conventions are found at design time, by <c>dotnet ef</c>, as when the
/// application runs, since they follow from the context's own references and nothing registered: the migrations see
/// the model the application uses.
/// </para>
/// </summary>
/// <param name="convention">The convention's type: an <c>IModelFinalizingConvention</c> with a public constructor without parameters.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class ModelConventionAttribute(Type convention) : Attribute
{
    /// <summary>The convention's type.</summary>
    public Type Convention { get; } = convention;
}
