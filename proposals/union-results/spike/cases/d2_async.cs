public union CreateStock(Reservation, SkuInvalid);
public union ReserveStock(Reservation, OutOfStock);
public static class C
{
    public static async System.Threading.Tasks.Task<ReserveStock> A() { await System.Threading.Tasks.Task.Yield(); return new OutOfStock(0); }
}
