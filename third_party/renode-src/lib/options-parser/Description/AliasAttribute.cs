using System;

namespace Antmicro.OptionsParser
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
    public class AliasAttribute : Attribute
    {
        public AliasAttribute(string longName)
        {
            LongName = longName;
        }

        // This is needed to enable the reflection api to return all instances of the attribute
        public override object TypeId
        {
            get
            {
                return this;
            }
        }

        public string LongName { get; private set; }
    }
}

