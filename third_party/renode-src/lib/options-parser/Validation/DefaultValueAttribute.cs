using System;

namespace Antmicro.OptionsParser
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class DefaultValueAttribute : Attribute
    {
        public DefaultValueAttribute(object value)
        {
            DefaultValue = value;
        }

        public object DefaultValue { get; private set; }
    }
}

