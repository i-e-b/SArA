namespace Sara
{
    /// <summary>
    /// A bottom-to-top sequential allocator, for experimental purposes
    /// </summary>
    public class Allocator
    {
        /// <summary>
        /// Memory could never be allocated (program fault)
        /// </summary>
        public const long INVALID_ALLOC = -2;

        /// <summary>
        /// Memory exhausted. A GC scan or defrag might help.
        /// </summary>
        public const long OUT_OF_MEMORY = -1;

        /// <summary>
        /// Size of each arena in bytes. This is the maximum allocation size
        /// </summary>
        public const long ArenaSize = ushort.MaxValue;



        /// <summary>
        /// Bottom of memory
        /// </summary>
        private readonly long _start;

        /// <summary>
        /// Top of memory
        /// </summary>
        private readonly long _limit;

        /// <summary>
        /// Metadata records for all the arenas
        /// </summary>
        private readonly Arena[] _meta;

        /// <summary>
        /// The most recent arena that had a successful alloc or clear
        /// </summary>
        private int _currentArena;

        /// <summary>
        /// Count of available arenas. This is the limit of memory
        /// </summary><remarks>Using int for count and 64KB arenas gives a limit of 127 TB. Should be enough for a Raspi</remarks>
        private readonly int _arenaCount;

        /// <summary>
        /// Start a new allocator.
        /// This tracks memory usage, but does not do any physical work
        /// </summary>
        /// <param name="start">Start of allocation space (bytes)</param>
        /// <param name="limit">Maximum memory before an out-of-memory condition is flagged (bytes)</param>
        public Allocator(long start, long limit)
        {
            _start = start;
            _limit = limit;

            // with 64KB arenas (ushort) and 1GB of RAM, we get 16384 arenas.
            // recording only heads and refs would take 64KB of management space
            // recording heads, refs and back-step (an optimisation for very short-lived items) would use 96KB of management space.
            // This seems pretty reasonable.
            _arenaCount = (int)( (_limit - _start) / ArenaSize );
            _currentArena = 0;
            _meta = new Arena[_arenaCount];
        }

        /// <summary>
        /// Allocate some bytes. Returns pointer to start of memory. This also implicitly adds a single reference to the arena.
        /// Returns negative if failed.
        /// </summary><remarks>
        /// We could distribute allocations among arenas to improve the chance we can use a back-step to reduce fragmenting
        /// </remarks>
        public long Alloc(long byteCount)
        {
            if (byteCount > ArenaSize) return INVALID_ALLOC;
            var maxOff = ArenaSize - byteCount;

            // scan for first arena where there is enough room
            // we can either start from scratch each time, start from last success, or last emptied
            for (int seq = 0; seq < _arenaCount; seq++)
            {
                var i = (seq + _currentArena) % _arenaCount; // simple scan from last active, looping back if needed

                if (_meta[i].Head > maxOff) continue;

                // found a slot where it will fit
                _currentArena = i;
                var result = _meta[i].Head; // new pointer
                _meta[i].Head += (ushort)byteCount; // advance pointer to end of allocated data
                _meta[i].RefCount++; // increase arena ref count

                return result + (i * ArenaSize) + _start; // turn the offset into an absolute position
            }

            // found nothing
            return OUT_OF_MEMORY;
        }


        /// <summary>
        /// Claim another reference to a pointer
        /// </summary>
        public void Reference(long ptr)
        {
            var arena = ArenaForPtr(ptr);
            if (arena < 0) return;

            if (_meta[arena].RefCount == ushort.MaxValue) return; // saturated references. Fix your code.

            _meta[arena].RefCount++;
        }

        /// <summary>
        /// Drop a claim to a pointer
        /// </summary>
        public void Deref(long ptr)
        {
            var arena = ArenaForPtr(ptr);
            if (arena < 0) return;

            var refCount = _meta[arena].RefCount;
            if (refCount == 0) return; // Overfree. Fix your code.

            refCount--;
            _meta[arena].RefCount = refCount;

            // If no more references, free the block
            if (refCount == 0) {
                _meta[arena].Head = 0;
                if (arena < _currentArena) _currentArena = arena; // keep allocations packed in low memory. Is this worth it?
            }
        }

        private int ArenaForPtr(long ptr)
        {
            if (ptr < _start || ptr > _limit) return -1;
            int arena = (int) ((ptr - _start) / ArenaSize);
            if (arena < 0 || arena >= _arenaCount) return -1;
            return arena;
        }

        /// <summary>
        /// Which arena index is currently active? (the one to be tried first)
        /// </summary>
        public int CurrentArena()
        {
            return _currentArena;
        }

        /// <summary>
        /// Get the head position offset for the given arena.
        /// Empty or reset arenas will return 0. Invalid references will give negative result.
        /// </summary>
        public int GetArenaOccupation(int arena)
        {
            if (arena < 0 || arena >= _arenaCount) return -1;
            return _meta[arena].Head;
        }
        
        /// <summary>
        /// Get the recorded reference count for the given arena. This is a sum of all contained pointers
        /// Empty or reset arenas will return 0. Invalid references will give negative result.
        /// </summary>
        public int ArenaRefCount(int arena)
        {
            if (arena < 0 || arena >= _arenaCount) return -1;
            return _meta[arena].RefCount;
        }

        /// <summary>
        /// Try to immediately free memory.
        /// Pass in an exhaustive list of referenced pointers. Any arenas
        /// with no referenced pointers will be immediately reset
        /// </summary>
        public void ScanAndSweep(long[] referenceList)
        {
            // mark all arenas zero referenced
            for (int i = 0; i < _arenaCount; i++)
            {
                _meta[i].RefCount = 0;
            }

            // increment for each reference
            for (int i = 0; i < referenceList.Length; i++)
            {
                var a = ArenaForPtr(referenceList[i]);
                _meta[a].RefCount++;
            }

            // reset any arenas still zeroed
            for (int i = 0; i < _arenaCount; i++)
            {
                if (_meta[i].RefCount == 0) {
                    _meta[i].Head = 0;
                    if (i < _currentArena) _currentArena = i; // keep allocations packed in low memory
                }
            }
        }

        /// <summary>
        /// Scan the arena meta-data to read the current allocation state
        /// </summary>
        /// <param name="allocatedBytes">Sum of all bytes that are claimed.</param>
        /// <param name="unallocatedBytes">Sum of all bytes that are not yet allocated. This counts all fragments together.</param>
        /// <param name="occupiedArenas">Number of arenas with at least some </param>
        /// <param name="emptyArenas">Number of arenas with no allocations</param>
        /// <param name="totalReferenceCount">Number of claimed references across all arenas</param>
        /// <param name="largestContiguous">The largest block that can be allocated</param>
        public void GetState(out long allocatedBytes, out long unallocatedBytes, out long occupiedArenas, out long emptyArenas, out long totalReferenceCount, out long largestContiguous)
        {
            allocatedBytes = 0;
            unallocatedBytes = 0;
            occupiedArenas = 0;
            emptyArenas = 0;
            totalReferenceCount = 0;
            largestContiguous = 0;

            for (int i = 0; i < _arenaCount; i++)
            {
                var arena = _meta[i];
                totalReferenceCount += arena.RefCount;

                if (arena.Head > 0) occupiedArenas++;
                else emptyArenas++;

                var free = ArenaSize - arena.Head;
                allocatedBytes += arena.Head;
                unallocatedBytes += free;
                if (free > largestContiguous) largestContiguous = free;
            }
        }
    }
}
