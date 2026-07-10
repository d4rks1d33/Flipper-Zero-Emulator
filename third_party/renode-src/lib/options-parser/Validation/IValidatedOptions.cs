using System;

namespace Antmicro.OptionsParser
{
    public interface IValidatedOptions
    {
        bool Validate(out string error);
    }
}

