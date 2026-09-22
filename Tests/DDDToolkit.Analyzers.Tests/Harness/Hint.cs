namespace DDDToolkit.Analyzers.Tests.Harness;

/// <summary>
/// The hint name a generator gives the part it generates for a type, computed the way
/// <c>TypeDeclarationInfo.HintName</c> computes it: the type's own name, the generator's suffix and
/// an FNV-1a hash of the qualified name, as in <c>Basket.EntityFramework.1f3a9c2e.g.cs</c>.
/// <para>
/// It is written out again here rather than shared on purpose. A hint name ends up in paths on
/// every consumer's machine; if the generator's version drifts, these tests should notice.
/// </para>
/// </summary>
public static class Hint
{
    /// <param name="qualifiedTypeName">The type's namespace-qualified name, such as <c>Sample.Basket</c>.</param>
    /// <param name="suffix">The generator's suffix, such as <c>.EntityFramework</c>; empty for the core generators.</param>
    public static string Of(string qualifiedTypeName, string suffix = "")
    {
        var hash = 2166136261u;
        foreach (var character in qualifiedTypeName)
        {
            hash = unchecked((hash ^ character) * 16777619u);
        }

        var name = qualifiedTypeName[(qualifiedTypeName.LastIndexOf('.') + 1)..];
        return name + suffix + "." + hash.ToString("x8") + ".g.cs";
    }
}
