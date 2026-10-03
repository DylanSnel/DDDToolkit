using System.Reflection;
using System.Reflection.Emit;

namespace Examples.Tenancy.Tests.Infrastructure;

/// <summary>Where a type was found: in a declaration, a signature, a field, a local, or the code of a method.</summary>
public enum Seen
{
    /// <summary>A base type, an interface, a generic constraint or an attribute of a type or one of its members.</summary>
    Declaration,

    /// <summary>A parameter of a method or a constructor, or what a method returns.</summary>
    Signature,

    /// <summary>A field, which is also what a lambda captures and what a state machine keeps.</summary>
    Field,

    /// <summary>A local of a method.</summary>
    Local,

    /// <summary>A call, a <c>typeof</c>, a cast, a field read or the type a <c>catch</c> names in the code of a method.</summary>
    Code,
}

/// <summary>A type that a type of an assembly names, and where.</summary>
/// <param name="Type">The type named, or a type it is built of: an element type, a type argument, a generic definition.</param>
/// <param name="By">The type that names it.</param>
/// <param name="Seen">How it is named.</param>
/// <param name="Where">The member that names it, for a failure to point at.</param>
public sealed record TypeUse(Type Type, Type By, Seen Seen, string Where)
{
    /// <inheritdoc />
    public override string ToString() => $"{Where}: {Type} ({Seen})";
}

/// <summary>A member the code of a type refers to: a method it calls, a field it reads.</summary>
/// <param name="Member">The method, constructor or field.</param>
/// <param name="By">The type whose code refers to it.</param>
/// <param name="Where">The method that does, for a failure to point at.</param>
public sealed record MemberUse(MemberInfo Member, Type By, string Where)
{
    /// <inheritdoc />
    public override string ToString() => $"{Where}: {Member.DeclaringType}.{Member.Name}";
}

/// <summary>
/// Every type the types of an assembly name: what they derive from and implement, their attributes and generic
/// constraints, the signature of every method and constructor, every field and every local, and every type, method
/// and field the code of each method refers to, with the type each <c>catch</c> names. The types the compiler makes for lambdas, what they capture and
/// their state machines are types like any other, so what a route's lambda asks the container for is found, as the
/// type argument of the call, though no parameter, field or local of the route is of that type.
/// </summary>
/// <remarks>
/// A type is reported with everything it is built of: an array's element type, a constructed type's definition and
/// each of its type arguments, to the end. So a rule is a plain question about one type, and
/// <c>Func&lt;IProjectStore, Task&gt;</c> answers it for the port inside it.
/// <para>
/// A token the scan cannot resolve is not skipped in silence, and neither is a type of an assembly that could not
/// be loaded: both are kept in <see cref="NotResolved"/>, and a test that rests on the scan checks that there is
/// none. A rule that says "nothing names X" proves nothing over code that was not read.
/// </para>
/// </remarks>
public sealed class TypeScan
{
    private const BindingFlags Everything = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static readonly OpCode[] OneByteCodes = CodesWhere(static value => value < 0x100, static value => value);

    private static readonly OpCode[] TwoByteCodes = CodesWhere(static value => (value & 0xFF00) == 0xFE00, static value => value & 0xFF);

    private readonly List<TypeUse> _uses = [];
    private readonly List<MemberUse> _membersNamed = [];
    private readonly List<string> _notResolved = [];

    private TypeScan()
    {
    }

    /// <summary>Every type named, with the type that names it.</summary>
    public IReadOnlyList<TypeUse> Uses => _uses;

    /// <summary>
    /// Every method and field the code of a method refers to, with the type whose code it is: for a rule about who
    /// calls one method, where a rule about a type would say too much.
    /// </summary>
    public IReadOnlyList<MemberUse> MembersNamed => _membersNamed;

    /// <summary>
    /// What the scan could not read: a token it could not resolve, by the method that holds it, and a type of a
    /// scanned assembly that could not be loaded, by what the loader said.
    /// </summary>
    public IReadOnlyList<string> NotResolved => _notResolved;

    /// <summary>
    /// Scans every type of <paramref name="assembly"/>, the compiler's own included. A type that could not be
    /// loaded was not read, and is reported in <see cref="NotResolved"/>.
    /// </summary>
    public static TypeScan Of(Assembly assembly)
    {
        var scan = new TypeScan();
        return scan.Reading(TypesOf(assembly, scan._notResolved));
    }

    /// <summary>Scans <paramref name="types"/>.</summary>
    public static TypeScan Of(IEnumerable<Type> types) => new TypeScan().Reading(types);

