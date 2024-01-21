//using DDDToolkit.Abstractions.Attributes;


//namespace DDDToolkit.NugetApi.Common.ValueObjects;


////[ComplexType]
//[ValueObject]
//public partial record PersonName
//{
//    public PersonName(string firstName, string middleNames, string lastName)
//    {
//        FirstName = firstName;
//        MiddleNames = middleNames;
//        LastName = lastName;
//    }

//    public PersonName(string firstName, string lastName)
//    {
//        FirstName = firstName;
//        LastName = lastName;
//    }

//    public string FirstName { get; private set; }
//    [DontCompare]
//    public string? MiddleNames { get; private set; }
//    public string LastName { get; private set; }

//    [DontCompare]
//    public string FullName => string.Join(" ", FirstName, MiddleNames, LastName).Trim();
//    [DontCompare]
//    public string Initials => string.Join("", FirstName[0], LastName[0]).Trim();

//    public override string ToString() => FullName;


//}
