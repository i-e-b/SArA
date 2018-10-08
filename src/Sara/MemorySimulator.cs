﻿using System;

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
            // debug test:
            if (location < 0) throw new Exception("Invalid pointer: " + location);
            var sz = sizeof(T);
            if (location + sz >= _data.LongLength) throw new Exception("Requested location ["+location+" to "+(location+sz)+"] of a maximum ["+_data.Length+"]");


            // actual code
            T result;
            fixed (byte* basePtr = &_data[location]) {
                var tgt = (T*)basePtr;
                result = *tgt;
            }
            return result;
        }

        public unsafe void Write<T>(long location, T value) where T : unmanaged
        {
            // debug test:
            if (location < 0) throw new Exception("Invalid pointer: " + location);
            var sz = (long)sizeof(T);
            if (location + sz >= _data.LongLength) throw new Exception("Requested location ["+location+" to "+(location+sz)+"] of a maximum ["+_data.Length+"]");


            // actual code
            fixed (byte* basePtr = &_data[location]) {
                var tgt = (T*)basePtr;
                *tgt = value;
            }
        }
    }
}