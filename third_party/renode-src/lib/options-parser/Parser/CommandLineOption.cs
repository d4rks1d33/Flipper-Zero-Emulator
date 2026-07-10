using System;

namespace Antmicro.OptionsParser
{
    public class CommandLineOption<T> : CommandLineOption
    {
        public CommandLineOption(CommandLineOptionDescriptor descriptor) : base(descriptor)
        {
        }

        public override object Value 
        {
            get 
            {
                return base.Value;
            }
            set 
            {
                base.Value = value;

                var parsed = Parsed;
                if(parsed != null)
                {
                    parsed(this, (T)base.Value);
                }
            }
        }
        
        public event Action<CommandLineOption<T>, T> Parsed;
    }

    public class CommandLineOption : IParsedArgument
    {
        public CommandLineOption(IFlag descriptor)
        {
            Flag = descriptor;
        }

        public bool ParseArgument(string arg, bool isSeparated)
        {
            object parsedValue;
            if(Flag.OptionType.IsArray)
            {
                var values = (Flag.MaxElements > 0) ? arg.Split(new[] { Flag.Delimiter }, Flag.MaxElements) : arg.Split(Flag.Delimiter); 
                var array = Array.CreateInstance(Flag.OptionType.GetElementType(), values.Length);
                
                for(int i = 0; i < values.Length; i++)
                {
                    if(!ParseHelper.TryParse(values[i], Flag.OptionType.GetElementType(), out parsedValue))
                    {
                        return false;
                    }
                    array.SetValue(parsedValue, i);
                }
                Value = array;
            }
            else
            {
                if(!ParseHelper.TryParse(arg, Flag.OptionType, out parsedValue))
                {
                    return false;
                }
                Value = parsedValue;
            }
            
            HasArgument = true;
            IsSeparated = isSeparated;
            return true;
        }

        public IFlag Flag { get; private set; }

        public bool HasArgument { get; protected set; }

        public bool IsSeparated { get; protected set; }

        public ElementDescriptor Descriptor { get; set; }

        public virtual object Value { get; set; }
    }
}

