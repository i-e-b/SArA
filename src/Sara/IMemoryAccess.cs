namespace Sara
{
    /// <summary>
    /// Abstraction over reading and writing a random-access byte store
    /// </summary>
    public interface IMemoryAccess
    {
        T Read<T>(long location) where T : unmanaged;
        void Write<T>(long location, T value) where T : unmanaged;
    }
}