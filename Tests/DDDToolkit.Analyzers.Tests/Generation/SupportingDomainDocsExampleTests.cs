using DDDToolkit.Interfaces;

namespace DDDToolkit.Analyzers.Tests.Generation;

/// <summary>
/// The example in docs/writing-a-supporting-domain.md, compiled and run: a supporting domain for
/// subscriptions written as a package, and an application that extends it. The page copies its code from
/// here, so what it shows is known to build and to behave the way it says.
/// </summary>
public class SupportingDomainDocsExampleTests
{
    /// <summary>The package: its parents, its templates, and the factory it creates the application's lines through.</summary>
    internal const string Package =
        """
        using System;
        using System.Collections.Generic;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Abstractions.Interfaces;
        using DDDToolkit.Invariants;

        namespace Acme.Subscriptions;

        [AggregateRootBase]
        public abstract partial class SubscriptionAggregate<TSubscriptionId>
            where TSubscriptionId : IEntityId, IEquatable<TSubscriptionId>
        {
            protected SubscriptionAggregate(TSubscriptionId id, string plan) : base(id) => Plan = plan;

            public string Plan { get; private set; } = "";

            public sealed class PlanIsRequired : IInvariant<SubscriptionAggregate<TSubscriptionId>>
            {
                public string Code => "subscription.plan";

                public InvariantFailure? Check(SubscriptionAggregate<TSubscriptionId> entity)
                    => entity.Plan.Length > 0 ? null : "A subscription is on a plan.";
            }
        }

        [AggregateRootTemplate(typeof(SubscriptionAggregate<>))]
        [AttributeUsage(AttributeTargets.Class, Inherited = false)]
        public sealed class SubscriptionAttribute<TSubscriptionId> : Attribute;

        [EntityBase]
        public abstract partial class InvoiceLineEntity<TLineId>
            where TLineId : IEntityId, IEquatable<TLineId>
        {
            protected InvoiceLineEntity(TLineId id, decimal amount) : base(id) => Amount = amount;

            public decimal Amount { get; private set; }

            public sealed class AmountIsPositive : IInvariant<InvoiceLineEntity<TLineId>>
            {
                public string Code => "invoice.line.amount";

                public InvariantFailure? Check(InvoiceLineEntity<TLineId> entity)
                    => entity.Amount > 0 ? null : "An invoice line charges something.";
            }
        }

        [EntityTemplate(typeof(InvoiceLineEntity<>))]
        [AttributeUsage(AttributeTargets.Class, Inherited = false)]
        public sealed class InvoiceLineAttribute<TLineId> : Attribute;

        public interface IInvoiceLineFactory<TSelf, TLineId>
            where TSelf : IInvoiceLineFactory<TSelf, TLineId>
        {
            static abstract TSelf Create(TLineId id, decimal amount);
        }

        [AggregateRootBase]
        public abstract partial class InvoiceAggregate<TInvoiceId, TSubscriptionId, TLine, TLineId>
            where TInvoiceId : IEntityId, IEquatable<TInvoiceId>
            where TSubscriptionId : IEntityId, IEquatable<TSubscriptionId>
            where TLine : InvoiceLineEntity<TLineId>, IInvoiceLineFactory<TLine, TLineId>
            where TLineId : IEntityId, IEquatable<TLineId>
        {
            protected InvoiceAggregate(TInvoiceId id, TSubscriptionId subscriptionId) : base(id) => SubscriptionId = subscriptionId;

            public TSubscriptionId SubscriptionId { get; private set; }

            public partial IReadOnlyList<TLine> Lines { get; }

            public TLine Charge(TLineId id, decimal amount)
            {
                var line = TLine.Create(id, amount);
                _lines.Add(line);
                return line;
            }
        }

        [AggregateRootTemplate(typeof(InvoiceAggregate<,,,>))]
        [TemplateArgument(1, typeof(SubscriptionAttribute<>))]
        [TemplateArgument(2, typeof(InvoiceLineAttribute<>), Take = TemplateArgumentKind.Type)]
        [TemplateArgument(3, typeof(InvoiceLineAttribute<>))]
        [AttributeUsage(AttributeTargets.Class, Inherited = false)]
        public sealed class InvoiceAttribute<TInvoiceId> : Attribute;
        """;

