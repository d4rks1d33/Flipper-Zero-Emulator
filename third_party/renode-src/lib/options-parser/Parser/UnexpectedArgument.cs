using System;
namespace Antmicro.OptionsParser
{
    public class UnexpectedArgument : IUnexpectedArgument
    {
        public UnexpectedArgument(string value)
        {
            Value = value;
        }

        public string Value { get; private set;}
    }
}

