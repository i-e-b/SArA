namespace Sara
{
    /// <summary>
    /// Behaves like raw memory access, for simulating embedded systems
    /// </summary>
    public class MemorySimulator
    {
        private readonly byte[] _data;

        public MemorySimulator(int byteSize)
        {
            _data = new byte[byteSize];
        }

        public static unsafe byte[] ToByteArray<T>(T argument) where T : unmanaged
        {
            var size = sizeof(T);
            var result = new byte[size];
            byte* p = (byte*)&argument;
            for (var i = 0; i < size; i++)
                result[i] = *p++;
            return result;
        }

        public unsafe T Read<T>(int location) where T: unmanaged 
        {
            T result;
            fixed (byte* basePtr = &_data[location]) {
                var tgt = (T*)basePtr;
                result = *tgt;
            }
            return result;
        }

        public unsafe void Write<T>(int location, T value) where T : unmanaged
        {
            fixed (byte* basePtr = &_data[location]) {
                var tgt = (T*)basePtr;
                *tgt = value;
            }
        }
    }
}