    /// <summary>The application's ids, in what would be its contracts project.</summary>
    internal const string Contracts =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;

        namespace Shop.Contracts;

        [EntityId<Guid>]
        public readonly partial record struct SubscriptionId;

        [EntityId<Guid>]
        public readonly partial record struct InvoiceId;

        [EntityId<Guid>]
        public readonly partial record struct InvoiceLineId;
        """;

    /// <summary>The application: its own classes, declared with the package's templates.</summary>
    internal const string Application =
        """
        using Acme.Subscriptions;
        using DDDToolkit.Invariants;
        using Shop.Contracts;

        namespace Shop.Billing;

        [Subscription<SubscriptionId>]
        public sealed partial class ShopSubscription
        {
            public ShopSubscription(SubscriptionId id, string plan, bool isTrial) : base(id, plan) => IsTrial = isTrial;

            public bool IsTrial { get; private set; }

            public sealed class TrialsAreOnTheFreePlan : IInvariant<ShopSubscription>
            {
                public string Code => "shop.subscription.trial";

                public InvariantFailure? Check(ShopSubscription entity)
                    => !entity.IsTrial || entity.Plan == "free" ? null : "A trial is on the free plan.";
            }

            public sealed class PlanIsKnown : IInvariant<SubscriptionAggregate<SubscriptionId>>
            {
                public string Code => "shop.subscription.plan-known";

                public InvariantFailure? Check(SubscriptionAggregate<SubscriptionId> entity)
                    => entity.Plan is "" or "free" or "pro" ? null : "A subscription is on a plan the shop sells.";
            }
        }

        [InvoiceLine<InvoiceLineId>]
        public sealed partial class ShopInvoiceLine : IInvoiceLineFactory<ShopInvoiceLine, InvoiceLineId>
        {
            private ShopInvoiceLine(InvoiceLineId id, decimal amount) : base(id, amount) { }

            public string? CostCentre { get; private set; }

            public static ShopInvoiceLine Create(InvoiceLineId id, decimal amount) => new(id, amount);
        }

        [Invoice<InvoiceId>]
        public sealed partial class ShopInvoice
        {
            public ShopInvoice(InvoiceId id, SubscriptionId subscriptionId) : base(id, subscriptionId) { }
        }
        """;

    /// <summary>The package's registration, generic over the application's classes, closed for the application by the generator.</summary>
    internal const string Registration =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Abstractions.Interfaces;
        using Microsoft.EntityFrameworkCore;

        [assembly: TemplateRegistrations(typeof(Acme.Subscriptions.SubscriptionModelBuilderExtensions))]

        namespace Acme.Subscriptions;

        public static class SubscriptionModelBuilderExtensions
        {
            [TemplateRegistration]
            public static ModelBuilder AddSubscriptions<
                [TemplateType(typeof(SubscriptionAttribute<>), Take = TemplateArgumentKind.Type)] TSubscription,
                [TemplateType(typeof(SubscriptionAttribute<>))] TSubscriptionId,
                [TemplateType(typeof(InvoiceAttribute<>), Take = TemplateArgumentKind.Type)] TInvoice,
                [TemplateType(typeof(InvoiceAttribute<>))] TInvoiceId,
                [TemplateType(typeof(InvoiceLineAttribute<>), Take = TemplateArgumentKind.Type)] TLine,
                [TemplateType(typeof(InvoiceLineAttribute<>))] TLineId>(
                this ModelBuilder modelBuilder, string schema = "sales")
                where TSubscription : SubscriptionAggregate<TSubscriptionId>
                where TSubscriptionId : IEntityId, IEquatable<TSubscriptionId>
                where TInvoice : InvoiceAggregate<TInvoiceId, TSubscriptionId, TLine, TLineId>
                where TInvoiceId : IEntityId, IEquatable<TInvoiceId>
                where TLine : InvoiceLineEntity<TLineId>, IInvoiceLineFactory<TLine, TLineId>
                where TLineId : IEntityId, IEquatable<TLineId>
            {
                modelBuilder.Entity<TSubscription>().ToTable("Subscriptions", schema);
                modelBuilder.Entity<TInvoice>().ToTable("Invoices", schema);
                return modelBuilder;
            }
        }
        """;

