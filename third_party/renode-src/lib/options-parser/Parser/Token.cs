namespace Antmicro.OptionsParser
{
    public abstract class Token
    {
        protected Token(ElementDescriptor descriptor)
        {
            Descriptor = descriptor;
        }

        public ElementDescriptor Descriptor { get; protected set; }
    }

    public class LongNameToken : Token
    {
        public LongNameToken(string name, bool hasAssignment, ElementDescriptor desc) : base(desc.WithLengthChangedBy(name.Length))
        {
            Name = name;
            HasAssignment = hasAssignment;
        }

        public string Name { get; private set; }
        public bool HasAssignment { get; private set; }
    }

    public class ShortNameToken : Token
    {
        public ShortNameToken(char name, ElementDescriptor desc) : base(desc.WithLengthChangedBy(1))
        {
            Name = name;
        }

        public char Name { get; private set; }
    }

    public class PositionalArgumentToken : Token
    {
        public PositionalArgumentToken(string value, ElementDescriptor desc) : base(desc.WithLengthChangedBy(value.Length))
        {
            Value = value;
        }

        public string Value { get; private set; }
    }
}

