namespace DDDToolkit.Abstractions.Attributes;

/// <summary>
/// Gives a domain event a stable name that survives renaming or moving the class. Used wherever an
/// event is serialized by name, for example the EF Core outbox. Defaults to the class name when absent.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class DomainEventNameAttribute : Attribute
{
    public DomainEventNameAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A domain event name cannot be empty.", nameof(name));
        }

        Name = name;
    }

    public string Name { get; }
}
