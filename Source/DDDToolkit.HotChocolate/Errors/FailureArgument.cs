namespace DDDToolkit.HotChocolate.Errors;

/// <summary>
/// One of the values a failure's message was built from, such as <c>MaxLength</c> and <c>40</c>, as a typed
/// error in a mutation's payload lists them.
/// <para>
/// GraphQL has no type for "a map of anything", so the arguments are a list of names with their values as
/// text: a string as it is, <c>true</c> or <c>false</c>, a number the way JSON writes it, and anything else,
/// such as an id, as its own text. A value that was null stays null.
/// </para>
/// </summary>
/// <param name="Name">The argument's name, as a translation's template spells it between braces.</param>
/// <param name="Value">Its value as text, or <see langword="null"/> when there was none.</param>
public sealed record FailureArgument(string Name, string? Value);
