using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System;
using System.Text;

namespace Antmicro.OptionsParser
{
    public class OptionsParser
    {
        public OptionsParser() : this(null)
        {
        }

        public OptionsParser(ParserConfiguration configuration)
        {
            values = new List<PositionalArgument>();
            options = new HashSet<IFlag>();
            parsedOptions = new List<IParsedArgument>();
            unexpectedArguments = new List<IUnexpectedArgument>();
            this.configuration = configuration ?? new ParserConfiguration();
        }

        public OptionsParser WithOption<T>(char shortName)
        {
            var option = new CommandLineOptionDescriptor(shortName, typeof(T));
            options.Add(option);
            return this;
        }

        public OptionsParser WithOption<T>(string longName)
        {
            var option = new CommandLineOptionDescriptor(longName, typeof(T));
            options.Add(option);
            return this;
        }

        public OptionsParser WithOption<T>(char shortName, string longName)
        {
            var option = new CommandLineOptionDescriptor(shortName, longName, typeof(T));
            options.Add(option);
            return this;
        }
        
        public void WithValue(string name)
        {
            values.Add(new PositionalArgument(name, null));
        }

        /// <summary>
        /// Parses arguments provided in command line based on configuration described in type T.
        /// </summary>
        /// <param name="args">Arguments.</param>
        /// <param name="option">Configuration.</param>
        /// <returns>True if parsing was sucessful and 'help' option was not detected. False when 'help' was encountered.</returns>
        public bool Parse<T>(T option, string[] args)
        {
            helpProvider = HelpOption.CreateInstance();
            helpProvider.CustomFooterGenerator = configuration.CustomFooterGenerator;
            helpProvider.CustomOptionEntryHelpGenerator = configuration.CustomOptionEntryHelpGenerator;
            helpProvider.CustomUsageLineGenerator = configuration.CustomUsageLineGenerator;
            
            foreach(var property in typeof(T).GetProperties())
            {
                var positionalAttribute = property.GetCustomAttribute<PositionalArgumentAttribute>();
                if(positionalAttribute != null)
                {
                    var argument = new PositionalArgument(property);
                    if(values.Count > positionalAttribute.Position)
                    {
                        values.Insert(positionalAttribute.Position, argument);
                    }
                    else
                    {
                        values.Add(argument);
                    }
                }
                else
                {
                    options.Add(new CommandLineOptionDescriptor(property));
                }
            }

            if(option is IValidatedOptions)
            {
                customValidationMethod = ((IValidatedOptions)option).Validate;
            }

            if(configuration.GenerateHelp)
            {
                options.Add(helpProvider);
            }

            InnerParse(args);

            // set values
            foreach(var o in parsedOptions.Where(x => x.Flag.UnderlyingProperty != null).GroupBy(x => x.Flag))
            {
                // multi-values
                if(o.Count() > 1)
                {
                    if(!o.Key.UnderlyingProperty.PropertyType.IsArray)
                    {
                        // if it's not an array we will throw validation exception later
                        continue;
                    }

                    var finalValue = CreateDynamicList((dynamic)(((Array)o.First().Value).GetValue(0)));
                    foreach(var localValue in o.Select(x => x.Value))
                    {
                        finalValue.AddRange((dynamic)localValue);
                    }
                    o.Key.UnderlyingProperty.SetValue(option, finalValue.ToArray());
                }
                // single-value
                else
                {
                    o.Key.UnderlyingProperty.SetValue(option, o.First().Value);
                }
            }

            // set default values
            foreach(var o in options.Where(x => x.UnderlyingProperty != null && x.DefaultValue != null).Except(parsedOptions.Where(x => x.Value != null).Select(x => x.Flag)))
            {
                o.UnderlyingProperty.SetValue(option, o.DefaultValue);
            }

            foreach(var property in typeof(T).GetProperties())
            {
                var attribute = property.GetCustomAttribute<PositionalArgumentAttribute>();
                if(attribute != null && attribute.Position < values.Count)
                {
                    property.SetValue(option, values[attribute.Position].Value);
                }
            }

            return Validate();
        }

