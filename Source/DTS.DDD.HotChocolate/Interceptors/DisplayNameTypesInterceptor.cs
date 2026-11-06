using DTS.DDD.Abstractions.Attributes;
using HotChocolate.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors.Configurations;
using System.Reflection;
using System.Text.RegularExpressions;

namespace DTS.DDD.HotChocolate.Interceptors;

/// <summary>
/// Names an object type or an interface type after the <see cref="DisplayNameAttribute">[DisplayName]</see> of its
/// runtime type.
/// </summary>
/// <remarks>
/// <para>
/// HotChocolate names a generic type after its type arguments, so Tenancy's
/// <c>Placement&lt;SeatId, OrganizationUnitId, RoleId&gt;</c> becomes <c>PlacementOfSeatIdAndOrganizationUnitIdAndRoleId</c>.
/// Tenancy puts <c>[DisplayName("Placement")]</c> on the class, and the type is named <c>Placement</c>.
/// </para>
/// <para>
/// The display name replaces HotChocolate's default name only. A name the application gives the type with
/// <c>descriptor.Name(...)</c> or <c>[GraphQLName]</c> differs from the default, and it stays. An input object type
/// keeps HotChocolate's name, because the display name of a class names its output type.
/// </para>
/// <para>
/// A display name that is not a GraphQL name fails the schema build. A GraphQL name has letters, digits and
/// underscores only, and its first character is not a digit. Registered by <see cref="DependencyInjection.AddDDDToolkitTypes"/>.
/// </para>
/// </remarks>
public sealed partial class DisplayNameTypesInterceptor : TypeInterceptor
{
    /// <summary>Replaces the default name of a type whose runtime type has a display name.</summary>
    /// <exception cref="InvalidOperationException">The display name is not a GraphQL name.</exception>
    public override void OnBeforeCompleteName(ITypeCompletionContext completionContext, TypeSystemConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(completionContext);

        var (runtimeType, kind) = configuration switch
        {
            ObjectTypeConfiguration { IsExtension: false } objectType => (objectType.RuntimeType, TypeKind.Object),
            InterfaceTypeConfiguration interfaceType => (interfaceType.RuntimeType, TypeKind.Interface),
            _ => (null, default),
        };

        if (runtimeType is null || runtimeType.GetCustomAttribute<DisplayNameAttribute>(inherit: false) is not { } display)
        {
            return;
        }

        if (!GraphQLName().IsMatch(display.Name))
        {
            throw new InvalidOperationException(
                $"The display name '{display.Name}' of {runtimeType.FullName ?? runtimeType.Name} is not a GraphQL name. "
                + "A GraphQL name has letters, digits and underscores only, and its first character is not a digit.");
        }

        if (configuration.Name == completionContext.DescriptorContext.Naming.GetTypeName(runtimeType, kind))
        {
            configuration.Name = display.Name;
        }
    }

    [GeneratedRegex("^[_A-Za-z][_0-9A-Za-z]*$", RegexOptions.CultureInvariant)]
    private static partial Regex GraphQLName();
}
