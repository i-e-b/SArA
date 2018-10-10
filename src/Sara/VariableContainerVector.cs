﻿namespace Sara
{
    /// <summary>
    /// A variable length array of variable elements.
    /// Acts as an expandable array and/or a stack.
    /// Uses an allocator and memory interface
    /// </summary>
    /// <typeparam name="TElement">A simple type</typeparam>
    public class VariableContainerVector<TElement> where TElement: unmanaged 
    {
        public readonly int ELEMS_PER_CHUNK;
        public readonly int ELEM_SIZE;
        public readonly int PTR_SIZE;
        public readonly int CHUNK_BYTES;

        private readonly Allocator _alloc;
        private readonly IMemoryAccess _mem;
        private readonly long _baseChunkTable;
        private int _elementCount;

        /// <summary>
        /// A variable length array of variable elements.
        /// Create a new vector base in the first free section
        /// </summary>
        public VariableContainerVector(Allocator alloc, IMemoryAccess mem)
        {
            _alloc = alloc;
            _mem = mem;

            unsafe
            {
                ELEM_SIZE = sizeof(TElement);
            }

            // Work out how many elements can fit in an arena
            PTR_SIZE = sizeof(long);
            var spaceForElements = Allocator.ArenaSize - PTR_SIZE; // need pointer space
            ELEMS_PER_CHUNK = (int)(spaceForElements / ELEM_SIZE);

            //if (ELEMS_PER_CHUNK <= 1) // TODO: error condition propagation.
            if (ELEMS_PER_CHUNK > 32) ELEMS_PER_CHUNK = 32; // no need to go crazy with small items.

            CHUNK_BYTES = PTR_SIZE + (ELEMS_PER_CHUNK * ELEM_SIZE);

            // We first make a table, which can store a few chunks, and can have a next-chunk-table pointer
            // Each chunk can hold a few elements.
            _baseChunkTable = NewChunk();
            _elementCount = 0;
        }

        private long NewChunk()
        {
            var ptr = _alloc.Alloc(CHUNK_BYTES);
            _mem.Write<long>(ptr, -1); // need to make sure the continuation pointer is invalid
            return ptr;
        }

        public long[] References()
        {
            return null;
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
            var newChunkIdx = _elementCount / ELEMS_PER_CHUNK;
            var entryIdx = _elementCount % ELEMS_PER_CHUNK;
            
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
            _mem.Write(chunkHeadPtr + PTR_SIZE + (ELEM_SIZE * entryIdx), value);

            _elementCount++;
        }

        /// <summary>
        /// Get item at zero-based index
        /// </summary>
        public TElement Get(int index)
        {
            if (index >= _elementCount) return default; // TODO: some kind of failure flag. No using exceptions

            // Figure out what chunk we should be in:
            var chunkIdx = index / ELEMS_PER_CHUNK;
            var entryIdx = index % ELEMS_PER_CHUNK;

            // Walk through the chunk chain
            var chunkHeadPtr = _baseChunkTable;
            for (int i = 0; i < chunkIdx; i++)
            {
                chunkHeadPtr = _mem.Read<long>(chunkHeadPtr);
                if (chunkHeadPtr <= 0) return default; // bad chunk table
            }

            // pull out the value
            return _mem.Read<TElement>(chunkHeadPtr + PTR_SIZE + (ELEM_SIZE * entryIdx));
        }

        /// <summary>
        /// Remove the last item from the vector, returning its value
        /// </summary>
        public TElement Pop()
        {
            var index = _elementCount - 1;
            // Walk to find the chunk; if it's the FIRST entry in NOT-the-first chunk, dealloc the chunk and write to the prev.

            // Figure out what chunk we should be in:
            var chunkIdx = index / ELEMS_PER_CHUNK;
            var entryIdx = index % ELEMS_PER_CHUNK;

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
            var result = _mem.Read<TElement>(chunkHeadPtr + PTR_SIZE + (ELEM_SIZE * entryIdx));

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
    }
}