    /// <summary>The application's context, which calls the registration without naming its classes.</summary>
    internal const string Context =
        """
        using Acme.Subscriptions;
        using Microsoft.EntityFrameworkCore;

        namespace Shop.Billing;

        public sealed class BillingContext(DbContextOptions<BillingContext> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddSubscriptions();
        }
        """;

    /// <summary>
    /// The package's use cases and a record they answer, nested in one class generic over the application's classes,
    /// which the project that declares the classes gets closed over them, as a class named as the package's is without its type parameters.
    /// </summary>
    internal const string UseCases =
        """
        using System;
        using System.Linq;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Abstractions.Interfaces;

        [assembly: TemplateFacade(typeof(Acme.Subscriptions.SubscriptionUseCases<,,,,,>))]

        namespace Acme.Subscriptions;

        public abstract partial class SubscriptionUseCases<
            [TemplateType(typeof(SubscriptionAttribute<>), Take = TemplateArgumentKind.Type)] TSubscription,
            [TemplateType(typeof(SubscriptionAttribute<>))] TSubscriptionId,
            [TemplateType(typeof(InvoiceAttribute<>), Take = TemplateArgumentKind.Type)] TInvoice,
            [TemplateType(typeof(InvoiceAttribute<>))] TInvoiceId,
            [TemplateType(typeof(InvoiceLineAttribute<>), Take = TemplateArgumentKind.Type)] TLine,
            [TemplateType(typeof(InvoiceLineAttribute<>))] TLineId>
            where TSubscription : SubscriptionAggregate<TSubscriptionId>
            where TSubscriptionId : IEntityId, IEquatable<TSubscriptionId>
            where TInvoice : InvoiceAggregate<TInvoiceId, TSubscriptionId, TLine, TLineId>
            where TInvoiceId : IEntityId, IEquatable<TInvoiceId>
            where TLine : InvoiceLineEntity<TLineId>, IInvoiceLineFactory<TLine, TLineId>
            where TLineId : IEntityId, IEquatable<TLineId>
        {
            // For the class the application gets, which derives from this one: nothing makes either.
            protected SubscriptionUseCases()
            {
            }

            public sealed record InvoiceDue(TInvoiceId Invoice, TSubscriptionId Subscription, decimal Amount);

            public sealed class Dunning
            {
                public InvoiceDue Due(TInvoice invoice) => new(invoice.Id, invoice.SubscriptionId, invoice.Lines.Sum(line => line.Amount));
            }
        }
        """;

    /// <summary>A class of a project above the one that declares the classes, which names the use cases through the class the project that declares the classes gets.</summary>
    internal const string Reminders =
        """
        using Shop.Billing;

        namespace Shop.Billing.Application;

        public sealed class Reminders(SubscriptionUseCases.Dunning dunning)
        {
            public SubscriptionUseCases.InvoiceDue DueFor(ShopInvoice invoice) => dunning.Due(invoice);
        }
        """;

    /// <summary>
    /// The package's comments: a template with a second type argument, what the application identifies an author
    /// by, which an application declares once per thing it has comments on.
    /// </summary>
    internal const string Comments =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Abstractions.Interfaces;
        using Microsoft.EntityFrameworkCore;

        [assembly: TemplateRegistrations(typeof(Acme.Subscriptions.CommentModelBuilderExtensions))]

        namespace Acme.Subscriptions;

        [AggregateRootBase]
        public abstract partial class CommentAggregate<TCommentId, TAuthorId>
            where TCommentId : IEntityId, IEquatable<TCommentId>
            where TAuthorId : struct, IEquatable<TAuthorId>
        {
            protected CommentAggregate(TCommentId id, TAuthorId writtenBy, string text) : base(id)
            {
                WrittenBy = writtenBy;
                Text = text;
            }

            public TAuthorId WrittenBy { get; private set; }

            public string Text { get; private set; } = "";
        }

        [AggregateRootTemplate(typeof(CommentAggregate<,>), AllowSeveral = true)]
        [AttributeUsage(AttributeTargets.Class, Inherited = false)]
        public sealed class CommentAttribute<TCommentId, TAuthorId> : Attribute;