    /// <summary>
    /// Every type of an assembly that could be loaded, nested and compiler-made ones included. For a list of types
    /// to look through; a scan of the assembly itself also says which could not be loaded.
    /// </summary>
    public static IReadOnlyList<Type> TypesOf(Assembly assembly) => TypesOf(assembly, notLoaded: null);

    private static IReadOnlyList<Type> TypesOf(Assembly assembly, List<string>? notLoaded)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException partial)
        {
            notLoaded?.AddRange(partial.LoaderExceptions.OfType<Exception>().Select(failure => $"{assembly.GetName().Name}: a type could not be loaded, and was not read. {failure.Message}"));
            return [.. partial.Types.OfType<Type>()];
        }
    }

    private TypeScan Reading(IEnumerable<Type> types)
    {
        foreach (var type in types)
        {
            Read(type);
        }

        return this;
    }

    /// <summary>
    /// <paramref name="type"/> and every type declared inside it, to any depth: a class with the closures and
    /// state machines the compiler made for its lambdas and its asynchronous methods.
    /// </summary>
    public static IEnumerable<Type> WithNested(Type type)
        => type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(WithNested).Prepend(type);

    /// <summary>
    /// The type <paramref name="type"/> is declared in, to the top: a handler for the state machine of its method,
    /// a class for the closure of its lambda.
    /// </summary>
    public static Type Outermost(Type type) => type.DeclaringType is { } declaring ? Outermost(declaring) : type;

    /// <summary>
    /// The types <paramref name="type"/> is built of: itself; for an array, a reference or a pointer, what it is
    /// one of; for a constructed type, its definition and every type argument, each taken apart the same way. A
    /// generic parameter stands for nothing by itself: its constraints are read where it is declared.
    /// </summary>
    public static IEnumerable<Type> PartsOf(Type type)
    {
        if (type.HasElementType)
        {
            return PartsOf(type.GetElementType()!);
        }

        if (type.IsGenericParameter)
        {
            return [];
        }

        if (!type.IsGenericType || type.IsGenericTypeDefinition)
        {
            return [type];
        }

        return type.GetGenericArguments().SelectMany(PartsOf).Prepend(type.GetGenericTypeDefinition()).Prepend(type);
    }

    private void Read(Type type)
    {
        var name = type.FullName ?? type.Name;

        if (type.BaseType is { } parent)
        {
            Add(parent, type, Seen.Declaration, $"{name}, which derives from {parent}");
        }

        foreach (var implemented in type.GetInterfaces())
        {
            Add(implemented, type, Seen.Declaration, $"{name}, which implements {implemented}");
        }

        ReadConstraints(type.GetGenericArguments(), type, name);
        ReadAttributes(type.GetCustomAttributesData(), type, name);

        foreach (var field in type.GetFields(Everything))
        {
            Add(field.FieldType, type, Seen.Field, $"{name}.{field.Name}");
            ReadAttributes(field.GetCustomAttributesData(), type, $"{name}.{field.Name}");
        }

        foreach (var property in type.GetProperties(Everything))
        {
            ReadAttributes(property.GetCustomAttributesData(), type, $"{name}.{property.Name}");
        }

        foreach (var method in type.GetMethods(Everything).Cast<MethodBase>().Concat(type.GetConstructors(Everything)))
        {
            var where = $"{name}.{method.Name}";
            ReadAttributes(method.GetCustomAttributesData(), type, where);

            if (method is MethodInfo returning)
            {
                Add(returning.ReturnType, type, Seen.Signature, $"{where}, which returns {returning.ReturnType}");
                ReadConstraints(returning.GetGenericArguments(), type, where);
            }

            foreach (var parameter in method.GetParameters())
            {
                Add(parameter.ParameterType, type, Seen.Signature, $"{where}({parameter.Name})");
                ReadAttributes(parameter.GetCustomAttributesData(), type, $"{where}({parameter.Name})");
            }

            if (method.GetMethodBody() is not { } body)
            {
                continue;
            }

            foreach (var local in body.LocalVariables)
            {
                Add(local.LocalType, type, Seen.Local, $"{where}, local {local.LocalIndex}");
            }

            // A catch names its type in the method's table of handlers, not among its instructions: one without a
            // variable has no local of that type either.
            foreach (var caught in body.ExceptionHandlingClauses.Where(clause => clause.Flags == ExceptionHandlingClauseOptions.Clause && clause.CatchType is not null))
            {
                Add(caught.CatchType!, type, Seen.Code, $"{where}, which catches {caught.CatchType}");
            }

            foreach (var named in NamedBy(method, body))
            {
                if (named is not Type)
                {
                    _membersNamed.Add(new MemberUse(named, type, where));
                }

                foreach (var used in TypesIn(named))
                {
                    Add(used, type, Seen.Code, $"{where}, which names {named}");
                }
            }
        }
    }

    private void Add(Type named, Type by, Seen seen, string where)
    {
        foreach (var part in PartsOf(named))
        {
            _uses.Add(new TypeUse(part, by, seen, where));
        }
    }

    private void ReadConstraints(Type[] arguments, Type by, string where)
    {
        foreach (var argument in arguments.Where(argument => argument.IsGenericParameter))
        {
            foreach (var constraint in argument.GetGenericParameterConstraints())
            {
                Add(constraint, by, Seen.Declaration, $"{where}, whose {argument.Name} is constrained to {constraint}");
            }
        }
    }

    private void ReadAttributes(IList<CustomAttributeData> attributes, Type by, string where)
    {
        foreach (var attribute in attributes)
        {
            Add(attribute.AttributeType, by, Seen.Declaration, $"{where}, which carries [{attribute.AttributeType.Name}]");

            // typeof(X) as an argument names X as surely as a call does.
            foreach (var argument in attribute.ConstructorArguments.Concat(attribute.NamedArguments.Select(named => named.TypedValue)))
            {
                foreach (var value in argument.Value is IEnumerable<CustomAttributeTypedArgument> several ? several : [argument])
                {
                    if (value.Value is Type typeArgument)
                    {
                        Add(typeArgument, by, Seen.Declaration, $"{where}, whose [{attribute.AttributeType.Name}] names {typeArgument}");
                    }
                }
            }
        }
    }

    /// <summary>The types a member the code refers to is made of: its own type, where it is declared, and its signature.</summary>
    private static IEnumerable<Type> TypesIn(MemberInfo member)
    {
        switch (member)
        {
            case Type type:
                yield return type;
                break;
            case FieldInfo field:
                yield return field.DeclaringType!;
                yield return field.FieldType;
                break;
            case MethodBase method:
                if (method.DeclaringType is { } declaring)
                {
                    yield return declaring;
                }

                if (method is MethodInfo { ReturnType: var returned })
                {
                    yield return returned;
                }

                foreach (var parameter in method.GetParameters())
                {
                    yield return parameter.ParameterType;
                }

                if (method.IsGenericMethod)
                {
                    foreach (var argument in method.GetGenericArguments())
                    {
                        yield return argument;
                    }
                }

                break;
        }
    }

    /// <summary>
    /// Every type, method and field the code of <paramref name="method"/> refers to: a call, a <c>typeof</c>, a
    /// cast, a field read.
    /// </summary>
    private IEnumerable<MemberInfo> NamedBy(MethodBase method, MethodBody body)
    {
        var il = body.GetILAsByteArray() ?? [];
        var typeArguments = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;

        for (var at = 0; at < il.Length;)
        {
            var code = il[at] == 0xFE && at + 1 < il.Length ? TwoByteCodes[il[at + 1]] : OneByteCodes[il[at]];
            if (code.Size == 0)
            {
                throw new InvalidOperationException($"{method.DeclaringType}.{method.Name} has an instruction this scan does not know at {at}.");
            }

            at += code.Size;
            switch (code.OperandType)
            {
                case OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok:
                    if (Resolve(method, BitConverter.ToInt32(il, at), typeArguments, methodArguments) is { } member)
                    {
                        yield return member;
                    }

                    at += 4;
                    break;
                case OperandType.InlineSwitch:
                    at += 4 + (4 * BitConverter.ToInt32(il, at));
                    break;
                default:
                    at += code.OperandType switch
                    {
                        OperandType.InlineNone => 0,
                        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                        OperandType.InlineVar => 2,
                        OperandType.InlineI8 or OperandType.InlineR => 8,
                        _ => 4,
                    };
                    break;
            }
        }
    }

    /// <summary>The member a metadata token names. One that cannot be resolved here is kept in <see cref="NotResolved"/>.</summary>
    private MemberInfo? Resolve(MethodBase method, int token, Type[]? typeArguments, Type[]? methodArguments)
    {
        try
        {
            return method.Module.ResolveMember(token, typeArguments, methodArguments);
        }
        catch (Exception exception) when (exception is ArgumentException or BadImageFormatException or TypeLoadException or FileNotFoundException or MissingMemberException)
        {
            _notResolved.Add($"{method.DeclaringType}.{method.Name}: token 0x{token:X8} ({exception.GetType().Name}: {exception.Message})");
            return null;
        }
    }

    private static OpCode[] CodesWhere(Func<int, bool> takes, Func<int, int> index)
    {
        var codes = new OpCode[0x100];
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is OpCode code && takes((ushort)code.Value))
            {
                codes[index((ushort)code.Value)] = code;
            }
        }

        return codes;
    }
}
