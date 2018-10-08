namespace Sara
{
    /// <summary>
    /// A variable length array of ulong elements
    /// Using an allocator and memory interface
    /// </summary>
    public class VariableVector : IGcContainer
    {
        public const int ELEMS_PER_CHUNK = 32;
        public const int ELEM_SIZE = sizeof(ulong);
        public const int CHUNK_BYTES = (1 + ELEMS_PER_CHUNK) * ELEM_SIZE;

        private readonly Allocator _alloc;
        private readonly MemorySimulator _mem;
        private readonly long _baseChunkTable;
        private int _elementCount;

        /// <summary>
        /// Create a new vector base in the first free section
        /// </summary>
        public VariableVector(Allocator alloc, MemorySimulator mem)
        {
            _alloc = alloc;
            _mem = mem;

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
        public void Add(ulong value)
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
            _mem.Write<ulong>(chunkHeadPtr + ELEM_SIZE + (ELEM_SIZE * entryIdx), value);

            _elementCount++;
        }

        /// <summary>
        /// Get item at zero-based index
        /// </summary>
        public ulong Get(int index)
        {
            if (index >= _elementCount) return 0UL; // TODO: some kind of failure flag. No using exceptions

            // Figure out what chunk we should be in:
            var chunkIdx = index / ELEMS_PER_CHUNK;
            var entryIdx = index % ELEMS_PER_CHUNK;

            // Walk through the chunk chain
            var chunkHeadPtr = _baseChunkTable;
            for (int i = 0; i < chunkIdx; i++)
            {
                chunkHeadPtr = _mem.Read<long>(chunkHeadPtr);
                if (chunkHeadPtr <= 0) return 0UL; // bad chunk table
            }

            // pull out the value
            return _mem.Read<ulong>(chunkHeadPtr + ELEM_SIZE + (ELEM_SIZE * entryIdx));
        }
    }
}