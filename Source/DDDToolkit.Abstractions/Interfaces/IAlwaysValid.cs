namespace DDDToolkit.Abstractions.Interfaces;

/// <summary>
/// Marks the always-valid twin the generators emit beside a value object or a reference-type
/// identifier. A twin can only be reached through <c>ToValid()</c>, which validates first, so a
/// parameter typed as the twin cannot be handed an invalid value. The serializers refuse to read one
/// for the same reason: deserialization would bypass that check.
/// </summary>
public interface IAlwaysValid;
