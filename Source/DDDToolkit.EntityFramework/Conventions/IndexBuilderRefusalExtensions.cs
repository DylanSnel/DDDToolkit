using DDDToolkit.EntityFramework.Interceptors;
using DDDToolkit.Exceptions;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DDDToolkit.EntityFramework.Conventions;

/// <summary>Says, where a unique index is configured, what a save that breaks it is refused with.</summary>
public static class IndexBuilderRefusalExtensions
{
    /// <summary>
    /// A save that breaks this unique index is refused with <paramref name="code"/> instead of failing: the same
    /// answer as a check in C# that lost a race, so a command checks first and refuses by code, and the index
    /// gives that very refusal when two of them pass the check at the same moment.
    /// <code>
    /// project.HasIndex(row => new { row.TenantId, row.Number })
    ///     .IsUnique()
    ///     .RefusesAs("projects.number-taken", "Another project already has the number {Number}.");
    /// </code>
    /// <para>
    /// <paramref name="message"/> may name properties of the row in braces, <c>{Number}</c>: the index's own, or
    /// any other the entity type maps. Each is filled from the row that broke the index, and is an argument of
    /// the refusal under that name, for a translation to place. A property with a value converter gives the
    /// converted value, so an id or a value object shows as the plain value underneath. When the database does
    /// not say which row of a save broke the index and the rows differ in what the message names, the
    /// placeholders stay as written and the refusal has no arguments.
    /// </para>
    /// <para>
    /// <see cref="DatabaseRefusalInterceptor"/>, which <c>UseDDDToolkit</c> adds, makes the refusal; the failure
    /// the database gave is its inner exception. This is configuration of the mapping and changes nothing in
    /// the database: it needs no migration. With <c>AddDDDToolkitConventions</c>, a message that names a
    /// property the entity type does not have, or an index that is not unique, fails when the model is built.
    /// </para>
    /// </summary>
    /// <param name="index">The unique index.</param>
    /// <param name="code">The refusal's code, such as <c>projects.number-taken</c>.</param>
    /// <param name="message">The refusal's text, which may name properties of the row in braces.</param>
    /// <param name="kind">The refusal's kind: a conflict, unless said otherwise.</param>
    /// <exception cref="ArgumentNullException"><paramref name="index"/> or <paramref name="message"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="code"/> is null or blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not one of the four kinds.</exception>
    public static IndexBuilder RefusesAs(this IndexBuilder index, string code, string message, RefusalKind kind = RefusalKind.Conflict)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(message);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "A refusal is Invalid, NotPermitted, NotFound or Conflict.");
        }

        index.Metadata.SetAnnotation(IndexRefusal.CodeAnnotation, code);
        index.Metadata.SetAnnotation(IndexRefusal.MessageAnnotation, message);
        index.Metadata.SetAnnotation(IndexRefusal.KindAnnotation, kind.ToString());
        return index;
    }

    /// <inheritdoc cref="RefusesAs(IndexBuilder, string, string, RefusalKind)"/>
    public static IndexBuilder<TEntity> RefusesAs<TEntity>(this IndexBuilder<TEntity> index, string code, string message, RefusalKind kind = RefusalKind.Conflict)
    {
        RefusesAs((IndexBuilder)index, code, message, kind);
        return index;
    }
}

/// <summary>The refusal a unique index declares with <c>RefusesAs</c>, as the model carries it.</summary>
/// <param name="Code">The refusal's code.</param>
/// <param name="Message">The refusal's text, with its placeholders as written.</param>
/// <param name="Kind">The refusal's kind.</param>
internal sealed record IndexRefusal(string Code, string Message, RefusalKind Kind)
{
    /// <summary>The annotation that holds the code. The three are plain strings, so a model snapshot writes them as they are.</summary>
    internal const string CodeAnnotation = "DDDToolkit:RefusesAs:Code";

    /// <summary>The annotation that holds the text.</summary>
    internal const string MessageAnnotation = "DDDToolkit:RefusesAs:Message";

    /// <summary>The annotation that holds the kind, by its name.</summary>
    internal const string KindAnnotation = "DDDToolkit:RefusesAs:Kind";

    /// <summary>What <paramref name="index"/> declares, or <see langword="null"/> when it declares nothing.</summary>
    internal static IndexRefusal? Of(IReadOnlyIndex index)
    {
        if (index.FindAnnotation(CodeAnnotation)?.Value is not string code || string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var message = index.FindAnnotation(MessageAnnotation)?.Value as string ?? string.Empty;
        var kind = Enum.TryParse<RefusalKind>(index.FindAnnotation(KindAnnotation)?.Value as string, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : RefusalKind.Conflict;
        return new IndexRefusal(code, message, kind);
    }

    /// <summary>
    /// The property of <paramref name="entityType"/> a placeholder names, matched without regard to case when no
    /// property has exactly that name, as the arguments of a failure are; <see langword="null"/> when there is none.
    /// </summary>
    internal static IReadOnlyProperty? PropertyNamed(IReadOnlyEntityType entityType, string name)
        => entityType.FindProperty(name)
            ?? entityType.GetProperties().FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase));
}
