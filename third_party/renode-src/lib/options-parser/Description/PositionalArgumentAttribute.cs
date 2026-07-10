using System;

namespace Antmicro.OptionsParser
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class PositionalArgumentAttribute : Attribute
    {
        public PositionalArgumentAttribute(int position)
        {
            Position = position;
        }
        
        public int Position { get; private set; }
    }
}

