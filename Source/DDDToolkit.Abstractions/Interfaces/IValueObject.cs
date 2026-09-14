namespace DDDToolkit.Abstractions.Interfaces;

/// <summary>
/// A value object: equality is structural, and the object knows whether it is valid. Validation is
/// lazy, so <see cref="IsValidated"/> says whether the verdict has been computed yet.
/// </summary>
public interface IValueObject
{
    /// <summary>True once validation has run, so <see cref="IsValid"/> is answered from the cache.</summary>
    bool IsValidated { get; }

    /// <summary>Whether the value satisfies its rules. Reading this validates when it has to.</summary>
    bool IsValid { get; }
}
