using System.Reflection;
using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;
using DDDToolkit.Abstractions.Interfaces;
using DDDToolkit.Access;
using DDDToolkit.BaseTypes;
using DDDToolkit.Interfaces;
using DDDToolkit.Localization;
using DDDToolkit.Supporting.Membership.Access;
using DDDToolkit.Supporting.Membership.UseCases;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DDDToolkit.Supporting.Membership.EntityFramework;

/// <summary>Registers Membership, stored with Entity Framework.</summary>
public static class MembershipEntityFrameworkServiceCollectionExtensions
{
    private static readonly MethodInfo MakerMethod =
        typeof(MembershipEntityFrameworkServiceCollectionExtensions).GetMethod(nameof(Maker), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// Registers one kind of resource with members, kept in <typeparamref name="TContext"/>, the context whose
    /// model calls <c>HasMembers</c> on it, under <paramref name="rules"/>. An application calls the
    /// registration generated for its resource, named after it:
    /// <code>
    /// services.AddDocumentMembership&lt;DocumentsContext&gt;(DocumentMembership.Rules);
    /// </code>
    /// What it registers is asked for by the resource, so there is one for each kind of resource an
    /// application has members on, its documents and its folders, in one container, and none gets another's
    /// rules:
    /// <list type="bullet">
    /// <item><see cref="IMemberQuestions{TResourceId}"/>, the access questions of the resource, answered inside the
    /// context's own statements, which is what <see cref="MemberAccessCheck{TResource, TResourceId}"/> asks
    /// before a handler runs. That check is added for the module's request interface by the registration
    /// generated beside this one: <c>services.AddDocumentMemberAccess&lt;IDocumentsRequest&gt;()</c>
    /// (<see cref="AddMemberAccess{TResource, TResourceId, TRequests}"/>).</item>
    /// <item><see cref="MemberAdmission{TResourceId, TMemberId, TRoleId}"/>, what a handler asks before it
    /// changes the members, over the roles the rules declare (<see cref="NamedRoles{TResourceId}"/>) or the
    /// rows of the roles kept for the resource, with the application's
    /// <see cref="IMemberDirectory{TResourceId, TMemberId}"/> and
    /// <see cref="IMemberRolePolicy{TResourceId, TRoleId}"/> where it registered them.</item>
    /// <item>A <see cref="MembershipRegistration"/>, which a start-up check reads.</item>
    /// <item>The package's texts for what the resource refuses with, in English and Dutch, under the
    /// resource's own codes (<see cref="MembershipCodes.TextKeys"/>): offered to the application's failure
    /// localizer, so an application that localizes its failures (<c>AddDDDToolkitLocalization</c>) adds no line
    /// for them, and one that does not is given nothing. A text of the application's own for one of these
    /// codes is asked first, wherever it was added.</item>
    /// </list>
    /// <para>
    /// The rules say three things apart from each other, and what they say decides what the application has
    /// to answer. Rules that need an answer nobody registered are refused here, when the application starts,
    /// with what to register, rather than read as something they are not:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Who a member is.</b> The caller's user id (<see cref="MemberSource.CallerId"/>), for a member
    /// id over a <see cref="Guid"/>; a claim of its token (<see cref="MemberSource.Claim"/>), for one over a
    /// text; or what the application resolves for the caller (<see cref="MemberSource.Resolved"/>), which
    /// needs an <see cref="ICallerMember{TResourceId, TMemberId}"/>.</item>
    /// <item><b>Where the roles come from.</b> The rules declare them, and a member holds them as
    /// <see cref="NamedRole"/>; or they are kept for the resource (<see cref="MembershipRules.RolesKept"/>), as
    /// rows of the application's role class, which needs nothing registered: the context maps that class
    /// with <c>IsKeptRole</c>, and the roles are read from there; or they are kept elsewhere
    /// (<see cref="MembershipRules.RolesKeptElsewhere"/>), which needs an
    /// <see cref="IRolesWithKey{TResourceId, TRoleId}"/> for which roles give a key and an
    /// <see cref="IMemberRoles{TResourceId, TRoleId}"/> for which roles there are.</item>
    /// <item><b>Whether a resource is reached from above</b> (<see cref="MembershipRules.Above"/>), which needs
    /// an <see cref="IPlacesReached{TResourceId, TPlaceId}"/> for where a caller holds a key, and the resource
    /// mapped with where it sits (<c>HasMembers(..., at: ...)</c>).</item>
    /// </list>
    /// <para>
    /// The application registers what it answers before it registers the resource, or hands the class that
    /// answers to the registration that takes one:
    /// <see cref="AddMembership{TMember, TId, TMemberId, TRoleId, TContext, TResource, TResourceId, TPorts}"/>.
    /// </para>
    /// <para>
    /// The questions read on a context of their own, from the context's factory
    /// (<c>IDbContextFactory&lt;TContext&gt;</c>), where the application registered one, a pooled one with
    /// <c>AddScopedFromPool</c> say, since the questions of one request may be asked side by side; otherwise on
    /// the request's own context. Where the resource, or the role class of a resource whose roles are kept,
    /// has a query filter that reads a member of the context it runs on, the questions read on the request's
    /// own context wherever the scope has one, as the admission does: a rule kept on the request's context
    /// applies to both alike, and questions asked side by side then each take a scope of their own. A filter
    /// that reads what is around the work, and no member of its context, holds on every context.
    /// A <see cref="TimeProvider"/> and an <see cref="ICallerAccessor"/> registered before stay; otherwise the
    /// system clock and the ambient caller are used.
    /// </para>
    /// <para>
    /// The ambient caller answers <see cref="Caller.System"/> where nobody began one, and the application
    /// itself holds every key on every resource. An application whose requests each have a caller says so,
    /// with <c>services.RequireExplicitCallers()</c>, and a request that reaches the questions with no caller
    /// then fails rather than passes as the application. Work in a scope
    /// (<see cref="Caller.SystemIn"/>) holds nothing on a resource unless the rules name that scope
    /// (<see cref="MembershipRules.SystemScopes"/>).
    /// </para>
    /// <para>
    /// <c>AddDocumentMembership&lt;DocumentsContext&gt;</c> is generated into the project that declares the member
    /// class, <c>[Member&lt;DocumentShareId, UserId, NamedRole, Document&gt;]</c>, closed over that class, its
    /// types, the resource it names and the resource's id (<see cref="TemplateRegistrationAttribute"/>), or
    /// into a project of the same <c>[assembly: Module]</c> that declares none. It is named after the resource
    /// whether a project declares one member class or several: <c>AddDocumentMembership</c> and
    /// <c>AddFolderMembership</c>. This is the method they call, and it can be called as well, with all seven
    /// types written out.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="rules">The resource's rules.</param>
    /// <typeparam name="TMember">The application's member class.</typeparam>
    /// <typeparam name="TId">The member class's own id.</typeparam>
    /// <typeparam name="TMemberId">What a member is known by.</typeparam>
    /// <typeparam name="TRoleId">
    /// What a role is known by: <see cref="NamedRole"/> where the rules declare the roles, and the id of the
    /// application's role class where the roles are kept for the resource.
    /// </typeparam>
    /// <typeparam name="TContext">The context that maps the resource and its members.</typeparam>
    /// <typeparam name="TResource">The resource's aggregate.</typeparam>
    /// <typeparam name="TResourceId">The resource's id.</typeparam>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="rules"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The rules take the caller's member id from its user id for a member id that is not over a
    /// <see cref="Guid"/>, or from a claim for one that is not over a text; they declare the roles, and the
    /// members hold roles that are not <see cref="NamedRole"/>; they say the roles are kept for the resource,
    /// and the members hold them by name; or they need something of the application that is not registered.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The resource is registered already, with other rules, another context or another member class; or the
    /// rules, or the member class, are those of another resource already.
    /// </exception>
    [TemplateRegistration(Name = "Add{TResource}Membership")]
    public static IServiceCollection AddMembership<
        [TemplateType(typeof(MemberAttribute<,,,>), Take = TemplateArgumentKind.Type)] TMember,
        [TemplateType(typeof(MemberAttribute<,,,>))] TId,
        [TemplateType(typeof(MemberAttribute<,,,>), Argument = 1)] TMemberId,
        [TemplateType(typeof(MemberAttribute<,,,>), Argument = 2)] TRoleId,
        TContext,
        [TemplateType(typeof(MemberAttribute<,,,>), Argument = 3)] TResource,
        [TemplateType(typeof(MemberAttribute<,,,>), Argument = 3, IdOfArgument = true)] TResourceId>(
        this IServiceCollection services,
        MembershipRules rules)
        where TMember : MemberEntity<TId, TMemberId, TRoleId>
        where TId : struct, IEntityId, IEquatable<TId>
        where TMemberId : struct, IEntityId, IEquatable<TMemberId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
        where TContext : DbContext
        where TResource : AggregateRoot<TResourceId>
        where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(rules);

        var asMember = CallerAsMember<TResource, TResourceId, TMemberId>(services, rules);
        RequireRoles<TResource, TResourceId, TId, TMemberId, TRoleId>(services, rules);
        RequirePlacesReached<TResource, TResourceId>(services, rules);

        var registration = new MembershipRegistration(typeof(TContext), typeof(TResource), typeof(TResourceId), typeof(TMember), rules);
        if (AlreadyRegistered(services, registration))
        {
            return services;
        }

        services.AddSingleton(registration);

        // The texts of what this resource refuses with, under its own codes. Offered, not added: only an
        // application that localizes its failures reads them, and its own texts for these codes come first.
        services.AddFailureTexts<MembershipFailures>(rules.Codes.TextKeys);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ICallerAccessor, AmbientCallerAccessor>();
        services.AddSingleton(new MemberNamesOf<TContext, TResource, TRoleId>());

        // Each made by a factory that holds this resource's rules: the rules themselves are never a service,
        // since a second resource would be handed the first one's.
        services.AddScoped<IMemberQuestions<TResourceId>>(provider =>
            new EfMemberQuestions<TContext, TResource, TResourceId, TMember, TId, TMemberId, TRoleId>(registration, asMember, provider));
        if (rules.RolesKept)
        {
            // The rows of the application's role class, read on the request's own context. One the
            // application registered before stays, as every answer of its own does.
            services.TryAddScoped<IMemberRoles<TResourceId, TRoleId>>(provider => new KeptMemberRoles<TContext, TResource, TResourceId, TRoleId>(registration, provider));
        }
        else if (rules.RolesKeptElsewhere is null)
        {
            services.TryAddSingleton(_ => (IMemberRoles<TResourceId, TRoleId>)(object)new NamedRoles<TResourceId>(rules));
        }

        services.AddScoped(provider => new MemberAdmission<TResourceId, TMemberId, TRoleId>(
            rules,
            provider.GetRequiredService<IMemberRoles<TResourceId, TRoleId>>(),
            provider.GetService<IMemberDirectory<TResourceId, TMemberId>>(),
            provider.GetService<IMemberRolePolicy<TResourceId, TRoleId>>()));

        return services;
    }

