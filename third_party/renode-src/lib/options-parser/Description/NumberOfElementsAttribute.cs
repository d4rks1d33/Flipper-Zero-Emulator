using System;

namespace Antmicro.OptionsParser
{
    public class NumberOfElementsAttribute : Attribute
    {
        public NumberOfElementsAttribute(int max)
        {
            Max = max;
        }
        
        public int Max { get; set; }
    }
}

