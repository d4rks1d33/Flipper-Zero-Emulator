using System;

namespace Antmicro.OptionsParser
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class DescriptionAttribute : Attribute
    {
        public DescriptionAttribute(string description)
        {
            Value = description;
        }

        public string Value { get; private set; }
    }
}

