using System.Reflection;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// A registration is called what its method is called, unless the method says otherwise:
/// <c>[TemplateRegistration(Name = "Add{TAbout}Notes")]</c> names the wrapper after the type that fills
/// <c>TAbout</c>, so an application with notes on songs calls <c>AddSongNotes()</c>, with one class of the
/// template and with several.
/// <para>
/// What a wrapper is named after is usually not the class itself but something the class says about itself: a
/// template may have more type arguments than its parent takes, <c>[Note&lt;SongNoteId, Song&gt;]</c>, and a
/// registration takes those by position, and with <c>IdOfArgument = true</c> the id of the entity or aggregate
/// root one of them names. Every positive test calls the wrapper from the application and runs it.
/// </para>
/// </summary>
public class TemplateRegistrationNamingTests
{
    /// <summary>
    /// What a package for notes would ship: a parent that knows only its own id, and a template that also
    /// says what the notes are about, which the parent has no use for.
    /// </summary>
    private const string Package =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Abstractions.Interfaces;

        namespace Sample.Notes;

        [EntityBase]
        public abstract partial class NoteEntity<TNoteId>
            where TNoteId : IEntityId, IEquatable<TNoteId>
        {
            protected NoteEntity(TNoteId id) : base(id) { }
        }

        [EntityTemplate(typeof(NoteEntity<>), AllowSeveral = true)]
        [AttributeUsage(AttributeTargets.Class, Inherited = false)]
        public sealed class NoteAttribute<TNoteId, TAbout> : Attribute;
        """;

    /// <summary>The package's registrations: each writes down what it was closed over.</summary>
    private const string Registrations =
        """
        using System;
        using System.Collections.Generic;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Abstractions.Interfaces;
        using DDDToolkit.BaseTypes;

        [assembly: TemplateRegistrations(typeof(Sample.Notes.NoteRegistrations))]

        namespace Sample.Notes;

        public sealed class Registry
        {
            public List<string> Entries { get; } = new();
        }

        public static class NoteRegistrations
        {
            // Named after what the notes are about, and closed over that aggregate and its id.
            [TemplateRegistration(Name = "Add{TAbout}Notes")]
            public static Registry AddNotes<
                [TemplateType(typeof(NoteAttribute<,>), Take = TemplateArgumentKind.Type)] TNote,
                [TemplateType(typeof(NoteAttribute<,>))] TNoteId,
                [TemplateType(typeof(NoteAttribute<,>), Argument = 1)] TAbout,
                [TemplateType(typeof(NoteAttribute<,>), Argument = 1, IdOfArgument = true)] TAboutId,
                TContext>(this Registry registry)
                where TNote : NoteEntity<TNoteId>
                where TNoteId : IEntityId, IEquatable<TNoteId>
                where TAbout : AggregateRoot<TAboutId>
                where TAboutId : struct, IEntityId, IEquatable<TAboutId>
                where TContext : class
            {
                registry.Entries.Add(string.Join(", ", typeof(TNote).Name, typeof(TNoteId).Name, typeof(TAbout).Name, typeof(TAboutId).Name, typeof(TContext).Name));
                return registry;
            }

            // Named after two things, one of them an id, and asking nothing of what the notes are about.
            [TemplateRegistration(Name = "Index{TAbout}By{TAboutId}")]
            public static Registry AddIndex<
                [TemplateType(typeof(NoteAttribute<,>), Take = TemplateArgumentKind.Type)] TNote,
                [TemplateType(typeof(NoteAttribute<,>), Argument = 1)] TAbout,
                [TemplateType(typeof(NoteAttribute<,>), Argument = 1, IdOfArgument = true)] TAboutId>(this Registry registry)
                where TNote : class
                where TAbout : class
            {
                registry.Entries.Add(string.Join(", ", typeof(TNote).Name, typeof(TAbout).Name, typeof(TAboutId).Name));
                return registry;
            }

            // Named after the class and after its own id, like any other type parameter that is closed.
            [TemplateRegistration(Name = "List{TNote}Of{TNoteId}")]
            public static Registry AddList<
                [TemplateType(typeof(NoteAttribute<,>), Take = TemplateArgumentKind.Type)] TNote,
                [TemplateType(typeof(NoteAttribute<,>), IdOfArgument = true)] TNoteId>(this Registry registry)
                where TNote : class
            {
                registry.Entries.Add(string.Join(", ", typeof(TNote).Name, typeof(TNoteId).Name));
                return registry;
            }

