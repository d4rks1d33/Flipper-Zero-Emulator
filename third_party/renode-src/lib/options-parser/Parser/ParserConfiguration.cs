using System;

namespace Antmicro.OptionsParser
{
    public class ParserConfiguration
    {
        public ParserConfiguration()
        {
            GenerateHelp = true;
            ThrowValidationException = false;
        }

        public bool AllowUnexpectedArguments { get; set; }
        public bool ThrowValidationException { get; set; }
        public bool GenerateHelp { get; set; }
        public Func<IFlag, string> CustomOptionEntryHelpGenerator { get; set; }
        public Func<string, string> CustomUsageLineGenerator { get; set; }
        public Func<string> CustomFooterGenerator { get; set; }
    }
}

