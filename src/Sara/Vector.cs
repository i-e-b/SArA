﻿namespace Sara
{
    /// <summary>
    /// A variable length array of variable elements.
    /// Acts as an expandable array and/or a stack.
    /// Uses an allocator and memory interface. Internally, it's a skip list.
    /// </summary>
    /// <typeparam name="TElement">A simple type that can be serialised to a byte array</typeparam>
    public class Vector<TElement> where TElement: unmanaged 
    {
        public const int TARGET_ELEMS_PER_CHUNK = 64; // Bigger = faster, but more wasteful on small arrays

        public readonly int ElemsPerChunk;
        public readonly int ElementByteSize;
        public readonly int PtrSize;
        public readonly int ChunkBytes;

        private readonly Allocator _alloc;
        private readonly IMemoryAccess _mem;
        private readonly long _baseChunkTable;
        private int _elementCount;

        /// <summary>
        /// A variable length array of variable elements.
        /// Create a new vector base in the first free section
        /// </summary>
        public Vector(Allocator alloc, IMemoryAccess mem)
        {
            _alloc = alloc;
            _mem = mem;

            unsafe
            {
                ElementByteSize = sizeof(TElement);
            }

            // Work out how many elements can fit in an arena
            PtrSize = sizeof(long);
            var spaceForElements = Allocator.ArenaSize - PtrSize; // need pointer space
            ElemsPerChunk = (int)(spaceForElements / ElementByteSize);

            //if (ELEMS_PER_CHUNK <= 1) // TODO: error condition propagation.
            if (ElemsPerChunk > TARGET_ELEMS_PER_CHUNK)
                ElemsPerChunk = TARGET_ELEMS_PER_CHUNK; // no need to go crazy with small items.

            ChunkBytes = PtrSize + (ElemsPerChunk * ElementByteSize);

            // We first make a table, which can store a few chunks, and can have a next-chunk-table pointer
            // Each chunk can hold a few elements.
            _baseChunkTable = NewChunk();
            _elementCount = 0;
        }

        private long NewChunk()
        {
            var ptr = _alloc.Alloc(ChunkBytes);
            /**/if (ptr < 0) return ptr; //TODO: handle failure
            _mem.Write<long>(ptr, -1); // need to make sure the continuation pointer is invalid
            return ptr;
        }

        public int Length()
        {
            return _elementCount;
        }

        /// <summary>
        /// Add element to the end of the list
        /// </summary>
        public void Push(TElement value)
        {
            var newChunkIdx = _elementCount / ElemsPerChunk;
            var entryIdx = _elementCount % ElemsPerChunk;
            
            // Walk through the chunk chain, adding where needed
            var chunkHeadPtr = _baseChunkTable;
            for (int i = 0; i < newChunkIdx; i++)
            {
                var nextChunkPtr = _mem.Read<long>(chunkHeadPtr);
                if (nextChunkPtr <= 0) {
                    // need to alloc a new chunk
                    nextChunkPtr = NewChunk();
                    _mem.Write(chunkHeadPtr, nextChunkPtr);
                }
                chunkHeadPtr = nextChunkPtr;
            }

            // Write value
            _mem.Write(chunkHeadPtr + PtrSize + (ElementByteSize * entryIdx), value);

            _elementCount++;
        }

        /// <summary>
        /// Get item at zero-based index
        /// </summary>
        public TElement Get(uint index)
        {
            if (index >= _elementCount) return default; // TODO: some kind of failure flag. No using exceptions

            // Figure out what chunk we should be in:
            var chunkIdx = index / ElemsPerChunk;
            var entryIdx = index % ElemsPerChunk;

            // Walk through the chunk chain
            var chunkHeadPtr = _baseChunkTable;
            for (int i = 0; i < chunkIdx; i++)
            {
                chunkHeadPtr = _mem.Read<long>(chunkHeadPtr);
                if (chunkHeadPtr <= 0) return default; // bad chunk table
            }

            // pull out the value
            return _mem.Read<TElement>(chunkHeadPtr + PtrSize + (ElementByteSize * entryIdx));
        }

        /// <summary>
        /// Remove the last item from the vector, returning its value
        /// </summary>
        public TElement Pop()
        {
            var index = _elementCount - 1;
            // Walk to find the chunk; if it's the FIRST entry in NOT-the-first chunk, dealloc the chunk and write to the prev.

            // Figure out what chunk we should be in:
            var chunkIdx = index / ElemsPerChunk;
            var entryIdx = index % ElemsPerChunk;

            // Walk through the chunk chain
            var chunkHeadPtr = _baseChunkTable;
            var chunkPrev = _baseChunkTable;
            for (int i = 0; i < chunkIdx; i++)
            {
                chunkPrev = chunkHeadPtr;
                chunkHeadPtr = _mem.Read<long>(chunkHeadPtr);
                if (chunkHeadPtr <= 0) return default; // bad chunk table
            }
            
            // Get the value
            var result = _mem.Read<TElement>(chunkHeadPtr + PtrSize + (ElementByteSize * entryIdx));

            // If we've removed the only entry in a chunk,
            if (chunkIdx > 0 && entryIdx == 0)
            {
                // dealloc last chunk
                _alloc.Deref(chunkHeadPtr);
                _mem.Write<long>(chunkPrev, -1); // drop pointer
            }

            _elementCount--;
            return result;
        }

        /// <summary>
        /// Remove the entire vector from memory, including base pointers.
        /// After calling this, the vector will not be usable
        /// </summary>
        public void Deallocate()
        {
            // Walk through the chunk chain, removing until we hit an invalid pointer
            var current = _baseChunkTable;
            while(true)
            {
                var next = _mem.Read<long>(current);
                _alloc.Deref(current);
                _mem.Write<long>(current, -1); // just in case we have a loop
                if (next <= 0) return; // end of chunks
                current = next;
            }
            
        }

        /// <summary>
        /// Set a value at a given index.
        /// Ignored if out-of-bounds
        /// </summary>
        public void Set(uint index, TElement element)
        {
            if (index >= _elementCount) return; // TODO: some kind of failure flag. No using exceptions

            // Figure out what chunk we should be in:
            var chunkIdx = index / ElemsPerChunk;
            var entryIdx = index % ElemsPerChunk;

            // Walk through the chunk chain
            var chunkHeadPtr = _baseChunkTable;
            for (int i = 0; i < chunkIdx; i++)
            {
                chunkHeadPtr = _mem.Read<long>(chunkHeadPtr);
                if (chunkHeadPtr <= 0) return; // bad chunk table
            }

            // push in the value
            _mem.Write(chunkHeadPtr + PtrSize + (ElementByteSize * entryIdx), element);
        }

        
        /// <summary>
        /// Get a pointer for an index
        /// </summary>
        protected long PtrOfElem(uint index)
        {
            if (index >= _elementCount) return -1; // TODO: some kind of failure flag. No using exceptions

            // Figure out what chunk we should be in:
            var chunkIdx = index / ElemsPerChunk;
            var entryIdx = index % ElemsPerChunk;

            // Walk through the chunk chain
            var chunkHeadPtr = _baseChunkTable;
            for (int i = 0; i < chunkIdx; i++)
            {
                chunkHeadPtr = _mem.Read<long>(chunkHeadPtr);
                if (chunkHeadPtr <= 0) return -1; // bad chunk table
            }

            // push in the value
            return chunkHeadPtr + PtrSize + (ElementByteSize * entryIdx);
        }

        /// <summary>
        /// Ensure the vector is at least the given length.
        /// Any additional slots are filled with the given element.
        /// If the array is longer or equal, no changes are made.
        /// </summary>
        public void Prealloc(uint length, TElement element)
        {
            var remain = length - _elementCount;
            for (int i = 0; i < remain; i++)
            {
                Push(element); // could probably be optimised. This will scan multiple times.
            }
        }

        /// <summary>
        /// Swap two elements by index.
        /// </summary>
        /// <param name="index1"></param>
        /// <param name="index2"></param>
        public void Swap(uint index1, uint index2)
        {
            long ptrA = PtrOfElem(index1);
            long ptrB = PtrOfElem(index2);

            var valA = _mem.Read<TElement>(ptrA);
            var valB = _mem.Read<TElement>(ptrB);

            _mem.Write(ptrA, valB);
            _mem.Write(ptrB, valA);
        }
    }
}