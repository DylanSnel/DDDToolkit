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

    private static GeneratorRunOutcome Run()
        => GeneratorTestHost.Create(Package, "Subscriptions.cs")
            .WithSource(Contracts, "Contracts.cs")
            .WithSource(Application, "Billing.cs")
            .RunCore();

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
