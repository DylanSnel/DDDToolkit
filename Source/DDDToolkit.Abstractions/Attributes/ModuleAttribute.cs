using System;

namespace DDDToolkit.Abstractions.Attributes;
#pragma warning disable CS9113 // Parameter is unread.
#pragma warning disable IDE1006 // Naming Styles
[AttributeUsage(AttributeTargets.Assembly)]
public class ModuleAttribute(string Name) : Attribute
#pragma warning restore IDE1006 // Naming Styles
#pragma warning restore CS9113 // Parameter is unread.
{
}
