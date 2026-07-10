using System;

namespace Antmicro.OptionsParser
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class RequiredAttribute : Attribute
    {
    }
}

