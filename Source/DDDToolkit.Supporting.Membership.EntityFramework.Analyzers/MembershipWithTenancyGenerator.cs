using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using DDDToolkit.Analyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DDDToolkit.Supporting.Membership.EntityFramework.Analyzers;

/// <summary>
/// Joins a resource with members to an organization of the Tenancy packages, in an application that has both.
/// The two packages do not know each other, so what joins them is written into the application: for each
/// member class whose members are seats of a tenant, the class that answers what the resource's rules ask of
/// the organization, from Tenancy's own queries, and a registration named after the resource:
/// <code>
/// services.AddCourseMembershipWithTenancy&lt;CampusContext&gt;(CourseMembership.Rules);
/// </code>
/// <para>
/// <b>Where.</b> In the project that gets the resource's own registration, <c>AddCourseMembership</c>, and
/// nowhere else: the project that declares the member class, or the one of its module that holds the context.
/// It asks the generator that writes that registration which classes it was closed over, so both are
/// about the same class, the same types, the same resource and the same project.
/// </para>
/// <para>
/// <b>When.</b> Only where the project can see what the written class is made of: Tenancy's access manager and its
/// queries, its read source over a context, and its catalogue. Without them nothing is written, so an application
/// without an organization gets nothing it could not compile.
/// </para>
/// <para>
/// <b>Which ids.</b> A member is a seat, so the member class's member id is Tenancy's seat id. Tenancy's
/// tenant, unit and role id are the application's own as well, and are taken from the classes it declares
/// with Tenancy's templates, in the project or in the projects it references. A member class whose member id
/// is not the seat's id is no member list of seats, and gets nothing. A project that sees none of those
/// classes, a module that knows the organization only by its ids, is told which of its ids they are by the line
/// <c>[assembly: TenancyIds&lt;TenantId, SeatId, OrganizationUnitId, RoleId&gt;]</c>, in the project or in a project of
/// its module it references, and is then closed over them as well. Without the line it cannot be told: its
/// registration leaves them for the application to write. Nor can it be told a seat's id from another, so there each
/// member class gets the registration, which takes its member id for the seat's, and the application calls it for the
/// resources whose members are seats.
/// </para>
/// <para>
/// <b>Which roles.</b> A member that holds roles by the id of Tenancy's role class holds the tenant's own
/// roles, and the written class answers which of them give a permission and which there are. A member that holds
/// roles by name, or by the id of a role class kept for the resource, holds roles Tenancy knows nothing of,
/// and nothing is answered about roles.
/// </para>
/// <para>
/// <b>Starter roles.</b> A member that holds roles by the id of a role class kept for the resource,
/// <c>[CustomRole&lt;TRoleId, TResource&gt;]</c>, gets a class that makes the tenant's starter roles and makes the rest
/// follow, Tenancy's <c>ITenantStarterRoles</c>, and the registration adds it. So a new tenant has the roles right after
/// <c>ProvisionAsync</c>, and <c>SyncStarterRoles()</c> keeps them up to date. It makes a role with the role class's public
/// constructor that takes the role id, the tenant id and the draft, in any order, and the rules if it asks for them. The
/// role id must have a <c>Create()</c>. The registration has a parameter <c>makeRole</c>, for names, descriptions or ids
/// of the application's own. Where no constructor fits, <c>makeRole</c> is required, so the failure shows at the call. A
/// project that does not know the tenant id, which it writes at the call, always passes <c>makeRole</c>. The roles are read
/// through the role class's own filter, so a role class kept per tenant marks its tenant <c>[OfTenant]</c>. A member that
/// holds the tenant's own roles, or named roles, gets none of this.
/// </para>
/// <para>
/// Every type of the other package is named here by its metadata name, as text: this assembly references
/// nothing of it, and neither does the package it ships with.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class MembershipWithTenancyGenerator : IIncrementalGenerator
{
    private const string Registrations = "DDDToolkit.Supporting.Membership.EntityFramework.MembershipEntityFrameworkServiceCollectionExtensions";
    private const string RegistrationMethod = "AddMembership";

    /// <summary>The registration that takes the application's class beside the context: its type parameters, in order.</summary>
    private const int Member = 0, MemberOwnId = 1, MemberId = 2, RoleId = 3, Resource = 5, IdOfResource = 6, TypeParameters = 8;

    private const string NamedRole = "global::DDDToolkit.Supporting.Membership.NamedRole";
    private const string CustomRoleTemplate = "DDDToolkit.Supporting.Membership.CustomRoleAttribute`2";
    private const string CustomRoleDraftName = "DDDToolkit.Supporting.Membership.CustomRoleDraft";
    private const string MembershipRulesName = "DDDToolkit.Supporting.Membership.Access.MembershipRules";

    private const string Tenancy = "DDDToolkit.Supporting.Tenancy";
    private const string TenantTemplate = Tenancy + ".TenantAggregateAttribute`1";
    private const string SeatTemplate = Tenancy + ".SeatAggregateAttribute`1";
    private const string UnitTemplate = Tenancy + ".OrganizationUnitAttribute`1";
    private const string RoleTemplate = Tenancy + ".RoleAggregateAttribute`1";

    /// <summary>What the written class is made of. A project that cannot see every one of them gets nothing.</summary>
    private static readonly string[] Needed =
    [
        Tenancy + ".Access.TenancyAccessManager`4",
        Tenancy + ".Access.ITenancyQueries`4",
        Tenancy + ".Catalogue.TenancyCatalogue",
        Tenancy + ".EntityFramework.EfTenancyReadSource`4",
        Tenancy + ".ITenantStarterRoles",
    ];

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var joins = context.DeclaredTemplateEntities()
            .Combine(context.CompilationProvider)
            .SelectMany(static (all, cancellationToken) => Discover(all.Left, all.Right, cancellationToken));

        context.RegisterSourceOutput(joins, static (production, join) =>
            production.AddSource("GeneratedMembershipWithTenancyExtensions." + join.Registration + ".g.cs", SourceText.From(Emit(join), Encoding.UTF8)));
    }

    // ------------------------------------------------------------------ what to write

    private static ImmutableArray<Join> Discover(ImmutableArray<EntityDefinition> declared, Compilation compilation, CancellationToken cancellationToken)
    {
        if (compilation.GetTypeByMetadataName(Registrations) is not { } registrations
            || Needed.Any(needed => compilation.GetTypeByMetadataName(needed) is null))
        {
            return ImmutableArray<Join>.Empty;
        }

        // The classes the resource's own registration was closed over, as the generator that writes it closes
        // it: a class it refuses is refused here too, and reported there.
        var wrappers = TemplateRegistrations
            .Resolve(
                declared,
                compilation,
                cancellationToken,
                only: method => method.Name == RegistrationMethod
                                && method.TypeParameters.Length == TypeParameters
                                && SymbolEqualityComparer.Default.Equals(method.ContainingType, registrations))
            .SelectMany(static file => file.Wrappers)
            .Where(static wrapper => wrapper.TypeArguments.Count == TypeParameters)
            .ToList();
        if (wrappers.Count == 0)
        {
            return ImmutableArray<Join>.Empty;
        }

        var tenant = IdOfTheClassDeclaredWith(TenantTemplate, declared, compilation, cancellationToken);
        var seat = IdOfTheClassDeclaredWith(SeatTemplate, declared, compilation, cancellationToken);
        var unit = IdOfTheClassDeclaredWith(UnitTemplate, declared, compilation, cancellationToken);
        var role = IdOfTheClassDeclaredWith(RoleTemplate, declared, compilation, cancellationToken);

        var seen = tenant.Id is not null && seat.Id is not null && unit.Id is not null && role.Id is not null;
        var unseen = tenant.Classes == 0 && seat.Classes == 0 && unit.Classes == 0 && role.Classes == 0;
        if (!seen && !unseen)
        {
            // Some of Tenancy's classes and not the others, or two of one: the organization is not declared as
            // one yet, which is reported where its own registration is closed. Nothing is guessed here.
            return ImmutableArray<Join>.Empty;
        }

        // Only a project that cannot see Tenancy's role class has to tell the tenant's roles from kept ones another way:
        // by the role classes kept for a resource, wherever the project can see one.
        var kept = seen ? null : IdsOf(CustomRoleTemplate, declared, compilation, cancellationToken, everywhere: true);
        var roleClasses = RoleClasses(declared, compilation, cancellationToken);

        var joins = ImmutableArray.CreateBuilder<Join>();
        foreach (var wrapper in wrappers)
        {
            var arguments = wrapper.TypeArguments;
            var member = arguments[MemberId];
            var held = arguments[RoleId];

            if (seen && member != seat.Id)
            {
                // Members that are not seats: users of an identity provider, say, in an application that has an
                // organization for other things.
                continue;
            }

            var tenantsRoles = seen ? held == role.Id : held != NamedRole && !kept!.Contains(held);
            var resourceName = LastNameOf(arguments[Resource]);
            var keptRole = tenantsRoles || held == NamedRole
                ? null
                : roleClasses.FirstOrDefault(candidate => candidate.Id == held && candidate.Resource == arguments[Resource]);
            joins.Add(new Join(
                Registration: wrapper.Name + "WithTenancy",
                Plain: wrapper.Name,
                ClassName: "Generated" + resourceName + "MembershipWithTenancy",
                ResourceName: resourceName,
                Member: arguments[Member],
                MemberOwnId: arguments[MemberOwnId],
                Seat: member,
                Role: held,
                Resource: arguments[Resource],
                IdOfResource: arguments[IdOfResource],
                Tenant: tenant.Id,
                Unit: unit.Id,
                TenancyRole: seen ? role.Id : tenantsRoles ? held : null,
                TenantsRoles: tenantsRoles,
                KeptRole: keptRole?.Name,
                KeptRoleMade: keptRole?.Symbol is { } roleClass && tenant.Id is { } tenantId
                    ? NewRoleOf(roleClass, keptRole.Name, held, tenantId, compilation, cancellationToken)
                    : null));
        }

        return joins.ToImmutable();
    }

    /// <summary>
    /// The id of the one class declared with a template, and how many classes there are: in this project, or,
    /// when it declares none, in the projects it references, as a template takes a type from another class.
    /// The id is null unless there is exactly one class and it is declared over an entity id.
    /// </summary>
    private static (string? Id, int Classes) IdOfTheClassDeclaredWith(
        string template,
        ImmutableArray<EntityDefinition> declared,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var ids = IdsOf(template, declared, compilation, cancellationToken, everywhere: false);
        return (ids.Count == 1 ? ids[0] : null, ids.Count);
    }

    /// <summary>
    /// The id of each class declared with a template, one entry per class: this project's own, and those of
    /// the projects it references when it declares none or when <paramref name="everywhere"/> is asked. A
    /// class declared over something that is no entity id is counted and has no id to take.
    /// </summary>
    private static List<string?> IdsOf(
        string template,
        ImmutableArray<EntityDefinition> declared,
        Compilation compilation,
        CancellationToken cancellationToken,
        bool everywhere)
    {
        var ids = new List<string?>();
        if (compilation.GetTypeByMetadataName(template) is not { } attribute)
        {
            return ids;
        }

        var permission = attribute.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        foreach (var definition in declared)
        {
            if (definition.TemplateKey == permission)
            {
                ids.Add(definition.TemplateIdIsEntityId ? definition.IdType : null);
            }
        }

        if (everywhere || ids.Count == 0)
        {
            foreach (var source in DefinitionFactory.ReferencedSources(compilation, template, cancellationToken))
            {
                ids.Add(source.IdIsEntityId ? source.IdType : null);
            }
        }

        // A module that sees only the ids says which they are with a line, in the project or in a project of its module.
        if (!everywhere && ids.Count == 0)
        {
            var lines = TemplateIdLines.Here(compilation, cancellationToken);
            lines.AddRange(TemplateIdLines.InModule(compilation, ModuleBoundary.ModuleOf(compilation.Assembly), cancellationToken));
            var take = new TemplateRegistrations.Take(permission, string.Empty, template, TakeType: false, Argument: 0, IdOfArgument: false, AllowSeveral: false);
            foreach (var source in TemplateIdLines.SourcesFor(take, lines))
            {
                ids.Add(source.IdIsEntityId ? source.IdType : null);
            }
        }

        return ids;
    }

    /// <summary>
    /// The classes declared with the role template, in this project and in the projects it references: each by its name, the
    /// id it was declared with and the resource it is of. The symbol is null for a class whose declaration cannot be read.
    /// </summary>
    private static List<RoleClass> RoleClasses(ImmutableArray<EntityDefinition> declared, Compilation compilation, CancellationToken cancellationToken)
    {
        var found = new List<RoleClass>();
        if (compilation.GetTypeByMetadataName(CustomRoleTemplate) is not { } template)
        {
            return found;
        }

        var key = template.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        foreach (var definition in declared)
        {
            if (definition.TemplateKey == key && definition.MetadataName is { } metadataName && compilation.GetTypeByMetadataName(metadataName) is { } symbol)
            {
                Add(definition.Type.FullyQualifiedName, definition.IdType, symbol);
            }
        }

        foreach (var source in DefinitionFactory.ReferencedSources(compilation, CustomRoleTemplate, cancellationToken))
        {
            if (source.Symbol is { } symbol)
            {
                Add(source.FullyQualifiedName, source.IdType, symbol);
            }
        }

        return found;

        void Add(string name, string? id, INamedTypeSymbol symbol)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass?.OriginalDefinition, template)
                    && attribute.AttributeClass is { TypeArguments.Length: 2 } closed)
                {
                    found.Add(new RoleClass(name, id, closed.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), symbol));
                }
            }
        }
    }

    /// <summary>
    /// The expression that makes a role with the role class's public constructor, in the order of its parameters: the role
    /// id, the tenant id and the draft, and the rules if it asks for them. Null when no constructor takes exactly those, and
    /// when the role id has no <c>Create()</c>. A parameter of a type another generator writes cannot be bound here, so it
    /// is compared by name.
    /// </summary>
    private static string? NewRoleOf(INamedTypeSymbol role, string roleName, string roleId, string tenant, Compilation compilation, CancellationToken cancellationToken)
    {
        if (RoleIdOf(role) is { } id && IdCreation.Lacks(id, compilation, cancellationToken))
        {
            return null;
        }

        foreach (var constructor in role.InstanceConstructors.Where(static each => each.DeclaredAccessibility == Accessibility.Public).OrderByDescending(static each => each.Parameters.Length))
        {
            var arguments = new List<string>();
            foreach (var parameter in constructor.Parameters)
            {
                if (parameter.RefKind != RefKind.None)
                {
                    break;
                }

                if (IsType(parameter.Type, roleId))
                {
                    arguments.Add(roleId + ".Create()");
                }
                else if (IsType(parameter.Type, tenant))
                {
                    arguments.Add("tenant");
                }
                else if (IsType(parameter.Type, "global::" + CustomRoleDraftName))
                {
                    arguments.Add("draft");
                }
                else if (IsType(parameter.Type, "global::" + MembershipRulesName))
                {
                    arguments.Add("_rules");
                }
                else
                {
                    break;
                }
            }

            if (arguments.Count == constructor.Parameters.Length
                && arguments.Distinct(StringComparer.Ordinal).Count() == arguments.Count
                && arguments.Contains(roleId + ".Create()")
                && arguments.Contains("tenant")
                && arguments.Contains("draft"))
            {
                return "new " + roleName + "(" + string.Join(", ", arguments) + ")";
            }
        }

        return null;
    }

    /// <summary>The first type argument of the role template on <paramref name="role"/>: the role's id.</summary>
    private static ITypeSymbol? RoleIdOf(INamedTypeSymbol role)
    {
        foreach (var attribute in role.GetAttributes())
        {
            if (attribute.AttributeClass is { TypeArguments.Length: 2 } closed
                && closed.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).StartsWith("global::DDDToolkit.Supporting.Membership.CustomRoleAttribute<", StringComparison.Ordinal))
            {
                return closed.TypeArguments[0];
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="type"/> is the type named <paramref name="qualified"/>: by its name alone when the compiler cannot bind it yet.</summary>
    private static bool IsType(ITypeSymbol type, string qualified)
        => type.TypeKind == TypeKind.Error
            ? type.Name == LastNameOf(qualified)
            : type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == qualified;

    /// <summary>A type's own name, from its fully qualified one: <c>Course</c> of <c>global::Campus.Courses.Course</c>.</summary>
    private static string LastNameOf(string qualified)
    {
        var dot = qualified.LastIndexOfAny(['.', ':']);
        return dot < 0 ? qualified : qualified.Substring(dot + 1);
    }

    /// <summary>What is written for one member class whose members are seats.</summary>
    /// <param name="Registration">The registration's name: <c>AddCourseMembershipWithTenancy</c>.</param>
    /// <param name="Plain">The resource's own registration, which this one stands beside: <c>AddCourseMembership</c>.</param>
    /// <param name="ClassName">The class that answers: <c>GeneratedCourseMembershipWithTenancy</c>.</param>
    /// <param name="ResourceName">The resource's own name, for what the written code says of itself.</param>
    /// <param name="Member">The member class.</param>
    /// <param name="MemberOwnId">The member class's own id.</param>
    /// <param name="Seat">What a member is known by: the seat's id.</param>
    /// <param name="Role">What a member holds a role by.</param>
    /// <param name="Resource">The resource.</param>
    /// <param name="IdOfResource">The resource's id.</param>
    /// <param name="Tenant">Tenancy's tenant id, or null where the application writes it.</param>
    /// <param name="Unit">Tenancy's unit id, or null where the application writes it.</param>
    /// <param name="TenancyRole">Tenancy's role id, or null where the application writes it.</param>
    /// <param name="TenantsRoles">Whether the members hold the tenant's own roles, so the class answers about roles as well.</param>
    /// <param name="KeptRole">The role class kept for the resource, whose starter roles a tenant gets; null for any other member.</param>
    /// <param name="KeptRoleMade">The expression that makes a role with the role class's constructor; null when none fits, and <c>makeRole</c> is required.</param>
    private sealed record Join(
        string Registration,
        string Plain,
        string ClassName,
        string ResourceName,
        string Member,
        string MemberOwnId,
        string Seat,
        string Role,
        string Resource,
        string IdOfResource,
        string? Tenant,
        string? Unit,
        string? TenancyRole,
        bool TenantsRoles,
        string? KeptRole,
        string? KeptRoleMade);

    /// <summary>A class declared with the role template.</summary>
    /// <param name="Name">The class, in full.</param>
    /// <param name="Id">Its id, in full.</param>
    /// <param name="Resource">The resource it is of, in full.</param>
    /// <param name="Symbol">The class.</param>
    private sealed record RoleClass(string Name, string? Id, string Resource, INamedTypeSymbol Symbol);

    // ------------------------------------------------------------------ writing it

    private const string Services = "global::Microsoft.Extensions.DependencyInjection";
    private const string EntityFramework = "global::Microsoft.EntityFrameworkCore";
    private const string Membership = "global::DDDToolkit.Supporting.Membership";
    private const string TenancyAccessNamespace = "global::" + Tenancy + ".Access";
    private const string CurrentUserSnapshot = "global::DDDToolkit.Abstractions.Access.CurrentUserSnapshot";
    private const string CancellationTokenType = "global::System.Threading.CancellationToken";
    private const string TenancyEntityFramework = "global::" + Tenancy + ".EntityFramework";
    private const string Queryable = "global::System.Linq.Queryable";
    private const string Asynchronous = EntityFramework + ".EntityFrameworkQueryableExtensions";

    private static string Emit(Join join)
    {
        // What the application writes itself is a type parameter, of the registration and of the class alike.
        var open = new List<string>();
        var tenant = join.Tenant ?? Open("TTenantId");
        var unit = join.Unit ?? Open("TUnitId");
        var role = join.TenancyRole ?? Open("TRoleId");
        string Open(string parameter)
        {
            open.Add(parameter);
            return parameter;
        }

        var parameters = "<" + string.Join(", ", new[] { "TContext" }.Concat(open)) + ">";
        var constraints = new List<string> { "where TContext : " + EntityFramework + ".DbContext" };
        constraints.AddRange(open.Select(static parameter =>
            "where " + parameter + " : struct, global::DDDToolkit.Abstractions.Interfaces.IEntityId, global::System.IEquatable<" + parameter + ">"));

        var ids = tenant + ", " + join.Seat + ", " + unit + ", " + role;
        var tenancy = TenancyAccessNamespace + ".TenancyAccessManager<" + ids + ">";
        var queries = TenancyAccessNamespace + ".ITenancyQueries<" + ids + ">";
        var currentUser = TenancyAccessNamespace + ".TenancyUsers.Current<" + tenant + ", " + join.Seat + ">()";
        var catalogue = "global::" + Tenancy + ".Catalogue.TenancyCatalogue";
        var joined = join.ClassName + parameters;
        var starterRoles = "Generated" + join.ResourceName + "StarterRoles";
        var writtenStarterRoles = starterRoles + parameters;
        var makeRole = join.KeptRole is { } kept
            ? "global::System.Func<global::System.IServiceProvider, " + tenant + ", " + Membership + ".CustomRoleDraft, " + kept + ">"
            : null;

        var writer = new CodeWriter().Header();
        writer.Line("namespace DDDToolkit.Supporting.Membership.EntityFramework;");
        writer.Line();

        writer.Line("/// <summary>The registrations of this project's resources whose members are seats of a tenant.</summary>");
        using (writer.Block("internal static partial class GeneratedMembershipWithTenancyExtensions"))
        {
            writer.Line("/// <summary>");
            writer.Line("/// Registers " + join.ResourceName + " with its members, as " + join.Plain + " does, for members that are seats of a tenant:");
            writer.Line("/// together with " + join.ClassName + ", which answers what the rules ask of the organization from Tenancy's own queries.");
            writer.Line("/// </summary>");
            writer.Line("/// <typeparam name=\"TContext\">The context that maps " + join.ResourceName + " with its members, and Tenancy's read model beside them.</typeparam>");
            foreach (var parameter in open)
            {
                // Only a project that sees none of the organization's classes is asked for these, and it is told which is which.
                writer.Line("/// <typeparam name=\"" + parameter + "\">" + WhatItIs(parameter) + ", which this project sees no class of and so cannot be given.</typeparam>");
            }

            if (makeRole is not null)
            {
                writer.Line("/// <param name=\"services\">The service collection.</param>");
                writer.Line("/// <param name=\"rules\">The rules of " + join.ResourceName + ".</param>");
                writer.Line("/// <param name=\"makeRole\">");
                writer.Line("/// Makes a role of a starter role in a tenant, for names, descriptions or ids of the application's own. It gets the scope's services,");
                writer.Line(join.KeptRoleMade is null
                    ? "/// the tenant and the starter role's draft. Required here: no public constructor of the role class takes the role id, the tenant id and the draft."
                    : "/// the tenant and the starter role's draft. Left out, the role class's constructor makes it, with the name the rules declare and a new id.");
                writer.Line("/// </param>");
            }

            writer.Line("/// <exception cref=\"global::System.ArgumentException\">The rules take a member from the caller's token, where a member is a seat.</exception>");
            using (Declared(
                       writer,
                       [
                           "public static " + Services + ".IServiceCollection " + join.Registration + parameters + "(",
                           "    this " + Services + ".IServiceCollection services,",
                           makeRole is null
                               ? "    " + Membership + ".Access.MembershipRules rules)"
                               : "    " + Membership + ".Access.MembershipRules rules,",
                           .. makeRole is null
                               ? Array.Empty<string>()
                               : new[] { "    " + makeRole + (join.KeptRoleMade is null ? " makeRole)" : "? makeRole = null)") },
                           .. constraints.Select(static constraint => "    " + constraint),
                       ]))
            {
                writer.Line("global::System.ArgumentNullException.ThrowIfNull(services);");
                writer.Line("global::System.ArgumentNullException.ThrowIfNull(rules);");
                if (makeRole is not null && join.KeptRoleMade is null)
                {
                    writer.Line("global::System.ArgumentNullException.ThrowIfNull(makeRole);");
                }

                writer.Line();

                // The one thing the resource's own registration cannot tell: a seat's id is over the same value as a
                // user's, so rules that take the member from the token would be accepted, and read a user for a seat.
                // Who a member is follows from the member id where the rules say nothing: here, the caller's seat.
                using (writer.Block("if (rules.Members.Kind is " + Membership + ".Access.MemberSourceKind.CurrentUserId or " + Membership + ".Access.MemberSourceKind.Claim)"))
                {
                    writer.Line("throw new global::System.ArgumentException(");
                    writer.Line("    \"The rules '\" + rules.Name + \"' take a member from the caller's token (MemberSource.\" + rules.Members.Kind + \"), and "
                                + join.Registration + " registers " + join.ResourceName + " for members that are seats of a tenant, which no token carries. \"");
                    writer.Line("    + \"Leave members out of the rules, and a member is the caller's seat, or register " + join.ResourceName
                                + " with " + join.Plain + " where its members are not seats.\",");
                    writer.Line("    nameof(rules));");
                }

                writer.Line();
                writer.Line("// One for each scope, made with the rules it is registered with. One the application registered before stays.");
                writer.Line(Services + ".Extensions.ServiceCollectionDescriptorExtensions.TryAddScoped<" + joined + ">(");
                writer.Line("    services,");
                writer.Line("    provider => new " + joined + "(provider, rules));");
                writer.Line();
                if (makeRole is not null)
                {
                    writer.Line("// A new tenant gets the starter roles, and every start of the host makes them follow: Tenancy runs this one for both.");
                    writer.Line(Services + ".Extensions.ServiceCollectionDescriptorExtensions.TryAddEnumerable(");
                    writer.Line("    services,");
                    writer.Line("    " + Services + ".ServiceDescriptor.Scoped<global::" + Tenancy + ".ITenantStarterRoles, " + writtenStarterRoles + ">(");
                    writer.Line("        provider => new " + writtenStarterRoles + "(provider, rules, makeRole)));");
                    writer.Line();
                }

                writer.Line("return " + Membership + ".EntityFramework.MembershipEntityFrameworkServiceCollectionExtensions.AddMembership<");
                writer.Line("    " + join.Member + ", " + join.MemberOwnId + ", " + join.Seat + ", " + join.Role + ", TContext, " + join.Resource + ", " + join.IdOfResource + ",");
                writer.Line("    " + joined + ">(services, rules);");
            }
        }

        writer.Line();
        writer.Line("/// <summary>");
        writer.Line("/// What joins " + join.ResourceName + " to the organization: each of Membership's questions about it is forwarded to one of Tenancy's.");
        writer.Line("/// A member is the caller's seat, somebody can be made a member while that seat is active, and a permission the organization");
        writer.Line("/// gives at a unit counts for what sits there and below." + (join.TenantsRoles ? " The roles a member holds are the tenant's own." : string.Empty));
        writer.Line("/// </summary>");
        var answered = new List<string>
        {
            Membership + ".Access.ICurrentMember<" + join.IdOfResource + ", " + join.Seat + ">",
            Membership + ".Managers.IMemberDirectory<" + join.IdOfResource + ", " + join.Seat + ">",
            Membership + ".EntityFramework.IPlacesReached<" + join.IdOfResource + ", " + unit + ">",
            Membership + ".Managers.IOwnPlaceWork<" + join.IdOfResource + ", " + join.Seat + ">",
            Membership + ".Managers.ICustomRoleAccess<" + join.Resource + ">",
        };
        if (join.TenantsRoles)
        {
            answered.Add(Membership + ".EntityFramework.IRolesWithPermission<" + join.IdOfResource + ", " + join.Role + ">");
            answered.Add(Membership + ".Access.IMemberRoles<" + join.IdOfResource + ", " + join.Role + ">");
        }

        using (Declared(
                   writer,
                   [
                       "internal sealed class " + joined + " :",
                       .. answered.Select((port, index) => "    " + port + (index < answered.Count - 1 ? "," : string.Empty)),
                       .. constraints.Select(static constraint => "    " + constraint),
                   ]))
        {
            writer.Line("private readonly global::System.IServiceProvider _services;");
            writer.Line("private readonly " + tenancy + " _access;");
            writer.Line("private readonly " + catalogue + " _catalogue;");
            writer.Line();
            writer.Line("/// <param name=\"services\">The services of the scope the questions are asked in.</param>");
            writer.Line("/// <param name=\"rules\">The rules the resource is registered with.</param>");
            using (writer.Block("public " + join.ClassName + "(global::System.IServiceProvider services, " + Membership + ".Access.MembershipRules rules)"))
            {
                writer.Line("_services = services;");
                writer.Line("_access = " + Services + ".ServiceProviderServiceExtensions.GetRequiredService<" + tenancy + ">(services);");
                writer.Line("_catalogue = " + Services + ".ServiceProviderServiceExtensions.GetRequiredService<" + catalogue + ">(services);");
            }

            writer.Line();
            writer.Line("/// <summary>The caller's seat, while it is one: the application's own work in a tenant acts for a seat at most, and is nobody's member.</summary>");
            using (writer.Block("public " + join.Seat + "? Find(" + CurrentUserSnapshot + " user)"))
            {
                writer.Line("var current = " + currentUser + ";");
                writer.Line("return current.Kind == " + TenancyAccessNamespace + ".TenancyUserKind.Seat ? current.Seat : null;");
            }

            writer.Line();
            writer.Line("/// <summary>Refuses somebody who has no seat that counts in the tenant, as Tenancy refuses them, before anything is read.</summary>");
            writer.Line("public void Require(" + CurrentUserSnapshot + " user) => _access.CurrentTenant.Require();");

            writer.Line();
            writer.Line("/// <summary>");
            writer.Line("/// Whether <paramref name=\"member\"/> is an active seat of the tenant the caller acts in, among the seats the caller reads:");
            writer.Line("/// every seat of the tenant by default, and on Postgres those a read rule of the application's on its seat class lets it read.");
            writer.Line("/// A seat the caller does not read is refused as one that does not exist is, so the refusal says nothing about it.");
            writer.Line("/// </summary>");
            writer.Line("public async global::System.Threading.Tasks.ValueTask<bool> IsActiveAsync(" + join.Seat + " member, " + CancellationTokenType + " cancellationToken)");
            writer.Line("    => await AskAsync(");
            writer.Line("        queries => " + Asynchronous + ".AnyAsync(");
            writer.Line("            queries.Seats(),");
            writer.Line("            seat => seat.Id.Equals(member) && seat.Status == global::" + Tenancy + ".SeatStatus.Active,");
            writer.Line("            cancellationToken),");
            writer.Line("        cancellationToken).ConfigureAwait(false);");

            writer.Line();
            writer.Line("/// <summary>");
            writer.Line("/// Begins the resource's own work in the caller's tenant, for the caller's seat, for the save of a change to the caller's own");
            writer.Line("/// place: Tenancy's system work in the tenant, in the scope the resource's rules name or Tenancy's own where they name none,");
            writer.Line("/// recorded as done for that seat.");
            writer.Line("/// </summary>");
            writer.Line("public global::System.IDisposable Begin(" + join.Seat + " member, string? scope)");
            writer.Line("    => scope is null");
            writer.Line("        ? _access.BeginSystemIn(_access.CurrentTenant.Require().Id, member)");
            writer.Line("        : _access.BeginSystemIn(_access.CurrentTenant.Require().Id, member, scope);");

            writer.Line();
            writer.Line("/// <summary>");
            writer.Line("/// Requires the caller to hold <paramref name=\"permission\"/>, the one the rules name for changing the roles a customer makes,");
            writer.Line("/// for the whole tenant, as Tenancy refuses it: somebody with no seat that counts in the tenant first, as nobody; system");
            writer.Line("/// work in the tenant holds every permission there. A permission the catalogue does not know is held by nobody.");
            writer.Line("/// </summary>");
            using (writer.Block("public async global::System.Threading.Tasks.ValueTask RequireAsync(string permission, " + CancellationTokenType + " cancellationToken)"))
            {
                writer.Line("_access.CurrentTenant.Require();");
                writer.Line("if (!_catalogue.Knows(permission)");
                using (writer.Block("    || !await AskAsync(queries => queries.HoldsAtTenantAsync(permission, cancellationToken), cancellationToken).ConfigureAwait(false))"))
                {
                    writer.Line("throw global::" + Tenancy + ".TenancyRefusals.Refuse(global::" + Tenancy + ".TenancyRefusals.NotPermitted, (\"Permission\", permission));");
                }
            }

            writer.Line();
            writer.Line("/// <summary>");
            writer.Line("/// The units the caller's hold of <paramref name=\"permission\"/> reaches: where it is held, and every unit below. A permission the");
            writer.Line("/// catalogue does not know is one no role of the organization can hold, so it is held nowhere: Tenancy itself would");
            writer.Line("/// take the question for a typo.");
            writer.Line("/// </summary>");
            using (writer.Block("public global::System.Linq.IQueryable<" + unit + "> PlacesReached(" + EntityFramework + ".DbContext context, " + CurrentUserSnapshot + " user, string permission)"))
            {
                writer.Line("var queries = QueriesOver(context);");
                writer.Line("return _catalogue.Knows(permission)");
                writer.Line("    ? queries.UnitsWhereIHold(permission)");
                writer.Line("    : " + Queryable + ".Select(" + Queryable + ".Where(queries.Units(), unit => false), unit => unit.Id);");
            }

            if (join.TenantsRoles)
            {
                writer.Line();
                writer.Line("/// <summary>The tenant's roles in use that give <paramref name=\"permission\"/>; none for a permission the catalogue does not know.</summary>");
                using (writer.Block("public global::System.Linq.IQueryable<" + join.Role + "> RolesWith(" + EntityFramework + ".DbContext context, " + CurrentUserSnapshot + " user, string permission)"))
                {
                    writer.Line("var queries = QueriesOver(context);");
                    writer.Line("return _catalogue.Knows(permission)");
                    writer.Line("    ? queries.RolesWithPermission(permission)");
                    writer.Line("    : " + Queryable + ".Select(" + Queryable + ".Where(queries.Roles(), role => false), role => role.Id);");
                }

                writer.Line();
                writer.Line("/// <summary>Whether <paramref name=\"role\"/> is a role of the tenant's in use.</summary>");
                writer.Line("public async global::System.Threading.Tasks.ValueTask<bool> ExistsAsync(" + join.Role + " role, " + CancellationTokenType + " cancellationToken)");
                writer.Line("    => await AskAsync(");
                writer.Line("        queries => " + Asynchronous + ".AnyAsync(");
                writer.Line("            queries.Roles(),");
                writer.Line("            row => row.Id.Equals(role) && row.Status == global::" + Tenancy + ".RoleStatus.Active,");
                writer.Line("            cancellationToken),");
                writer.Line("        cancellationToken).ConfigureAwait(false);");
            }

            writer.Line();
            writer.Line("/// <summary>");
            writer.Line("/// Asks Tenancy outside a statement of the resource's: over the request's own context where the scope has one, and");
            writer.Line("/// otherwise over a context of the factory's, disposed when it is answered. The context maps Tenancy's read model.");
            writer.Line("/// </summary>");
            using (Declared(
                       writer,
                       [
                           "private async global::System.Threading.Tasks.Task<TAnswer> AskAsync<TAnswer>(",
                           "    global::System.Func<" + queries + ", global::System.Threading.Tasks.Task<TAnswer>> ask,",
                           "    " + CancellationTokenType + " cancellationToken)",
                       ]))
            {
                using (writer.Block("if (" + Services + ".ServiceProviderServiceExtensions.GetService<TContext>(_services) is { } own)"))
                {
                    writer.Line("return await ask(QueriesOver(own)).ConfigureAwait(false);");
                }

                writer.Line();
                writer.Line("var factory = " + Services + ".ServiceProviderServiceExtensions.GetRequiredService<" + EntityFramework + ".IDbContextFactory<TContext>>(_services);");
                writer.Line("var context = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);");
                // Called by its full name: what is written here depends on no using of the application's.
                using (writer.Block("await using (global::System.Threading.Tasks.TaskAsyncEnumerableExtensions.ConfigureAwait(context, false))"))
                {
                    writer.Line("return await ask(QueriesOver(context)).ConfigureAwait(false);");
                }
            }

            writer.Line();
            writer.Line("/// <summary>Tenancy's queries for the current caller over <paramref name=\"context\"/>, at this moment.</summary>");
            writer.Line("private " + queries + " QueriesOver(" + EntityFramework + ".DbContext context)");
            writer.Line("    => _access.In(new " + TenancyEntityFramework + ".EfTenancyReadSource<" + ids + ">(context), " + TenancyEntityFramework + ".EfQueryExecutor.Instance);");
        }

        if (join.KeptRole is { } role2 && makeRole is not null)
        {
            WriteStarterRoles(writer, join, role2, makeRole, starterRoles, parameters, constraints, tenancy);
        }

        return writer.ToString();
    }

    /// <summary>
    /// The class Tenancy runs in a tenant: it makes the starter roles the tenant lacks, and makes the rest follow, through the
    /// role manager of the role class. It runs as system work in the tenant, in a scope of its own, so the store's context
    /// runs as that work and reads the tenant's roles through the role class's filter.
    /// </summary>
    private static void WriteStarterRoles(CodeWriter writer, Join join, string role, string makeRole, string name, string parameters, List<string> constraints, string tenancy)
    {
        var id = join.Role;
        writer.Line();
        writer.Line("/// <summary>");
        writer.Line("/// The starter roles of " + join.ResourceName + " in the tenant the current work acts in. Tenancy runs this after it provisions a tenant and at every");
        writer.Line("/// sync of the starter roles: it makes the roles the tenant lacks and makes the rest follow the rules.");
        writer.Line("/// </summary>");
        using (Declared(
                   writer,
                   [
                       "internal sealed class " + name + parameters + " :",
                       "    global::" + Tenancy + ".ITenantStarterRoles",
                       .. constraints.Select(static constraint => "    " + constraint),
                   ]))
        {
            writer.Line("private readonly global::System.IServiceProvider _services;");
            writer.Line("private readonly " + Membership + ".Access.MembershipRules _rules;");
            writer.Line("private readonly " + makeRole + (join.KeptRoleMade is null ? " _makeRole;" : "? _makeRole;"));
            writer.Line();
            writer.Line("/// <param name=\"services\">The services of the scope the work runs in.</param>");
            writer.Line("/// <param name=\"rules\">The rules the resource is registered with.</param>");
            writer.Line("/// <param name=\"makeRole\">What makes a role, where the application says.</param>");
            using (writer.Block("public " + name + "(global::System.IServiceProvider services, " + Membership + ".Access.MembershipRules rules, " + makeRole + (join.KeptRoleMade is null ? " makeRole)" : "? makeRole)")))
            {
                writer.Line("_services = services;");
                writer.Line("_rules = rules;");
                writer.Line("_makeRole = makeRole;");
            }

            writer.Line();
            writer.Line("/// <inheritdoc />");
            using (writer.Block("public async global::System.Threading.Tasks.Task<global::" + Tenancy + ".TenantStarterRolesFollowed> FollowAsync(" + CancellationTokenType + " cancellationToken)"))
            {
                writer.Line("var tenant = " + Services + ".ServiceProviderServiceExtensions.GetRequiredService<" + tenancy + ">(_services).CurrentTenant.Require().Id;");
                writer.Line("var roles = new " + Membership + ".Managers.CustomRoleManager<" + role + ", " + id + ", " + join.Resource + ">(");
                writer.Line("    " + Services + ".ServiceProviderServiceExtensions.GetRequiredService<" + Membership + ".Managers.ICustomRoleStore<" + role + ", " + id + ">>(_services),");
                writer.Line("    " + Services + ".ServiceProviderServiceExtensions.GetRequiredService<" + Membership + ".Access.MembershipAccessManager<" + join.Resource + ">>(_services),");
                writer.Line("    " + Services + ".ServiceProviderServiceExtensions.GetRequiredService<global::DDDToolkit.Abstractions.Access.ICurrentUser>(_services),");
                writer.Line("    " + Services + ".ServiceProviderServiceExtensions.GetService<" + Membership + ".Managers.ICustomRoleAccess<" + join.Resource + ">>(_services));");
                writer.Line("var followed = await roles.FollowStarterRolesAsync(");
                writer.Line(join.KeptRoleMade is null
                    ? "    draft => _makeRole(_services, tenant, draft),"
                    : "    draft => _makeRole is { } makeRole ? makeRole(_services, tenant, draft) : " + join.KeptRoleMade + ",");
                writer.Line("    cancellationToken).ConfigureAwait(false);");
                writer.Line("return new global::" + Tenancy + ".TenantStarterRolesFollowed(followed.Made.Count, followed.Changed.Count, followed.WithoutTheirStarterRole.Count);");
            }
        }
    }

    /// <summary>What an id the application names where it registers stands for, as its registration says it.</summary>
    private static string WhatItIs(string parameter) => parameter switch
    {
        "TTenantId" => "The id of the application's tenant class",
        "TUnitId" => "The id of the application's organization unit class",
        _ => "The id of the application's role class for the organization's roles",
    };

    /// <summary>Writes a declaration of several lines and opens its body.</summary>
    private static IDisposable Declared(CodeWriter writer, string[] lines)
    {
        for (var index = 0; index < lines.Length - 1; index++)
        {
            writer.Line(lines[index]);
        }

        return writer.Block(lines[lines.Length - 1]);
    }
}