        public static class CommentModelBuilderExtensions
        {
            [TemplateRegistration]
            public static ModelBuilder AddComments<
                [TemplateType(typeof(CommentAttribute<,>), Take = TemplateArgumentKind.Type)] TComment,
                [TemplateType(typeof(CommentAttribute<,>))] TCommentId,
                [TemplateType(typeof(CommentAttribute<,>), Argument = 1)] TAuthorId>(
                this ModelBuilder modelBuilder, string table)
                where TComment : CommentAggregate<TCommentId, TAuthorId>
                where TCommentId : IEntityId, IEquatable<TCommentId>
                where TAuthorId : struct, IEquatable<TAuthorId>
            {
                modelBuilder.Entity<TComment>().ToTable(table);
                return modelBuilder;
            }
        }
        """;

    /// <summary>The ids the application's comments are declared with, and what it identifies its staff by.</summary>
    internal const string CommentContracts =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;

        namespace Shop.Contracts;

        [EntityId<Guid>]
        public readonly partial record struct SubscriptionCommentId;

        [EntityId<Guid>]
        public readonly partial record struct InvoiceCommentId;

        [EntityId<Guid>]
        public readonly partial record struct StaffId;
        """;

    internal const string InvoiceComment =
        """
        using Acme.Subscriptions;
        using Shop.Contracts;

        namespace Shop.Billing;

        [Comment<InvoiceCommentId, StaffId>]
        public sealed partial class ShopInvoiceComment
        {
            public ShopInvoiceComment(InvoiceCommentId id, StaffId writtenBy, string text) : base(id, writtenBy, text) { }
        }
        """;

    internal const string SubscriptionComment =
        """
        using Acme.Subscriptions;
        using Shop.Contracts;

        namespace Shop.Billing;

        [Comment<SubscriptionCommentId, StaffId>]
        public sealed partial class ShopSubscriptionComment
        {
            public ShopSubscriptionComment(SubscriptionCommentId id, StaffId writtenBy, string text) : base(id, writtenBy, text) { }
        }
        """;

    private static GeneratorTestHost CommentsOfTheShop()
        => GeneratorTestHost.Create(Comments, "Comments.cs")
            .WithSource(CommentContracts, "CommentContracts.cs")
            .WithSource(InvoiceComment, "ShopInvoiceComment.cs")
            .WithEntityFramework();

    [Fact]
    public void A_registration_takes_the_second_type_argument_of_the_comment_template()
    {
        var result = CommentsOfTheShop()
            .WithSource(
                """
                using Acme.Subscriptions;
                using Microsoft.EntityFrameworkCore;

                namespace Shop.Billing;

                public sealed class BillingContext(DbContextOptions<BillingContext> options) : DbContext(options)
                {
                    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddComments("InvoiceComments");
                }
                """,
                "BillingContext.cs")
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain(
            "CommentModelBuilderExtensions.AddComments.Registration.",
            "public static global::Microsoft.EntityFrameworkCore.ModelBuilder AddComments(this global::Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder, string table)");
        result.ShouldContain(
            "CommentModelBuilderExtensions.AddComments.Registration.",
            "AddComments<global::Shop.Billing.ShopInvoiceComment, global::Shop.Contracts.InvoiceCommentId, global::Shop.Contracts.StaffId>(modelBuilder, table)");
        result.ShouldContain(
            Hint.Of("Shop.Billing.ShopInvoiceComment"),
            "partial class ShopInvoiceComment : global::Acme.Subscriptions.CommentAggregate<global::Shop.Contracts.InvoiceCommentId, global::Shop.Contracts.StaffId>");
    }

