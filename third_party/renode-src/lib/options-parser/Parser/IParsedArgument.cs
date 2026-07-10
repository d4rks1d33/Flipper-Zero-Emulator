namespace Antmicro.OptionsParser
{
    public interface IParsedArgument
    {
        IFlag Flag { get; }
        bool HasArgument { get; }
        bool IsSeparated { get; }
        ElementDescriptor Descriptor { get; set; }
        object Value { get; }
    }
}

