using System;

namespace Antmicro.OptionsParser
{
    public class ValidationException : Exception
    {
        public ValidationException()
        {
        }

        public ValidationException(string errorMessage) : base(errorMessage)
        {
        }
    }
}

