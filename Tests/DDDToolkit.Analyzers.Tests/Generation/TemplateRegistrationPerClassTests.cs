using System.Reflection;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// Most templates stand for something an application has one of, and a second class declared with one is a
/// mistake (DDD00045). A template marked <c>AllowSeveral = true</c> is declared once per thing the application
/// has, the comments on a song and the comments on a video, and a registration that takes its types from it
/// is written once per class, named after the class: <c>AddCommentsForSongComment</c> and
/// <c>AddCommentsForVideoComment</c>.
/// <para>
/// What is refused stays what cannot be told apart: two templates of one method that each have several
/// classes, two classes of one name, and a parent that takes a type from the template, which is closed over
/// one class whatever the template allows.
/// </para>
/// </summary>
public class TemplateRegistrationPerClassTests
{
    /// <summary>A second template that allows several, for an aggregate root, with a registration of its own.</summary>
    private const string Topics =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Abstractions.Interfaces;

        [assembly: TemplateRegistrations(typeof(Sample.Comments.TopicRegistrations))]

        namespace Sample.Comments;

        [AggregateRootBase]
        public abstract partial class TopicAggregate<TTopicId>
            where TTopicId : IEntityId, IEquatable<TTopicId>
        {
            protected TopicAggregate(TTopicId id) : base(id) { }
        }

        [AggregateRootTemplate(typeof(TopicAggregate<>), AllowSeveral = true)]
        [AttributeUsage(AttributeTargets.Class, Inherited = false)]
        public sealed class TopicAttribute<TTopicId> : Attribute;

        public static class TopicRegistrations
        {
            [TemplateRegistration]
            public static Registry AddTopics<
                [TemplateType(typeof(TopicAttribute<>), Take = TemplateArgumentKind.Type)] TTopic,
                [TemplateType(typeof(TopicAttribute<>))] TTopicId>(this Registry registry)
                where TTopic : TopicAggregate<TTopicId>
                where TTopicId : IEntityId, IEquatable<TTopicId>
            {
                registry.Entries.Add(typeof(TTopic).Name + ", " + typeof(TTopicId).Name);
                return registry;
            }
        }
        """;

    /// <summary>A registration that takes a class of each template: a topic and a comment.</summary>
    private const string TopicComments =
        """
        using DDDToolkit.Abstractions.Attributes;

        [assembly: TemplateRegistrations(typeof(Sample.Comments.TopicCommentRegistrations))]

        namespace Sample.Comments;

        public static class TopicCommentRegistrations
        {
            [TemplateRegistration]
            public static Registry AddTopicComments<
                [TemplateType(typeof(TopicAttribute<>), Take = TemplateArgumentKind.Type)] TTopic,
                [TemplateType(typeof(CommentAttribute<,,>), Take = TemplateArgumentKind.Type)] TComment>(this Registry registry)
                where TTopic : class
                where TComment : class
            {
                registry.Entries.Add(typeof(TTopic).Name + ", " + typeof(TComment).Name);
                return registry;
            }
        }
        """;

    private const string SongTopic =
        """
        using DDDToolkit.Abstractions.Attributes;
        using Sample.Comments;

        namespace Sample;

        [EntityId<int>]
        public readonly partial record struct SongTopicId;

        [Topic<SongTopicId>]
        public sealed partial class SongTopic
        {
            public SongTopic(SongTopicId id) : base(id) { }
        }
        """;

    private const string VideoTopic =
        """
        using DDDToolkit.Abstractions.Attributes;
        using Sample.Comments;

        namespace Sample;

        [EntityId<int>]
        public readonly partial record struct VideoTopicId;

        [Topic<VideoTopicId>]
        public sealed partial class VideoTopic
        {
            public VideoTopic(VideoTopicId id) : base(id) { }
        }
        """;

    private const string CommentsFile = "CommentRegistrations.AddComments.Registration.";

    private static GeneratorTestHost BothComments()
        => TemplateRegistrationArgumentTests.PackageAndIds()
            .WithSource(TemplateRegistrationArgumentTests.SongComment, "SongComment.cs")
            .WithSource(TemplateRegistrationArgumentTests.VideoComment, "VideoComment.cs");

    // ------------------------------------------------------------------ what is generated

    [Fact]
    public void Each_class_of_a_template_that_allows_several_gets_a_registration_named_after_it()
    {
        var result = BothComments()
            .WithSource(
                """
                using System.Collections.Generic;
                using Sample.Comments;

                namespace Sample;

