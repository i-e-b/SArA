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
        // Tuning parameters:
        public const int TARGET_ELEMS_PER_CHUNK = 32; // Bigger = faster, but more memory-wasteful on small arrays
        public const int SECOND_LEVEL_SKIPS = 32; // Maximum hop-off points. Bigger = faster, but more memory. Careful that it's not bigger than an arena (keep it under 1000)
        public const int MIN_REBUILD_AGE = 5;

        public const int PTR_SIZE = sizeof(long);
        public const int INDEX_SIZE =  sizeof(uint);
        public const int SKIP_ELEM_SIZE = INDEX_SIZE + PTR_SIZE;

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
        public readonly int ChunkBytes;

        private readonly Allocator _alloc;
        private readonly IMemoryAccess _mem;
        private uint _elementCount; // how long is the logical array
        private int _skipEntries;  // how long is the logical skip table
        private int _skipTableAge;  // how many chunks have been added since we updated the skip table

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

            ChunkBytes = (ChunkHeaderSize) + (ElemsPerChunk * ElementByteSize);



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
            _skipTableAge++; 

            _mem.Write<long>(ptr, -1);                             // set the continuation pointer of the new chunk to invalid
            if (_endChunkPtr >= 0) _mem.Write(_endChunkPtr, ptr);  // update the continuation pointer of the old end chunk
            _endChunkPtr = ptr;                                    // update the end chunk pointer

            MaybeRebuildSkipTable();

            return Result.Ok(ptr);
        }

        private void MaybeRebuildSkipTable() {
            if (_skipTableAge < MIN_REBUILD_AGE) return;
            // If we've added a few chunks since last update, then refresh the skip table
            RebuildSkipTable();
            _skipTableAge = 0;
        }

        private void RebuildSkipTable()
        {
            unchecked
            {
                var chunkTotal = _elementCount / ElemsPerChunk;
                if (chunkTotal < 3) // not worth having a skip table
                {
                    if (_skipTable >= 0) _alloc.Deref(_skipTable);
                    _skipTable = -1;
                    _skipEntries = 0;
                }

                // Simple case: every chunk will fit in the skip table
                // scan through and build it
                if (chunkTotal <= SECOND_LEVEL_SKIPS) // each chunk can be simply represented
                {
                    BuildSimpleSkipTable(chunkTotal);
                    return;
                }

                // General case: not every chunk will fit in the skip table
                // Find representative chunks using the existing table.
                // (finding will be a combination of search and scan)
                var newTable = _alloc.Alloc(SKIP_ELEM_SIZE * SECOND_LEVEL_SKIPS);
                if (!newTable.Success) return; // live with the old one
                var newTablePtr = newTable.Value;

                var stride = _elementCount / SECOND_LEVEL_SKIPS;
                uint target = 0;
                var newSkipEntries = 0;
                for (int i = 0; i < SECOND_LEVEL_SKIPS; i++)
                {
                    FindNearestChunk(target, out var found, out var chunkPtr, out var chunkIndex);
                    if (!found) break; // dropped off the end?
                    if (chunkPtr < 0) break; // total fail
                    var iptr = newTablePtr + (SKIP_ELEM_SIZE * i);
                    _mem.Write<uint>(iptr, (uint)chunkIndex);
                    _mem.Write<long>(iptr + INDEX_SIZE, chunkPtr);
                    newSkipEntries++;
                    target += stride;
                }
                if (newSkipEntries < 1) // total fail
                {
                    _alloc.Deref(newTablePtr);
                    return;
                }
                _skipEntries = newSkipEntries;
                if (_skipTable >= 0) _alloc.Deref(_skipTable);
                _skipTable = newTablePtr;
            }
        }

        private void BuildSimpleSkipTable(long chunkTotal)
        {
            unchecked
            {
                var newTable = _alloc.Alloc(SKIP_ELEM_SIZE * chunkTotal);
                if (!newTable.Success) return;
                var newTablePtr = newTable.Value;

                var next = _baseChunkTable;
                var newSkipEntries = 0;
                for (int i = 0; i < chunkTotal; i++)
                {
                    if (next < 0) break;
                    var iptr = newTablePtr + (SKIP_ELEM_SIZE * i);
                    _mem.Write<uint>(iptr, (uint)i);
                    _mem.Write<long>(iptr + INDEX_SIZE, next);
                    newSkipEntries++;
                    next = _mem.Read<long>(next);
                }

                if (newSkipEntries < 1) // total fail
                {
                    _alloc.Deref(newTablePtr);
                    return;
                }

                _skipEntries = newSkipEntries;
                if (_skipTable >= 0) _alloc.Deref(_skipTable);
                _skipTable = newTablePtr;
                return;
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
                uint endChunk = (uint)((_elementCount - 1) / ElemsPerChunk);

                // 2. Optimise for start- and end- of chain (small lists & very likely for Push & Pop)
                if (_elementCount == 0 || targetChunkIdx == endChunk)
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
                if (targetChunkIdx == 0)
                { // start of chain
                    found = true;
                    chunkPtr = _baseChunkTable;
                    chunkIndex = targetChunkIdx;
                    return;
                }

                // 3. Use the skip table to find a chunk near the target
                uint skipIdx = 0;
                uint startChunkIdx = 0;
                var chunkHeadPtr = _baseChunkTable;
                
                if (_skipEntries > 1) {
                    var upperChunkIdx = _mem.Read<uint>(_skipTable + (SKIP_ELEM_SIZE * (_skipEntries - 1)));
                    uint step = (uint)(upperChunkIdx / (_skipEntries - 1));
                    skipIdx = targetChunkIdx / step;
                    if (skipIdx >= _skipEntries) { skipIdx = (uint)(_skipEntries - 1); }

                    var baseAddr = _skipTable + (SKIP_ELEM_SIZE * skipIdx);
                    startChunkIdx = _mem.Read<uint>(baseAddr);
                    chunkHeadPtr = _mem.Read<long>(baseAddr + INDEX_SIZE);
                }

                // 4. Walk the chain until we find the chunk we want
                var walk = targetChunkIdx - startChunkIdx;
                if (walk > 5 && _skipEntries < SECOND_LEVEL_SKIPS) _skipTableAge = 10000; // if we are walking too far, try builing a better table

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
            if (_elementCount == 1) {
                result = _mem.Read<TElement>(_baseChunkTable + ChunkHeaderSize);
                _elementCount--;
                return Result.Ok(result);
            }

            var index = _elementCount - 1;
            
            var entryIdx = index % ElemsPerChunk;

            // Get the value
            result = _mem.Read<TElement>(_endChunkPtr + ChunkHeaderSize + (ElementByteSize * entryIdx));
            _elementCount--;
            
            if (entryIdx < 1) // need to dealloc last chunk
            {
                FindNearestChunk(index - 1, out _, out var prevChunkPtr, out _);
                _alloc.Deref(_endChunkPtr);
                _endChunkPtr = prevChunkPtr;
                _mem.Write<long>(prevChunkPtr, -1); // drop pointer in previous
                MaybeRebuildSkipTable();
            }
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