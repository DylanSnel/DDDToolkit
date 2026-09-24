using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Examples.Payments.Api;

/// <summary>Payments' HTTP surface: one list, for looking at what happened.</summary>
public static class PaymentsEndpoints
{
    public static IEndpointRouteBuilder MapPaymentsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/payments", async (PaymentsContext payments, CancellationToken cancellationToken) =>
            (await payments.Payments.ToListAsync(cancellationToken)).Select(p => new
            {
                id = p.Id.ToString(),
                order = p.Order.ToString(),
                amount = p.Amount.Amount,
                p.Amount.Currency,
                status = p.Status.ToString(),
                p.Refusal,
            }));

        return app;
    }
}