                public static class Startup
                {
                    public static List<string> Register()
                        => new Registry()
                            .AddCommentsForSongComment<SchemaContext>(author => author.ToString())
                            .AddCommentsForVideoComment<SchemaContext>()
                            .Entries;
                }
                """,
                "Startup.cs")
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty("several classes are what the template is for");
        var source = result.Source(CommentsFile);
        source.Should().Contain(
            "public static global::Sample.Comments.Registry AddCommentsForSongComment<TContext>(this global::Sample.Comments.Registry registry, global::System.Func<global::Sample.AuthorId, string>? ratingOf = null)");
        source.Should().Contain(
            "=> global::Sample.Comments.CommentRegistrations.AddComments<global::Sample.SongComment, global::Sample.SongCommentId, global::Sample.AuthorId, string, TContext>(registry, ratingOf);");
        source.Should().Contain(
            "public static global::Sample.Comments.Registry AddCommentsForVideoComment<TContext>(this global::Sample.Comments.Registry registry, global::System.Func<global::System.Guid, int>? ratingOf = null)");
        source.Should().Contain(
            "=> global::Sample.Comments.CommentRegistrations.AddComments<global::Sample.VideoComment, global::Sample.VideoCommentId, global::System.Guid, int, TContext>(registry, ratingOf);",
            "every type parameter that names the template is filled from the same class");
        source.Should().NotContain(" AddComments<TContext>(", "no wrapper has the method's own name, since there would be no telling which class it means");

        var emitted = result.Emit();
        TemplateRegistrationArgumentTests.Call(emitted, "Register").Should().Equal(
            "SongComment, SongCommentId, AuthorId, String, SchemaContext, ratings",
            "VideoComment, VideoCommentId, Guid, Int32, SchemaContext, no ratings");
        emitted.Type("Sample.Comments.GeneratedCommentRegistrations").GetMethods(BindingFlags.Public | BindingFlags.Static).Select(method => method.Name)
            .Should().BeEquivalentTo(
                ["AddCommentsForSongComment", "AddCommentsForVideoComment"],
                "the wrappers of one method share its file, in one partial class");
    }

    [Fact]
    public void What_is_wrong_with_one_class_leaves_the_others_their_registration()
    {
        // AddRatedComments asks for a rating that is a struct. The song's is a string, the video's a number.
        var result = BothComments().WithSource(TemplateRegistrationArgumentTests.RatedRegistrations, "RatedRegistrations.cs").RunCore();

        result.ShouldHaveDiagnostic("DDD00050", at: "SongComment").GetMessage()
            .Should().Contain("'RatedRegistrations.AddRatedComments' takes 'string' as 'TRating', which requires a struct");
        result.ShouldHaveExactlyDiagnostics("DDD00050");
        var source = result.Source("RatedRegistrations.AddRatedComments.Registration.");
        source.Should().Contain("AddRatedCommentsForVideoComment(this global::Sample.Comments.Registry registry)");
        source.Should().NotContain("AddRatedCommentsForSongComment");
        result.CompilationErrors.Should().BeEmpty("nothing is written that would not compile");
    }

    [Fact]
    public void A_class_that_cannot_be_generated_leaves_the_others_their_registration_without_a_second_report()
    {
        // VideoComment is not partial: DDD00005 says so, and a registration taking it would only add errors in
        // generated code. The song's registration is still named after its class, because there are two.
        var result = TemplateRegistrationArgumentTests.PackageAndIds()
            .WithSource(TemplateRegistrationArgumentTests.SongComment, "SongComment.cs")
            .WithSource(
                """
                using System;
                using Sample.Comments;

                namespace Sample;

                [Comment<VideoCommentId, Guid, int>]
                public sealed class VideoComment;
                """,
                "VideoComment.cs")
            .RunCore();

        result.ShouldHaveDiagnostic("DDD00005", at: "VideoComment");
        result.Count("DDD00045").Should().Be(0);
        var source = result.Source(CommentsFile);
        source.Should().Contain("AddCommentsForSongComment<TContext>(");
        source.Should().NotContain("VideoComment");
    }

    [Fact]
    public void An_aggregate_root_template_allows_several_the_same_way()
    {
        var result = TemplateRegistrationArgumentTests.PackageAndIds()
            .WithSource(Topics, "Topics.cs")
            .WithSource(SongTopic, "SongTopic.cs")
            .WithSource(VideoTopic, "VideoTopic.cs")
            .WithSource(
                """
                using System.Collections.Generic;
                using Sample.Comments;

                namespace Sample;

