namespace Sara
{
    /// <summary>
    /// A variable length array of variable elements.
    /// Acts as an expandable array and/or a stack.
    /// Uses an allocator and memory interface. Internally, it's a kind of binary-search skip list.
    /// </summary>
    /// <typeparam name="TElement">A simple type that can be serialised to a byte array</typeparam>
    public class Vector<TElement> : IGcContainer where TElement: unmanaged 
    {
        // Fixed sizes -- these are structural to the code and must not change
        public const int PTR_SIZE = sizeof(long);
        public const int INDEX_SIZE =  sizeof(uint);
        public const int SKIP_ELEM_SIZE = INDEX_SIZE + PTR_SIZE;


        // Tuning parameters: have a play if you have performance or memory issues.
        /// <summary>
        /// Desired maximum elements per chunk. This will be reduced if TElement is large (to fit in Arena limit)
        /// Larger values are significantly faster for big arrays, but more memory-wasteful on small arrays
        /// </summary>
        public const int TARGET_ELEMS_PER_CHUNK = 64;

        /// <summary>
        /// Maximum size of the skip table.
        /// This is dynamically sizes, so large values won't use extra memory for small arrays.
        /// This limits the memory growth of larger arrays. If it's bigger that an arena, everything will fail.
        /// </summary><remarks>The maximum size is (Allocator.ArenaSize / SKIP_ELEM_SIZE), or about 5461</remarks>
        public const long SKIP_TABLE_SIZE_LIMIT = 1024;

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
         * [ChunkIdx]      <-- 4 bytes (uint)
         * [ChunkPtr]      <-- 8 bytes (ptr)
         * . . .
         * [ChunkIdx]
         * [ChunkPtr]
         *
         * Recalculate that after a prealloc, or after a certain number of chunks
         * have been added. This table could be biased, but for simplicity just
         * evenly distribute for now.
         */

        public readonly int ElemsPerChunk;
        public readonly int ElementByteSize;
        public readonly int ChunkHeaderSize;
        public readonly ushort ChunkBytes;

        private readonly Allocator _alloc;
        private readonly IMemoryAccess _mem;
        private uint _elementCount;    // how long is the logical array
        private int  _skipEntries;     // how long is the logical skip table
        private bool _skipTableDirty;  // does the skip table need updating?
        private bool _rebuilding;      // are we in the middle of rebuilding the skip table?

        #region Table pointers
        /// <summary>Start of the chunk chain</summary>
        private readonly long _baseChunkTable;

        /// <summary>End of the chunk chain</summary>
        private long _endChunkPtr;

        /// <summary>Pointer to skip table</summary>
        private long _skipTable;
        #endregion

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

            ChunkBytes = (ushort) ((ChunkHeaderSize) + (ElemsPerChunk * ElementByteSize));



            // Make a table, which can store a few chunks, and can have a next-chunk-table pointer
            // Each chunk can hold a few elements.
            _skipEntries = -1;
            _skipTable = -1;
            _endChunkPtr = -1;
            _baseChunkTable = -1;
            var res = NewChunk(); 

            if ( ! res.Success) {
                IsValid = false;
                return;
            }
            _baseChunkTable = res.Value;
            _elementCount = 0;
            RebuildSkipTable();

            // All done
            IsValid = true;
        }

        private Result<long> NewChunk()
        {
            var res = _alloc.Alloc(ChunkBytes);
            if ( ! res.Success) return Result.Fail<long>();
            if (res.Value < 0) return Result.Fail<long>();

            var ptr = res.Value;

            _mem.Write<long>(ptr, -1);                             // set the continuation pointer of the new chunk to invalid
            if (_endChunkPtr >= 0) _mem.Write(_endChunkPtr, ptr);  // update the continuation pointer of the old end chunk
            _endChunkPtr = ptr;                                    // update the end chunk pointer
            _skipTableDirty = true;

            return Result.Ok(ptr);
        }

        private void MaybeRebuildSkipTable() {
            if (_rebuilding) return;

            // If we've added a few chunks since last update, then refresh the skip table
            if (_skipTableDirty) RebuildSkipTable();
        }

