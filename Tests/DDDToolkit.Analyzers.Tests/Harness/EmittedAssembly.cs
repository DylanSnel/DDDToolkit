using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace DDDToolkit.Analyzers.Tests.Harness;

/// <summary>
/// A reflection facade over the assembly a test snippet compiled into, so tests can exercise generated
/// members (constructors, <c>Parse</c>, <c>ToString</c>, operators, JSON converters) as real running code
/// rather than as text. Everything here throws a descriptive exception rather than returning null, so a
/// failing test says which member was missing.
/// </summary>
public sealed class EmittedAssembly(Assembly assembly)
{
    private const BindingFlags AllStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;
    private const BindingFlags AllInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

    public Assembly Assembly { get; } = assembly;

    /// <summary>The type with this full name (use <c>Outer+Inner</c> for nested types).</summary>
    public Type Type(string fullName)
        => Assembly.GetType(fullName)
           ?? throw new InvalidOperationException(
               $"No type '{fullName}' in the emitted assembly. Types: {string.Join(", ", Assembly.GetTypes().Select(type => type.FullName))}");

    /// <summary>True when the assembly declares this type at all.</summary>
    public bool HasType(string fullName) => Assembly.GetType(fullName) is not null;

    // ------------------------------------------------------------------ construction

    /// <summary>Creates an instance through any constructor, public or not.</summary>
    public object New(string typeName, params object?[] arguments) => New(Type(typeName), arguments);

    /// <summary>Creates an instance through any constructor, public or not.</summary>
    public static object New(Type type, params object?[] arguments)
    {
        var constructor = type.GetConstructors(AllInstance)
            .FirstOrDefault(candidate => Matches(candidate.GetParameters(), arguments))
            ?? throw new InvalidOperationException(
                $"No constructor on '{type.FullName}' accepting ({string.Join(", ", arguments.Select(argument => argument?.GetType().Name ?? "null"))}). "
                + $"Constructors: {string.Join(" | ", type.GetConstructors(AllInstance).Select(candidate => candidate.ToString()))}");

        return constructor.Invoke(arguments);
    }

    /// <summary>The default value of a value type (for structs, <c>default</c>).</summary>
    public object Default(string typeName)
        => Activator.CreateInstance(Type(typeName)) ?? throw new InvalidOperationException($"'{typeName}' has no default value.");

    // ------------------------------------------------------------------ members

    public object? StaticProperty(string typeName, string propertyName) => StaticProperty(Type(typeName), propertyName);

    public static object? StaticProperty(Type type, string propertyName)
    {
        var property = type.GetProperty(propertyName, AllStatic);
        if (property is not null)
        {
            return property.GetValue(null);
        }

        var field = type.GetField(propertyName, AllStatic)
            ?? throw new InvalidOperationException($"No static property or field '{propertyName}' on '{type.FullName}'. {DescribeMembers(type)}");
        return field.GetValue(null);
    }

    public object? Property(object instance, string propertyName)
    {
        var type = instance.GetType();
        var property = type.GetProperty(propertyName, AllInstance)
            ?? throw new InvalidOperationException($"No instance property '{propertyName}' on '{type.FullName}'. {DescribeMembers(type)}");
        return property.GetValue(instance);
    }

    public object? CallStatic(string typeName, string methodName, params object?[] arguments) => CallStatic(Type(typeName), methodName, arguments);

    public static object? CallStatic(Type type, string methodName, params object?[] arguments)
    {
        var method = FindMethod(type, methodName, AllStatic, arguments);
        return method.Invoke(null, arguments);
    }

    public object? Call(object instance, string methodName, params object?[] arguments)
    {
        var method = FindMethod(instance.GetType(), methodName, AllInstance, arguments);
        return method.Invoke(instance, arguments);
    }

    /// <summary>True when the type declares a member with this name (any visibility, static or instance).</summary>
    public bool HasMember(string typeName, string memberName)
        => Type(typeName).GetMember(memberName, AllStatic | AllInstance).Length > 0;

    // ------------------------------------------------------------------ generated id conveniences

