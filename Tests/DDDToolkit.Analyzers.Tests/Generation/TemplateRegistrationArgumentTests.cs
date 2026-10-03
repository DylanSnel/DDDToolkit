using System.Collections;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// A template may have more than one type argument: <c>[Comment&lt;CommentId, AuthorId, Rating&gt;]</c> names the
/// comment's id, and after it whatever the application identifies an author by and rates with. A registration
/// that takes the class has to take those as well, with <c>[TemplateType(..., Argument = n)]</c>: the class is
/// its parent closed over exactly those types, so a wrapper that left them open would not compile.
/// <para>
/// Every positive test calls the wrapper from the application and runs it, so a wrapper that compiles but
/// forwards to the wrong types fails too.
/// </para>
/// </summary>
public class TemplateRegistrationArgumentTests
{
    /// <summary>
    /// What a package for comments would ship: a parent with three type parameters, all of them chosen by the
    /// application, and a template an application declares once per thing it has comments on.
    /// </summary>
    internal const string Package =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Abstractions.Interfaces;

        namespace Sample.Comments;

        [EntityBase]
        public abstract partial class CommentEntity<TCommentId, TAuthorId, TRating>
            where TCommentId : IEntityId, IEquatable<TCommentId>
            where TAuthorId : struct, IEquatable<TAuthorId>
            where TRating : notnull
        {
            protected CommentEntity(TCommentId id, TAuthorId author, TRating rating) : base(id)
            {
                WrittenBy = author;
                Rating = rating;
            }

            public TAuthorId WrittenBy { get; private set; }

            public TRating Rating { get; private set; }
        }

        [EntityTemplate(typeof(CommentEntity<,,>), AllowSeveral = true)]
        [AttributeUsage(AttributeTargets.Class, Inherited = false)]
        public sealed class CommentAttribute<TCommentId, TAuthorId, TRating> : Attribute;
        """;

    /// <summary>The package's registrations: each writes down what it was closed over, so a test reads back what the wrapper forwarded.</summary>
    internal const string Registrations =
        """
        using System;
        using System.Collections.Generic;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Abstractions.Interfaces;

        [assembly: TemplateRegistrations(typeof(Sample.Comments.CommentRegistrations))]

        namespace Sample.Comments;

        public sealed class Registry
        {
            public List<string> Entries { get; } = new();
        }

        public static class CommentRegistrations
        {
            [TemplateRegistration]
            public static Registry AddComments<
                [TemplateType(typeof(CommentAttribute<,,>), Take = TemplateArgumentKind.Type)] TComment,
                [TemplateType(typeof(CommentAttribute<,,>))] TCommentId,
                [TemplateType(typeof(CommentAttribute<,,>), Argument = 1)] TAuthorId,
                [TemplateType(typeof(CommentAttribute<,,>), Argument = 2)] TRating,
                TContext>(this Registry registry, Func<TAuthorId, TRating>? ratingOf = null)
                where TComment : CommentEntity<TCommentId, TAuthorId, TRating>
                where TCommentId : IEntityId, IEquatable<TCommentId>
                where TAuthorId : struct, IEquatable<TAuthorId>
                where TRating : notnull
                where TContext : class
            {
                registry.Entries.Add(string.Join(", ", typeof(TComment).Name, typeof(TCommentId).Name, typeof(TAuthorId).Name, typeof(TRating).Name, typeof(TContext).Name, ratingOf is null ? "no ratings" : "ratings"));
                return registry;
            }
        }
        """;

    /// <summary>A registration that asks more of a rating than the parent does: a struct that can be compared.</summary>
    internal const string RatedRegistrations =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Abstractions.Interfaces;

        [assembly: TemplateRegistrations(typeof(Sample.Comments.RatedRegistrations))]

        namespace Sample.Comments;

        public static class RatedRegistrations
        {
            [TemplateRegistration]
            public static Registry AddRatedComments<
                [TemplateType(typeof(CommentAttribute<,,>), Take = TemplateArgumentKind.Type)] TComment,
                [TemplateType(typeof(CommentAttribute<,,>), Argument = 0)] TCommentId,
                [TemplateType(typeof(CommentAttribute<,,>), Argument = 1)] TAuthorId,
                [TemplateType(typeof(CommentAttribute<,,>), Argument = 2)] TRating>(this Registry registry)
                where TComment : CommentEntity<TCommentId, TAuthorId, TRating>
                where TCommentId : IEntityId, IEquatable<TCommentId>
                where TAuthorId : struct, IEquatable<TAuthorId>
                where TRating : struct, IComparable<TRating>
            {
                registry.Entries.Add(string.Join(", ", typeof(TComment).Name, typeof(TRating).Name));
                return registry;
            }
        }
        """;

    /// <summary>The ids an application declares, and what else it identifies and rates with.</summary>
    internal const string Ids =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;

        namespace Sample;

        [EntityId<int>]
        public readonly partial record struct SongCommentId;

        [EntityId<int>]
        public readonly partial record struct VideoCommentId;

        [EntityId<Guid>]
        public readonly partial record struct AuthorId;

