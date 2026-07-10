using System;

namespace Antmicro.OptionsParser
{
    public class DelimiterAttribute : Attribute
    {
        public DelimiterAttribute(char delimiter)
        {
            Delimiter = delimiter;
        }
        
        public char Delimiter { get; set; }
    }
}

