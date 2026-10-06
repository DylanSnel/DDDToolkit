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
/// the organization, from Tenancy's own answers, and a registration named after the resource:
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
/// <b>When.</b> Only where the project can see what the written class is made of: Tenancy's questions and
/// answers, its way of asking them over a context, and its catalogue. Without them nothing is written, so an
/// application without an organization gets nothing it could not compile.
/// </para>
/// <para>
/// <b>Which ids.</b> A member is a seat, so the member class's member id is Tenancy's seat id. Tenancy's
/// tenant, unit and role id are the application's own as well, and are taken from the classes it declares
/// with Tenancy's templates, in the project or in the projects it references. A member class whose member id
/// is not the seat's id is no member list of seats, and gets nothing. A project that sees none of those
/// classes, a module that knows the organization only by its ids, cannot be told which of its ids they are:
/// its registration leaves them for the application to write. Nor can it be told a seat's id from another,
/// so there each member class gets the registration, which takes its member id for the seat's, and the
/// application calls it for the resources whose members are seats.
/// </para>
/// <para>
/// <b>Which roles.</b> A member that holds roles by the id of Tenancy's role class holds the tenant's own
/// roles, and the written class answers which of them give a key and which there are. A member that holds
/// roles by name, or by the id of a role class kept for the resource, holds roles Tenancy knows nothing of,
/// and nothing is answered about roles.
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
    private const string KeptRoleTemplate = "DDDToolkit.Supporting.Membership.KeptRoleAttribute`2";

    private const string Tenancy = "DDDToolkit.Supporting.Tenancy";
    private const string TenantTemplate = Tenancy + ".TenantAggregateAttribute`1";
    private const string SeatTemplate = Tenancy + ".SeatAggregateAttribute`1";
    private const string UnitTemplate = Tenancy + ".OrganizationUnitAttribute`1";
    private const string RoleTemplate = Tenancy + ".RoleAggregateAttribute`1";

    /// <summary>What the written class is made of. A project that cannot see every one of them gets nothing.</summary>
    private static readonly string[] Needed =
    [
        Tenancy + ".Access.ITenancyAnswers`4",
        Tenancy + ".Access.ITenancyQuestions`4",
        Tenancy + ".Catalogue.TenancyCatalogue",
        Tenancy + ".EntityFramework.TenancyAnswersEntityFrameworkExtensions",
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
        var kept = seen ? null : IdsOf(KeptRoleTemplate, declared, compilation, cancellationToken, everywhere: true);

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
                TenantsRoles: tenantsRoles));
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

        var key = attribute.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        foreach (var definition in declared)
        {
            if (definition.TemplateKey == key)
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

        return ids;
    }

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
        bool TenantsRoles);

    // ------------------------------------------------------------------ writing it

    private const string Services = "global::Microsoft.Extensions.DependencyInjection";
    private const string EntityFramework = "global::Microsoft.EntityFrameworkCore";
    private const string Membership = "global::DDDToolkit.Supporting.Membership";
    private const string TenancyAccessNamespace = "global::" + Tenancy + ".Access";
    private const string Caller = "global::DDDToolkit.Abstractions.Access.Caller";
    private const string CancellationTokenType = "global::System.Threading.CancellationToken";
    private const string Over = "global::" + Tenancy + ".EntityFramework.TenancyAnswersEntityFrameworkExtensions.Over";
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

        var answers = TenancyAccessNamespace + ".ITenancyAnswers<" + tenant + ", " + join.Seat + ", " + unit + ", " + role + ">";
        var questions = TenancyAccessNamespace + ".ITenancyQuestions<" + tenant + ", " + join.Seat + ", " + unit + ", " + role + ">";
        var catalogue = "global::" + Tenancy + ".Catalogue.TenancyCatalogue";
        var joined = join.ClassName + parameters;

        var writer = new CodeWriter().Header();
        writer.Line("namespace DDDToolkit.Supporting.Membership.EntityFramework;");
        writer.Line();

        writer.Line("/// <summary>The registrations of this project's resources whose members are seats of a tenant.</summary>");
        using (writer.Block("internal static partial class GeneratedMembershipWithTenancyExtensions"))
        {
            writer.Line("/// <summary>");
            writer.Line("/// Registers " + join.ResourceName + " with its members, as " + join.Plain + " does, for members that are seats of a tenant:");
            writer.Line("/// together with " + join.ClassName + ", which answers what the rules ask of the organization from Tenancy's own answers.");
            writer.Line("/// </summary>");
            writer.Line("/// <typeparam name=\"TContext\">The context that maps " + join.ResourceName + " with its members, and Tenancy's read model beside them.</typeparam>");
            foreach (var parameter in open)
            {
                // Only a project that sees none of the organization's classes is asked for these, and it is told which is which.
                writer.Line("/// <typeparam name=\"" + parameter + "\">" + WhatItIs(parameter) + ", which this project sees no class of and so cannot be given.</typeparam>");
            }

            writer.Line("/// <exception cref=\"global::System.ArgumentException\">The rules take a member from the caller itself, where a member is a seat.</exception>");
            using (Declared(
                       writer,
                       [
                           "public static " + Services + ".IServiceCollection " + join.Registration + parameters + "(",
                           "    this " + Services + ".IServiceCollection services,",
                           "    " + Membership + ".Access.MembershipRules rules)",
                           .. constraints.Select(static constraint => "    " + constraint),
                       ]))
            {
                writer.Line("global::System.ArgumentNullException.ThrowIfNull(services);");
                writer.Line("global::System.ArgumentNullException.ThrowIfNull(rules);");
                writer.Line();

                // The one thing the resource's own registration cannot tell: a seat's id is over the same value as a
                // user's, so rules that take the member from the caller would be accepted, and read a user for a seat.
                using (writer.Block("if (rules.Members.Kind != " + Membership + ".Access.MemberSourceKind.Resolved)"))
                {
                    writer.Line("throw new global::System.ArgumentException(");
                    writer.Line("    \"The rules '\" + rules.Name + \"' take a member from the caller itself (MemberSource.\" + rules.Members.Kind + \"), and "
                                + join.Registration + " registers " + join.ResourceName + " for members that are seats of a tenant, which no token carries. \"");
                    writer.Line("    + \"Say members: MemberSource.Resolved(\\\"tenancy/caller_seat\\\") in the rules, or register " + join.ResourceName
                                + " with " + join.Plain + " where its members are not seats.\",");
                    writer.Line("    nameof(rules));");
                }

                writer.Line();
                writer.Line("// One for each scope, made with the rules it is registered with. One the application registered before stays.");
                writer.Line(Services + ".Extensions.ServiceCollectionDescriptorExtensions.TryAddScoped<" + joined + ">(");
                writer.Line("    services,");
                writer.Line("    provider => new " + joined + "(provider, rules));");
                writer.Line();
                writer.Line("return " + Membership + ".EntityFramework.MembershipEntityFrameworkServiceCollectionExtensions.AddMembership<");
                writer.Line("    " + join.Member + ", " + join.MemberOwnId + ", " + join.Seat + ", " + join.Role + ", TContext, " + join.Resource + ", " + join.IdOfResource + ",");
                writer.Line("    " + joined + ">(services, rules);");
            }
        }

        writer.Line();
        writer.Line("/// <summary>");
        writer.Line("/// What joins " + join.ResourceName + " to the organization: each of Membership's questions about it is forwarded to one of Tenancy's.");
        writer.Line("/// A member is the caller's seat, somebody can be made a member while that seat is active, and a key the organization");
        writer.Line("/// gives at a unit counts for what sits there and below." + (join.TenantsRoles ? " The roles a member holds are the tenant's own." : string.Empty));
        writer.Line("/// </summary>");
        var answered = new List<string>
        {
            Membership + ".Access.ICallerMember<" + join.IdOfResource + ", " + join.Seat + ">",
            Membership + ".UseCases.IMemberDirectory<" + join.IdOfResource + ", " + join.Seat + ">",
            Membership + ".EntityFramework.IPlacesReached<" + join.IdOfResource + ", " + unit + ">",
        };
        if (join.TenantsRoles)
        {
            answered.Add(Membership + ".EntityFramework.IRolesWithKey<" + join.IdOfResource + ", " + join.Role + ">");
            answered.Add(Membership + ".UseCases.IMemberRoles<" + join.IdOfResource + ", " + join.Role + ">");
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
            writer.Line("private readonly " + answers + " _tenancy;");
            writer.Line("private readonly " + catalogue + " _catalogue;");
            if (join.TenantsRoles)
            {
                writer.Line("private readonly string _ownerRole;");
            }

            writer.Line();
            writer.Line("/// <param name=\"services\">The services of the scope the questions are asked in.</param>");
            writer.Line("/// <param name=\"rules\">The rules the resource is registered with.</param>");
            using (writer.Block("public " + join.ClassName + "(global::System.IServiceProvider services, " + Membership + ".Access.MembershipRules rules)"))
            {
                writer.Line("_services = services;");
                writer.Line("_tenancy = " + Services + ".ServiceProviderServiceExtensions.GetRequiredService<" + answers + ">(services);");
                writer.Line("_catalogue = " + Services + ".ServiceProviderServiceExtensions.GetRequiredService<" + catalogue + ">(services);");
                if (join.TenantsRoles)
                {
                    writer.Line("_ownerRole = rules.OwnerRole;");
                }
            }

            writer.Line();
            writer.Line("/// <summary>The caller's seat, while it is one: the application's own work in a tenant acts for a seat at most, and is nobody's member.</summary>");
            using (writer.Block("public " + join.Seat + "? Find(" + Caller + " caller)"))
            {
                writer.Line("var current = _tenancy.Caller;");
                writer.Line("return current.Kind == " + TenancyAccessNamespace + ".TenancyCallerKind.Seat ? current.Seat : null;");
            }

            writer.Line();
            writer.Line("/// <summary>Refuses somebody who has no seat that counts in the tenant, as Tenancy refuses them, before anything is read.</summary>");
            writer.Line("public void Require(" + Caller + " caller) => _tenancy.RequireTenant();");

            writer.Line();
            writer.Line("/// <summary>");
            writer.Line("/// Whether <paramref name=\"member\"/> is an active seat of the tenant the caller acts in, among the seats the caller reads:");
            writer.Line("/// every seat of the tenant by default, and on Postgres those a read rule of the application's on its seat class lets it read.");
            writer.Line("/// A seat the caller does not read is refused as one that does not exist is, so the refusal says nothing about it.");
            writer.Line("/// </summary>");
            writer.Line("public async global::System.Threading.Tasks.ValueTask<bool> IsActiveAsync(" + join.Seat + " member, " + CancellationTokenType + " cancellationToken)");
            writer.Line("    => await AskAsync(");
            writer.Line("        tenancy => " + Asynchronous + ".AnyAsync(");
            writer.Line("            tenancy.Seats(),");
            writer.Line("            seat => seat.Id.Equals(member) && seat.Status == global::" + Tenancy + ".SeatStatus.Active,");
            writer.Line("            cancellationToken),");
            writer.Line("        cancellationToken).ConfigureAwait(false);");

            writer.Line();
            writer.Line("/// <summary>");
            writer.Line("/// The units the caller's hold of <paramref name=\"key\"/> reaches: where it is held, and every unit below. A key the");
            writer.Line("/// catalogue does not know is one no role of the organization can hold, so it is held nowhere: Tenancy itself would");
            writer.Line("/// take the question for a typo.");
            writer.Line("/// </summary>");
            using (writer.Block("public global::System.Linq.IQueryable<" + unit + "> PlacesReached(" + EntityFramework + ".DbContext context, " + Caller + " caller, string key)"))
            {
                writer.Line("var tenancy = " + Over + "(_tenancy, context);");
                writer.Line("return _catalogue.Knows(key)");
                writer.Line("    ? tenancy.UnitsWhereIHold(key)");
                writer.Line("    : " + Queryable + ".Select(" + Queryable + ".Where(tenancy.Units(), unit => false), unit => unit.Id);");
            }

            if (join.TenantsRoles)
            {
                writer.Line();
                writer.Line("/// <summary>The tenant's roles in use that give <paramref name=\"key\"/>; none for a key the catalogue does not know.</summary>");
                using (writer.Block("public global::System.Linq.IQueryable<" + join.Role + "> RolesWith(" + EntityFramework + ".DbContext context, " + Caller + " caller, string key)"))
                {
                    writer.Line("var tenancy = " + Over + "(_tenancy, context);");
                    writer.Line("return _catalogue.Knows(key)");
                    writer.Line("    ? tenancy.RolesWithKey(key)");
                    writer.Line("    : " + Queryable + ".Select(" + Queryable + ".Where(tenancy.Roles(), role => false), role => role.Id);");
                }

                writer.Line();
                writer.Line("/// <summary>Whether <paramref name=\"role\"/> is a role of the tenant's in use.</summary>");
                writer.Line("public async global::System.Threading.Tasks.ValueTask<bool> ExistsAsync(" + join.Role + " role, " + CancellationTokenType + " cancellationToken)");
                writer.Line("    => await AskAsync(");
                writer.Line("        tenancy => " + Asynchronous + ".AnyAsync(");
                writer.Line("            tenancy.Roles(),");
                writer.Line("            row => row.Id.Equals(role) && row.Status == global::" + Tenancy + ".RoleStatus.Active,");
                writer.Line("            cancellationToken),");
                writer.Line("        cancellationToken).ConfigureAwait(false);");

                writer.Line();
                writer.Line("/// <summary>");
                writer.Line("/// The tenant's role in use that was made from the pack the rules name as the owner's role, or null when it has none.");
                writer.Line("/// Where a tenant made several from that pack, the first of them by id.");
                writer.Line("/// </summary>");
                using (writer.Block("public async global::System.Threading.Tasks.ValueTask<" + join.Role + "?> FindOwnerRoleAsync(" + CancellationTokenType + " cancellationToken)"))
                {
                    writer.Line("var pack = _ownerRole;");
                    writer.Line("return await AskAsync(");
                    writer.Line("    tenancy =>");
                    writer.Line("    {");
                    writer.Line("        var fromThePack = " + Queryable + ".Where(tenancy.Roles(), row => row.FromPack == pack && row.Status == global::" + Tenancy + ".RoleStatus.Active);");
                    writer.Line("        var ids = " + Queryable + ".Select(" + Queryable + ".OrderBy(fromThePack, row => row.Id), row => (" + join.Role + "?)row.Id);");
                    writer.Line("        return " + Asynchronous + ".FirstOrDefaultAsync(ids, cancellationToken);");
                    writer.Line("    },");
                    writer.Line("    cancellationToken).ConfigureAwait(false);");
                }
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
                           "    global::System.Func<" + questions + ", global::System.Threading.Tasks.Task<TAnswer>> ask,",
                           "    " + CancellationTokenType + " cancellationToken)",
                       ]))
            {
                using (writer.Block("if (" + Services + ".ServiceProviderServiceExtensions.GetService<TContext>(_services) is { } own)"))
                {
                    writer.Line("return await ask(" + Over + "(_tenancy, own)).ConfigureAwait(false);");
                }

                writer.Line();
                writer.Line("var factory = " + Services + ".ServiceProviderServiceExtensions.GetRequiredService<" + EntityFramework + ".IDbContextFactory<TContext>>(_services);");
                writer.Line("var context = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);");
                // Called by its full name: what is written here depends on no using of the application's.
                using (writer.Block("await using (global::System.Threading.Tasks.TaskAsyncEnumerableExtensions.ConfigureAwait(context, false))"))
                {
                    writer.Line("return await ask(" + Over + "(_tenancy, context)).ConfigureAwait(false);");
                }
            }
        }

        return writer.ToString();
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