        private void RebuildSkipTable()
        {
            unchecked
            {
                _rebuilding = true;
                _skipTableDirty = false;
                var chunkTotal = _elementCount / ElemsPerChunk;
                if (chunkTotal < 4) // not worth having a skip table
                {
                    if (_skipTable >= 0) _alloc.Deref(_skipTable);
                    _skipEntries = 0;
                    _skipTable = -1;
                    _rebuilding = false;
                    return;
                }

                // Guess a reasonable size for the skip table
                var entries = (chunkTotal < SKIP_TABLE_SIZE_LIMIT) ? chunkTotal : SKIP_TABLE_SIZE_LIMIT;

                // General case: not every chunk will fit in the skip table
                // Find representative chunks using the existing table.
                // (finding will be a combination of search and scan)
                var newTable = _alloc.Alloc((ushort) (SKIP_ELEM_SIZE * entries));
                if (!newTable.Success) { _rebuilding = false; return; } // live with the old one
                var newTablePtr = newTable.Value;

                var stride = _elementCount / entries;
                if (stride < 1) stride = 1; 

                long target = 0;
                var newSkipEntries = 0;
                for (int i = 0; i < entries; i++)
                {
                    FindNearestChunk((uint)target, out var found, out var chunkPtr, out var chunkIndex);

                    if (!found || chunkPtr < 0) // total fail
                    {
                        _alloc.Deref(newTablePtr);
                        _rebuilding = false;
                        return;
                    }

                    var iptr = newTablePtr + (SKIP_ELEM_SIZE * i);
                    _mem.Write<uint>(iptr, (uint)chunkIndex);
                    _mem.Write<long>(iptr + INDEX_SIZE, chunkPtr);
                    newSkipEntries++;
                    target += stride;
                }

                if (newSkipEntries < 1) // total fail
                {
                    _alloc.Deref(newTablePtr);
                    _rebuilding = false;
                    return;
                }

                _skipEntries = newSkipEntries;
                if (_skipTable >= 0) _alloc.Deref(_skipTable);
                _skipTable = newTablePtr;
                _rebuilding = false;
            }
        }

        /// <summary>
        /// Find the chunk (with start index) that contains or is before the given index
        /// </summary>
        protected void FindNearestChunk(uint targetIndex, out bool found, out long chunkPtr, out uint chunkIndex)
        {
            unchecked
            {
                // 1. Calculate desired chunk index
                uint targetChunkIdx = (uint)(targetIndex / ElemsPerChunk);
                uint endChunkIdx = (uint)((_elementCount - 1) / ElemsPerChunk);

                // 2. Optimise for start- and end- of chain (small lists & very likely for Push & Pop)
                if (targetChunkIdx == 0)
                { // start of chain
                    found = true;
                    chunkPtr = _baseChunkTable;
                    chunkIndex = targetChunkIdx;
                    return;
                }
                if (_elementCount == 0 || targetChunkIdx == endChunkIdx)
                { // lands in a chunk
                    found = true;
                    chunkPtr = _endChunkPtr;
                    chunkIndex = targetChunkIdx;
                    return;
                }
                if (targetIndex >= _elementCount)
                { // lands outside a chunk -- off the end
                    found = false;
                    chunkPtr = _endChunkPtr;
                    chunkIndex = targetChunkIdx;
                    return;
                }

                // All the simple optimal paths failed. Make sure the skip list is good...
                MaybeRebuildSkipTable();

                // 3. Use the skip table to find a chunk near the target
                //    By ensuring the skip table is fresh, we can calculate the correct location
                uint startChunkIdx = 0;
                var chunkHeadPtr = _baseChunkTable;

                if (_skipEntries > 1)
                {
                    // guess search bounds
                    var guess = (targetChunkIdx * _skipEntries) / endChunkIdx;
                    var upper = guess + 2;
                    var lower = guess - 2;
                    if (upper > _skipEntries) upper = _skipEntries;
                    if (lower < 0) lower = 0;

                    // binary search for the best chunk
                    while (lower < upper) {
                        var mid = ((upper-lower) / 2) + lower;
                        if (mid == lower) break;

                        var midChunkIdx = _mem.Read<uint>(_skipTable + (SKIP_ELEM_SIZE * mid));
                        if (midChunkIdx == targetChunkIdx) break;

                        if (midChunkIdx < targetChunkIdx) lower = mid;
                        else upper = mid;
                    }

                    var baseAddr = _skipTable + (SKIP_ELEM_SIZE * lower); // pointer to skip table entry
                    startChunkIdx = _mem.Read<uint>(baseAddr);
                    chunkHeadPtr = _mem.Read<long>(baseAddr + INDEX_SIZE);
                }

                var walk = targetChunkIdx - startChunkIdx;
                if (walk > 5 && _skipEntries < SKIP_TABLE_SIZE_LIMIT) {
                    _skipTableDirty = true; // if we are walking too far, try builing a better table
                }

                // 4. Walk the chain until we find the chunk we want
                for (; startChunkIdx < targetChunkIdx; startChunkIdx++)
                {
                    chunkHeadPtr = _mem.Read<long>(chunkHeadPtr);
                }

                found = true;
                chunkPtr = chunkHeadPtr;
                chunkIndex = targetChunkIdx;
            }
        }

