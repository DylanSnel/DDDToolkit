// Sketch of what DTS.DDD would need: an Error that a generated part can fill in.
namespace Sketch;
public abstract record Error
{
    public abstract string Code { get; }
    public abstract string Message { get; }
    public abstract IReadOnlyDictionary<string, object?> Arguments { get; }
}
public abstract record NotFoundError : Error;
public abstract record ConflictError : Error;
public abstract record InvalidError : Error;
[AttributeUsage(AttributeTargets.Class)]
public sealed class ErrorCodeAttribute(string code, string message) : Attribute;