            // Named after whatever the application wrote as its second type argument.
            [TemplateRegistration(Name = "Tag{TAbout}")]
            public static Registry AddTags<
                [TemplateType(typeof(NoteAttribute<,>), Take = TemplateArgumentKind.Type)] TNote,
                [TemplateType(typeof(NoteAttribute<,>), Argument = 1)] TAbout>(this Registry registry)
                where TNote : class
            {
                registry.Entries.Add(string.Join(", ", typeof(TNote).Name, typeof(TAbout).Name));
                return registry;
            }
        }
        """;

    /// <summary>What an application has notes on: two aggregates of its own, and its ids.</summary>
    private const string Aggregates =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;

        namespace Sample;

        [EntityId<Guid>]
        public readonly partial record struct SongId;

        [EntityId<int>]
        public readonly partial record struct SongNoteId;

        [EntityId<int>]
        public readonly partial record struct VideoNoteId;

        [AggregateRoot<SongId>]
        public sealed partial class Song
        {
            public Song(SongId id) : base(id) { }
        }

        // Declared over a raw value: its id, VideoId, is generated beside it.
        [AggregateRoot<Guid>]
        public sealed partial class Video
        {
            public Video(VideoId id) : base(id) { }
        }

        public sealed class SchemaContext;
        """;

    private const string SongNote =
        """
        using Sample.Notes;

        namespace Sample;

        [Note<SongNoteId, Song>]
        public sealed partial class SongNote
        {
            public SongNote(SongNoteId id) : base(id) { }
        }
        """;

    private const string VideoNote =
        """
        using Sample.Notes;

        namespace Sample;

        [Note<VideoNoteId, Video>]
        public sealed partial class VideoNote
        {
            public VideoNote(VideoNoteId id) : base(id) { }
        }
        """;

    private const string NotesFile = "NoteRegistrations.AddNotes.Registration.";

    private static GeneratorTestHost PackageAndAggregates()
        => GeneratorTestHost.Create(Package, "Package.cs")
            .WithSource(Registrations, "Registrations.cs")
            .WithSource(Aggregates, "Aggregates.cs");

    private static string Startup(string calls)
        => $$"""
             using System.Collections.Generic;
             using Sample.Notes;

             namespace Sample;

             public static class Startup
             {
                 public static List<string> Register() => new Registry(){{calls}}.Entries;
             }
             """;

    // ------------------------------------------------------------------ a template that says more than its parent takes

    [Fact]
    public void A_template_may_have_type_arguments_its_parent_does_not_take()
    {
        var result = PackageAndAggregates().WithSource(SongNote, "SongNote.cs").RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty("a type argument beyond the parent's is the template's own, and no mistake");
        result.Emit().Type("Sample.SongNote").BaseType!.ToString()
            .Should().Be("Sample.Notes.NoteEntity`1[Sample.SongNoteId]", "the parent is closed over what it takes, and no more");
    }

    // ------------------------------------------------------------------ what a registration is called

    [Fact]
    public void A_registration_is_named_after_the_type_that_fills_the_type_parameter_its_method_names()
    {
        var result = PackageAndAggregates()
            .WithSource(SongNote, "SongNote.cs")
            .WithSource(Startup(".AddSongNotes<SchemaContext>()"), "Startup.cs")
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        var source = result.Source(NotesFile);
        source.Should().Contain(
            "public static global::Sample.Notes.Registry AddSongNotes<TContext>(this global::Sample.Notes.Registry registry)",
            "the aggregate the notes are about and its id are closed with the class, so only the context is left to write");
        source.Should().Contain(
            "=> global::Sample.Notes.NoteRegistrations.AddNotes<global::Sample.SongNote, global::Sample.SongNoteId, global::Sample.Song, global::Sample.SongId, TContext>(registry);");
        source.Should().Contain("closed over this project's SongNote, SongNoteId, Song and SongId, with TContext still to choose.");
        source.Should().NotContain(" AddNotes<TContext>(", "a method that names its registration is never called by its own name, with one class either");
        source.Should().NotContain("AddNotesFor");

        TemplateRegistrationArgumentTests.Call(result.Emit(), "Register").Should().Equal("SongNote, SongNoteId, Song, SongId, SchemaContext");
    }