        /// <summary>
        /// Get a pointer for an index
        /// </summary>
        protected Result<long> PtrOfElem(uint index)
        {
            if (index >= _elementCount) return Result.Fail<long>();

            var entryIdx = index % ElemsPerChunk;
            FindNearestChunk(index, out var found, out var chunkPtr, out _);
            if (!found) return Result.Fail<long>();

            // push in the value
            return Result.Ok(chunkPtr + ChunkHeaderSize + (ElementByteSize * entryIdx) );
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
            var entryIdx = _elementCount % ElemsPerChunk;
            
            FindNearestChunk(_elementCount, out var found, out var chunkPtr, out _);
            if (!found) // need a new chunk, write at start
            {
                var ok = NewChunk();
                if (!ok.Success) return Result.Fail<Unit>();
                _mem.Write(_endChunkPtr + ChunkHeaderSize, value);
                _elementCount++;
                return Result.Ok();
            }

            // Writing value into existing chunk
            _mem.Write(chunkPtr + ChunkHeaderSize + (ElementByteSize * entryIdx), value);
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
            TElement result;
            if (_elementCount == 0) return Result.Fail<TElement>();

            var index = _elementCount - 1;
            var entryIdx = index % ElemsPerChunk;

            // Get the value
            result = _mem.Read<TElement>(_endChunkPtr + ChunkHeaderSize + (ElementByteSize * entryIdx));
            
            if (entryIdx < 1 && _elementCount > 0) // need to dealloc end chunk
            {
                FindNearestChunk(index - 1, out _, out var prevChunkPtr, out var deadChunkIdx);
                _alloc.Deref(_endChunkPtr);
                _endChunkPtr = prevChunkPtr;
                _mem.Write<long>(prevChunkPtr, -1); // drop pointer in previous

                if (_skipEntries > 0)
                {
                    // Check to see if we've made the skip list invalid
                    var skipTableEnd = _mem.Read<uint>(_skipTable + (SKIP_ELEM_SIZE * (_skipEntries - 1)));

                    // knock the last element off if it's too big. Then let the walk limit in FindNearestChunk set the dirty flag.
                    if (skipTableEnd >= deadChunkIdx)
                    {
                        _skipEntries--;
                    }
                }
            }

            // FindNearestChunk uses this, so we must decrement last
            _elementCount--;
            return Result.Ok(result);
        }

        /// <summary>
        /// Remove the entire vector from memory, including base pointers.
        /// After calling this, the vector will not be usable
        /// </summary>
        public void Deallocate()
        {
            _alloc.Deref(_skipTable);
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
                }
                chunkHeadPtr = nextChunkPtr;
            }

            _elementCount = length;

            RebuildSkipTable(); // make sure we're up to date

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