    [Fact]
    public void Each_comment_class_gets_a_registration_named_after_it()
    {
        var result = CommentsOfTheShop()
            .WithSource(SubscriptionComment, "ShopSubscriptionComment.cs")
            .WithSource(
                """
                using Acme.Subscriptions;
                using Microsoft.EntityFrameworkCore;

                namespace Shop.Billing;

                public sealed class BillingContext(DbContextOptions<BillingContext> options) : DbContext(options)
                {
                    protected override void OnModelCreating(ModelBuilder modelBuilder)
                    {
                        modelBuilder.AddCommentsForShopSubscriptionComment("SubscriptionComments");
                        modelBuilder.AddCommentsForShopInvoiceComment("InvoiceComments");
                    }
                }
                """,
                "BillingContext.cs")
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain(
            "CommentModelBuilderExtensions.AddComments.Registration.",
            "internal static partial class GeneratedCommentModelBuilderExtensions");
        result.ShouldContain(
            "CommentModelBuilderExtensions.AddComments.Registration.",
            "public static global::Microsoft.EntityFrameworkCore.ModelBuilder AddCommentsForShopSubscriptionComment(this global::Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder, string table)");
        result.ShouldContain(
            "CommentModelBuilderExtensions.AddComments.Registration.",
            "AddComments<global::Shop.Billing.ShopSubscriptionComment, global::Shop.Contracts.SubscriptionCommentId, global::Shop.Contracts.StaffId>(modelBuilder, table)");
        result.ShouldContain(
            "CommentModelBuilderExtensions.AddComments.Registration.",
            "AddComments<global::Shop.Billing.ShopInvoiceComment, global::Shop.Contracts.InvoiceCommentId, global::Shop.Contracts.StaffId>(modelBuilder, table)");
    }

    /// <summary>
    /// The package's discounts: a template that says what a class of discounts is given on, which its
    /// parent has no use for, and a registration named after that and closed over it and its id.
    /// </summary>
    internal const string Discounts =
        """
        using System;
        using DDDToolkit.Abstractions.Attributes;
        using DDDToolkit.Abstractions.Interfaces;
        using DDDToolkit.BaseTypes;
        using Microsoft.EntityFrameworkCore;

        [assembly: TemplateRegistrations(typeof(Acme.Subscriptions.DiscountModelBuilderExtensions))]

        namespace Acme.Subscriptions;

        [EntityBase]
        public abstract partial class DiscountEntity<TDiscountId>
            where TDiscountId : IEntityId, IEquatable<TDiscountId>
        {
            protected DiscountEntity(TDiscountId id, decimal percent) : base(id) => Percent = percent;

            public decimal Percent { get; private set; }
        }

        // TOwner is what a class of discounts is given on. The parent takes the id alone.
        [EntityTemplate(typeof(DiscountEntity<>), AllowSeveral = true)]
        [AttributeUsage(AttributeTargets.Class, Inherited = false)]
        public sealed class DiscountAttribute<TDiscountId, TOwner> : Attribute;

        public static class DiscountModelBuilderExtensions
        {
            [TemplateRegistration(Name = "Add{TOwner}Discounts")]
            public static ModelBuilder AddDiscounts<
                [TemplateType(typeof(DiscountAttribute<,>), Take = TemplateArgumentKind.Type)] TDiscount,
                [TemplateType(typeof(DiscountAttribute<,>))] TDiscountId,
                [TemplateType(typeof(DiscountAttribute<,>), Argument = 1)] TOwner,
                [TemplateType(typeof(DiscountAttribute<,>), Argument = 1, IdOfArgument = true)] TOwnerId>(
                this ModelBuilder modelBuilder)
                where TDiscount : DiscountEntity<TDiscountId>
                where TDiscountId : IEntityId, IEquatable<TDiscountId>
                where TOwner : AggregateRoot<TOwnerId>
                where TOwnerId : IEntityId, IEquatable<TOwnerId>
            {
                modelBuilder.Entity<TDiscount>().ToTable(typeof(TOwner).Name + "Discounts");
                return modelBuilder;
            }
        }
        """;

    /// <summary>The application's discounts, on its invoices and on its subscriptions.</summary>
    internal const string ShopDiscounts =
        """
        using System;
        using Acme.Subscriptions;
        using DDDToolkit.Abstractions.Attributes;
        using Shop.Contracts;

        namespace Shop.Contracts
        {
            [EntityId<Guid>]
            public readonly partial record struct InvoiceDiscountId;

            [EntityId<Guid>]
            public readonly partial record struct SubscriptionDiscountId;
        }

        namespace Shop.Billing
        {
            [Discount<InvoiceDiscountId, ShopInvoice>]
            public sealed partial class ShopInvoiceDiscount
            {
                public ShopInvoiceDiscount(InvoiceDiscountId id, decimal percent) : base(id, percent) { }
            }

            [Discount<SubscriptionDiscountId, ShopSubscription>]
            public sealed partial class ShopSubscriptionDiscount
            {
                public ShopSubscriptionDiscount(SubscriptionDiscountId id, decimal percent) : base(id, percent) { }
            }
        }
        """;

