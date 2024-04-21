using Microsoft.CodeAnalysis;

namespace DDDToolkit.Analyzers;
internal static class Diagnostics
{
    public static readonly DiagnosticDescriptor ValueObjectShouldBeRecord = new(
               id: "DDD00001",
               title: "DDDToolkit",
               messageFormat: "DDDToolkit: {0} is a ValueObject and should be a record",
               category: "ValueObjects",
               DiagnosticSeverity.Error,
               isEnabledByDefault: true);


    public static readonly DiagnosticDescriptor EntityShouldBeClass = new(
               id: "DDD00002",
               title: "DDDToolkit",
               messageFormat: "DDDToolkit: {0} is an Entity and should be a class",
               category: "Entities",
               DiagnosticSeverity.Error,
               isEnabledByDefault: true);



    //public static readonly DiagnosticDescriptor AlwaysValidShouldEnsureValidated = new(
    //           id: "DDD00003",
    //           title: "DDDToolkit",
    //           messageFormat: "DDDToolkit: {0} has a public constructor that should call EnsureValidated",
    //           category: "ValueObjects",
    //           DiagnosticSeverity.Error,
    //           isEnabledByDefault: true,
    //           description: "ValueObjects that are always valid should call EnsureValidated in their public constructors, not doing this could result in invalid valueObjects.");

    public static readonly DiagnosticDescriptor UseProtectedSetters = new(
               id: "DDD00010",
               title: "DDDToolkit",
               messageFormat: "DDDToolkit: {0} should have a protected setter. (Use protected init).",
               category: "ValueObjects",
               DiagnosticSeverity.Error,
               isEnabledByDefault: true,
               description: "A record with non protected setters can be cloned using the with keyword. This would allow for an object to be created in an invalid state.");
    public static readonly DiagnosticDescriptor UseInitSetters = new(
              id: "DDD00011",
              title: "DDDToolkit",
              messageFormat: "DDDToolkit: {0} should have an init setter. (Use protected init).",
              category: "ValueObjects",
              DiagnosticSeverity.Error,
              isEnabledByDefault: true,
              description: "With a non-init setter every class that inherits from this record could manipulate the object at other times than creation. This should be avoided for now.");

    public static readonly DiagnosticDescriptor ValueObjectsCantBeSealed = new(
           id: "DDD00013",
           title: "DDDToolkit",
           messageFormat: "DDDToolkit: {0} is sealed. This way the always-valid variant of the ValueObject can not be created.",
           category: "ValueObjects",
           DiagnosticSeverity.Error,
           isEnabledByDefault: true,
           description: "A record with non private setters can be cloned using the with keyword. This would allow for an object to be created in an invalid state.");

}