        public sealed class SchemaContext;
        """;

    /// <summary>The comments on a song: by an author the application has an id for, rated in words.</summary>
    internal const string SongComment =
        """
        using Sample.Comments;

        namespace Sample;

        [Comment<SongCommentId, AuthorId, string>]
        public sealed partial class SongComment
        {
            public SongComment(SongCommentId id, AuthorId author, string rating) : base(id, author, rating) { }
        }
        """;

    /// <summary>The comments on a video: by whoever a <see cref="Guid"/> names, rated with a number.</summary>
    internal const string VideoComment =
        """
        using System;
        using Sample.Comments;

        namespace Sample;

        [Comment<VideoCommentId, Guid, int>]
        public sealed partial class VideoComment
        {
            public VideoComment(VideoCommentId id, Guid author, int rating) : base(id, author, rating) { }
        }
        """;

    internal static GeneratorTestHost PackageAndIds()
        => GeneratorTestHost.Create(Package, "Package.cs")
            .WithSource(Registrations, "Registrations.cs")
            .WithSource(Ids, "Ids.cs");

    internal static List<string> Call(EmittedAssembly emitted, string method)
        => ((IEnumerable)emitted.CallStatic("Sample.Startup", method)!).Cast<string>().ToList();

    private const string WrapperFile = "CommentRegistrations.AddComments.Registration.";

    // ------------------------------------------------------------------ what is generated

    [Fact]
    public void A_registration_takes_the_later_type_arguments_of_a_template()
    {
        var result = PackageAndIds()
            .WithSource(SongComment, "SongComment.cs")
            .WithSource(
                """
                using System.Collections.Generic;
                using Sample.Comments;

                namespace Sample;

                public static class Startup
                {
                    public static List<string> Register() => new Registry().AddComments<SchemaContext>().Entries;

                    // The lambda compiles only against the wrapper's closed parameter, a Func of the application's own types.
                    public static List<string> RegisterWithRatings() => new Registry().AddComments<SchemaContext>(author => author.ToString()).Entries;
                }
                """,
                "Startup.cs")
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        var source = result.Source(WrapperFile);
        source.Should().Contain(
            "public static global::Sample.Comments.Registry AddComments<TContext>(this global::Sample.Comments.Registry registry, global::System.Func<global::Sample.AuthorId, string>? ratingOf = null)",
            "only the type parameter without a template stays open, and a parameter that names the others names the application's types");
        source.Should().Contain("where TContext : class");
        source.Should().Contain(
            "=> global::Sample.Comments.CommentRegistrations.AddComments<global::Sample.SongComment, global::Sample.SongCommentId, global::Sample.AuthorId, string, TContext>(registry, ratingOf);");
        source.Should().Contain("closed over this project's SongComment, SongCommentId, AuthorId and string, with TContext still to choose.");
        source.Should().NotContain("AddCommentsFor", "with one class the registration keeps the method's own name");

        var emitted = result.Emit();
        Call(emitted, "Register").Should().Equal("SongComment, SongCommentId, AuthorId, String, SchemaContext, no ratings");
        Call(emitted, "RegisterWithRatings").Should().Equal("SongComment, SongCommentId, AuthorId, String, SchemaContext, ratings");
    }

    [Fact]
    public void A_later_type_argument_is_whatever_the_application_wrote_there()
    {
        // Neither an entity id nor a type of the application's: only the first type argument is an id.
        var result = PackageAndIds()
            .WithSource(RatedRegistrations, "RatedRegistrations.cs")
            .WithSource(VideoComment, "VideoComment.cs")
            .WithSource(
                """
                using System.Collections.Generic;
                using Sample.Comments;

                namespace Sample;

                public static class Startup
                {
                    public static List<string> Register() => new Registry().AddComments<SchemaContext>().Entries;

                    public static List<string> RegisterRated() => new Registry().AddRatedComments().Entries;
                }
                """,
                "Startup.cs")
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldContain(
            WrapperFile,
            "=> global::Sample.Comments.CommentRegistrations.AddComments<global::Sample.VideoComment, global::Sample.VideoCommentId, global::System.Guid, int, TContext>(registry, ratingOf);");
        result.ShouldContain(WrapperFile, "global::System.Func<global::System.Guid, int>? ratingOf = null");
        result.ShouldContain(
            "RatedRegistrations.AddRatedComments.Registration.",
            "public static global::Sample.Comments.Registry AddRatedComments(this global::Sample.Comments.Registry registry)",
            "a registration that takes every type argument has none left to write");

        var emitted = result.Emit();
        Call(emitted, "Register").Should().Equal("VideoComment, VideoCommentId, Guid, Int32, SchemaContext, no ratings");
        Call(emitted, "RegisterRated").Should().Equal("VideoComment, Int32");
    }

    [Fact]
    public void A_class_the_modules_domain_project_declares_hands_over_its_later_arguments_too()
    {
        // The infrastructure project declares no class; the wrapper is closed over what its module's domain declares.
        var result = GeneratorTestHost.Create(
                """
                using System.Collections.Generic;
                using Sample.Comments;

                namespace Shop.Infrastructure;

                public sealed class ShopContext;

