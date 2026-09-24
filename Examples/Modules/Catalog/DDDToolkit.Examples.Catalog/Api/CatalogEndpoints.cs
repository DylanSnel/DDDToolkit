using DDDToolkit.Exceptions;
using DDDToolkit.Examples.SharedKernel;
using DDDToolkit.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace DDDToolkit.Examples.Catalog.Api;

/// <summary>Catalog's HTTP surface: list products, add one, reprice one.</summary>
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/products", async (CatalogContext catalog, CancellationToken cancellationToken) =>
            (await catalog.Products.OrderBy(product => product.Sku).ToListAsync(cancellationToken)).Select(Describe));

        app.MapPost("/products", async (ListProduct body, CatalogContext catalog, CancellationToken cancellationToken) =>
        {
            var errors = new List<ValidationError>();

            if (string.IsNullOrWhiteSpace(body.Sku))
            {
                errors.Add(new ValidationError("A SKU is required.", "sku", "Required"));
            }

            if (string.IsNullOrWhiteSpace(body.Name))
            {
                errors.Add(new ValidationError("A name is required.", "name", "Required"));
            }

            if (!new Money(body.Price, body.Currency).TryToValid(out var price, out var priceErrors))
            {
                errors.AddRange(priceErrors.Prefixed("price"));
            }

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors.ToErrorDictionary());
            }

            if (await catalog.Products.AnyAsync(product => product.Sku == body.Sku, cancellationToken))
            {
                return Results.Conflict($"{body.Sku} is already listed.");
            }

            var product = new Product(ProductId.CreateSequential(), body.Sku, body.Name, price!);
            catalog.Products.Add(product);
            await catalog.SaveChangesAsync(cancellationToken);

            return Results.Created($"/products/{product.Sku}", Describe(product));
        });

        // Two people repricing the same product at once: the second save finds a newer version than the
        // one it read and is told so with a 409, rather than quietly replacing a price it never saw.
        app.MapPut("/products/{sku}/price", async (string sku, Reprice body, CatalogContext catalog, CancellationToken cancellationToken) =>
        {
            if (!new Money(body.Price, body.Currency).TryToValid(out var price, out var errors))
            {
                return Results.ValidationProblem(errors.Prefixed("price").ToErrorDictionary());
            }

            var product = await catalog.Products.SingleOrDefaultAsync(p => p.Sku == sku, cancellationToken);
            if (product is null)
            {
                return Results.NotFound();
            }

            product.ChangePrice(price);

            try
            {
                await catalog.SaveChangesAsync(cancellationToken);
            }
            catch (ConcurrencyConflictException conflict)
            {
                return Results.Conflict(conflict.Message);
            }

            return Results.Ok(Describe(product));
        });

        return app;
    }

    private static object Describe(Product product) => new
    {
        id = product.Id.ToString(),
        product.Sku,
        product.Name,
        price = product.Price.Amount,
        product.Price.Currency,
        product.Version,
    };

    public sealed record ListProduct(string Sku, string Name, decimal Price, string Currency = Money.Euro);

    public sealed record Reprice(decimal Price, string Currency = Money.Euro);
}
