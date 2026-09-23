using DDDToolkit.Examples.Ordering.Contracts;
using DDDToolkit.Examples.Ordering.Contracts.GraphQl;
using DDDToolkit.Examples.Payments.GraphQl;
using GreenDonut;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDDToolkit.Examples.Payments.Api.GraphQL;

/// <summary>Payments' part of a GraphQL schema. The host builds the schema and calls this.</summary>
public static class PaymentsGraphQL
{
    public static IRequestExecutorBuilder AddPaymentsGraphQL(this IRequestExecutorBuilder graphql)
    {
        ArgumentNullException.ThrowIfNull(graphql);

        return graphql
            .AddPaymentsGraphQlRuntimeBindings()
            // For OrderId: Payments publishes its order reference as an Order node id.
            .AddOrderingContractsGraphQlRuntimeBindings()
            .AddType<PaymentType>()
            .AddTypeExtension<PaymentsQueries>()
            .AddDataLoader<PaymentByIdDataLoader>()
            .AddDataLoader<PaymentByOrderDataLoader>();
    }
}

/// <summary>A payment: a node of its own, pointing at its order by the order's node id.</summary>
public sealed class PaymentType : ObjectType<Payment>
{
    protected override void Configure(IObjectTypeDescriptor<Payment> descriptor)
    {
        descriptor.BindFieldsExplicitly();

        descriptor
            .ImplementsNode()
            .IdField(payment => payment.Id)
            .ResolveNode(async (context, id) =>
                await context.DataLoader<PaymentByIdDataLoader>().LoadAsync(id, context.RequestAborted));

        // The order, as the node id Ordering gives it. Payments knows the id and nothing else of the
        // order; a client that wants the order asks node(id:) with this, or the composed Order.payment.
        descriptor.Field(payment => payment.Order).ID("Order");
        descriptor.Field(payment => payment.Amount);
        descriptor.Field(payment => payment.Status);
        descriptor.Field(payment => payment.Refusal);
    }
}

[ExtendObjectType(OperationTypeNames.Query)]
public sealed class PaymentsQueries
{
    public async Task<IReadOnlyList<Payment>> GetPaymentsAsync(PaymentsContext payments, CancellationToken cancellationToken)
        => await payments.Payments.ToListAsync(cancellationToken);
}

public sealed class PaymentByIdDataLoader(IServiceScopeFactory scopes, IBatchScheduler batchScheduler, DataLoaderOptions options)
    : BatchDataLoader<PaymentId, Payment>(batchScheduler, options)
{
    protected override async Task<IReadOnlyDictionary<PaymentId, Payment>> LoadBatchAsync(IReadOnlyList<PaymentId> ids, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentsContext>().Payments
            .Where(payment => ids.Contains(payment.Id))
            .ToDictionaryAsync(payment => payment.Id, cancellationToken);
    }
}

/// <summary>
/// The payment for each order, batched. Payments' lookup: whatever composes the modules into one schema
/// puts <c>Order.payment</c> on top of it, and Payments stays the only one reading its table.
/// </summary>
public sealed class PaymentByOrderDataLoader(IServiceScopeFactory scopes, IBatchScheduler batchScheduler, DataLoaderOptions options)
    : BatchDataLoader<OrderId, Payment>(batchScheduler, options)
{
    protected override async Task<IReadOnlyDictionary<OrderId, Payment>> LoadBatchAsync(IReadOnlyList<OrderId> orders, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentsContext>().Payments
            .Where(payment => orders.Contains(payment.Order))
            .ToDictionaryAsync(payment => payment.Order, cancellationToken);
    }
}
