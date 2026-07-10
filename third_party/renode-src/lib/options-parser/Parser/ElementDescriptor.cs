namespace Antmicro.OptionsParser
{
    public struct ElementDescriptor
    {
        public ElementDescriptor WithLengthChangedBy(int i)
        {
            return new ElementDescriptor
            {
                Index = this.Index,
                LocalPosition = this.LocalPosition,
                Length = this.Length + i
            };
        }
        
        public int Index { get; set; }
        public int LocalPosition { get; set; }
        public int Length { get; set; }
    }
}