        /// <summary>
        /// Parses arguments provided in command line.
        /// </summary>
        /// <param name="args">Arguments.</param>
        /// <returns>True if parsing was sucessful and 'help' option was not detected. False when 'help' was encountered.</returns>
        public bool Parse(string[] args)
        {
            helpProvider = HelpOption.CreateInstance();
            InnerParse(args);
            return Validate();
        }

        public string RecreateUnparsedArguments()
        {
            var escapeMarkerDetected = false;
            var bldr = new StringBuilder();
            for(int i = 0; i < parsedArgs.Length; i++)
            {
                var arg = parsedArgs[i];
                if(!escapeMarkerDetected && arg == Tokenizer.EscapeMarker)
                {
                    escapeMarkerDetected = true;
                    continue;
                }
                
                var shift = 0;
                var pOpts = ParsedOptions
                    .Cast<IParsedArgument>()
                    .Union(Values.Where(y => y.IsSet))
                    .Where(x => x.Descriptor.Index == i)
                    .OrderBy(y => y.Descriptor.LocalPosition)
                    .ToList();
                foreach (var pOpt in pOpts)
                {
                    arg = arg.Remove(pOpt.Descriptor.LocalPosition - shift, pOpt.Descriptor.Length);
                    shift += pOpt.Descriptor.Length;
                    
                    if(pOpt.IsSeparated)
                    {
                        // skip next argument as it was parsed by this option
                        i++;
                    }
                }

                if(arg != "-" && arg.Length > 0)
                {
                    arg = arg.Replace(@"""", @"\""");
                    if(arg.Contains(" "))
                    {
                        arg = string.Format("\"{0}\"", arg);
                    }
                    
                    bldr.Append(arg).Append(' ');
                }
            }
            
            // trim last space
            if(bldr.Length > 0 && bldr[bldr.Length - 1] == ' ')
            {
                bldr.Remove(bldr.Length - 1, 1);
            }
            return bldr.ToString();
        }

        public IEnumerable<IFlag> Options { get { return options; } }
        public IEnumerable<IParsedArgument> ParsedOptions { get { return parsedOptions; } }
        public IEnumerable<IUnexpectedArgument> UnexpectedArguments { get { return unexpectedArguments; } }
        public IEnumerable<PositionalArgument> Values { get { return values; } }

        private static List<T> CreateDynamicList<T>(T obj)
        {
            return new List<T>();
        }

        private void InnerParse(string[] args)
        {
            parsedArgs = args;

            var tokenizer = new Tokenizer(args);
            while(!tokenizer.Finished)
            {
                var token = tokenizer.ReadNextToken();
                if(token is PositionalArgumentToken)
                {
                    if(currentValuesCount < values.Count())
                    {
                        values[currentValuesCount].Descriptor = token.Descriptor;
                        values[currentValuesCount++].Value = ((PositionalArgumentToken)token).Value;
                    }
                    else
                    {
                        unexpectedArguments.Add(new UnexpectedArgument(((PositionalArgumentToken)token).Value));
                    }
                }
                else if(token is LongNameToken)
                {
                    var name = ((LongNameToken)token).Name;
                    var foundOption = options.SingleOrDefault(x => x.LongName == name || x.Aliases.Contains(name));
                    if(foundOption != null)
                    {
                        var parsedOption = new CommandLineOption(foundOption);

                        int additionalLength = 0;
                        var isSeparated = true;
                        if(foundOption.AcceptsArgument)
                        {
                            tokenizer.MarkPosition();
                            var argumentString = tokenizer.ReadUntilTheEndOfString();
                            if(((LongNameToken)token).HasAssignment)
                            {
                                additionalLength = argumentString.Length + 1; // argument length + '='
                                isSeparated = false;
                            }
                            if(parsedOption.ParseArgument(argumentString, isSeparated))
                            {
                                tokenizer.MoveToTheNextString();
                            }
                            else
                            {
                                tokenizer.ResetPosition();
                            }
                        }

                        parsedOption.Descriptor = token.Descriptor.WithLengthChangedBy(2 + additionalLength); // -- prefix
                        if(foundOption.OptionType == typeof(bool))
                        {
                            parsedOption.Value = true;
                        }
                        parsedOptions.Add(parsedOption);
                    }
                    else
                    {
                        unexpectedArguments.Add(new UnexpectedArgument(((LongNameToken)token).Name));
                    }
                }
                else if(token is ShortNameToken)
                {
                    var foundOption = options.SingleOrDefault(x => x.ShortName == ((ShortNameToken)token).Name);
                    if(foundOption != null)
                    {
                        var parsedOption = new CommandLineOption(foundOption);

                        int additionalLength = 0;
                        var isSeparated = false;
                        if(foundOption.AcceptsArgument)
                        {
                            tokenizer.MarkPosition();
                            var argumentString = tokenizer.ReadUntilTheEndOfString();
                            if(argumentString == string.Empty)
                            {
                                // it means that the value is separated by a whitespace
                                tokenizer.MoveToTheNextString();
                                argumentString = tokenizer.ReadUntilTheEndOfString();
                                isSeparated = true;
                            }
                            if(argumentString != null)
                            {
                                additionalLength = isSeparated ? 0 : argumentString.Length;
                                if(!parsedOption.ParseArgument(argumentString, isSeparated))
                                {
                                    tokenizer.ResetPosition();
                                }
                            }
                        }
                        parsedOption.Descriptor = token.Descriptor.WithLengthChangedBy(additionalLength);
                        if(foundOption.OptionType == typeof(bool))
                        {
                            parsedOption.Value = true;
                        }
                        parsedOptions.Add(parsedOption);
                    }
                    else
                    {
                        unexpectedArguments.Add(new UnexpectedArgument(((ShortNameToken)token).Name.ToString()));
                    }
                }
            }
        }

        private bool Validate()
        {
            var forceHelp = false;
            var isHelpSelected = parsedOptions.Any(x => x.Flag == helpProvider);
            try
            {
                var missingValue = values.FirstOrDefault(x => x.IsRequired && !x.IsSet);
                if(missingValue != null)
                {
                    throw new ValidationException(string.Format("Required value '{0}' is missing.", missingValue.Name));
                }
                
                var requiredOptions = options.Where(x => x.IsRequired);
                foreach(var requiredOption in requiredOptions)
                {
                    if(!parsedOptions.Any(x => x.Flag == requiredOption))
                    {
                        throw new ValidationException(string.Format("Required option '{0}' is missing.", requiredOption.LongName ?? requiredOption.ShortName.ToString()));
                    }
                }

                foreach(var parsed in parsedOptions)
                {
                    if(!parsed.HasArgument && parsed.Flag.AcceptsArgument)
                    {
                        throw new ValidationException(string.Format("Option '{0}' requires parameter of type '{1}'", parsed.Flag.LongName ?? parsed.Flag.ShortName.ToString(), parsed.Flag.OptionType.Name));
                    }
                }

                foreach(var parsed in parsedOptions.GroupBy(x => x.Flag))
                {
                    if(parsed.Count() > 1 && !parsed.Key.AllowMultipleOccurences)
                    {
                        throw new ValidationException(string.Format("Option '{0}' occurs more than once", parsed.Key.LongName ?? parsed.Key.ShortName.ToString()));
                    }
                }

                if(customValidationMethod != null)
                {
                    string errorMessage;
                    if(!customValidationMethod(out errorMessage))
                    {
                        throw new ValidationException(errorMessage);
                    }
                }
                
                if(!configuration.AllowUnexpectedArguments && unexpectedArguments.Any())
                {
                    throw new ValidationException(string.Format("Unexpected options detected: {0}", RecreateUnparsedArguments()));
                }
                
            } 
            catch(ValidationException e)
            {
                if(configuration.ThrowValidationException)
                {
                    throw;
                }

                if(!isHelpSelected)
                {
                    Console.WriteLine(e.Message);
                    Console.WriteLine();
                }
                forceHelp = true;
            }

            if(isHelpSelected || forceHelp)
            {
                // help option is special case - we should present help and set flag
                helpProvider.PrintHelp(this);
                return false;
            }

            return true;
        }

        private string[] parsedArgs;
        private readonly List<IUnexpectedArgument> unexpectedArguments;
        private readonly ParserConfiguration configuration;
        private readonly List<IParsedArgument> parsedOptions;
        private readonly List<PositionalArgument> values;
        private readonly HashSet<IFlag> options;
        private CustomValidationMethod customValidationMethod;
        private int currentValuesCount;
        private HelpOption helpProvider;
    }

    internal delegate bool CustomValidationMethod(out string errorMessage);
}

