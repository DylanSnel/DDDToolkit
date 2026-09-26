using DDDToolkit.Abstractions.Access;
using DDDToolkit.Abstractions.Attributes;

namespace DDDToolkit.Examples.Ordering.Domain.Orders;

// Who may do what with an order, as C# the generator translates into SQL when this module compiles. The
// Supabase host's build writes them into supabase/migrations as policies, one access file for Ordering,
// and Postgres then enforces them for every query the module runs as a caller, and for supabase-js too.
// Each rule is also an ordinary method, so a handler or a test asks the same question in C#.
//
// The order's lines follow the order: their table gets a policy that asks the orders table, so an order is
// visible whole or not at all. Nothing here mentions them.
//
// On a host without row level security (SQLite, SQL Server, Postgres without Supabase:Url) nothing
// enforces these; there, every caller is the system and every order a guest's.

/// <summary>
/// A customer sees and changes their own orders. A guest's order has no customer, and anybody who has
/// its id may follow it, as before customers could sign in.
/// </summary>
[RowAccess<Order>(RowOperations.Read | RowOperations.Change)]
public static partial class ACustomerHasTheirOrders
{
    public static bool Allows(Order order, Caller caller)
        => order.PlacedBy == null || order.PlacedBy?.Value == caller.UserId;
}

/// <summary>
/// An order is placed in the caller's own name, or as a guest's by somebody who has not signed in. Nobody
/// places one for somebody else, even by writing the row with supabase-js.
/// </summary>
[RowAccess<Order>(RowOperations.Create)]
public static partial class NobodyOrdersForSomebodyElse
{
    public static bool Allows(Order order, Caller caller) => order.PlacedBy?.Value == caller.UserId;
}
