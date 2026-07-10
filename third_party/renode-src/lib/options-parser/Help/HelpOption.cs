using System;
using System.Text;
using System.Reflection;
using System.Linq;

namespace Antmicro.OptionsParser
{
    public class HelpOption : CommandLineOptionDescriptor
    {
        public static HelpOption CreateInstance()
        {
            return new HelpOption(new ApplicationInfo());
        }

        public void PrintHelp(OptionsParser parser)
        {
            if(!string.IsNullOrWhiteSpace(appInfo.ApplicationName))
            {
                Console.Write(appInfo.ApplicationName);
                Console.WriteLine(string.IsNullOrWhiteSpace(appInfo.ApplicationVersion) ? string.Empty : " " + appInfo.ApplicationVersion);

                if(!string.IsNullOrWhiteSpace(appInfo.ApplicationCopyrights))
                {
                    Console.WriteLine(appInfo.ApplicationCopyrights);
                }
                Console.WriteLine();
            }

            var valuesBuilder = new StringBuilder();
            foreach(var value in parser.Values)
            {
                if(value.IsRequired)
                {
                    valuesBuilder.Append(' ').Append(value.Name);
                }
                else
                {
                    valuesBuilder.AppendFormat(" [{0}]", value.Name);
                }
            }

            var usageLine = string.Format(UsageLineFormat, appInfo.ApplicationBinaryName, valuesBuilder);
            Console.WriteLine(CustomUsageLineGenerator != null
                ? CustomUsageLineGenerator(usageLine)
                : usageLine);

            Console.WriteLine();

            var interestingPositionalArguments = parser.Values.Where(x => !string.IsNullOrEmpty(x.Description) || x.ParameterType.IsEnum).ToList();
            if(interestingPositionalArguments.Count > 0)
            {
                Console.WriteLine("Arguments:");
                Console.WriteLine();

                foreach(var value in interestingPositionalArguments)
                {
                    Console.WriteLine(GeneratePositionalArgumentHelpEntry(value));
                }
            }

            Console.WriteLine("Options:");
            Console.WriteLine();

            foreach(var option in parser.Options)
            {
                Console.WriteLine(GenerateOptionHelpEntry(option));
                Console.WriteLine();
            }

            if(CustomFooterGenerator != null)
            {
                Console.WriteLine(CustomFooterGenerator());
            }
        }

        public Func<IFlag, string> CustomOptionEntryHelpGenerator { get; set; }

        public Func<string, string> CustomUsageLineGenerator { get; set; }

        public Func<string> CustomFooterGenerator { get; set; }

        public const string UsageLineFormat = "usage: {0} [options]{1}";

        private static string GeneratePositionalArgumentHelpEntry(PositionalArgument argument)
        {
            var optionBuilder = new StringBuilder();
            optionBuilder.AppendFormat(" '{0}' has possible values:\n\n", argument.Name);

            if(argument.ParameterType.IsEnum)
            {
                foreach(var e in GetEnumNames(argument.ParameterType))
                {
                    optionBuilder.AppendFormat("    {0,-26}", e.Item1);
                    if(e.Item2 != null)
                    {
                        optionBuilder.Append(e.Item2);
                    }

                    optionBuilder.Append("\n\n");
                }
            }

            return optionBuilder.ToString();
        }

        private static string GenerateOptionHelpEntry(IFlag option)
        {
            var optionBuilder = new StringBuilder("  ");

            AppendSwitchName(optionBuilder, option);
            optionBuilder.Append(' ', Math.Max(0, DefaultIndent - optionBuilder.Length));
            AppendTypeInformation(optionBuilder, option);

            if(option.IsRequired)
            {
                optionBuilder.Append(" (required)");
            }

            if(!string.IsNullOrWhiteSpace(option.Description))
            {
                optionBuilder.AppendLine();

                optionBuilder.Append(' ', DefaultIndent);
                optionBuilder.Append(option.Description);
            }

            foreach(var alias in option.Aliases)
            {
                optionBuilder.AppendLine().AppendLine();
                optionBuilder.AppendFormat("  --{0}", alias);
                optionBuilder.Append(' ', Math.Max(0, DefaultIndent - 4 - alias.Length)); // -4 to account for dashes and spaces
                AppendTypeInformation(optionBuilder, option);
                optionBuilder.AppendLine();

                optionBuilder.Append(' ', DefaultIndent);
                optionBuilder.Append("Alias to ");
                AppendSwitchName(optionBuilder, option);
                optionBuilder.Append(".");
            }

            return optionBuilder.ToString();
        }

        private static void AppendSwitchName(StringBuilder optionBuilder, IFlag option)
        {
            if(option.ShortName != Tokenizer.NullCharacter)
            {
                optionBuilder.AppendFormat("-{0}", option.ShortName);
            }
            if(option.LongName != null)
            {
                if(option.ShortName != Tokenizer.NullCharacter)
                {
                    optionBuilder.Append(", ");
                }

                optionBuilder.AppendFormat("--{0}", option.LongName);
            }
        }

        private static void AppendTypeInformation(StringBuilder optionBuilder, IFlag option)
        {
            if(option.OptionType.IsArray)
            {
                optionBuilder.AppendFormat("{0}s separated by '{1}'", option.OptionType.GetElementType().Name.ToUpper(), option.Delimiter);
            }
            else if(option.OptionType.IsEnum)
            {
                optionBuilder.AppendLine("ENUM with possible values: ");
                foreach(var name in GetEnumNames(option.OptionType))
                {
                    optionBuilder.Append(' ', 32).Append(name.Item1).Append('\n');
                }
            }
            else
            {
                optionBuilder.Append(option.OptionType.Name.ToUpper());
            }
        }

        private static Tuple<string, string>[] GetEnumNames(Type enumType)
        {
            DescriptionAttribute attr;

            return Enum.GetValues(enumType)
                .Cast<Enum>()
                .Select(x => Enum.GetName(enumType, x))
                .Where(x => enumType.GetMember(x)[0].GetCustomAttribute<HideAttribute>() == null)
                .Select(x => Tuple.Create(x, (attr = enumType.GetMember(x)[0].GetCustomAttribute<DescriptionAttribute>()) != null ? attr.Value : null))
                .ToArray();
        }

        private HelpOption(ApplicationInfo info) : base('h', "help", typeof(bool))
        {
            Description = "Display this help page.";
            appInfo = info;
        }

        private readonly ApplicationInfo appInfo;
        private const int DefaultIndent = 30;
    }
}