    /// <summary>Invokes the generated <c>TryParse(string?, out T)</c>.</summary>
    public (bool Success, object? Value) TryParse(string typeName, string? input)
    {
        var type = Type(typeName);
        var method = type.GetMethods(AllStatic)
            .FirstOrDefault(candidate => candidate.Name == "TryParse" && candidate.GetParameters().Length == 2 && candidate.GetParameters()[1].IsOut)
            ?? throw new InvalidOperationException($"No TryParse(string, out {type.Name}) on '{type.FullName}'. {DescribeMembers(type)}");

        var arguments = new object?[] { input, null };
        var success = (bool)method.Invoke(null, arguments)!;
        return (success, arguments[1]);
    }

    /// <summary>Invokes the generated explicit conversion in either direction.</summary>
    public object? Convert(Type from, Type to, object? value)
    {
        var declaring = to.IsGenericParameter ? from : to;
        var method = FindConversion(declaring, from, to) ?? FindConversion(from, from, to) ?? FindConversion(to, from, to)
            ?? throw new InvalidOperationException($"No explicit conversion from '{from.Name}' to '{to.Name}'.");
        return method.Invoke(null, [value]);
    }

    private static MethodInfo? FindConversion(Type declaring, Type from, Type to)
        => declaring.GetMethods(AllStatic).FirstOrDefault(candidate =>
            candidate.Name is "op_Explicit" or "op_Implicit"
            && candidate.ReturnType == to
            && candidate.GetParameters() is [{ } parameter]
            && parameter.ParameterType == from);

    // ------------------------------------------------------------------ JSON

    /// <summary>Serializes a value using its run-time type, so a generated <c>[JsonConverter]</c> applies.</summary>
    public static string ToJson(object value, JsonSerializerOptions? options = null)
        => JsonSerializer.Serialize(value, value.GetType(), options);

    public object? FromJson(string typeName, string json, JsonSerializerOptions? options = null)
        => JsonSerializer.Deserialize(json, Type(typeName), options);

    /// <summary>Round trips a value through System.Text.Json.</summary>
    public object? JsonRoundTrip(object value, JsonSerializerOptions? options = null)
        => JsonSerializer.Deserialize(JsonSerializer.Serialize(value, value.GetType(), options), value.GetType(), options);

    /// <summary>
    /// Serializes a one-entry <c>Dictionary&lt;TKey, string&gt;</c> keyed by a generated id, which drives the
    /// converter's <c>WriteAsPropertyName</c>/<c>ReadAsPropertyName</c> path.
    /// </summary>
    public (string Json, IDictionary RoundTripped) DictionaryKeyRoundTrip(object key, string value)
    {
        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(key.GetType(), typeof(string));
        var dictionary = (IDictionary)Activator.CreateInstance(dictionaryType)!;
        dictionary.Add(key, value);

        var json = JsonSerializer.Serialize(dictionary, dictionaryType);
        var roundTripped = (IDictionary)JsonSerializer.Deserialize(json, dictionaryType)!;
        return (json, roundTripped);
    }

    // ------------------------------------------------------------------ internals

    private static MethodInfo FindMethod(Type type, string methodName, BindingFlags flags, object?[] arguments)
        => type.GetMethods(flags).FirstOrDefault(candidate => candidate.Name == methodName && Matches(candidate.GetParameters(), arguments))
           ?? throw new InvalidOperationException(
               $"No method '{methodName}' on '{type.FullName}' accepting ({string.Join(", ", arguments.Select(argument => argument?.GetType().Name ?? "null"))}). {DescribeMembers(type)}");

    private static bool Matches(ParameterInfo[] parameters, object?[] arguments)
    {
        if (parameters.Length != arguments.Length)
        {
            return false;
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            if (arguments[i] is null)
            {
                if (parameters[i].ParameterType.IsValueType && Nullable.GetUnderlyingType(parameters[i].ParameterType) is null)
                {
                    return false;
                }

                continue;
            }

            if (!parameters[i].ParameterType.IsInstanceOfType(arguments[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static string DescribeMembers(Type type)
        => "Members: " + string.Join(", ", type.GetMembers(AllStatic | AllInstance).Select(member => member.Name).Distinct());
}
