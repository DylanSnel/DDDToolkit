using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace DDDToolkit.EntityFramework.Analyzers.Models;
public class GenericObjectDefinition : BaseObjectDefinition
{
    public string Type { get; set; } = "";
    public Dictionary<string, TypedConstant>? Arguments { get; set; }
}
