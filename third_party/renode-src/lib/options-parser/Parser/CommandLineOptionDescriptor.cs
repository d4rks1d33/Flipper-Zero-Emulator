using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

namespace Antmicro.OptionsParser
{
    public class CommandLineOptionDescriptor : IFlag
    {
        public CommandLineOptionDescriptor(char shortName, Type type) : this(shortName, null, type)
        {
        }

        public CommandLineOptionDescriptor(string longName, Type type) : this(Tokenizer.NullCharacter, longName, type)
        {
        }

        public CommandLineOptionDescriptor(char shortName, string longName, Type type) : this()
        {
            ShortName = shortName;
            LongName = longName;
            OptionType = type;

            AcceptsArgument = (OptionType != typeof(bool));
            AllowMultipleOccurences = false;
        }

        public CommandLineOptionDescriptor(PropertyInfo pinfo) : this()
        {
            var nameAttribute = pinfo.GetCustomAttribute<NameAttribute>();
            if(nameAttribute != null)
            {
                ShortName = nameAttribute.ShortName;
                LongName = nameAttribute.LongName;
            }
            else
            {
                ShortName = char.ToLower(pinfo.Name.ElementAt(0));
                LongName = ShortName + pinfo.Name.Substring(1);
            }

            OptionType = pinfo.PropertyType;

            UnderlyingProperty = pinfo;

            IsRequired = (pinfo.GetCustomAttribute<RequiredAttribute>() != null);
            AcceptsArgument = (pinfo.PropertyType != typeof(bool));
            AllowMultipleOccurences = pinfo.PropertyType.IsArray;

            var defaultValueAttribute = pinfo.GetCustomAttribute<DefaultValueAttribute>();
            if(defaultValueAttribute != null)
            {
                if(OptionType != defaultValueAttribute.DefaultValue.GetType())
                {
                    throw new ArgumentException(string.Format("Default value for option '{0}' is of unexpected type.", LongName ?? ShortName.ToString()));
                }
                DefaultValue = defaultValueAttribute.DefaultValue;
            }

            var descriptionAttribute = pinfo.GetCustomAttribute<DescriptionAttribute>();
            if(descriptionAttribute != null)
            {
                Description = descriptionAttribute.Value;
            }

            if(OptionType.IsArray)
            {
                var numberOfElementsAttribute = pinfo.GetCustomAttribute<NumberOfElementsAttribute>();
                if(numberOfElementsAttribute != null)
                {
                    MaxElements = numberOfElementsAttribute.Max;
                }
                var delimiterAttribute = pinfo.GetCustomAttribute<DelimiterAttribute>();
                if(delimiterAttribute != null)
                {
                    Delimiter = delimiterAttribute.Delimiter;
                }
            }

            var aliases = pinfo.GetCustomAttributes(typeof(AliasAttribute), false).Cast<AliasAttribute>();
            Aliases = aliases.Select(x => x.LongName);
        }

        public bool AcceptsArgument { get; private set; }

        public object DefaultValue { get; private set; }

        public char Delimiter { get; private set; }

        public string Description { get; set; }

        public int MaxElements { get; private set; }

        public bool IsRequired { get; private set; }

        public string LongName { get; private set; }

        public Type OptionType { get; private set; }

        public char ShortName { get; private set; }

        public IEnumerable<string> Aliases { get; private set; }

        public PropertyInfo UnderlyingProperty { get; private set; }

        public bool AllowMultipleOccurences { get; private set; }

        private CommandLineOptionDescriptor()
        {
            Delimiter = ';';
            Aliases = Enumerable.Empty<string>();
        }
    }
}

