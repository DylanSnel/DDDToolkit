using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace DDDToolkit.Examples.AppHost.Tests;

/// <summary>
/// What a customer of the shop sees, whichever way the shop is run. Every sample runs these same
/// scenarios, through a subclass that names its AppHost; the point of the samples is that the answers
/// do not change with the database, the topology or the transport.
/// </summary>
/// <remarks>
/// The answers arrive a second or two after the request: each module hears about the order from an
/// outbox poller. So a scenario places an order and then polls until the order has settled, the way a
/// client of an eventually consistent system has to.
/// </remarks>
public abstract class ShopScenarios<TAppHost>(ShopFixture<TAppHost> shop) where TAppHost : class
{
    private static readonly TimeSpan Settles = TimeSpan.FromSeconds(60);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_order_that_can_be_filled_and_paid_for_is_confirmed_and_shipped()
    {
        shop.SkipUnlessStarted();

        var id = await PlaceAsync(("COFFEE-1KG", 1));

        var order = await SettledAsync(id);

        order.GetProperty("status").GetString().Should().Be("Confirmed");
        order.GetProperty("stockReserved").GetBoolean().Should().BeTrue();
        order.GetProperty("paid").GetBoolean().Should().BeTrue();
        await EventuallyAsync(async () => (await GetAsync("/shipments")).EnumerateArray()
            .Any(shipment => shipment.GetProperty("order").GetString() == id));
    }

    [Fact]
    public async Task A_payment_the_provider_refuses_cancels_the_order_and_puts_the_stock_back()
    {
        shop.SkipUnlessStarted();

        var id = await PlaceAsync(("GRINDER", 1));

        var order = await SettledAsync(id);

        order.GetProperty("status").GetString().Should().Be("Cancelled");
        order.GetProperty("cancellationReason").GetString().Should().Contain("limit");

        // The compensation: Inventory reserved the grinder, heard the order was cancelled, and let it go.
        await EventuallyAsync(async () => (await ReservationAsync(id)) == "Released");
    }

    [Fact]
    public async Task An_order_there_is_not_enough_stock_for_is_cancelled_and_its_payment_voided()
    {
        shop.SkipUnlessStarted();

        var id = await PlaceAsync(("MUG", 50));

        var order = await SettledAsync(id);

        order.GetProperty("status").GetString().Should().Be("Cancelled");
        order.GetProperty("cancellationReason").GetString().Should().Contain("Not enough stock");
        await EventuallyAsync(async () => (await PaymentAsync(id)) == "Voided");
    }

    [Fact]
    public async Task A_confirmed_order_cannot_be_cancelled_and_the_rule_says_so_by_its_code()
    {
        shop.SkipUnlessStarted();

        var id = await PlaceAsync(("COFFEE-1KG", 1));
        (await SettledAsync(id)).GetProperty("status").GetString().Should().Be("Confirmed");

        using var response = await shop.Client().PostAsJsonAsync($"/orders/{id}/cancel", new { reason = "Changed my mind." }, Cancellation);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync(Cancellation)).Should().Contain("ORDER_ALREADY_CONFIRMED");
    }

    [Fact]
    public async Task A_line_with_no_SKU_is_refused_by_the_rule_of_the_line()
    {
        shop.SkipUnlessStarted();

        using var response = await PostOrderAsync(("   ", 1));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync(Cancellation)).Should().Contain("LINE_HAS_NO_SKU");
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Places an order and returns its id. Right after the shop starts, Ordering may not have heard
    /// Catalog's prices yet and answers 400 for an unknown SKU; that is the copy catching up, so this
    /// asks again until the copy has the price.
    /// </summary>
    private async Task<string> PlaceAsync(params (string Sku, int Quantity)[] lines)
    {
        var deadline = DateTime.UtcNow + Settles;

        while (true)
        {
            using var response = await PostOrderAsync(lines);

            if (response.StatusCode == HttpStatusCode.Created)
            {
                var order = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
                return order.GetProperty("id").GetString()!;
            }

            var body = await response.Content.ReadAsStringAsync(Cancellation);
            if (response.StatusCode != HttpStatusCode.BadRequest || !body.Contains("not for sale") || DateTime.UtcNow > deadline)
            {
                throw new InvalidOperationException($"Placing the order answered {(int)response.StatusCode}: {body}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), Cancellation);
        }
    }

    private Task<HttpResponseMessage> PostOrderAsync(params (string Sku, int Quantity)[] lines)
        => shop.Client().PostAsJsonAsync(
            "/orders",
            new
            {
                street = "Oudegracht 1",
                city = "Utrecht",
                postalCode = "3511 AA",
                lines = lines.Select(line => new { sku = line.Sku, quantity = line.Quantity }),
            },
            Cancellation);

    /// <summary>The order once it is no longer Placed: confirmed or cancelled.</summary>
    private async Task<JsonElement> SettledAsync(string id)
    {
        JsonElement order = default;
        await EventuallyAsync(async () =>
        {
            order = await GetAsync($"/orders/{id}");
            return order.GetProperty("status").GetString() != "Placed";
        });

        return order;
    }

    private async Task<string?> ReservationAsync(string orderId)
        => (await GetAsync("/reservations")).EnumerateArray()
            .Where(reservation => reservation.GetProperty("order").GetString() == orderId)
            .Select(reservation => reservation.GetProperty("status").GetString())
            .FirstOrDefault();

    private async Task<string?> PaymentAsync(string orderId)
        => (await GetAsync("/payments")).EnumerateArray()
            .Where(payment => payment.GetProperty("order").GetString() == orderId)
            .Select(payment => payment.GetProperty("status").GetString())
            .FirstOrDefault();

    private async Task<JsonElement> GetAsync(string path)
        => await shop.Client().GetFromJsonAsync<JsonElement>(path, Cancellation);

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + Settles;

        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Nothing settled within {Settles.TotalSeconds} seconds.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), Cancellation);
        }
    }
}
