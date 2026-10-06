using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers.Common;

/// <summary>
/// What a package's switch writes into a project: the classes of its templates the project leaves out, as the
/// package ships them, and their ids. A package marks an assembly attribute of its own with
/// <c>[TemplateDefaults(typeof(TenantAggregateAttribute&lt;&gt;), ...)]</c>, and a project that writes
/// <c>[assembly: GenerateTenancyClasses]</c> gets a <c>Tenant</c> declared <c>[TenantAggregate&lt;TenantId&gt;]</c> and a
/// <c>TenantId</c> declared <c>[EntityId&lt;Guid&gt;]</c>, unless it has them already.
/// <para>
/// <b>Why the plan feeds the providers, and not a file.</b> A generator never sees what another generator writes,
/// and the class and the id need everything the toolkit writes for a class and an id the application declared: the
/// base class, the converters of Entity Framework and HotChocolate, the registrations closed over the classes and the
/// class the use cases are named through. So the declarations this plan writes are only half of it. The other half
/// is that every provider those generators read, <see cref="Providers.EntityIds"/> and
/// <see cref="Providers.DeclaredTemplateEntities"/>, hands on the classes and ids of this plan beside the ones in the
/// source, as definitions made exactly as a declaration would have made them. Each generator then writes its part,
/// in the project that has the switch: a single project gets the lot, and a module split by layer gets in its next
/// project what the compiled output shows, since the declarations carry the same attributes a hand-written one does.
/// </para>
/// <para>
/// <b>What the project has wins.</b> A template the project declares a class with, or a project of its module that it
/// references, gets none. An id is taken where a type of its name is found, in the project or a project of its module
/// that it references, and written only where none is. The ids a class of the package takes from a class the
/// application declared itself, the organization's tenant id say, are that class's id, whatever it is called. A class
/// of the application's own whose id is a name the plan writes, <c>[SeatAggregate&lt;SeatId&gt;]</c> with no
/// <c>SeatId</c> anywhere, cannot be bound by the compiler a generator sees; the plan binds it. So it does a later
/// type argument of any template that names an id it writes, the <c>SeatId</c> of
/// <c>[Member&lt;CrewMemberId, SeatId, RoleId, Project&gt;]</c>: the class's definition names it in full, and a
/// registration closed over the class takes it from there (<see cref="EntityDefinition.WrittenArguments"/>).
/// </para>
/// <para>
/// <b>What is in the way is said once.</b> A class is not written where its full name is taken, by a type or a
/// namespace, nor one whose id cannot be had, and DDD00066 says so on the switch. A class that takes a type from it,
/// or shares its id, is kept out with it without a word, no id is written for a class that is not, and the
/// registrations stand back for every template kept out (<see cref="TemplateDefaultsPlan.Blocked"/>).
/// </para>
/// <para>
/// <b>Cost.</b> A project without a switch pays for reading its own assembly attributes on an edit, and hands on what
/// it declared. One with a switch reads its references once per reference, as the templates do.
/// </para>
/// </summary>
internal static class TemplateDefaults
{
    /// <summary>The plan of a project with no switch: what it declares, and nothing more.</summary>
    /// <param name="written">The classes the source declares with a template.</param>
    private static TemplateDefaultsPlan Nothing(ImmutableArray<EntityDefinition> written)
        => new(
            written.IsDefault ? EquatableArray<EntityDefinition>.Empty : new EquatableArray<EntityDefinition>(written),
            EquatableArray<EntityIdDefinition>.Empty,
            EquatableArray<DefaultDeclaration>.Empty,
            EquatableArray<DiagnosticInfo>.Empty,
            EquatableArray<string>.Empty);

