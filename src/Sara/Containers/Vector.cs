namespace Sara
{
    /// <summary>
    /// A variable length array of variable elements.
    /// Acts as an expandable array and/or a stack.
    /// Uses an allocator and memory interface. Internally, it's a skip list.
    /// </summary>
    /// <typeparam name="TElement">A simple type that can be serialised to a byte array</typeparam>
    public class Vector<TElement> : IGcContainer where TElement: unmanaged 
    {
        public const int PTR_SIZE = sizeof(long);
        public const int TARGET_ELEMS_PER_CHUNK = 64; // Bigger = faster, but more memory-wasteful on small arrays
        public const int SECOND_LEVEL_SKIPS = 32; // Maximum hop-off points. Bigger = faster, but more memory
        public const int SKIP_ELEM_SIZE = sizeof(uint) + PTR_SIZE;

        /*
         * Structure of the element chunk:
         *
         * [Ptr to next chunk, or -1]    <- 8 bytes
         * [Chunk value (if set)]        <- sizeof(TElement)
         * . . .
         * [Chunk value]                 <- ... up to ChunkBytes
         *
         */

        /*
         * Structure of skip table
         *
         * [Start Idx]      <-- 4 bytes (uint)
         * [ChunkPtr]       <-- 8 bytes (ptr)
         * . . .
         * [Start Idx]
         * [ChunkPtr]
         *
         * Recalculate that after a prealloc, or after a certain number of chunks
         * have been added. This table could be biased, but for simplicity just
         * evenly distribute for now.
         */

        public readonly int ElemsPerChunk;
        public readonly int ElementByteSize;
        public readonly int ChunkHeaderSize;
        public readonly int ChunkBytes;

        private readonly Allocator _alloc;
        private readonly IMemoryAccess _mem;
        private uint _elementCount; // how long is the logical array
        private uint _skipEntries; // how long is the logical skip table

        private readonly long _baseChunkTable;
        private readonly long _skipTable;

        /// <summary>
        /// If the initial setup worked ok, this is set to true
        /// </summary>
        public readonly bool IsValid;

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
            ChunkHeaderSize = PTR_SIZE;
            var spaceForElements = Allocator.ArenaSize - ChunkHeaderSize; // need pointer space
            ElemsPerChunk = (int)(spaceForElements / ElementByteSize);

            if (ElemsPerChunk <= 1) {
                IsValid = false;
                return;
            }

            if (ElemsPerChunk > TARGET_ELEMS_PER_CHUNK)
                ElemsPerChunk = TARGET_ELEMS_PER_CHUNK; // no need to go crazy with small items.

            ChunkBytes = (ChunkHeaderSize) + (ElemsPerChunk * ElementByteSize);


            // create the skip table
            var res = _alloc.Alloc(SKIP_ELEM_SIZE * SECOND_LEVEL_SKIPS);
            if ( ! res.Success) {
                IsValid = false;
                return;
            }
            _skipTable = res.Value;
            _skipEntries = 0;

            // Make a table, which can store a few chunks, and can have a next-chunk-table pointer
            // Each chunk can hold a few elements.
            res = NewChunk();

            if ( ! res.Success) {
                IsValid = false;
                return;
            }
            _baseChunkTable = res.Value;
            _elementCount = 0;

            // All done
            IsValid = true;
        }

        private Result<long> NewChunk()
        {
            var res = _alloc.Alloc(ChunkBytes);
            if ( ! res.Success) return Result.Fail<long>();
            if (res.Value < 0) return Result.Fail<long>();

            // TODO: if we've added a few chunks since last update, then refresh the skip table
            // Always set the last entry, as the end will tend to be hot.

            var ptr = res.Value;

            _mem.Write<long>(ptr, -1);           // set the continuation pointer to invalid
            return Result.Ok(ptr);
        }

        public uint Length()
        {
            return _elementCount;
        }

        /// <summary>
        /// Add element to the end of the list
        /// </summary>
        public Result<Unit> Push(TElement value)
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
                    var res = NewChunk();
                    if (!res.Success) return Result.Fail<Unit>();

                    nextChunkPtr = res.Value;
                    _mem.Write(chunkHeadPtr, nextChunkPtr);
                }
                chunkHeadPtr = nextChunkPtr;
            }

            // Write value
            _mem.Write(chunkHeadPtr + ChunkHeaderSize + (ElementByteSize * entryIdx), value);

            _elementCount++;
            return Result.Ok();
        }

        /// <summary>
        /// Get item at zero-based index
        /// </summary>
        public Result<TElement> Get(uint index)
        {
            // push in the value, returning previous value
            var res = PtrOfElem(index);
            if (! res.Success) return Result.Fail<TElement>();
            var ptr = res.Value;

            // pull out the value
            return Result.Ok( _mem.Read<TElement>(ptr) );
        }

        /// <summary>
        /// Remove the last item from the vector, returning its value
        /// </summary>
        public Result<TElement> Pop()
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
                if (chunkHeadPtr <= 0) return Result.Fail<TElement>(); // bad chunk table
            }
            
            // Get the value
            var result = _mem.Read<TElement>(chunkHeadPtr + ChunkHeaderSize + (ElementByteSize * entryIdx));

            // If we've removed the only entry in a chunk,
            if (chunkIdx > 0 && entryIdx == 0)
            {
                // dealloc last chunk
                _alloc.Deref(chunkHeadPtr);
                _mem.Write<long>(chunkPrev, -1); // drop pointer in previous
                // TODO: what about 2nd level skip?
            }

            _elementCount--;
            return Result.Ok(result);
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
        public Result<TElement> Set(uint index, TElement element)
        {
            // push in the value, returning previous value
            var res = PtrOfElem(index);
            if (! res.Success) return Result.Fail<TElement>();
            var ptr = res.Value;

            var old = _mem.Read<TElement>(ptr);
            _mem.Write(ptr, element);
            return Result.Ok(old);
        }

        
        /// <summary>
        /// Get a pointer for an index
        /// </summary>
        protected Result<long> PtrOfElem(uint index)
        {
            if (index >= _elementCount) return Result.Fail<long>();

            // Figure out what chunk we should be in:
            var chunkIdx = index / ElemsPerChunk;
            var entryIdx = index % ElemsPerChunk;

            // Walk through the chunk chain
            var chunkHeadPtr = _baseChunkTable;
            for (int i = 0; i < chunkIdx; i++)
            {
                chunkHeadPtr = _mem.Read<long>(chunkHeadPtr);
                if (chunkHeadPtr <= 0) return Result.Fail<long>(); // bad chunk table
            }

            // push in the value
            return Result.Ok( chunkHeadPtr + ChunkHeaderSize + (ElementByteSize * entryIdx) );
        }

        /// <summary>
        /// Ensure the vector is at least the given length.
        /// If the array is longer or equal, no changes are made.
        /// </summary>
        public Result<Unit> Prealloc(uint length)
        {
            var remain = length - _elementCount;
            if (remain < 1) return Result.Ok();

            var newChunkIdx = length / ElemsPerChunk;
            
            // Walk through the chunk chain, adding where needed
            var chunkHeadPtr = _baseChunkTable;
            for (int i = 0; i < newChunkIdx; i++)
            {
                var nextChunkPtr = _mem.Read<long>(chunkHeadPtr);
                if (nextChunkPtr <= 0) {
                    // need to alloc a new chunk
                    var res = NewChunk();
                    if (!res.Success) return Result.Fail<Unit>();

                    nextChunkPtr = res.Value;
                    _mem.Write(chunkHeadPtr, nextChunkPtr);
                }
                chunkHeadPtr = nextChunkPtr;
            }

            _elementCount = length;
            return Result.Ok();
        }

        /// <summary>
        /// Swap two elements by index.
        /// </summary>
        /// <param name="index1"></param>
        /// <param name="index2"></param>
        public Result<Unit> Swap(uint index1, uint index2)
        {
            var A = PtrOfElem(index1);
            var B = PtrOfElem(index2);

            if ( ! A.Success || ! B.Success) return Result.Fail<Unit>();

            var ptrA = A.Value;
            var ptrB = B.Value;

            var valA = _mem.Read<TElement>(ptrA);
            var valB = _mem.Read<TElement>(ptrB);

            _mem.Write(ptrA, valB);
            _mem.Write(ptrB, valA);

            return Result.Ok();
        }

        /// <summary>
        /// Return all continue pointers, maybe needs to return all tags in MECS
        /// </summary>
        public ulong[] References()
        {
            var index = _elementCount - 1;
            var maxChunkIdx = index / ElemsPerChunk;
            
            var result = new ulong[maxChunkIdx + 1];
            var chunkHeadPtr = _baseChunkTable;
            for (int i = 0; i < maxChunkIdx; i++)
            {
                result[i] = (ulong) chunkHeadPtr;
                chunkHeadPtr = _mem.Read<long>(chunkHeadPtr);
                if (chunkHeadPtr <= 0) break;
            }
            result[result.Length-1] = (ulong) _skipTable;

            return result;
        }
    }
}