                public static class Startup
                {
                    public static List<string> Register() => new Registry().AddTopicsForSongTopic().AddTopicsForVideoTopic().Entries;
                }
                """,
                "Startup.cs")
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain(
            "TopicRegistrations.AddTopics.Registration.",
            "=> global::Sample.Comments.TopicRegistrations.AddTopics<global::Sample.VideoTopic, global::Sample.VideoTopicId>(registry);");
        TemplateRegistrationArgumentTests.Call(result.Emit(), "Register").Should().Equal("SongTopic, SongTopicId", "VideoTopic, VideoTopicId");
    }

    [Fact]
    public void A_method_that_takes_a_second_template_with_one_class_is_closed_over_that_class_each_time()
    {
        var result = BothComments()
            .WithSource(Topics, "Topics.cs")
            .WithSource(TopicComments, "TopicComments.cs")
            .WithSource(SongTopic, "SongTopic.cs")
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        var source = result.Source("TopicCommentRegistrations.AddTopicComments.Registration.");
        source.Should().Contain("AddTopicCommentsForSongComment(this global::Sample.Comments.Registry registry)");
        source.Should().Contain("TopicCommentRegistrations.AddTopicComments<global::Sample.SongTopic, global::Sample.SongComment>(registry);");
        source.Should().Contain("AddTopicCommentsForVideoComment(this global::Sample.Comments.Registry registry)");
        source.Should().Contain("TopicCommentRegistrations.AddTopicComments<global::Sample.SongTopic, global::Sample.VideoComment>(registry);");
        result.ShouldContain("TopicRegistrations.AddTopics.Registration.", " AddTopics(this global::Sample.Comments.Registry registry)", "the one topic's own registration keeps the method's name");
    }

    [Fact]
    public void A_project_of_a_module_gets_a_registration_for_each_class_its_module_declares()
    {
        var result = GeneratorTestHost.Create(
                """
                using System.Collections.Generic;
                using Sample.Comments;

                namespace Shop.Infrastructure;

                public sealed class ShopContext;

                public static class Startup
                {
                    public static List<string> Register()
                        => new Registry().AddCommentsForSongComment<ShopContext>().AddCommentsForVideoComment<ShopContext>().Entries;
                }
                """,
                "Startup.cs")
            .WithSource("[assembly: DDDToolkit.Abstractions.Attributes.Module(\"Shop\")]\n", "Module.cs")
            .WithReferencedAssembly(TemplateRegistrationArgumentTests.Package, "Sample.Comments")
            .WithReferencedProject("Shop.Domain", project => project
                .WithSource("[assembly: DDDToolkit.Abstractions.Attributes.Module(\"Shop\")]\n", "Module.cs")
                .WithSource(InShopDomain(TemplateRegistrationArgumentTests.Ids), "Ids.cs")
                .WithSource(InShopDomain(TemplateRegistrationArgumentTests.SongComment), "SongComment.cs")
                .WithSource(InShopDomain(TemplateRegistrationArgumentTests.VideoComment), "VideoComment.cs"))
            .WithReferencedAssembly(TemplateRegistrationArgumentTests.Registrations, "Sample.Comments.Registrations")
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldContain(
            CommentsFile,
            "AddComments<global::Shop.Domain.SongComment, global::Shop.Domain.SongCommentId, global::Shop.Domain.AuthorId, string, TContext>(registry, ratingOf)");
        result.ShouldContain(
            CommentsFile,
            "AddComments<global::Shop.Domain.VideoComment, global::Shop.Domain.VideoCommentId, global::System.Guid, int, TContext>(registry, ratingOf)");

        static string InShopDomain(string source) => source.Replace("namespace Sample;", "namespace Shop.Domain;", StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ what stays refused

    [Fact]
    public void Two_templates_that_each_have_several_classes_are_DDD00045()
    {
        // AddTopicComments takes a topic and a comment. With two of each there is no telling which comment goes
        // with which topic; the registrations that take one template only are still written per class.
        var result = BothComments()
            .WithSource(Topics, "Topics.cs")
            .WithSource(TopicComments, "TopicComments.cs")
            .WithSource(SongTopic, "SongTopic.cs")
            .WithSource(VideoTopic, "VideoTopic.cs")
            .RunCore();

        var messages = result.ReportedDiagnostics.Where(diagnostic => diagnostic.Id == "DDD00045").Select(diagnostic => diagnostic.GetMessage()).ToList();
        messages.Should().HaveCount(2);
        messages.Should().Contain(message =>
            message.Contains("'TopicCommentRegistrations.AddTopicComments' is declared with [TemplateRegistration], which takes 'TTopic' from the class declared with [Topic]", StringComparison.Ordinal)
            && message.Contains("'SongTopic' and 'VideoTopic'", StringComparison.Ordinal)
            && message.Contains("this one takes two templates that each have several", StringComparison.Ordinal));
        messages.Should().Contain(message => message.Contains("which takes 'TComment' from the class declared with [Comment]", StringComparison.Ordinal));
        messages.Should().NotContain(message => message.Contains("keep one", StringComparison.Ordinal), "both templates are meant to be declared more than once");

        result.HintNames.Should().NotContain("AddTopicComments");
        result.ShouldContain("TopicRegistrations.AddTopics.Registration.", "AddTopicsForSongTopic(");
        result.ShouldContain(CommentsFile, "AddCommentsForVideoComment<TContext>(");
    }

    [Fact]
    public void A_template_that_allows_several_is_not_reported_beside_one_that_does_not()
    {
        // A package whose topic is something an application has one of. Two topics are the mistake, and once
        // one is left the two comments are no mistake at all, so nothing is said about them.
        var result = BothComments()
            .WithSource(Topics.Replace(", AllowSeveral = true", string.Empty, StringComparison.Ordinal), "Topics.cs")
            .WithSource(TopicComments, "TopicComments.cs")
            .WithSource(SongTopic, "SongTopic.cs")
            .WithSource(VideoTopic, "VideoTopic.cs")
            .RunCore();

        var messages = result.ReportedDiagnostics.Where(diagnostic => diagnostic.Id == "DDD00045").Select(diagnostic => diagnostic.GetMessage()).ToList();
        messages.Should().Contain(message =>
            message.Contains("'TopicCommentRegistrations.AddTopicComments' is declared with [TemplateRegistration], which takes 'TTopic' from the class declared with [Topic]", StringComparison.Ordinal)
            && message.EndsWith("'SongTopic' and 'VideoTopic'; keep one", StringComparison.Ordinal));
        messages.Should().OnlyContain(message => message.Contains("from the class declared with [Topic]", StringComparison.Ordinal), "the comments are meant to be declared more than once");
        messages.Should().OnlyContain(message => message.EndsWith("; keep one", StringComparison.Ordinal));

        result.HintNames.Should().NotContain("AddTopicComments", "the method is not closed while its topic cannot be told");
        result.ShouldContain(CommentsFile, "AddCommentsForSongComment<TContext>(", "the comments' own registration is written per class as ever");
        result.CompilationErrors.Should().BeEmpty("nothing is written that would not compile");
    }

    [Fact]
    public void Two_classes_of_one_name_are_DDD00045_because_their_registrations_are_named_after_them()
    {
        var result = TemplateRegistrationArgumentTests.PackageAndIds()
            .WithSource(
                """
                using System;
                using Sample.Comments;