    /// <summary>What the switches of this project write, and the classes every generator reads with them.</summary>
    /// <param name="written">The classes the source declares with a template, before any is resolved.</param>
    /// <param name="compilation">The project.</param>
    /// <param name="rootNamespace">The project's <c>RootNamespace</c>, where what is written goes; the assembly's name stands in without it.</param>
    /// <param name="implicitIds">The ids the generator writes for this project's <c>[AggregateRoot&lt;Guid&gt;]</c> and <c>[Entity&lt;Guid&gt;]</c> classes, which no generator sees as types either.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    public static TemplateDefaultsPlan Plan(
        ImmutableArray<EntityDefinition> written,
        Compilation compilation,
        string? rootNamespace,
        EquatableArray<ImplicitIdName> implicitIds,
        CancellationToken cancellationToken)
    {
        var switches = SwitchesIn(compilation, cancellationToken);
        if (switches.Count == 0)
        {
            return Nothing(written);
        }

        var declared = written.IsDefault ? ImmutableArray<EntityDefinition>.Empty : written;
        var wanted = Wanted(switches);
        var module = ModuleBoundary.ModuleOf(compilation.Assembly);
        var scope = Identifiers.NamespaceFrom(string.IsNullOrWhiteSpace(rootNamespace) ? compilation.AssemblyName : rootNamespace!.Trim());
        var diagnostics = new List<DiagnosticInfo>();

        // The ids the classes of the package already have, by the name the plan would give them: a class the project
        // declares, and one a project of its module declares. A class of the package the plan writes takes its id
        // from these first, so an organization written beside a tenant declared over ShopTenantId shares that one.
        var idsOfClasses = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var declaredSomewhere = new HashSet<string>(StringComparer.Ordinal);
        foreach (var template in wanted)
        {
            foreach (var definition in declared.Where(definition => definition.TemplateKey == template.Key))
            {
                declaredSomewhere.Add(template.Key);
                if (definition.TemplateIdIsEntityId)
                {
                    Add(idsOfClasses, template.IdName, definition.IdType);
                }
            }

            if (declaredSomewhere.Contains(template.Key))
            {
                continue;
            }

            foreach (var source in DefinitionFactory.ReferencedSources(compilation, template.MetadataName, cancellationToken, module))
            {
                declaredSomewhere.Add(template.Key);
                if (source.IdIsEntityId)
                {
                    Add(idsOfClasses, template.IdName, source.IdType);
                }
            }
        }

        // The classes to write.
        var classes = wanted.Where(template => template.WritesClass && !declaredSomewhere.Contains(template.Key)).ToList();

        // What becomes of each id, worked out once, when it is first asked about.
        var ids = new Dictionary<string, IdOutcome>(StringComparer.Ordinal);
        IdOutcome IdOf(string name)
        {
            if (!ids.TryGetValue(name, out var outcome))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ids[name] = outcome = Resolve(name, idsOfClasses, implicitIds, compilation, module, scope, cancellationToken);
            }

            return outcome;
        }

        // The classes whose name is taken where they would go: by a type or a namespace of that name, this project's or
        // one it references. Two of one name would be one class, or a compile error in code nobody wrote. Said here,
        // once for each class.
        var blocked = new HashSet<string>(StringComparer.Ordinal);
        var takenNames = new Dictionary<string, WantedTemplate>(StringComparer.Ordinal);
        foreach (var template in classes)
        {
            var id = IdOf(template.IdName) is { IdType: { } idType } ? LastNameOf(idType) : template.IdName;
            var asDeclared = "[" + template.AttributeName + "<" + id + ">]";
            var inTheWay = takenNames.TryGetValue(template.ClassName, out var first)
                ? "[assembly: " + first.Switch.Name + "] writes a class of that name for [" + first.AttributeName + "] already; declare one of the two yourself under a name of your own, "
                  + asDeclared
                : InTheWay(compilation, scope, template.ClassName, template.AttributeName, asDeclared);
            takenNames[template.ClassName] = first ?? template;
            if (inTheWay is not null)
            {
                blocked.Add(template.Key);
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticDescriptors.TemplateDefaultNotWritten, template.Switch.Location, template.Switch.Name, template.ClassName, inTheWay));
            }
        }

        Spread(blocked, classes);

        // The ids that are needed: those of the classes still to be written, those a switch writes alone, and one a
        // class of the project's own is declared over that only this plan can write, SeatId for a seat declared with
        // fields of its own and no id anywhere. An id that cannot be had keeps out every class that takes it, and is
        // said once, in the name of the first class that would take it, or of the id itself where a switch writes ids
        // alone.
        var unbound = declared
            .Where(definition => definition.UnboundIdName is { } name && wanted.Any(template => template.Key == definition.TemplateKey && template.IdName == name))
            .Select(static definition => definition.UnboundIdName!)
            .ToList();
        var needed = Needed(wanted, classes, blocked, unbound);
        foreach (var name in needed)
        {
            if (IdOf(name).Problem is not { } problem)
            {
                continue;
            }

            var about = wanted.First(template => template.IdName == name);
            var takers = classes.Where(template => template.IdName == name && !blocked.Contains(template.Key)).ToList();
            foreach (var taker in takers)
            {
                blocked.Add(taker.Key);
            }

            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.TemplateDefaultNotWritten,
                about.Switch.Location,
                about.Switch.Name,
                takers.Count > 0 ? takers[0].ClassName : name,
                takers.Count > 0 ? "its id would be '" + name + "', and " + problem : problem));
        }

        // Nor is a class written that takes a type from one of those, or shares the id of one, the organization the
        // tenant's: it could not be generated either, and what is in the way is said once, for the class in the way,
        // not again for every class that needed it.
        Spread(blocked, classes);

        var entities = ImmutableArray.CreateBuilder<EntityDefinition>(declared.Length + classes.Count);
        var declarations = new List<DefaultDeclaration>();
        var idDefinitions = new List<EntityIdDefinition>();
        var writtenIds = new Dictionary<string, string>(StringComparer.Ordinal);

        // The ids, of the classes that are written and of what else needs them: none for a class kept out.
        foreach (var name in Needed(wanted, classes, blocked, unbound))
        {
            if (IdOf(name).Written is not { } because)
            {
                continue;
            }

            var owners = wanted.Where(template => template.IdName == name).ToList();
            var type = TypeOf(name, scope, DeclarationKind.RecordStruct, owners[0].Switch.Location);
            idDefinitions.Add(IdDefinition(type, compilation));
            declarations.Add(IdDeclaration(type, owners, owners[0].Switch, because));
            writtenIds[name] = type.FullyQualifiedName;
        }

        foreach (var template in classes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (blocked.Contains(template.Key))
            {
                continue;
            }

            var idType = ids[template.IdName].IdType!;
            var type = TypeOf(template.ClassName, scope, DeclarationKind.Class, template.Switch.Location);
            if (DefinitionFactory.CreateDefaultTemplateEntity(template.Attribute, template.Marker, type, idType, compilation, cancellationToken) is not { } definition)
            {
                // A template the switch names that does not fit its parent: the package's mistake, which its own
                // tests show. Nothing is written, and nothing is said in the application.
                continue;
            }

            entities.Add(definition);
            declarations.Add(ClassDeclaration(type, template, idType, module));
        }

        // The classes the project declares, with the ids this plan writes bound where the compiler could not: the id
        // of a class of the package's, and a later type argument of any template, the SeatId a member class of
        // another package names.
        var bound = ImmutableArray.CreateBuilder<EntityDefinition>(declared.Length);
        foreach (var definition in declared)
        {
            bound.Add(Bind(definition, writtenIds));
        }

        bound.AddRange(entities);
        return new TemplateDefaultsPlan(
            bound.ToEquatableArray(),
            idDefinitions.ToEquatableArray(),
            declarations.ToEquatableArray(),
            diagnostics.ToEquatableArray(),
            classes.Where(template => blocked.Contains(template.Key)).Select(static template => template.Key).ToEquatableArray());
    }

    /// <summary>
    /// The ids the plan needs: those of the classes it writes, those a switch writes alone, and those classes of the
    /// project's own are declared over that only it can write.
    /// </summary>
    private static List<string> Needed(List<WantedTemplate> wanted, List<WantedTemplate> classes, HashSet<string> blocked, List<string> unbound)
    {
        var needed = new List<string>();
        foreach (var template in wanted.Where(static template => !template.WritesClass).Concat(classes.Where(template => !blocked.Contains(template.Key))))
        {
            AddOnce(needed, template.IdName);
        }

        foreach (var name in unbound)
        {
            AddOnce(needed, name);
        }

        return needed;
    }

    /// <summary>
    /// Keeps out every class that takes a type from a class kept out, or shares its id, until none is left: the
    /// organization, which takes the tenant's id and is declared over it, when the tenant cannot be written.
    /// </summary>
    private static void Spread(HashSet<string> blocked, List<WantedTemplate> classes)
    {
        for (var changed = blocked.Count > 0; changed;)
        {
            changed = false;
            foreach (var template in classes)
            {
                if (blocked.Contains(template.Key))
                {
                    continue;
                }

                if (template.Sources.Any(blocked.Contains)
                    || classes.Any(other => blocked.Contains(other.Key) && other != template && other.ClassName + "Id" == template.IdName))
                {
                    changed |= blocked.Add(template.Key);
                }
            }
        }
    }

    /// <summary>
    /// What is in the way of a class called <paramref name="className"/> in <paramref name="scope"/>, phrased to follow
    /// "does not write 'Role':", or null when nothing is: a type of that name, of this project or of one it
    /// references, or a namespace, a folder of that name directly under the project, say. Read off the namespace
    /// as the compiler merges it, so what a reference brings is seen with what the project declares.
    /// </summary>
    private static string? InTheWay(Compilation compilation, string scope, string className, string attributeName, string asDeclared)
    {
        INamespaceSymbol? container = compilation.GlobalNamespace;
        foreach (var part in scope.Length > 0 ? scope.Split('.') : [])
        {
            container = container.GetNamespaceMembers().FirstOrDefault(member => member.Name == part);
            if (container is null)
            {
                return null;
            }
        }

        var fullName = scope.Length > 0 ? scope + "." + className : className;
        var yourOwn = "declare the package's class yourself under a name of your own, " + asDeclared;
        foreach (var type in container.GetTypeMembers(className, 0))
        {
            if (SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly))
            {
                return type.TypeKind == TypeKind.Class
                    ? "this project has a class '" + fullName + "' already, which is not declared with [" + attributeName + "]: if it is meant to be that class, declare it "
                      + asDeclared + " and it is yours; if not, rename it, or " + yourOwn
                    : "this project has a type '" + fullName + "' already; rename it, or " + yourOwn;
            }

            if (compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
            {
                return "a project it references has a type '" + fullName + "' already; " + yourOwn;
            }
        }

        if (container.GetNamespaceMembers().FirstOrDefault(member => member.Name == className) is { } folder)
        {
            return (folder.ConstituentNamespaces.Any(part => SymbolEqualityComparer.Default.Equals(part.ContainingAssembly, compilation.Assembly))
                       ? "this project has a namespace '" + fullName + "', a folder of that name, say; "
                       : "a project it references has a namespace '" + fullName + "'; ")
                   + yourOwn + ", or rename the namespace";
        }

        return null;
    }

    /// <summary>
    /// A class of the project's own as it would have been had the ids this plan writes been there: its own id, when it
    /// is a class of the package's declared over one, and every later type argument that names one, so a registration
    /// closed over it and a generator that writes a part of it name the id in full.
    /// </summary>
    private static EntityDefinition Bind(EntityDefinition definition, Dictionary<string, string> writtenIds)
    {
        if (definition.Template is not { } template || writtenIds.Count == 0)
        {
            return definition;
        }

        var arguments = template.Arguments.ToArray();
        var positions = new List<int>();
        for (var position = 1; position < arguments.Length; position++)
        {
            // A name the compiler could not bind is shown as it was written, without global:: or a namespace.
            if (writtenIds.TryGetValue(arguments[position], out var written))
            {
                arguments[position] = written;
                positions.Add(position);
            }
        }

        if (definition.UnboundIdName is { } name && writtenIds.TryGetValue(name, out var idType))
        {
            arguments[0] = idType;
            return definition with
            {
                IdType = idType,
                TemplateIdIsEntityId = true,
                CanGenerate = true,
                UnboundIdName = null,
                Template = template with { Arguments = new EquatableArray<string>(arguments) },
                WrittenArguments = positions.ToEquatableArray(),
            };
        }

        return positions.Count > 0
            ? definition with { Template = template with { Arguments = new EquatableArray<string>(arguments) }, WrittenArguments = positions.ToEquatableArray() }
            : definition;
    }

    // ------------------------------------------------------------------ the switches

    /// <summary>One switch this project writes: <c>[assembly: GenerateTenancyClasses]</c>.</summary>
    /// <param name="Name">The switch as the project writes it, <c>GenerateTenancyClasses</c>.</param>
    /// <param name="Location">Where it is written, where what it cannot write is said.</param>
    /// <param name="Templates">The template attributes it names, open, that it can write a class of.</param>
    /// <param name="IdsOnly">Whether it writes the ids alone.</param>
    private sealed record Switch(string Name, LocationInfo? Location, IReadOnlyList<(INamedTypeSymbol Attribute, TemplateMarker Marker)> Templates, bool IdsOnly);

    /// <summary>The switches of this project: its assembly attributes whose class is marked <c>[TemplateDefaults]</c>.</summary>
    private static List<Switch> SwitchesIn(Compilation compilation, CancellationToken cancellationToken)
    {
        var found = new List<Switch>();
        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            if (attribute.AttributeClass is not { } switchClass)
            {
                continue;
            }

            foreach (var marker in switchClass.GetAttributes())
            {
                if (marker.AttributeClass is not { } markerClass
                    || !EntityDeclarations.Is(markerClass, KnownTypes.TemplateDefaultsAttribute)
                    || marker.ConstructorArguments.Length != 1
                    || marker.ConstructorArguments[0].Kind != TypedConstantKind.Array)
                {
                    continue;
                }

                var templates = new List<(INamedTypeSymbol, TemplateMarker)>();
                foreach (var named in marker.ConstructorArguments[0].Values)
                {
                    // Only a template whose attribute takes the id alone: what a later type argument should be is
                    // the application's to say, and the switch cannot.
                    if (named is { Kind: TypedConstantKind.Type, Value: INamedTypeSymbol { TypeKind: not TypeKind.Error } template }
                        && template.OriginalDefinition is { Arity: 1 } definition
                        && EntityDeclarations.TemplateOf(definition) is { Parent: { TypeParameters.Length: > 0 } } templateMarker)
                    {
                        templates.Add((definition, templateMarker));
                    }
                }

                var idsOnly = marker.NamedArguments.Any(static argument => argument.Key == "IdsOnly" && argument.Value.Value is true);
                var location = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is { } syntax ? LocationInfo.From(syntax) : null;
                found.Add(new Switch(DefinitionFactory.AttributeNameOf(switchClass), location, templates, idsOnly));
            }
        }

        return found;
    }

    /// <summary>One template a switch of this project names, with the names the plan gives its class and id.</summary>
    private sealed class WantedTemplate(INamedTypeSymbol attribute, TemplateMarker marker, Switch from)
    {
        public INamedTypeSymbol Attribute { get; } = attribute;

        public TemplateMarker Marker { get; } = marker;

        public string Key { get; } = attribute.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        public string MetadataName { get; } = EntityDeclarations.MetadataNameOf(attribute);

        public string AttributeName { get; } = DefinitionFactory.AttributeNameOf(attribute);

        /// <summary>The class's name: the template's noun, <c>Tenant</c> for <c>[TenantAggregate]</c>.</summary>
        public string ClassName => MissingTemplateClass.ShortName(AttributeName);

        /// <summary>
        /// The id's name: the class's with <c>Id</c> after it, when the parent's id parameter is that name or a shorter
        /// one for it (<c>TUnitId</c> of <c>OrganizationUnit</c>); otherwise the parameter's own, when the parent names its
        /// id after another class (<c>TTenantId</c> of <c>Organization</c>), whose id it then shares.
        /// </summary>
        public string IdName
        {
            get
            {
                var parameter = MissingTemplateClass.IdName(Marker.Parent!.TypeParameters[0].Name);
                return (ClassName + "Id").EndsWith(parameter, StringComparison.Ordinal) ? ClassName + "Id" : parameter;
            }
        }

        /// <summary>The templates the parent takes a type from with a <c>[TemplateArgument]</c>, by their open definition.</summary>
        public IReadOnlyList<string> Sources { get; } = attribute.GetAttributes()
            .Where(static marker => marker.AttributeClass is { } markerClass && EntityDeclarations.Is(markerClass, KnownTypes.TemplateArgumentAttribute))
            .Select(static marker => marker.ConstructorArguments.Length == 2 && marker.ConstructorArguments[1].Value is INamedTypeSymbol source
                ? source.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : null)
            .OfType<string>()
            .ToList();

        /// <summary>The switch the class is written for, or the ids, when no switch of the project writes the class.</summary>
        public Switch Switch { get; set; } = from;

        public bool WritesClass { get; set; } = !from.IdsOnly;
    }

    /// <summary>Every template the switches name, once, in the order they name them; a class is written when one switch writes classes.</summary>
    private static List<WantedTemplate> Wanted(List<Switch> switches)
    {
        var wanted = new List<WantedTemplate>();
        foreach (var each in switches)
        {
            foreach (var (attribute, marker) in each.Templates)
            {
                var known = wanted.FirstOrDefault(template => SymbolEqualityComparer.Default.Equals(template.Attribute, attribute));
                if (known is null)
                {
                    wanted.Add(new WantedTemplate(attribute, marker, each));
                }
                else if (!each.IdsOnly && !known.WritesClass)
                {
                    known.WritesClass = true;
                    known.Switch = each;
                }
            }
        }

        return wanted;
    }

    // ------------------------------------------------------------------ the ids

    /// <summary>What becomes of one id the plan needs: one that exists, one it writes, or why it can have neither.</summary>
    /// <param name="IdType">The id, fully qualified, when it can be had.</param>
    /// <param name="Written">The switch's reason to write it, when the plan writes it: <c>"no project declares a TenantId"</c>.</param>
    /// <param name="Problem">Why no class can take it, phrased to follow "its id would be 'TenantId', and".</param>
    private sealed record IdOutcome(string? IdType, string? Written, string? Problem);

    /// <summary>
    /// The id called <paramref name="name"/>: the one the package's classes the application declared are declared
    /// with; else the one type of that name in this project, or in the projects of its module that it references (in
    /// every project it references, for a project of no module); else one this plan writes, in <paramref name="scope"/>.
    /// Two to choose from, a type of the name that is no entity id, or an id of the name the generator writes for an
    /// <c>[AggregateRoot&lt;Guid&gt;]</c> class of the project, and no class takes any.
    /// </summary>
    private static IdOutcome Resolve(
        string name,
        Dictionary<string, SortedSet<string>> idsOfClasses,
        EquatableArray<ImplicitIdName> implicitIds,
        Compilation compilation,
        string? module,
        string scope,
        CancellationToken cancellationToken)
    {
        if (idsOfClasses.TryGetValue(name, out var ofClasses))
        {
            return ofClasses.Count == 1
                ? new IdOutcome(ofClasses.First(), null, null)
                : new IdOutcome(null, null, "the classes declared with the package's templates are declared over " + Quoted(ofClasses) + "; declare them over one");
        }

        var own = compilation.ContainsSymbolsWithName(name, SymbolFilter.Type, cancellationToken)
            ? compilation.GetSymbolsWithName(name, SymbolFilter.Type, cancellationToken).OfType<INamedTypeSymbol>().ToList()
            : [];
        if (own.Count > 0)
        {
            var notAnId = own.FirstOrDefault(type => !DefinitionFactory.IsEntityId(type));
            return notAnId is not null
                ? new IdOutcome(null, null, "this project declares '" + notAnId.ToDisplayString() + "', which is no entity id; mark it [EntityId<Guid>], or name it otherwise")
                : own.Count > 1
                    ? new IdOutcome(null, null, "this project declares " + Quoted(own.Select(static type => type.ToDisplayString())) + "; keep one")
                    : new IdOutcome(own[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), null, null);
        }

        // An id the generator writes for a class of the project's own, which is that class's and not the package's:
        // taken, two classes would share it; written beside it, the two would collide.
        foreach (var implicitId in implicitIds)
        {
            if (implicitId.Name == name)
            {
                return new IdOutcome(
                    null,
                    null,
                    "the generator writes '" + implicitId.FullyQualifiedName.Replace("global::", string.Empty) + "' already, the id of this project's "
                    + implicitId.Declaration + " '" + implicitId.Owner + "'; rename that class, or declare the package's class yourself under a name of your own, with an id of its own");
            }
        }

        var referenced = new List<INamedTypeSymbol>();
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (module is not null
                ? !string.Equals(ModuleBoundary.ModuleOf(assembly), module, StringComparison.Ordinal)
                : !assembly.Modules.Any(static part => part.ReferencedAssemblySymbols.Any(static reference => reference.Identity.Name == AbstractionsAssembly)))
            {
                continue;
            }

            if (IdsIn(assembly, cancellationToken).TryGetValue(name, out var found))
            {
                referenced.AddRange(found.Where(type => compilation.IsSymbolAccessibleWithin(type, compilation.Assembly)));
            }
        }

        if (referenced.Count > 1)
        {
            return new IdOutcome(null, null, "the projects it references declare " + Quoted(referenced.Select(static type => type.ToDisplayString())) + "; keep one");
        }

        if (referenced.Count == 1)
        {
            return new IdOutcome(referenced[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), null, null);
        }

        return new IdOutcome(
            "global::" + (scope.Length > 0 ? scope + "." : string.Empty) + name,
            module is not null
                ? "neither it nor a project of the module " + CodeWriter.XmlText(module) + " that it references declares <c>" + name + "</c>"
                : "neither it nor a project it references declares <c>" + name + "</c>",
            null);
    }

    /// <summary>The assembly the toolkit's attributes are in: only an assembly that references it can declare an id.</summary>
    private const string AbstractionsAssembly = "DDDToolkit.Abstractions";

    /// <summary>
    /// The entity ids a referenced assembly declares, by name. Walking an assembly is the expensive part, and a
    /// referenced assembly does not change while the project that references it is edited, so it is walked once.
    /// </summary>
    private static readonly ConditionalWeakTable<IAssemblySymbol, Dictionary<string, List<INamedTypeSymbol>>> IdsByAssembly = new();

    private static Dictionary<string, List<INamedTypeSymbol>> IdsIn(IAssemblySymbol assembly, CancellationToken cancellationToken)
    {
        if (IdsByAssembly.TryGetValue(assembly, out var known))
        {
            return known;
        }

        var found = new Dictionary<string, List<INamedTypeSymbol>>(StringComparer.Ordinal);
        foreach (var type in DefinitionFactory.TypesIn(assembly.GlobalNamespace, cancellationToken))
        {
            if (DefinitionFactory.IsEntityId(type))
            {
                Add(found, type.Name, type);
            }
        }

        return IdsByAssembly.GetValue(assembly, _ => found);
    }

    // ------------------------------------------------------------------ what is written

    /// <summary>A type the plan writes, public, in the project's root namespace.</summary>
    private static TypeDeclarationInfo TypeOf(string name, string scope, DeclarationKind kind, LocationInfo? location)
        => new(
            Name: name,
            Namespace: scope,
            FullyQualifiedName: "global::" + (scope.Length > 0 ? scope + "." : string.Empty) + name,
            Accessibility: "public",
            Kind: kind,
            IsPartial: true,
            IsSealed: kind == DeclarationKind.Class,
            IsReadOnly: kind == DeclarationKind.RecordStruct,
            IsAbstract: false,
            IsGeneric: false,
            ContainingTypeHeaders: EquatableArray<string>.Empty,
            Location: location);

    /// <summary>An id the plan writes, as <c>[EntityId&lt;Guid&gt;] public readonly partial record struct TenantId;</c> would be read.</summary>
    private static EntityIdDefinition IdDefinition(TypeDeclarationInfo type, Compilation compilation)
    {
        var guid = compilation.GetTypeByMetadataName("System.Guid")!;
        return new EntityIdDefinition(
            Type: type,
            Value: DefinitionFactory.CreateValueTypeInfo(guid),
            Prefix: Identifiers.DefaultIdPrefix,
            ColumnLength: -1,
            GraphQLSchemaType: null,
            SystemTextJsonAvailable: compilation.GetTypeByMetadataName(KnownTypes.StjJsonConverterAttribute) is not null,
            IParsableAvailable: compilation.GetTypeByMetadataName(KnownTypes.IParsable) is not null,
            CanGenerate: true,
            Diagnostics: EquatableArray<DiagnosticInfo>.Empty)
        {
            SingleValueAvailable = compilation.GetTypeByMetadataName(KnownTypes.SingleValueInterface) is not null,
        };
    }

    private static DefaultDeclaration IdDeclaration(TypeDeclarationInfo type, List<WantedTemplate> owners, Switch by, string because)
    {
        var templates = DefinitionFactory.Listed(owners.Select(static template => "<c>[" + template.AttributeName + "]</c>"));
        var documentation = new[]
        {
            "<summary>",
            "The id of the " + (owners.Count == 1 ? "class" : "classes") + " declared with " + templates + ": written by the generator, because this",
            "project says <c>[assembly: " + by.Name + "]</c> and " + because + ".",
            "</summary>",
            "<remarks>",
            "A <see cref=\"global::System.Guid\"/> without a prefix, made in code before a save, and published to the other modules",
            "with <c>[ModuleContract]</c>. To choose another key type, a prefix or a comment of your own, declare it yourself,",
            "in this project or in a project of its module that it references, and the generator writes this one no more:",
            "<code>",
            "[ModuleContract, EntityId&lt;Guid&gt;]",
            "public readonly partial record struct " + type.Name + ";",
            "</code>",
            OutsideTheNamespace(type),
            "</remarks>",
        };

        return new DefaultDeclaration(
            type.HintName(".TemplateDefault"),
            type.Namespace,
            documentation.Where(static line => line.Length > 0).ToEquatableArray(),
            new EquatableArray<string>(new[]
            {
                "[global::DDDToolkit.Abstractions.Attributes.ModuleContract]",
                "[global::DDDToolkit.Abstractions.Attributes.EntityId<global::System.Guid>]",
            }),
            "public readonly partial record struct " + type.Name + ";");
    }

    private static DefaultDeclaration ClassDeclaration(TypeDeclarationInfo type, WantedTemplate template, string idType, string? module)
    {
        var id = LastNameOf(idType);
        var nobody = module is not null ? "neither it nor a project of the module " + CodeWriter.XmlText(module) + " that it references" : "neither it nor a project it references";
        var documentation = new[]
        {
            "<summary>",
            "The application's <c>" + type.Name + "</c>, declared <c>[" + template.AttributeName + "&lt;" + id + "&gt;]</c> and nothing more: the package's class as it",
            "ships. Written by the generator, because this project says <c>[assembly: " + template.Switch.Name + "]</c> and",
            nobody + " declares a class with <c>[" + template.AttributeName + "]</c>.",
            "</summary>",
            "<remarks>",
            "To give it fields, rules or behaviour of its own, declare it yourself, anywhere in this project, and the generator",
            "writes this one no more. The package's rules run for yours as they do for this one:",
            "<code>",
            "[" + template.AttributeName + "&lt;" + id + "&gt;]",
            "public sealed partial class " + type.Name,
            "{",
            "}",
            "</code>",
            OutsideTheNamespace(type),
            "</remarks>",
        };

        var attribute = template.Attribute.ContainingType is { } outer
            ? outer.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + template.Attribute.Name
            : "global::" + (template.Attribute.ContainingNamespace is { IsGlobalNamespace: false } scope ? scope.ToDisplayString() + "." : string.Empty) + template.Attribute.Name;

        return new DefaultDeclaration(
            type.HintName(".TemplateDefault"),
            type.Namespace,
            documentation.Where(static line => line.Length > 0).ToEquatableArray(),
            new EquatableArray<string>(new[] { "[" + attribute + "<" + idType + ">]" }),
            "public sealed partial class " + type.Name);
    }

    /// <summary>
    /// How code outside the root namespace names what the switch wrote: with a <c>global using</c>, since the parts
    /// the generators write for the application's classes are files of their own, which a file's using does not
    /// reach. Empty for a project without a root namespace, whose written types are in the global namespace.
    /// </summary>
    private static string OutsideTheNamespace(TypeDeclarationInfo type)
        => type.Namespace.Length == 0
            ? string.Empty
            : "Code outside <c>" + type.Namespace + "</c> names it through <c>global using " + type.Namespace + ";</c>: a using at the top of a file does not reach the parts the generators write.";

    // ------------------------------------------------------------------ small things

    private static void Add<T>(Dictionary<string, List<T>> map, string key, T value)
    {
        if (!map.TryGetValue(key, out var list))
        {
            map.Add(key, list = []);
        }

        list.Add(value);
    }

    private static void Add(Dictionary<string, SortedSet<string>> map, string key, string value)
    {
        if (!map.TryGetValue(key, out var set))
        {
            map.Add(key, set = new SortedSet<string>(StringComparer.Ordinal));
        }

        set.Add(value);
    }

    private static void AddOnce(List<string> names, string name)
    {
        if (!names.Contains(name))
        {
            names.Add(name);
        }
    }

    private static string Quoted(IEnumerable<string> names)
        => DefinitionFactory.Listed(names.Select(static name => "'" + name.Replace("global::", string.Empty) + "'"));

    private static string LastNameOf(string fullyQualified)
        => fullyQualified.Substring(Math.Max(fullyQualified.LastIndexOf('.'), fullyQualified.LastIndexOf(':')) + 1);
}

