namespace Sara
{
    /// <summary>
    /// Abstraction over reading and writing a random-access byte store
    /// </summary>
    public interface IMemoryAccess
    {
        T Read<T>(long location) where T : unmanaged;
        void Write<T>(long location, T value) where T : unmanaged;

        // these to work around C# restrictions. Won't need for C?

        /// <summary> Write a head/value structure </summary>
        void WriteC<THead, TElement>(long location, THead head, TElement value)
            where THead : unmanaged
            where TElement:unmanaged;

        /// <summary> Write a head/value structure </summary>
        void ReadC<THead, TElement>(long location, out THead head, out TElement value)
            where THead : unmanaged
            where TElement:unmanaged;
    }
}