    /// <summary>
    /// Registers one kind of resource with members, as
    /// <see cref="AddMembership{TMember, TId, TMemberId, TRoleId, TContext, TResource, TResourceId}"/> does,
    /// together with the class that answers what the resource's rules ask of the application: for a resource
    /// beside an organization, the one class that joins the two.
    /// <code>
    /// services.AddCrateMembership&lt;DepotContext, CratesInTheDepot&gt;(CrateMembership.Rules);
    /// </code>
    /// <typeparamref name="TPorts"/> is registered, one for each scope, and every port of this resource it
    /// implements is answered by that one instance:
    /// <list type="bullet">
    /// <item><see cref="ICallerMember{TResourceId, TMemberId}"/>: who the caller is as a member, for rules with
    /// <see cref="MemberSource.Resolved"/>.</item>
    /// <item><see cref="IRolesWithKey{TResourceId, TRoleId}"/> and
    /// <see cref="IMemberRoles{TResourceId, TRoleId}"/>: which roles give a key, and which roles there are, for
    /// rules whose roles are kept elsewhere.</item>
    /// <item><see cref="IPlacesReached{TResourceId, TPlaceId}"/>: where the caller holds a key, for rules that
    /// let a resource be reached from above.</item>
    /// <item><see cref="IMemberDirectory{TResourceId, TMemberId}"/> and
    /// <see cref="IMemberRolePolicy{TResourceId, TRoleId}"/>, which no rules need: who can be made a member,
    /// and which roles go to one.</item>
    /// </list>
    /// <para>
    /// A port registered before stays, so an application can answer one of them elsewhere. What the rules
    /// need and neither the class nor the application answers is refused, as by the other registration. The
    /// generated <c>AddCrateMembership&lt;TContext, TPorts&gt;</c> calls this, closed over the member class as
    /// the other is.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="rules">The resource's rules.</param>
    /// <typeparam name="TMember">The application's member class.</typeparam>
    /// <typeparam name="TId">The member class's own id.</typeparam>
    /// <typeparam name="TMemberId">What a member is known by.</typeparam>
    /// <typeparam name="TRoleId">What a role is known by.</typeparam>
    /// <typeparam name="TContext">The context that maps the resource and its members.</typeparam>
    /// <typeparam name="TResource">The resource's aggregate.</typeparam>
    /// <typeparam name="TResourceId">The resource's id.</typeparam>
    /// <typeparam name="TPorts">The application's class that answers what the rules ask of it, for this resource.</typeparam>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="rules"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <typeparamref name="TPorts"/> answers nothing of this resource; or the rules are refused as
    /// <see cref="AddMembership{TMember, TId, TMemberId, TRoleId, TContext, TResource, TResourceId}"/> refuses them.
    /// </exception>
    /// <exception cref="InvalidOperationException">The resource, its rules or its member class are registered otherwise already.</exception>
    [TemplateRegistration(Name = "Add{TResource}Membership")]
    public static IServiceCollection AddMembership<
        [TemplateType(typeof(MemberAttribute<,,,>), Take = TemplateArgumentKind.Type)] TMember,
        [TemplateType(typeof(MemberAttribute<,,,>))] TId,
        [TemplateType(typeof(MemberAttribute<,,,>), Argument = 1)] TMemberId,
        [TemplateType(typeof(MemberAttribute<,,,>), Argument = 2)] TRoleId,
        TContext,
        [TemplateType(typeof(MemberAttribute<,,,>), Argument = 3)] TResource,
        [TemplateType(typeof(MemberAttribute<,,,>), Argument = 3, IdOfArgument = true)] TResourceId,
        TPorts>(
        this IServiceCollection services,
        MembershipRules rules)
        where TMember : MemberEntity<TId, TMemberId, TRoleId>
        where TId : struct, IEntityId, IEquatable<TId>
        where TMemberId : struct, IEntityId, IEquatable<TMemberId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
        where TContext : DbContext
        where TResource : AggregateRoot<TResourceId>
        where TResourceId : struct, IEntityId, IEquatable<TResourceId>
        where TPorts : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(rules);

