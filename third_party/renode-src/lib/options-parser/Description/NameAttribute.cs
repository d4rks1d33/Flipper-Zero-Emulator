using System;

namespace Antmicro.OptionsParser
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class NameAttribute : Attribute
    {
        public NameAttribute(char shortName) : this (shortName, null)
        {
        }

        public NameAttribute(string longName) : this(Tokenizer.NullCharacter, longName)
        {
        }

        public NameAttribute(char shortName, string longName)
        {
            ShortName = shortName;
            LongName = longName;
        }

        public char ShortName { get; private set; }
        public string LongName { get; private set; }
    }
}

