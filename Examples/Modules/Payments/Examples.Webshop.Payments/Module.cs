using DDDToolkit.Abstractions.Attributes;

// Taking the customer's money, through a payment provider this module keeps at arm's length.
[assembly: Module("Payments")]

// HotChocolate's attribute, not the toolkit's above, hence its full name: it names what HotChocolate's
// generator writes for this project, AddPaymentsTypes, which registers every GraphQL type, loader and
// operation the project declares.
[assembly: HotChocolate.Module("PaymentsTypes")]