    [Fact]
    public void A_registration_is_named_after_what_its_class_is_given_on_and_closed_over_that_and_its_id()
    {
        var result = GeneratorTestHost.Create(Package, "Subscriptions.cs")
            .WithSource(Contracts, "Contracts.cs")
            .WithSource(Application, "Billing.cs")
            .WithSource(Discounts, "Discounts.cs")
            .WithSource(ShopDiscounts, "ShopDiscounts.cs")
            .WithSource(
                """
                using Acme.Subscriptions;
                using Microsoft.EntityFrameworkCore;

                namespace Shop.Billing;

                public sealed class DiscountsContext(DbContextOptions<DiscountsContext> options) : DbContext(options)
                {
                    protected override void OnModelCreating(ModelBuilder modelBuilder)
                    {
                        modelBuilder.AddShopInvoiceDiscounts();
                        modelBuilder.AddShopSubscriptionDiscounts();
                    }
                }
                """,
                "DiscountsContext.cs")
            .WithEntityFramework()
            .RunCore();

        result.ShouldCompile();
        result.ReportedDiagnostics.Should().BeEmpty();
        result.ShouldContain(
            "DiscountModelBuilderExtensions.AddDiscounts.Registration.",
            "public static global::Microsoft.EntityFrameworkCore.ModelBuilder AddShopInvoiceDiscounts(this global::Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)");
        result.ShouldContain(
            "DiscountModelBuilderExtensions.AddDiscounts.Registration.",
            "AddDiscounts<global::Shop.Billing.ShopInvoiceDiscount, global::Shop.Contracts.InvoiceDiscountId, global::Shop.Billing.ShopInvoice, global::Shop.Contracts.InvoiceId>(modelBuilder)");
        result.ShouldContain(
            "DiscountModelBuilderExtensions.AddDiscounts.Registration.",
            "AddDiscounts<global::Shop.Billing.ShopSubscriptionDiscount, global::Shop.Contracts.SubscriptionDiscountId, global::Shop.Billing.ShopSubscription, global::Shop.Contracts.SubscriptionId>(modelBuilder)");
        result.ShouldContain(
            Hint.Of("Shop.Billing.ShopInvoiceDiscount"),
            "partial class ShopInvoiceDiscount : global::Acme.Subscriptions.DiscountEntity<global::Shop.Contracts.InvoiceDiscountId>",
            "the parent takes what it has a type parameter for, and the rest is the template's own");
    }

    private static GeneratorRunOutcome Run()
        => GeneratorTestHost.Create(Package, "Subscriptions.cs")
            .WithSource(Contracts, "Contracts.cs")
            .WithSource(Application, "Billing.cs")
            .RunCore();

    [Fact]
    public void The_registration_is_called_without_the_applications_classes()
    {
        var result = GeneratorTestHost.Create(Package, "Subscriptions.cs")
            .WithSource(Registration, "SubscriptionModelBuilderExtensions.cs")
            .WithSource(Contracts, "Contracts.cs")
            .WithSource(Application, "Billing.cs")
            .WithSource(Context, "BillingContext.cs")
            .WithEntityFramework()
            .RunCore();

        result.ShouldCompile();
        result.ShouldContain(
            "SubscriptionModelBuilderExtensions.AddSubscriptions.Registration.",
            "internal static partial class GeneratedSubscriptionModelBuilderExtensions");
        result.ShouldContain(
            "SubscriptionModelBuilderExtensions.AddSubscriptions.Registration.",
            "public static global::Microsoft.EntityFrameworkCore.ModelBuilder AddSubscriptions(this global::Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder, string schema = \"sales\")");
        result.ShouldContain(
            "SubscriptionModelBuilderExtensions.AddSubscriptions.Registration.",
            "AddSubscriptions<global::Shop.Billing.ShopSubscription, global::Shop.Contracts.SubscriptionId, global::Shop.Billing.ShopInvoice, global::Shop.Contracts.InvoiceId, global::Shop.Billing.ShopInvoiceLine, global::Shop.Contracts.InvoiceLineId>(modelBuilder, schema)");
    }