        // The ports of this resource, and of no other: a class that answers for two kinds of resource is
        // handed to the registration of each.
        List<Type> filled =
        [
            .. new[]
            {
                typeof(ICallerMember<TResourceId, TMemberId>),
                typeof(IRolesWithKey<TResourceId, TRoleId>),
                typeof(IMemberRoles<TResourceId, TRoleId>),
                typeof(IMemberDirectory<TResourceId, TMemberId>),
                typeof(IMemberRolePolicy<TResourceId, TRoleId>),
            }.Where(port => port.IsAssignableFrom(typeof(TPorts))),
            .. typeof(TPorts).GetInterfaces().Where(IsPlacesReached<TResourceId>),
        ];

        if (filled.Count == 0)
        {
            throw new ArgumentException(
                typeof(TPorts).Name + " answers nothing of " + typeof(TResource).Name + ": it implements none of ICallerMember<" + typeof(TResourceId).Name + ", "
                + typeof(TMemberId).Name + ">, IRolesWithKey<" + typeof(TResourceId).Name + ", " + typeof(TRoleId).Name + ">, IMemberRoles<" + typeof(TResourceId).Name + ", "
                + typeof(TRoleId).Name + ">, IPlacesReached<" + typeof(TResourceId).Name + ", TPlaceId>, IMemberDirectory<" + typeof(TResourceId).Name + ", " + typeof(TMemberId).Name
                + "> and IMemberRolePolicy<" + typeof(TResourceId).Name + ", " + typeof(TRoleId).Name + ">. Hand over the class that does, or register the resource without one.",
                nameof(TPorts));
        }

