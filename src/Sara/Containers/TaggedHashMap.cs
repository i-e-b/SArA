namespace Sara
{

    // ReSharper disable BuiltInTypeReferenceStyle
    using TKey = System.UInt64;
    using TValue = System.UInt64;

    public struct KVP{
        public TKey Key;
        public TValue Value;

        public KVP(TKey key, TValue value)
        {
            Key = key;
            Value = value;
        }
    }

    public struct Entry
    {
        public Entry(uint hash, TKey key, TValue value)
        {
            this.hash = hash;
            this.key = key;
            this.value = value;
        }

        public readonly uint hash;
        public readonly TKey key;
        public readonly TValue value;
    }

    /// <summary>
    /// Hash map designed for tagged data
    /// </summary>
    public class TaggedHashMap:IGcContainer
    {

        public virtual bool KeyComparer(TKey a, TKey b) {
            // User should provide comparison.
            return a == b;
        }
        

        public virtual uint GetHash(TKey tag)
        {
            // User should provide a reasonable hash.
            // This default is *NOT* good enough for the real world
            unchecked
            {
                if (tag == 0) return SAFE_HASH;
                return (uint)(tag ^ (tag >> 16));
            }
        }

        public const uint MAX_BUCKET_SIZE = int.MaxValue; // safety limit for scaling the buckets
        public const uint MIN_BUCKET_SIZE = 64; // default size used if none given
        public const float LOAD_FACTOR = 0.8f; // higher is more memory efficient. Lower is faster, to a point.
        public const uint SAFE_HASH = 0x8000_0000; // just in case you get a zero result

        private Vector<Entry> buckets;
        private uint count;
        private uint countMod;
        private uint countUsed;
        private uint growAt;
        private uint shrinkAt;
        private readonly Allocator _alloc;
        private readonly IMemoryAccess _mem;

        /// <summary>
        /// Create a new hash map
        /// </summary>
        /// <param name="size">Initial size</param>
        /// <param name="alloc">Memory allocator</param>
        /// <param name="mem">Memory access</param>
        public TaggedHashMap(uint size, Allocator alloc, IMemoryAccess mem)
        {
            _alloc = alloc;
            _mem = mem;
            Resize((uint)NextPow2(size), false);
        }

        private bool Resize(uint newSize, bool auto = true)
        {
            var oldCount = count;
            var oldBuckets = buckets;

            count = newSize;
            if (newSize > 0 && newSize < MIN_BUCKET_SIZE) newSize = MIN_BUCKET_SIZE;
            if (newSize > MAX_BUCKET_SIZE)
                newSize = MAX_BUCKET_SIZE;

            countMod = newSize - 1;

            buckets = new Vector<Entry>(_alloc, _mem);
            if ( ! buckets.IsValid) return false;
            if ( ! buckets.Prealloc(newSize).Success) return false;

            growAt = auto ? (uint)(newSize*LOAD_FACTOR) : newSize;
            shrinkAt = auto ? newSize >> 2 : 0;

            countUsed = 0;

            if (oldBuckets == null) return true;
            if ((oldCount <= 0) || (newSize == 0)) {
                oldBuckets.Deallocate();
                return true;
            }

            for (uint i = 0; i < oldCount; i++) {
                var res = oldBuckets.Get(i);
                if (!res.Success) continue;
                if (res.Value.hash != 0) PutInternal(res.Value, false, false);
            }

            oldBuckets.Deallocate();
            return true;
        }

        private bool ResizeNext()
        {
            // mild scaling can save memory, but resizing is very expensive -- so the default is an aggressive algorithm

            // Mild scaling
            //return Resize(count == 0 ? 32 : count*2);

            // Aggressive scaling
            ulong size = (ulong)count * 2;
            if (count < 8192) size = (ulong)count * count;
            if (size < MIN_BUCKET_SIZE) size = MIN_BUCKET_SIZE;
            return Resize((uint)size);
        }

        public bool Get(TKey key, out TValue value)
        {
            value = default;

            if (!Find(key, out var index)) return false;
            if (buckets == null) return false;


            var res = buckets.Get(index);
            if (!res.Success)
            {
                value = default;
                return false;
            }

            value = res.Value.value;
            return true;

        }


        public bool Put(TKey key, TValue val, bool canReplace)
        {
            if (countUsed >= growAt) {
                if ( ! ResizeNext()) return false;
            }

            return PutInternal(new Entry(GetHash(key), key, val), canReplace, true);
        }

        private bool PutInternal(Entry entry, bool canReplace, bool checkDuplicates)
        {
            uint indexInit = entry.hash & countMod;
            uint probeCurrent = 0;

            for (uint i = 0; i < count; i++)
            {
                var indexCurrent = (indexInit + i) & countMod;
                
                if (buckets == null) return false;
                var current = buckets.Get(indexCurrent);
                if ( ! current.Success) return false; // internal failure

                if (current.Value.hash == 0)
                {
                    countUsed++;
                    buckets.Set(indexCurrent, entry);
                    return true;
                }

                if (checkDuplicates && (entry.hash == current.Value.hash) &&
                    KeyComparer(entry.key, current.Value.key))
                {
                    if (!canReplace)
                        return false;
                    
                    if (buckets == null) return false;
                    buckets.Set(indexCurrent, entry);
                    return true;
                }

                var probeDistance = DistanceToInitIndex(indexCurrent);
                if (probeCurrent > probeDistance)
                {
                    probeCurrent = probeDistance;
                    if (!Swap(buckets,indexCurrent, ref entry))
                        return false;
                }
                probeCurrent++;
            }
            // need to grow?
            // Trying recursive insert:
            if (!ResizeNext()) return false;
            return PutInternal(entry, canReplace, checkDuplicates);
        }

        private bool Find(TKey key, out uint index)
        {
            index = 0;
            if (countUsed <= 0) return false;

            uint hash = GetHash(key);
            uint indexInit = hash & countMod;
            uint probeDistance = 0;

            for (uint i = 0; i < count; i++)
            {
                if (buckets == null) return false;

                index = (indexInit + i) & countMod;
                var res = buckets.Get(index);
                if ( ! res.Success ) return false; // internal failure

                if ((hash == res.Value.hash) && KeyComparer(key, res.Value.key))
                    return true;

                if (res.Value.hash != 0) probeDistance = DistanceToInitIndex(index);

                if (i > probeDistance) break;
            }

            return false;
        }

        private bool RemoveInternal(TKey key)
        {
            uint index;
            if (!Find(key, out index)) return false;

            for (uint i = 0; i < count; i++)
            {
                var curIndex = (index + i) & countMod;
                var nextIndex = (index + i + 1) & countMod;
                    
                if (buckets == null) return false;
                var res = buckets.Get(nextIndex);
                if ( ! res.Success ) return false; // internal failure

                if ((res.Value.hash == 0) || (DistanceToInitIndex(nextIndex) == 0))
                {
                    if (buckets == null) return false;
                    buckets.Set(curIndex, default);

                    if (--countUsed == shrinkAt) Resize(shrinkAt);

                    return true;
                }

                Swap(buckets, curIndex, nextIndex);
            }

            return false;
        }

        private uint DistanceToInitIndex(uint indexStored)
        {
            if (buckets == null) return indexStored + count;

            var indexInit = buckets.Get(indexStored).Value.hash & countMod;
            if (indexInit <= indexStored) return indexStored - indexInit;
            return indexStored + (count - indexInit);
        }


        private static ulong NextPow2(ulong c)
        {
            c--;
            c |= c >> 1;
            c |= c >> 2;
            c |= c >> 4;
            c |= c >> 8;
            c |= c >> 16;
            c |= c >> 32;
            return ++c;
        }
        

        /// <summary>
        /// Swap a vector entry with an external value
        /// </summary>
        private bool Swap(Vector<Entry> vec, uint idx, ref Entry newEntry) {
            if (vec == null) return false;

            var temp = vec.Get(idx);
            if (!temp.Success) return false;

            vec.Set(idx, newEntry);
            newEntry = temp.Value;
            return true;
        }
        
        /// <summary>
        /// Swap two vector entries
        /// </summary>
        private void Swap(Vector<Entry> vec, uint idx1, uint idx2) {
            if (vec == null) return;
            vec.Swap(idx1, idx2);
        }
        
        public Vector<KVP> AllEntries()
        {
            if (buckets == null) return null;

            var result = new Vector<KVP>(_alloc, _mem);

            for (uint i = 0; i < count; i++)
            {
                var ent = buckets.Get(i);
                if (ent.Value.hash == 0) continue;

                result.Push(new KVP(ent.Value.key, ent.Value.value));
            }
            return result;
        }

        public bool Add(TKey key, TValue value)
        {
            return Put(key, value, false);
        }

        public bool ContainsKey(TKey key)
        {
            return Find(key, out _);
        }

        public bool Remove(TKey key)
        {
            return RemoveInternal(key);
        }

        public void Clear()
        {
            Resize(0);
        }

        public uint Count()
        {
            return countUsed;
        }

        public Vector<ulong> References()
        {
            var everything = AllEntries();
            if (everything == null) return null;

            // TODO: filter pointers here, or let MECS do it?
            // Also, how do we host the GC-reference-list itself? Do we create a special one, ignore its references and immediately abandon?
            var result = new Vector<ulong>(_alloc, _mem);
            for (uint i = 0; i < everything.Length(); i++)
            {
                result.Push(everything.Get(i).Value.Value);
                result.Push(everything.Get(i).Value.Key);
            }
            everything.Deallocate();
            return result;
        }

        /// <summary>
        /// Free all memory. The container MUST NOT be used after calling.
        /// This is for use with manual management.
        /// </summary>
        public void Deallocate()
        {
            count = 0;
            buckets?.Deallocate();
        }
    }
}