    [Fact]
    public void A_second_class_gets_a_registration_of_the_same_kind_and_the_first_keeps_its_name()
    {
        var result = PackageAndAggregates()
            .WithSource(SongNote, "SongNote.cs")
            .WithSource(VideoNote, "VideoNote.cs")
            .WithSource(Startup(".AddSongNotes<SchemaContext>().AddVideoNotes<SchemaContext>()"), "Startup.cs")
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        var source = result.Source(NotesFile);
        source.Should().Contain(
            "=> global::Sample.Notes.NoteRegistrations.AddNotes<global::Sample.SongNote, global::Sample.SongNoteId, global::Sample.Song, global::Sample.SongId, TContext>(registry);");
        source.Should().Contain(
            "=> global::Sample.Notes.NoteRegistrations.AddNotes<global::Sample.VideoNote, global::Sample.VideoNoteId, global::Sample.Video, global::Sample.VideoId, TContext>(registry);",
            "an aggregate declared over a raw value has its id generated beside it, and that is the id taken");
        source.Should().Contain("closed over this project's VideoNote, VideoNoteId, Video and VideoId, with TContext still to choose.");
        source.Should().NotContain("AddNotesFor", "the method says what its registrations are called");

        var emitted = result.Emit();
        TemplateRegistrationArgumentTests.Call(emitted, "Register").Should().Equal(
            "SongNote, SongNoteId, Song, SongId, SchemaContext",
            "VideoNote, VideoNoteId, Video, VideoId, SchemaContext");
        emitted.Type("Sample.Notes.GeneratedNoteRegistrations").GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name.EndsWith("Notes", StringComparison.Ordinal)).Select(method => method.Name)
            .Should().BeEquivalentTo("AddSongNotes", "AddVideoNotes");
    }

    [Fact]
    public void A_name_takes_every_type_parameter_it_names_the_class_and_an_id_among_them()
    {
        var result = PackageAndAggregates()
            .WithSource(SongNote, "SongNote.cs")
            .WithSource(VideoNote, "VideoNote.cs")
            .WithSource(Startup(".IndexSongBySongId().IndexVideoByVideoId().ListSongNoteOfSongNoteId().ListVideoNoteOfVideoNoteId().TagSong().TagVideo()"), "Startup.cs")
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        TemplateRegistrationArgumentTests.Call(result.Emit(), "Register").Should().Equal(
            "SongNote, Song, SongId",
            "VideoNote, Video, VideoId",
            "SongNote, SongNoteId",
            "VideoNote, VideoNoteId",
            "SongNote, Song",
            "VideoNote, Video");
    }

    [Fact]
    public void Classes_of_one_name_are_told_apart_by_what_their_registrations_are_named_after()
    {
        // Named after their classes these two would be DDD00045. Named after what they are about they differ.
        var result = PackageAndAggregates()
            .WithSource(
                """
                using Sample.Notes;

                namespace Sample.Songs
                {
                    [Note<SongNoteId, Song>]
                    public sealed partial class Remark
                    {
                        public Remark(SongNoteId id) : base(id) { }
                    }
                }

                namespace Sample.Videos
                {
                    [Note<VideoNoteId, Video>]
                    public sealed partial class Remark
                    {
                        public Remark(VideoNoteId id) : base(id) { }
                    }
                }
                """,
                "Remarks.cs")
            .WithSource(Startup(".AddSongNotes<SchemaContext>().AddVideoNotes<SchemaContext>()"), "Startup.cs")
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        TemplateRegistrationArgumentTests.Call(result.Emit(), "Register").Should().Equal(
            "Remark, SongNoteId, Song, SongId, SchemaContext",
            "Remark, VideoNoteId, Video, VideoId, SchemaContext");
    }

    [Fact]
    public void A_project_of_a_module_gets_the_named_registrations_of_the_classes_its_module_declares()
    {
        // The infrastructure project declares no class. What the notes are about, and its id, are read from the
        // module's domain project as it was compiled.
        var result = GeneratorTestHost.Create(
                """
                using System.Collections.Generic;
                using Sample.Notes;

                namespace Shop.Infrastructure;

                public sealed class ShopContext;

                public static class Startup
                {
                    public static List<string> Register() => new Registry().AddSongNotes<ShopContext>().AddVideoNotes<ShopContext>().Entries;
                }
                """,
                "Startup.cs")
            .WithSource("[assembly: DDDToolkit.Abstractions.Attributes.Module(\"Shop\")]\n", "Module.cs")
            .WithReferencedAssembly(Package, "Sample.Notes")
            .WithReferencedProject("Shop.Domain", project => project
                .WithSource("[assembly: DDDToolkit.Abstractions.Attributes.Module(\"Shop\")]\n", "Module.cs")
                .WithSource(InShopDomain(Aggregates), "Aggregates.cs")
                .WithSource(InShopDomain(SongNote), "SongNote.cs")
                .WithSource(InShopDomain(VideoNote), "VideoNote.cs"))
            .WithReferencedAssembly(Registrations, "Sample.Notes.Registrations")
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldContain(
            NotesFile,
            "AddNotes<global::Shop.Domain.SongNote, global::Shop.Domain.SongNoteId, global::Shop.Domain.Song, global::Shop.Domain.SongId, TContext>(registry)");
        result.ShouldContain(
            NotesFile,
            "AddNotes<global::Shop.Domain.VideoNote, global::Shop.Domain.VideoNoteId, global::Shop.Domain.Video, global::Shop.Domain.VideoId, TContext>(registry)",
            "the generated id of a compiled aggregate is what its base class is closed over");

        static string InShopDomain(string source) => source.Replace("namespace Sample;", "namespace Shop.Domain;", StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ what is reported

    [Fact]
    public void Two_classes_whose_registrations_come_to_one_name_are_DDD00045_and_the_others_keep_theirs()
    {
        var result = PackageAndAggregates()
            .WithSource(SongNote, "SongNote.cs")
            .WithSource(VideoNote, "VideoNote.cs")
            .WithSource(
                """
                using DDDToolkit.Abstractions.Attributes;
                using Sample.Notes;

                namespace Sample;

                [EntityId<int>]
                public readonly partial record struct SongRemarkId;

                [Note<SongRemarkId, Song>]
                public sealed partial class SongRemark
                {
                    public SongRemark(SongRemarkId id) : base(id) { }
                }
                """,
                "SongRemark.cs")
            .RunCore();

        var messages = result.ReportedDiagnostics.Where(diagnostic => diagnostic.Id == "DDD00045").Select(diagnostic => diagnostic.GetMessage()).ToList();
        messages.Should().Contain(message =>
            message.Contains("'NoteRegistrations.AddNotes' is declared with [TemplateRegistration], which takes 'TNote' from the class declared with [Note]", StringComparison.Ordinal)
            && message.Contains("'SongNote' and 'SongRemark'", StringComparison.Ordinal)
            && message.Contains("the registration of each would be called 'AddSongNotes', and a call could not tell them apart", StringComparison.Ordinal));
        messages.Should().NotContain(message => message.Contains("keep one", StringComparison.Ordinal), "the template is meant to be declared more than once");
        messages.Should().NotContain(message => message.Contains("'VideoNote'", StringComparison.Ordinal), "nothing is wrong with the notes on videos");
        result.ReportedDiagnostics.Select(diagnostic => diagnostic.Id).Distinct().Should().Equal("DDD00045");
        messages.Should().HaveCount(1, "said once, though each of the three methods whose name is taken from what the notes are about refuses them; the fourth is named after the class");
        result.ShouldHaveDiagnostic("DDD00045", at: "SongRemark");

        var source = result.Source(NotesFile);
        source.Should().Contain("AddVideoNotes<TContext>(");
        source.Should().NotContain("AddSongNotes");
        result.ShouldContain("NoteRegistrations.AddList.Registration.", "ListSongRemarkOfSongRemarkId(", "a registration named after something that differs is written for both");
        result.CompilationErrors.Should().BeEmpty("nothing is written that would not compile");
    }

    [Fact]
    public void A_type_argument_whose_id_is_taken_and_that_is_no_entity_is_DDD00050()
    {
        var result = PackageAndAggregates()
            .WithSource(
                """
                using Sample.Notes;

                namespace Sample;

                public sealed class Playlist;

                [Note<SongNoteId, Playlist>]
                public sealed partial class PlaylistNote
                {
                    public PlaylistNote(SongNoteId id) : base(id) { }
                }
                """,
                "PlaylistNote.cs")
            .RunCore();

        // AddNotes and AddIndex both refuse the playlist for the one reason: said once, by the first of them.
        var messages = result.ReportedDiagnostics.Where(diagnostic => diagnostic.Id == "DDD00050").Select(diagnostic => diagnostic.GetMessage()).ToList();
        messages.Should().ContainSingle().Which.Should().Contain("takes 'Playlist' as 'TAboutId', which requires an entity or an aggregate root, whose id it takes");
        result.ShouldHaveDiagnostic("DDD00050", at: "PlaylistNote");

        result.HintNames.Should().NotContain("AddNotes").And.NotContain("AddIndex");
        result.ShouldContain("NoteRegistrations.AddTags.Registration.", "TagPlaylist(", "a registration that takes only the type itself is written");
        result.CompilationErrors.Should().BeEmpty("nothing is written that would not compile");
    }

    [Fact]
    public void A_registration_is_not_named_after_a_type_that_has_no_name()
    {
        var result = PackageAndAggregates()
            .WithSource(
                """
                using Sample.Notes;

                namespace Sample;

                [Note<SongNoteId, Song[]>]
                public sealed partial class SetlistNote
                {
                    public SetlistNote(SongNoteId id) : base(id) { }
                }
                """,
                "SetlistNote.cs")
            .RunCore();

        result.ReportedDiagnostics.Where(diagnostic => diagnostic.Id == "DDD00050").Select(diagnostic => diagnostic.GetMessage())
            .Should().Contain(message =>
                message.Contains("'NoteRegistrations.AddTags' takes 'Song[]' as 'TAbout', which requires a type with a name of its own, which the registration is named after", StringComparison.Ordinal));
        result.HintNames.Should().NotContain("AddTags");
        result.ShouldContain("NoteRegistrations.AddList.Registration.", "ListSetlistNoteOfSongNoteId(");
        result.CompilationErrors.Should().BeEmpty("nothing is written that would not compile");
    }

    [Fact]
    public void An_id_taken_from_a_type_argument_is_held_to_the_methods_constraints()
    {
        // AddNotes asks for an id that is a struct; this aggregate's is a record class.
        var result = PackageAndAggregates()
            .WithSource(
                """
                using DDDToolkit.Abstractions.Attributes;
                using Sample.Notes;

                namespace Sample;

                [EntityId<int>]
                public partial record AlbumId;

                [AggregateRoot<AlbumId>]
                public sealed partial class Album
                {
                    public Album(AlbumId id) : base(id) { }
                }

                [Note<SongNoteId, Album>]
                public sealed partial class AlbumNote
                {
                    public AlbumNote(SongNoteId id) : base(id) { }
                }
                """,
                "AlbumNote.cs")
            .RunCore();

        result.ShouldHaveDiagnostic("DDD00050", at: "AlbumNote").GetMessage()
            .Should().Contain("'NoteRegistrations.AddNotes' takes 'AlbumId' as 'TAboutId', which requires a struct");
        result.Count("DDD00050").Should().Be(1);
        result.HintNames.Should().NotContain("AddNotes");
        result.ShouldContain("NoteRegistrations.AddIndex.Registration.", "IndexAlbumByAlbumId(", "a registration that asks nothing of the id is written");
        result.CompilationErrors.Should().BeEmpty("nothing is written that would not compile");
    }

    [Theory]
    [InlineData("[EntityId<int>]\npublic readonly partial record struct VerseId;\n\n[Entity<VerseId>]", "over an id of its own")]
    [InlineData("[Entity<int>]", "over a raw value, whose id is still to be generated")]
    public void A_type_argument_that_names_a_child_entity_where_the_method_asks_for_an_aggregate_root_is_DDD00050(string declaredAs, string because)
    {
        // AddNotes asks for what the notes are about to be an aggregate root. What a class of this project will be
        // is the generator's to say, so it is said here, on the class, and not by the compiler inside the registration.
        var result = PackageAndAggregates()
            .WithSource(
                $$"""
                using System.Collections.Generic;
                using DDDToolkit.Abstractions.Attributes;
                using Sample.Notes;

                namespace Sample;

                [EntityId<int>]
                public readonly partial record struct HymnId;

                [AggregateRoot<HymnId>]
                public sealed partial class Hymn
                {
                    public Hymn(HymnId id) : base(id) { }

                    public partial IReadOnlyList<Verse> Verses { get; }
                }

                {{declaredAs}}
                public sealed partial class Verse
                {
                    public Verse(VerseId id) : base(id) { }
                }

                [Note<SongNoteId, Verse>]
                public sealed partial class VerseNote
                {
                    public VerseNote(SongNoteId id) : base(id) { }
                }
                """,
                "VerseNote.cs")
            .RunCore();

        result.ShouldHaveDiagnostic("DDD00050", at: "VerseNote").GetMessage()
            .Should().Contain("'NoteRegistrations.AddNotes' takes 'Verse' as 'TAbout', which requires an aggregate root", because);
        result.Count("DDD00050").Should().Be(1);
        result.HintNames.Should().NotContain("AddNotes");
        result.ShouldContain("NoteRegistrations.AddIndex.Registration.", "IndexVerseByVerseId(", "a registration that asks only for a class is written, with the entity's id");
        result.CompilationErrors.Should().BeEmpty("nothing is written that would not compile");
    }

    [Theory]
    [InlineData("public enum SongId { None }\n\n[AggregateRoot<Guid>]\npublic sealed partial class Song;", "DDD00007", "the name its id would have is taken")]
    [InlineData("[AggregateRoot<Guid>]\npublic sealed class Song;", "DDD00005", "it is not partial, over a raw value")]
    [InlineData("[EntityId<Guid>]\npublic readonly partial record struct SongId;\n\n[AggregateRoot<SongId>]\npublic sealed class Song;", "DDD00005", "it is not partial, over an id of its own")]
    [InlineData("[EntityId<Guid>]\npublic readonly partial record struct SongId;\n\n[AggregateRoot<SongId>]\n[Entity<SongId>]\npublic sealed partial class Song;", "DDD00009", "it is declared two ways")]
    public void An_aggregate_that_is_itself_refused_leaves_the_registration_out_without_a_second_report(string song, string reported, string because)
    {
        // Nothing is generated for an aggregate the toolkit refuses, neither its base class nor an id over a raw
        // value, and its own diagnostic says why. A registration closed over it would only add errors in generated code.
        var result = GeneratorTestHost.Create(Package, "Package.cs")
            .WithSource(Registrations, "Registrations.cs")
            .WithSource(
                $$"""
                using System;
                using DDDToolkit.Abstractions.Attributes;
                using Sample.Notes;

                namespace Sample;

                [EntityId<int>]
                public readonly partial record struct SongNoteId;

                {{song}}

                [Note<SongNoteId, Song>]
                public sealed partial class SongNote
                {
                    public SongNote(SongNoteId id) : base(id) { }
                }
                """,
                "Song.cs")
            .RunCore();

        result.ShouldHaveDiagnostic(reported, at: "Song");
        result.Count("DDD00050").Should().Be(0, "the aggregate's own diagnostic says what is wrong with it: " + because);
        result.Count("DDD00045").Should().Be(0);
        result.HintNames.Should().NotContain("AddNotes", because).And.NotContain("AddIndex");
        result.CompilationErrors.Should().BeEmpty("nothing is written that would not compile");
    }

    [Theory]
    [InlineData("Add{TMissing}Notes", "braces around nothing the method has")]
    [InlineData("Add{TContext}Notes", "a type parameter the application still chooses, so its name is not known where the wrapper is written")]
    [InlineData("Add{TAbout", "a brace that is not closed")]
    [InlineData("Add-{TAbout}", "text no name can have")]
    public void A_name_no_registration_can_have_leaves_the_method_without_one(string name, string because)
    {
        // The package's mistake: its own test of the call is what shows it.
        var mistaken = Registrations.Replace("\"Add{TAbout}Notes\"", "\"" + name + "\"", StringComparison.Ordinal);
        var result = GeneratorTestHost.Create(Package, "Package.cs")
            .WithSource(mistaken, "Registrations.cs")
            .WithSource(Aggregates, "Aggregates.cs")
            .WithSource(SongNote, "SongNote.cs")
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.HintNames.Should().NotContain("AddNotes", because);
        result.ShouldHaveGenerated("NoteRegistrations.AddTags.Registration.");
    }

    [Fact]
    public void A_method_that_names_nothing_keeps_the_names_it_always_had()
    {
        // No Name: the method's own name with one class, and For and the class with several.
        var unnamed = Registrations.Replace("[TemplateRegistration(Name = \"Add{TAbout}Notes\")]", "[TemplateRegistration]", StringComparison.Ordinal);
        var one = GeneratorTestHost.Create(Package, "Package.cs").WithSource(unnamed, "Registrations.cs").WithSource(Aggregates, "Aggregates.cs")
            .WithSource(SongNote, "SongNote.cs")
            .RunCore();
        var two = GeneratorTestHost.Create(Package, "Package.cs").WithSource(unnamed, "Registrations.cs").WithSource(Aggregates, "Aggregates.cs")
            .WithSource(SongNote, "SongNote.cs")
            .WithSource(VideoNote, "VideoNote.cs")
            .RunCore();

        one.ShouldCompile();
        one.ShouldContain(NotesFile, " AddNotes<TContext>(this global::Sample.Notes.Registry registry)");
        two.ShouldCompile();
        two.ShouldContain(NotesFile, " AddNotesForSongNote<TContext>(");
        two.ShouldContain(NotesFile, " AddNotesForVideoNote<TContext>(");
    }
}
