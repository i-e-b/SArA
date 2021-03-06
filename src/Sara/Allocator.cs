﻿using JetBrains.Annotations;

namespace Sara
{
    /// <summary>
    /// A bottom-to-top sequential allocator, for experimental purposes
    /// </summary>
    public class Allocator
    {
        /// <summary>
        /// Size of each arena in bytes. This is the maximum allocation size
        /// </summary>
        public const long ArenaSize = ushort.MaxValue; // 64K

        /// <summary>
        /// Bottom of free memory (after arena management is taken up)
        /// </summary>
        private readonly long _start;

        /// <summary>
        /// Top of memory
        /// </summary>
        private readonly long _limit;

        /// <summary>
        /// Abstract interface to memory state
        /// </summary>
        [NotNull] private readonly IMemoryAccess _memory;

        /// <summary>
        /// Pointer to array of ushort, length is equal to _arenaCount.
        /// Each element is offset of next pointer to allocate. Zero indicates an empty arena.
        /// </summary>
        private readonly long _headsPtr;

        /// <summary>
        /// Pointer to array of ushort, length is equal to _arenaCount.
        /// Each element is number of references claimed against the arena.
        /// </summary>
        private readonly long _refCountsPtr;

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
        /// <param name="start">Start of space (bytes) available to the allocator. Some will be reserved for arena tracking</param>
        /// <param name="limit">Maximum memory before an out-of-memory condition is flagged (bytes)</param>
        /// <param name="memory">Access to real or simulated memory</param>
        public Allocator(long start, long limit, [NotNull]IMemoryAccess memory)
        {
            _limit = limit;
            _memory = memory;

            // with 64KB arenas (ushort) and 1GB of RAM, we get 16384 arenas.
            // recording only heads and refs would take 64KB of management space
            // recording heads, refs and back-step (an optimisation for very short-lived items) would use 96KB of management space.
            // This seems pretty reasonable.
            _arenaCount = (int)( (_limit - _start) / ArenaSize );
            _currentArena = 0;

            // Allow space for arena tables, store adjusted base
            var sizeOfTables = sizeof(ushort) * _arenaCount;
            _headsPtr = start;
            _refCountsPtr = start + sizeOfTables;

            _start = start + (sizeOfTables * 2);

            // zero-out the tables
            var zptr = _headsPtr;
            while (zptr < _start)
            {
                _memory.Write<ushort>(zptr, 0);
                zptr += sizeof(ushort);
            }

        }

        private ushort GetHead(int arenaIndex) {
            return _memory.Read<ushort>(_headsPtr + (arenaIndex * sizeof(ushort)));
        }
        private ushort GetRefCount(int arenaIndex) {
            return _memory.Read<ushort>(_refCountsPtr + (arenaIndex * sizeof(ushort)));
        }
        
        private void SetHead(int arenaIndex, ushort val) {
            _memory.Write<ushort>(_headsPtr + (arenaIndex * sizeof(ushort)), val);
        }
        private void SetRefCount(int arenaIndex, ushort val) {
            _memory.Write<ushort>(_refCountsPtr + (arenaIndex * sizeof(ushort)), val);
        }

        /// <summary>
        /// Allocate some bytes. Returns pointer to start of memory. This also implicitly adds a single reference to the arena.
        /// Returns negative if failed.
        /// </summary><remarks>
        /// We could distribute allocations among arenas to improve the chance we can use a back-step to reduce fragmenting
        /// </remarks>
        public Result<long> Alloc(long byteCount)
        {
            if (byteCount > ArenaSize) return Result.Fail<long>(); //INVALID_ALLOC;
            var maxOff = ArenaSize - byteCount;

            // scan for first arena where there is enough room
            // we can either start from scratch each time, start from last success, or last emptied
            for (int seq = 0; seq < _arenaCount; seq++)
            {
                var i = (seq + _currentArena) % _arenaCount; // simple scan from last active, looping back if needed

                if (GetHead(i) > maxOff) continue;

                // found a slot where it will fit
                _currentArena = i;
                ushort result = GetHead(i); // new pointer
                SetHead(i, (ushort) (result + byteCount)); // advance pointer to end of allocated data

                var oldRefs = GetRefCount(i);
                SetRefCount(i, (ushort) (oldRefs + 1)); // increase arena ref count

                return Result.Ok(result + (i * ArenaSize) + _start); // turn the offset into an absolute position
            }

            // found nothing
            return Result.Fail<long>();
        }


