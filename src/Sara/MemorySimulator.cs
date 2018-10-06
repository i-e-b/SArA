namespace Sara
{
    /// <summary>
    /// Behaves like raw memory access, for simulating embedded systems
    /// </summary>
    public class MemorySimulator
    {
        private readonly byte[] _data;

        public MemorySimulator(long byteSize)
        {
            _data = new byte[byteSize];
        }

        public unsafe T Read<T>(long location) where T: unmanaged 
        {
            T result;
            fixed (byte* basePtr = &_data[location]) {
                var tgt = (T*)basePtr;
                result = *tgt;
            }
            return result;
        }

        public unsafe void Write<T>(long location, T value) where T : unmanaged
        {
            fixed (byte* basePtr = &_data[location]) {
                var tgt = (T*)basePtr;
                *tgt = value;
            }
        }
    }
}