namespace Sara
{
    /// <summary>
    /// Behaves like raw memory access, for simulating embedded systems
    /// </summary>
    public class MemorySimulator : IMemoryAccess
    {
        private readonly byte[] _data;

        public MemorySimulator(long byteSize)
        {
            _data = new byte[byteSize];
        }
        
        [System.Security.SuppressUnmanagedCodeSecurity]
        public unsafe T Read<T>(long location) where T: unmanaged 
        {
            fixed (byte* basePtr = &_data[location]) {
                var tgt = (T*)basePtr;
                return *tgt;
            }
        }
        
        [System.Security.SuppressUnmanagedCodeSecurity]
        public unsafe void Write<T>(long location, T value) where T : unmanaged
        {
            fixed (byte* basePtr = &_data[location]) {
                var tgt = (T*)basePtr;
                *tgt = value;
            }
        }
        
        [System.Security.SuppressUnmanagedCodeSecurity]
        public unsafe void WriteC<THead, TElement>(long location, THead head, TElement value) where THead : unmanaged where TElement : unmanaged
        {
            fixed (byte* basePtr = &_data[location]) {
                var tgt = (THead*)basePtr;
                *tgt = head;
                var tgt2 = (TElement*)(basePtr + sizeof(THead));
                *tgt2 = value;
            }
        }
        
        [System.Security.SuppressUnmanagedCodeSecurity]
        public unsafe void ReadC<THead, TElement>(long location, out THead head, out TElement value) where THead : unmanaged where TElement : unmanaged
        {
            fixed (byte* basePtr = &_data[location]) {
                var tgt = (THead*)basePtr;
                head = *tgt;
                var tgt2 = (TElement*)(basePtr + sizeof(THead));
                value = *tgt2;
            }
        }
    }
    /// <summary>
    /// Behaves like raw memory access, for simulating embedded systems.
    /// This version simulates an arbitrary offset into memory
    /// </summary>
    public class OffsetMemorySimulator : IMemoryAccess
    {
        private readonly long _offset;
        private readonly byte[] _data;

        public OffsetMemorySimulator(long byteSize, long offset)
        {
            _offset = offset;
            _data = new byte[byteSize];
        }
        
        [System.Security.SuppressUnmanagedCodeSecurity]
        public unsafe T Read<T>(long location) where T: unmanaged 
        {
            fixed (byte* basePtr = &_data[location-_offset]) {
                var tgt = (T*)basePtr;
                return *tgt;
            }
        }
        
        [System.Security.SuppressUnmanagedCodeSecurity]
        public unsafe void Write<T>(long location, T value) where T : unmanaged
        {
            fixed (byte* basePtr = &_data[location-_offset]) {
                var tgt = (T*)basePtr;
                *tgt = value;
            }
        }

        public unsafe void WriteC<THead, TElement>(long location, THead head, TElement value) where THead : unmanaged where TElement : unmanaged
        {
            fixed (byte* basePtr = &_data[location - _offset])
            {
                var tgt = (THead*)basePtr;
                *tgt = head;
                var tgt2 = (TElement*)(basePtr + sizeof(THead));
                *tgt2 = value;
            }
        }

        public unsafe void ReadC<THead, TElement>(long location, out THead head, out TElement value) where THead : unmanaged where TElement : unmanaged
        {
            fixed (byte* basePtr = &_data[location - _offset])
            {
                var tgt = (THead*)basePtr;
                head = *tgt;
                var tgt2 = (TElement*)(basePtr + sizeof(THead));
                value = *tgt2;
            }
        }
    }
}