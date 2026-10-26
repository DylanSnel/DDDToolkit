using System.Text.RegularExpressions;
using HotChocolate;
using HotChocolate.Types;

namespace DDDToolkit.HotChocolate.Tests.Infrastructure;

/// <summary>
/// What a schema says of its types, and a printed schema read without it. Compiled into every suite that reads one.
/// </summary>
internal static partial class SchemaDescriptions
{
    /// <summary>
    /// What a schema says of one type: a line for the type and one for each of its fields or values, as
    /// <c>Type: description</c> and <c>Type.field: description</c>.
    /// </summary>
    public static string[] Of(ISchemaDefinition schema, string name)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var type = schema.Types.GetType<ITypeDefinition>(name);
        IEnumerable<(string Name, string? Description)> members = type switch
        {
            IComplexTypeDefinition complex => complex.Fields.Where(field => !field.IsIntrospectionField).Select(field => (field.Name, field.Description)),
            IEnumTypeDefinition enumType => enumType.Values.Select(value => (value.Name, value.Description)),
            _ => [],
        };

        return [$"{name}: {type.Description}", .. members.Select(member => $"{name}.{member.Name}: {member.Description}")];
    }

    /// <summary>
    /// Whether the XML documentation of the toolkit's error types and of <c>RefusalKind</c> lies beside their
    /// assemblies, where HotChocolate looks for it. A test that compares a schema reading it with one that does
    /// not proves something only when there is a file to read.
    /// </summary>
    public static bool ToolkitXmlDocumentationIsBeside()
        => new[] { typeof(DDDToolkit.HotChocolate.Errors.ICodedError), typeof(DDDToolkit.Exceptions.RefusalKind) }
            .All(type => File.Exists(System.IO.Path.ChangeExtension(type.Assembly.Location, ".xml")));

    /// <summary>
    /// The printed schema without its descriptions. A description is text for a client and may say anything, the
    /// text of a declaration included; a test that looks for a declaration, counts one or reads a type's fields
    /// wants the schema's own lines and not a description that quotes them.
    /// </summary>
    public static string RemovedFrom(string sdl)
    {
        ArgumentNullException.ThrowIfNull(sdl);
        return SingleLine().Replace(Block().Replace(sdl.ReplaceLineEndings("\n"), string.Empty), string.Empty);
    }

    /// <summary>A block description: three quotes, anything, and three quotes that are not escaped.</summary>
    [GeneratedRegex(
        """"
        """[\s\S]*?(?<!\\)"""
        """")]
    private static partial Regex Block();

    /// <summary>A description on one line: a line that is a string and nothing else.</summary>
    [GeneratedRegex("""^[ \t]*"(?:[^"\\\n]|\\.)*"[ \t]*$""", RegexOptions.Multiline)]
    private static partial Regex SingleLine();
}