    [Fact]
    public void The_use_cases_are_named_as_the_packages_class_where_the_classes_are_and_in_every_project_above()
    {
        static GeneratorTestHost Billing(GeneratorTestHost project)
            => project
                .WithSource(Package, "Subscriptions.cs")
                .WithSource(UseCases, "SubscriptionUseCases.cs")
                .WithSource(Contracts, "Contracts.cs")
                .WithSource(Application, "Billing.cs")
                .WithSource("[assembly: DDDToolkit.Abstractions.Attributes.Module(\"Billing\")]", "Module.cs");

        var declaring = Billing(GeneratorTestHost.Create(Reminders, "Reminders.cs")).RunCore();
        declaring.ShouldCompile();
        declaring.ShouldContain(
            "SubscriptionUseCases.TemplateFacade",
            "public abstract class SubscriptionUseCases : global::Acme.Subscriptions.SubscriptionUseCases<global::Shop.Billing.ShopSubscription, global::Shop.Contracts.SubscriptionId, global::Shop.Billing.ShopInvoice, global::Shop.Contracts.InvoiceId, global::Shop.Billing.ShopInvoiceLine, global::Shop.Contracts.InvoiceLineId>");

        var above = GeneratorTestHost.Create(Reminders, "Reminders.cs")
            .WithReferencedProject("Shop.Billing", Billing)
            .RunCore();
        above.ShouldCompile();
        above.HintNames.Should().NotContain("TemplateFacade", "the project above names the class of the project that declares the classes");
    }

    [Fact]
    public void The_example_builds_and_derives_each_class_from_its_parent()
    {
        var result = Run();

        result.ShouldCompile();
        result.ShouldContain(Hint.Of("Shop.Billing.ShopSubscription"), "partial class ShopSubscription : global::Acme.Subscriptions.SubscriptionAggregate<global::Shop.Contracts.SubscriptionId>");
        result.ShouldContain(
            Hint.Of("Shop.Billing.ShopInvoice"),
            "partial class ShopInvoice : global::Acme.Subscriptions.InvoiceAggregate<global::Shop.Contracts.InvoiceId, global::Shop.Contracts.SubscriptionId, global::Shop.Billing.ShopInvoiceLine, global::Shop.Contracts.InvoiceLineId>");
    }

    [Fact]
    public void The_packages_rules_run_first_and_name_the_applications_class()
    {
        var emitted = Run().Emit();
        var subscription = (IHasInvariants)emitted.New("Shop.Billing.ShopSubscription", emitted.New("Shop.Contracts.SubscriptionId", Guid.NewGuid()), "", true);

        subscription.GetInvariantViolations().Select(violation => (violation.Code, violation.EntityType!.Name))
            .Should().Equal([("subscription.plan", "ShopSubscription"), ("shop.subscription.trial", "ShopSubscription")]);
    }

    [Fact]
    public void A_rule_of_the_application_about_the_parent_runs_too()
    {
        var emitted = Run().Emit();
        var subscription = (IHasInvariants)emitted.New("Shop.Billing.ShopSubscription", emitted.New("Shop.Contracts.SubscriptionId", Guid.NewGuid()), "gold", false);

        subscription.GetInvariantViolations().Should().ContainSingle().Which.Code.Should().Be("shop.subscription.plan-known");
    }

    [Fact]
    public void The_packages_invoice_walks_the_applications_lines()
    {
        var emitted = Run().Emit();
        var invoice = emitted.New("Shop.Billing.ShopInvoice", emitted.New("Shop.Contracts.InvoiceId", Guid.NewGuid()), emitted.New("Shop.Contracts.SubscriptionId", Guid.NewGuid()));
        emitted.Call(invoice, "Charge", emitted.New("Shop.Contracts.InvoiceLineId", Guid.NewGuid()), 0m);

        var violation = ((IHasInvariants)invoice).GetInvariantViolations().Should().ContainSingle().Which;
        violation.Code.Should().Be("invoice.line.amount");
        violation.EntityType!.Name.Should().Be("ShopInvoiceLine");
    }
}