                namespace Sample.Songs
                {
                    [Comment<SongCommentId, Guid, int>]
                    public sealed partial class Remark
                    {
                        public Remark(SongCommentId id, Guid author, int rating) : base(id, author, rating) { }
                    }
                }

                namespace Sample.Videos
                {
                    [Comment<VideoCommentId, Guid, int>]
                    public sealed partial class Remark
                    {
                        public Remark(VideoCommentId id, Guid author, int rating) : base(id, author, rating) { }
                    }
                }
                """,
                "Remarks.cs")
            .RunCore();

        var diagnostic = result.ShouldHaveDiagnostic("DDD00045", at: "Remark");
        diagnostic.GetMessage().Should().Contain("'Remark' and 'Remark'").And.Contain("a registration is named after its class, so give each a name of its own");
        result.HintNames.Should().NotContain("Registration");
        result.CompilationErrors.Should().BeEmpty("nothing is written that would not compile");
    }

    [Fact]
    public void A_parent_that_takes_a_type_from_the_template_is_still_closed_over_one_class()
    {
        // What the template allows is about registrations. A parent has one type argument to fill, and with two
        // classes there is still no telling whose id it is.
        var result = BothComments()
            .WithSource(
                """
                using System;
                using DDDToolkit.Abstractions.Attributes;
                using DDDToolkit.Abstractions.Interfaces;

                namespace Sample.Comments
                {
                    [AggregateRootBase]
                    public abstract partial class DigestAggregate<TDigestId, TCommentId>
                        where TDigestId : IEntityId, IEquatable<TDigestId>
                        where TCommentId : IEntityId, IEquatable<TCommentId>
                    {
                        protected DigestAggregate(TDigestId id) : base(id) { }
                    }

                    [AggregateRootTemplate(typeof(DigestAggregate<,>))]
                    [TemplateArgument(1, typeof(CommentAttribute<,,>))]
                    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
                    public sealed class DigestAttribute<TDigestId> : Attribute;
                }

                namespace Sample
                {
                    [EntityId<int>]
                    public readonly partial record struct DigestId;

                    [Sample.Comments.Digest<DigestId>]
                    public sealed partial class ShopDigest;
                }
                """,
                "Digest.cs")
            .RunCore();

        result.ShouldHaveDiagnostic("DDD00045", at: "ShopDigest").GetMessage()
            .Should().Contain("'ShopDigest' is declared with [Digest], which takes 'TCommentId' from the class declared with [Comment]")
            .And.Contain("'SongComment' and 'VideoComment'; keep one");
        result.ShouldContain(CommentsFile, "AddCommentsForSongComment<TContext>(", "the registrations are not held up by it");
    }
}
