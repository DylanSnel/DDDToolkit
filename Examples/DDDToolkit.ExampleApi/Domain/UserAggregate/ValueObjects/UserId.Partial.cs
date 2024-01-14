//using DDDToolkit.BaseTypes;
//using DDDToolkit.Interfaces;
//using FluentResults;

//namespace DDDToolkit.ExampleApi.Domain.UserAggregate.ValueObjects;

//public partial class UserId : EntityId<Guid>, IEntityId
//{
//    public UserId(Guid value) : base(value, "USR")
//    {
//    }
//    public static bool operator ==(UserId left, UserId right)
//    {
//        return Equals(left, right);
//    }

//    public static bool operator !=(UserId left, UserId right)
//    {
//        return !Equals(left, right);
//    }

//    public override int GetHashCode()
//        => base.GetHashCode();

//    public override bool Equals(object? obj)
//        => base.Equals(obj);

//    public static Result<UserId> Create(Guid value)
//    {
//        var obj = new UserId(value);
//        obj.Value = obj.Transform(value);
//        var result = obj.Validate(obj.Value);
//        if (result.IsFailed)
//        {
//            return result.ToResult<UserId>();
//        }
//        return obj;
//    }

//    public static UserId CreateUnique() => new(Guid.NewGuid());

//    public static Result<UserId?> Create(Guid? value)
//    {
//        if (!value.HasValue)
//        {
//            return Result.Ok<UserId?>(null);
//        }
//#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type.
//        return Create(value.Value);
//#pragma warning restore CS8619 // Nullability of reference types in value doesn't match target type.
//    }
//}
