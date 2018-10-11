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
    public class TaggedHashMap
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


        private const float LOAD_FACTOR = 0.6f; // higher is more memory efficient. Lower is faster, to a point.
        private const uint SAFE_HASH = 0x80000000; // just in case you get a zero result

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
            Resize(NextPow2(size), false);
        }

        private void Resize(uint newSize, bool auto = true)
        {
            var oldCount = count;
            var oldBuckets = buckets;

            count = newSize;
            countMod = newSize - 1;
            //buckets = new Entry[newSize];
            buckets = new Vector<Entry>(_alloc, _mem);
            buckets.Prealloc(newSize, default(Entry));

            growAt = auto ? (uint)(newSize*LOAD_FACTOR) : newSize;
            shrinkAt = auto ? newSize >> 2 : 0;

            if ((countUsed > 0) && (newSize != 0) && oldBuckets != null)
            {
                //Debug.Assert(countUsed <= newSize);
                //Debug.Assert(oldBuckets != null);

                countUsed = 0;

                for (uint i = 0; i < oldCount; i++) {
                    var res = oldBuckets.Get(i);
                    if (!res.Success) continue;
                    if (res.Value.hash != 0) PutInternal(res.Value, false, false);
                }

                oldBuckets.Deallocate();
            }

        }

        private bool Get(TKey key, out TValue value)
        {
            uint index;
            if (Find(key, out index))
            {
                var res = buckets.Get(index);
                if (!res.Success)
                {
                    value = default;
                    return false;
                }

                value = res.Value.value;
                return true;
            }

            value = default;
            return false;
        }

        public bool Put(TKey key, TValue val, bool canReplace)
        {
            //if (key == null) return false;
                //throw new ArgumentNullException(nameof(key));

            if (countUsed == growAt) ResizeNext();

            return PutInternal(new Entry(GetHash(key), key, val), canReplace, true);
        }

        private bool PutInternal(Entry entry, bool canReplace, bool checkDuplicates)
        {
            uint indexInit = entry.hash & countMod;
            uint probeCurrent = 0;

            for (uint i = 0; i < count; i++)
            {
                var indexCurrent = (indexInit + i) & countMod;

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
                    if (!canReplace) return false;

                    buckets.Set(indexCurrent, entry);
                    return true;
                }

                var probeDistance = DistanceToInitIndex(indexCurrent);
                if (probeCurrent > probeDistance)
                {
                    probeCurrent = probeDistance;
                    Swap(buckets,indexCurrent, ref entry);
                }
                probeCurrent++;
            }

            return false;
        }

        private bool Find(TKey key, out uint index)
        {
            //if (key == null)
            //    throw new ArgumentNullException(nameof(key));

            index = 0;
            if (countUsed <= 0) return false;

            uint hash = GetHash(key);
            uint indexInit = hash & countMod;
            uint probeDistance = 0;

            for (uint i = 0; i < count; i++)
            {
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
            if (Find(key, out index))
            {
                for (uint i = 0; i < count; i++)
                {
                    var curIndex = (index + i) & countMod;
                    var nextIndex = (index + i + 1) & countMod;

                    var res = buckets.Get(nextIndex);
                    if ( ! res.Success ) return false; // internal failure

                    if ((res.Value.hash == 0) || (DistanceToInitIndex(nextIndex) == 0))
                    {
                        buckets.Set(curIndex, default);

                        if (--countUsed == shrinkAt) Resize(shrinkAt);

                        return true;
                    }

                    Swap(buckets, curIndex, nextIndex);
                }
            }

            return false;
        }

        private uint DistanceToInitIndex(uint indexStored)
        {
            //Debug.Assert(buckets[indexStored].hash != 0);

            var indexInit = buckets.Get(indexStored).Value.hash & countMod;
            if (indexInit <= indexStored) return indexStored - indexInit;
            return indexStored + (count - indexInit);
        }

        private void ResizeNext()
        {
            Resize(count == 0 ? 1 : count*2);
        }

        private static uint NextPow2(uint c)
        {
            c--;
            c |= c >> 1;
            c |= c >> 2;
            c |= c >> 4;
            c |= c >> 8;
            c |= c >> 16;
            return ++c;
        }
        

        /// <summary>
        /// Swap a vector entry with an external value
        /// </summary>
        private void Swap(Vector<Entry> vec, uint idx, ref Entry newEntry) {
            var temp = vec.Get(idx);
            vec.Set(idx, newEntry);
            newEntry = temp.Value;
        }
        
        /// <summary>
        /// Swap two vector entries
        /// </summary>
        private void Swap(Vector<Entry> vec, uint idx1, uint idx2) {
            vec.Swap(idx1, idx2);
        }
        
        public KVP[] AllEntries()
        {
            var size = 0;
            for (uint i = 0; i < count; i++) {
                var res = buckets.Get(i);
                if (res.Success && res.Value.hash != 0) size++;
            }

            var result = new KVP[size];
            int j = 0;

            for (uint i = 0; i < count; i++)
            {
                var ent = buckets.Get(i);
                if (ent.Value.hash == 0) continue;

                result[j] = new KVP(ent.Value.key, ent.Value.value);
                j++;
            }
            return result;
        }

        public void Add(TKey key, TValue value)
        {
            Put(key, value, false);
        }

        public bool ContainsKey(TKey key)
        {
            return Find(key, out _);
        }

        public bool Remove(TKey key)
        {
            return RemoveInternal(key);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            return Get(key, out value);
        }

        public void Clear()
        {
            Resize(0);
        }

        public int Count => (int) countUsed;

        public bool IsReadOnly => false;
    }
}