                public static class Startup
                {
                    public static List<string> Register() => new Registry().AddComments<ShopContext>().Entries;
                }
                """,
                "Startup.cs")
            .WithSource("[assembly: DDDToolkit.Abstractions.Attributes.Module(\"Shop\")]\n", "Module.cs")
            .WithReferencedAssembly(Package, "Sample.Comments")
            .WithReferencedProject("Shop.Domain", project => project
                .WithSource("[assembly: DDDToolkit.Abstractions.Attributes.Module(\"Shop\")]\n", "Module.cs")
                .WithSource(Ids.Replace("namespace Sample;", "namespace Shop.Domain;", StringComparison.Ordinal), "Ids.cs")
                .WithSource(SongComment.Replace("namespace Sample;", "namespace Shop.Domain;", StringComparison.Ordinal), "SongComment.cs"))
            .WithReferencedAssembly(Registrations, "Sample.Comments.Registrations")
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldContain(
            WrapperFile,
            "AddComments<global::Shop.Domain.SongComment, global::Shop.Domain.SongCommentId, global::Shop.Domain.AuthorId, string, TContext>(registry, ratingOf)");
    }

    // ------------------------------------------------------------------ what is reported

    [Fact]
    public void A_later_type_argument_that_misses_a_constraint_of_the_method_is_DDD00050()
    {
        // The parent takes any rating; AddRatedComments only one that is a struct and can be compared.
        var result = PackageAndIds().WithSource(RatedRegistrations, "RatedRegistrations.cs").WithSource(SongComment, "SongComment.cs").RunCore();

        result.ShouldHaveDiagnostic("DDD00050", at: "SongComment").GetMessage()
            .Should().Contain("'RatedRegistrations.AddRatedComments' takes 'string' as 'TRating', which requires a struct");
        result.Count("DDD00050").Should().Be(1);
        result.HintNames.Should().NotContain("AddRatedComments");
        result.ShouldHaveGenerated(WrapperFile);
        result.CompilationErrors.Should().BeEmpty("nothing is written that would not compile");
    }

    [Fact]
    public void A_later_type_argument_is_held_to_an_interface_the_method_asks_for()
    {
        // A struct written out in full, so what it shows is all it will ever be, and it cannot be compared.
        var result = PackageAndIds()
            .WithSource(RatedRegistrations, "RatedRegistrations.cs")
            .WithSource(
                """
                using Sample.Comments;

                namespace Sample;

                public readonly record struct Mood(string Word);

                [Comment<SongCommentId, AuthorId, Mood>]
                public sealed partial class SongComment
                {
                    public SongComment(SongCommentId id, AuthorId author, Mood rating) : base(id, author, rating) { }
                }
                """,
                "SongComment.cs")
            .RunCore();

        result.ShouldHaveDiagnostic("DDD00050", at: "SongComment").GetMessage()
            .Should().Contain("takes 'Mood' as 'TRating', which requires 'IComparable<Mood>'");
        result.HintNames.Should().NotContain("AddRatedComments");
        result.CompilationErrors.Should().BeEmpty("nothing is written that would not compile");
    }

    [Fact]
    public void A_type_argument_the_compiler_cannot_bind_leaves_the_registration_out_without_a_second_report()
    {
        var result = PackageAndIds()
            .WithSource(
                """
                using Sample.Comments;

                namespace Sample;

                [Comment<SongCommentId, NotDeclared, string>]
                public sealed partial class SongComment;
                """,
                "SongComment.cs")
            .RunCore();

        result.ShouldNotCrash();
        result.ReportedDiagnostics.Should().BeEmpty("the compiler says the type is missing, and that is the error to fix");
        result.HintNames.Should().NotContain("Registration");
        result.CompilationErrors.Select(error => error.Id).Should().Contain("CS0246");
    }

    [Fact]
    public void A_method_that_asks_for_an_argument_its_template_does_not_have_is_passed_over()
    {
        const string Mistaken =
            """
            using DDDToolkit.Abstractions.Attributes;

            [assembly: TemplateRegistrations(typeof(Sample.Comments.MistakenRegistrations))]

            namespace Sample.Comments;

            public static class MistakenRegistrations
            {
                [TemplateRegistration]
                public static Registry AddBeyond<[TemplateType(typeof(CommentAttribute<,,>), Argument = 3)] TFourth>(this Registry registry) => registry;

                [TemplateRegistration]
                public static Registry AddBefore<[TemplateType(typeof(CommentAttribute<,,>), Argument = -1)] TNone>(this Registry registry) => registry;

                [TemplateRegistration]
                public static Registry AddBoth<[TemplateType(typeof(CommentAttribute<,,>), Take = TemplateArgumentKind.Type, Argument = 1)] TComment>(this Registry registry) => registry;
            }
            """;

        // The package's mistake: its own test of the call without type arguments is what shows it.
        var result = PackageAndIds().WithSource(Mistaken, "Mistaken.cs").WithSource(VideoComment, "VideoComment.cs").RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.HintNames.Should().NotContain("MistakenRegistrations");
        result.ShouldHaveGenerated(WrapperFile);
    }
}
