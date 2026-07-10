using System;
using System.Collections.Generic;
using System.Reflection;

namespace Antmicro.OptionsParser
{
    public interface IFlag
    {
        bool AcceptsArgument { get; }
        bool AllowMultipleOccurences { get; }
        char ShortName { get; }
        string LongName { get; }
        IEnumerable<string> Aliases { get; }
        string Description { get; }
        bool IsRequired { get; }
        char Delimiter { get; }
        Type OptionType { get; }
        object DefaultValue { get; }
        int MaxElements { get; }
        PropertyInfo UnderlyingProperty { get; }
    }
}

