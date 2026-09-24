using DDDToolkit.Examples.SharedKernel;

namespace DDDToolkit.Examples.Ordering.Domain.Services;

/// <summary>
/// Prices the lines of an order that is about to be placed. A <b>domain service</b>: a piece of domain
/// logic that belongs to no one object.
/// </summary>
/// <remarks>
/// It is not <see cref="Order"/>'s, because an order does not exist until its lines are priced, and it
/// is not <see cref="CatalogPrice"/>'s, because one price cannot see the other lines. It needs both, so
/// it is a class of its own, named after what the business calls the job.
/// <para>
/// It is stateless and does no I/O. The caller loads the prices and hands them in, which keeps the rule
/// testable without a database and keeps the question of where prices come from out of the domain.
/// A domain service that queried a <c>DbContext</c> itself would be an application service with a
/// domain name.
/// </para>
/// </remarks>
public static class OrderPricer
{
    /// <summary>
    /// Prices each requested line at the current price of its SKU. A SKU the price list does not know
    /// cannot be sold and is reported rather than priced.
    /// </summary>
    /// <remarks>
    /// A blank SKU is not looked up and not reported: it is priced at nothing and handed on, because
    /// <c>OrderLine.MustNameASku</c> is the rule about blank SKUs and the order reports it with a code
    /// of its own. Two places deciding the same thing would sooner or later decide it differently.
    /// </remarks>
    public static PricedLines Price(IEnumerable<RequestedLine> requested, IReadOnlyDictionary<string, Money> prices)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(prices);

        var lines = new List<OrderLine>();
        var unknown = new List<string>();

        foreach (var line in requested)
        {
            if (string.IsNullOrWhiteSpace(line.Sku))
            {
                lines.Add(new OrderLine(OrderLineId.CreateSequential(), line.Sku, line.Quantity, Money.Zero()));
            }
            else if (prices.TryGetValue(line.Sku, out var price))
            {
                lines.Add(new OrderLine(OrderLineId.CreateSequential(), line.Sku, line.Quantity, price));
            }
            else
            {
                unknown.Add(line.Sku);
            }
        }

        return new PricedLines(lines, unknown);
    }

    /// <summary>A line as the customer asked for it: which SKU and how many.</summary>
    public sealed record RequestedLine(string Sku, int Quantity);

    /// <summary>The priced lines, and the SKUs that could not be priced.</summary>
    public sealed record PricedLines(IReadOnlyList<OrderLine> Lines, IReadOnlyList<string> UnknownSkus);
}