        /// <summary>
        /// Claim another reference to a pointer
        /// </summary>
        public Result<Unit> Reference(long ptr)
        {
            var res = ArenaForPtr(ptr);
            if ( ! res.Success) return Result.Fail<Unit>();
            var arena = res.Value;

            var oldRefs = GetRefCount(arena);
            if (oldRefs == ushort.MaxValue) return Result.Fail<Unit>(); // saturated references. Fix your code.

            SetRefCount(arena, (ushort) (oldRefs + 1));
            return Result.Ok();
        }

        /// <summary>
        /// Drop a claim to a pointer
        /// </summary>
        public Result<Unit> Deref(long ptr)
        {
            var res = ArenaForPtr(ptr);
            if ( ! res.Success) return Result.Fail<Unit>();
            var arena = res.Value;

            var refCount = GetRefCount(arena);
            if (refCount == 0) return Result.Fail<Unit>(); // Overfree. Fix your code.

            refCount--;
            SetRefCount(arena, refCount);

            // If no more references, free the block
            if (refCount == 0) {
                SetHead(arena, 0);
                if (arena < _currentArena) _currentArena = arena; // keep allocations packed in low memory. Is this worth it?
            }
            return Result.Ok();
        }

        private Result<int> ArenaForPtr(long ptr)
        {
            if (ptr < _start || ptr > _limit) return Result.Fail<int>();
            int arena = (int) ((ptr - _start) / ArenaSize);
            if (arena < 0 || arena >= _arenaCount) return Result.Fail<int>();
            return Result.Ok(arena);
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
        public Result<int> GetArenaOccupation(int arena)
        {
            if (arena < 0 || arena >= _arenaCount) return Result.Fail<int>();
            return Result.Ok((int)GetHead(arena));
        }
        
        /// <summary>
        /// Get the recorded reference count for the given arena. This is a sum of all contained pointers
        /// Empty or reset arenas will return 0. Invalid references will give negative result.
        /// </summary>
        public Result<int> ArenaRefCount(int arena)
        {
            if (arena < 0 || arena >= _arenaCount) return Result.Fail<int>();
            return Result.Ok((int)GetRefCount(arena));
        }

        /// <summary>
        /// Try to immediately free memory.
        /// Pass in an exhaustive list of referenced pointers. Any arenas
        /// with no referenced pointers will be immediately reset
        /// </summary>
        public void ScanAndSweep(Vector<long> referenceList)
        {
            if (referenceList == null) return;

            // mark all arenas zero referenced
            for (int i = 0; i < _arenaCount; i++)
            {
                SetRefCount(i, 0);
            }

            // increment for each reference
            for (uint i = 0; i < referenceList.Length(); i++)
            {
                var a = ArenaForPtr(referenceList.Get(i).Value);
                if (a.Success)
                {
                    var refC = GetRefCount(a.Value);
                    SetRefCount(a.Value, (ushort) (refC + 1));
                }
            }

            // reset any arenas still zeroed
            for (int i = _arenaCount - 1; i >= 0; i--)
            {
                if (GetRefCount(i) == 0) {
                    SetHead(i, 0);
                    _currentArena = i; // keep allocations packed in low memory
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
                var arenaRefCount = GetRefCount(i);
                var arenaHead = GetHead(i);
                totalReferenceCount += arenaRefCount;

                if (arenaHead > 0) occupiedArenas++;
                else emptyArenas++;

                var free = ArenaSize - arenaHead;
                allocatedBytes += arenaHead;
                unallocatedBytes += free;
                if (free > largestContiguous) largestContiguous = free;
            }
        }
    }
}