        // One instance for a scope answers every port, and a port the application registered before stays.
        services.TryAddScoped<TPorts>();
        foreach (var port in filled)
        {
            services.TryAdd(ServiceDescriptor.Scoped(port, static provider => provider.GetRequiredService<TPorts>()));
        }

        return services.AddMembership<TMember, TId, TMemberId, TRoleId, TContext, TResource, TResourceId>(rules);
    }

    /// <summary>
    /// Adds the access check of one kind of resource with members to the checks the requests of one module
    /// are held to (<see cref="AccessChecks{TRequests}"/>): what decides the requirements a request declares
    /// about that resource (<see cref="MemberAccess{TResourceId}"/>) before its handler runs. An application
    /// calls the registration generated for its resource, named after it:
    /// <code>
    /// services.AddDocumentMemberAccess&lt;IDocumentsRequest&gt;();
    /// </code>
    /// A module calls it for its own request interface, once for each kind of resource its requests are
    /// about, next to the resource's registration. A module whose requests are about another module's
    /// resource calls it for its own interface as well: a check is asked only about the requests of the
    /// interface it was added for.
    /// <para>
    /// It registers <see cref="MemberAccessCheck{TResource, TResourceId}"/> and nothing else, so what the
    /// check asks, the resource's access questions, is there once the resource itself is registered, in
    /// either order. The check is free of any dispatcher, as the package is: whatever calls
    /// <see cref="AccessChecks{TRequests}.RequireAsync"/> in front of the module's handlers holds the
    /// requests to it. Adding it for an interface more than once is harmless.
    /// </para>
    /// <para>
    /// <c>AddDocumentMemberAccess&lt;IDocumentsRequest&gt;</c> is generated wherever
    /// <c>AddDocumentMembership</c> is, closed over the resource its member class names and that resource's
    /// id (<see cref="TemplateRegistrationAttribute"/>). This is the method it calls, and it can be called as
    /// well, with the resource and its id written out.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <typeparam name="TResource">The resource's aggregate.</typeparam>
    /// <typeparam name="TResourceId">The resource's id.</typeparam>
    /// <typeparam name="TRequests">The module's request interface, which every command and query of the module implements.</typeparam>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    [TemplateRegistration(Name = "Add{TResource}MemberAccess")]
    public static IServiceCollection AddMemberAccess<
        [TemplateType(typeof(MemberAttribute<,,,>), Argument = 3)] TResource,
        [TemplateType(typeof(MemberAttribute<,,,>), Argument = 3, IdOfArgument = true)] TResourceId,
        TRequests>(
        this IServiceCollection services)
        where TResource : AggregateRoot<TResourceId>
        where TResourceId : struct, IEntityId, IEquatable<TResourceId>
        where TRequests : class, IRequireAccess
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddAccessCheck<TRequests, MemberAccessCheck<TResource, TResourceId>>();
    }

    /// <summary>
    /// Whether <paramref name="registration"/> is in <paramref name="services"/> already, exactly as it is: a
    /// second call that says the same is harmless.
    /// </summary>
    /// <exception cref="InvalidOperationException">It, its rules or its member class are registered otherwise.</exception>
    private static bool AlreadyRegistered(IServiceCollection services, MembershipRegistration registration)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType != typeof(MembershipRegistration) || descriptor.IsKeyedService || descriptor.ImplementationInstance is not MembershipRegistration other)
            {
                continue;
            }

            if (other.Resource == registration.Resource || other.ResourceIdType == registration.ResourceIdType)
            {
                return other.Resource == registration.Resource
                       && other.Context == registration.Context
                       && other.Member == registration.Member
                       && ReferenceEquals(other.Rules, registration.Rules)
                    ? true
                    : throw new InvalidOperationException(
                        other.Resource.Name + " is registered already, in " + other.Context.Name + " with the member class " + other.Member.Name
                        + " and the rules '" + other.Rules.Name + "'. A kind of resource is registered once, with one set of rules.");
            }

            if (ReferenceEquals(other.Rules, registration.Rules) || string.Equals(other.Rules.Name, registration.Rules.Name, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The rules '" + registration.Rules.Name + "' are those of " + other.Resource.Name + " already. Each kind of resource has rules of its own, with a name, "
                    + "codes and functions of its own: declare a MembershipRules for " + registration.Resource.Name + ".");
            }

            if (other.Member == registration.Member)
            {
                throw new InvalidOperationException(
                    registration.Member.Name + " is the member class of " + other.Resource.Name + " already. Each kind of resource has a member class of its own: declare one for "
                    + registration.Resource.Name + " with the member template.");
            }
        }

        return false;
    }

    /// <summary>
    /// Who a caller is as a member, as <paramref name="rules"/> say: made once, where the resource is
    /// registered. <see langword="null"/> where the application resolves it, which is then asked whenever a
    /// question is.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The rules take the members from a value the member id is not over, or from the application, which
    /// registered nothing that answers.
    /// </exception>
    private static Func<Caller, TMemberId?>? CallerAsMember<TResource, TResourceId, TMemberId>(IServiceCollection services, MembershipRules rules)
        where TResourceId : struct, IEntityId, IEquatable<TResourceId>
        where TMemberId : struct, IEntityId, IEquatable<TMemberId>
    {
        switch (rules.Members.Kind)
        {
            case MemberSourceKind.CallerId:
            {
                var known = FromValue<TMemberId, Guid>()
                    ?? throw new ArgumentException(
                        "The rules '" + rules.Name + "' take the caller's member id from MemberSource.CallerId, the caller's user id, and the members of " + typeof(TResource).Name
                        + " are known by " + typeof(TMemberId).Name + ", which is not an id over a Guid. Declare it with [EntityId<Guid>], take the members from "
                        + "MemberSource.Claim(path) for an id over a text, or have the application resolve who its caller is: MemberSource.Resolved().",
                        nameof(rules));

                return caller => caller.UserId is { } user ? known(user) : null;
            }

            case MemberSourceKind.Claim:
            {
                var path = rules.Members.ClaimPath!;
                var known = FromValue<TMemberId, string>()
                    ?? throw new ArgumentException(
                        "The rules '" + rules.Name + "' take the caller's member id from the claim '" + path + "', a text, and the members of " + typeof(TResource).Name
                        + " are known by " + typeof(TMemberId).Name + ", which is not an id over a text. Declare it with [EntityId<string>], or take the members from MemberSource.CallerId for an id over a Guid.",
                        nameof(rules));

                return caller => caller.Claim(path) is { } claimed ? known(claimed) : null;
            }

            default:
                // Asked of the application, about each caller: never read from the caller's own id instead.
                return IsRegistered(services, typeof(ICallerMember<TResourceId, TMemberId>))
                    ? null
                    : throw new ArgumentException(
                        "The rules '" + rules.Name + "' have the application resolve who a caller is as a member (MemberSource.Resolved), and nothing answers that for "
                        + typeof(TResource).Name + ": no ICallerMember<" + typeof(TResourceId).Name + ", " + typeof(TMemberId).Name + "> is registered. "
                        + HowToAnswer<TResource>("ICallerMember<" + typeof(TResourceId).Name + ", " + typeof(TMemberId).Name + ">"),
                        nameof(rules));
        }
    }

    /// <summary>
    /// Holds the roles a member holds to what the rules say of them: by name, where the rules declare them;
    /// by the id of the application's role class, where they are kept for the resource; and answered by the
    /// application, where they are kept elsewhere.
    /// </summary>
    /// <exception cref="ArgumentException">The roles are not held as the rules say, or the application registered nothing that answers.</exception>
    private static void RequireRoles<TResource, TResourceId, TId, TMemberId, TRoleId>(IServiceCollection services, MembershipRules rules)
        where TResourceId : struct, IEntityId, IEquatable<TResourceId>
        where TRoleId : struct, IEntityId, IEquatable<TRoleId>
    {
        if (rules.RolesKept)
        {
            // Which class the roles are of is the model's to say, so that is held when the resource is first
            // asked about. What can be told here: a role that is a row is not held by a name.
            if (typeof(TRoleId) == typeof(NamedRole))
            {
                throw new ArgumentException(
                    "The rules '" + rules.Name + "' say the roles of " + typeof(TResource).Name + " are kept, as rows of a role class, and its members hold roles by name. "
                    + "Declare a role class for it, [KeptRole<" + typeof(TResource).Name + "RoleId, " + typeof(TResource).Name + ">], and the member class with that class's id as its role: "
                    + "[Member<" + typeof(TId).Name + ", " + typeof(TMemberId).Name + ", " + typeof(TResource).Name + "RoleId, " + typeof(TResource).Name + ">].",
                    nameof(rules));
            }

            return;
        }

        if (rules.RolesKeptElsewhere is null)
        {
            if (typeof(TRoleId) != typeof(NamedRole))
            {
                throw new ArgumentException(
                    "The members of " + typeof(TResource).Name + " hold roles known by " + typeof(TRoleId).Name + ", and the rules '" + rules.Name + "' declare the roles, which a member "
                    + "holds by name. Declare the member class with NamedRole as its role, [Member<" + typeof(TId).Name + ", " + typeof(TMemberId).Name + ", NamedRole, "
                    + typeof(TResource).Name + ">]; or say in the rules where the roles are rows: rolesKept: true for a role class of this resource, rolesKeptElsewhere: new() for roles kept elsewhere.",
                    nameof(rules));
            }

            return;
        }

        var giving = "IRolesWithKey<" + typeof(TResourceId).Name + ", " + typeof(TRoleId).Name + ">";
        if (!IsRegistered(services, typeof(IRolesWithKey<TResourceId, TRoleId>)))
        {
            throw new ArgumentException(
                "The rules '" + rules.Name + "' say the roles of " + typeof(TResource).Name + " are kept elsewhere, and nothing answers which of them give a key: no "
                + giving + " is registered. " + HowToAnswer<TResource>(giving),
                nameof(rules));
        }

        var known = "IMemberRoles<" + typeof(TResourceId).Name + ", " + typeof(TRoleId).Name + ">";
        if (!IsRegistered(services, typeof(IMemberRoles<TResourceId, TRoleId>)))
        {
            throw new ArgumentException(
                "The rules '" + rules.Name + "' say the roles of " + typeof(TResource).Name + " are kept elsewhere, and nothing answers which roles there are and which is the owner's: no "
                + known + " is registered. " + HowToAnswer<TResource>(known),
                nameof(rules));
        }
    }

    /// <summary>Holds rules that reach a resource from above to an application that answers where a caller holds a key.</summary>
    /// <exception cref="ArgumentException">The application registered nothing that answers.</exception>
    private static void RequirePlacesReached<TResource, TResourceId>(IServiceCollection services, MembershipRules rules)
        where TResourceId : struct, IEntityId, IEquatable<TResourceId>
    {
        if (rules.Above is not null && !services.Any(descriptor => !descriptor.IsKeyedService && IsPlacesReached<TResourceId>(descriptor.ServiceType)))
        {
            var above = "IPlacesReached<" + typeof(TResourceId).Name + ", TPlaceId>";
            throw new ArgumentException(
                "The rules '" + rules.Name + "' let " + typeof(TResource).Name + " be reached from above, and nothing answers where a caller holds a key: no "
                + above + " is registered, for what the place a " + typeof(TResource).Name + " sits at is known by. " + HowToAnswer<TResource>(above),
                nameof(rules));
        }
    }

    /// <summary>What to do about a port nobody registered.</summary>
    private static string HowToAnswer<TResource>(string port)
        => "Register one before the resource, services.AddScoped<" + port + ", YourClass>(), or hand the class that answers to the resource's registration: services.Add"
           + typeof(TResource).Name + "Membership<TContext, YourClass>(rules).";

    private static bool IsRegistered(IServiceCollection services, Type service)
        => services.Any(descriptor => descriptor.ServiceType == service && !descriptor.IsKeyedService);

    /// <summary>Whether <paramref name="type"/> is <see cref="IPlacesReached{TResourceId, TPlaceId}"/> for the resource, whatever a place is known by.</summary>
    private static bool IsPlacesReached<TResourceId>(Type type)
        => type.IsGenericType
           && type.GetGenericTypeDefinition() == typeof(IPlacesReached<,>)
           && type.GenericTypeArguments[0] == typeof(TResourceId);

    /// <summary>
    /// How a <typeparamref name="T"/> is made from the value it holds, or <see langword="null"/> when it is
    /// not an id over a <typeparamref name="TValue"/>.
    /// </summary>
    private static Func<TValue, T>? FromValue<T, TValue>()
        => typeof(T).GetInterfaces().Any(implemented =>
                implemented.IsGenericType
                && implemented.GetGenericTypeDefinition() == typeof(ISingleValue<,>)
                && implemented.GenericTypeArguments[0] == typeof(T)
                && implemented.GenericTypeArguments[1] == typeof(TValue))
            ? (Func<TValue, T>)MakerMethod.MakeGenericMethod(typeof(T), typeof(TValue)).Invoke(null, null)!
            : null;

    /// <summary>The way back from a value to the id that holds it, which every generated id offers.</summary>
    private static Func<TValue, T> Maker<T, TValue>()
        where T : ISingleValue<T, TValue>
        => static value => T.FromValue(value);
}