/// <summary>What the switches of a project write, and the classes every generator reads with them.</summary>
/// <param name="Entities">
/// The classes declared with a template, the source's own, with an id the plan writes bound where the compiler could
/// not, and those the plan writes after them: what <see cref="Providers.DeclaredTemplateEntities"/> hands on.
/// </param>
/// <param name="Ids">The ids the plan writes, which <see cref="Providers.EntityIds"/> hands on beside the declared ones.</param>
/// <param name="Declarations">The declarations the plan writes: the half no other generator writes.</param>
/// <param name="Diagnostics">What a switch cannot write, and why: DDD00066.</param>
/// <param name="Blocked">
/// The templates, by their open definition, whose class a switch was asked for and could not write. DDD00066 says why,
/// so a registration or a class the use cases are named through that misses one stands back, as it does behind
/// DDD00044, rather than say again that nobody declares it.
/// </param>
internal sealed record TemplateDefaultsPlan(
    EquatableArray<EntityDefinition> Entities,
    EquatableArray<EntityIdDefinition> Ids,
    EquatableArray<DefaultDeclaration> Declarations,
    EquatableArray<DiagnosticInfo> Diagnostics,
    EquatableArray<string> Blocked);

/// <summary>An id the generator writes for an <c>[AggregateRoot&lt;Guid&gt;]</c> or <c>[Entity&lt;Guid&gt;]</c> class of the project.</summary>
/// <param name="Name">The id's name, <c>TenantId</c>.</param>
/// <param name="FullyQualifiedName">The id in full, <c>global::Shop.Rentals.TenantId</c>.</param>
/// <param name="Owner">The class it is the id of, <c>Tenant</c>.</param>
/// <param name="Declaration">What that class is, as a message says it: <c>aggregate root</c> or <c>entity</c>.</param>
internal sealed record ImplicitIdName(string Name, string FullyQualifiedName, string Owner, string Declaration);

/// <summary>One declaration a switch writes: the part of a class or an id that its author would have written.</summary>
/// <param name="HintName">The file's name.</param>
/// <param name="Namespace">The namespace it is in: the project's root namespace.</param>
/// <param name="Documentation">The lines of its documentation comment, without the slashes.</param>
/// <param name="Attributes">The attributes it is declared with, fully qualified.</param>
/// <param name="Declaration">The declaration itself, <c>public sealed partial class Tenant</c>: a class gets an empty body, an id a semicolon.</param>
internal sealed record DefaultDeclaration(
    string HintName,
    string Namespace,
    EquatableArray<string> Documentation,
    EquatableArray<string> Attributes,
    string Declaration);
