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
        private const float LOAD_FACTOR = 0.86f;
        private const uint SAFE_HASH = 0x80000000; // just in case you get a zero result

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

        private Entry[] buckets;
        private uint count;
        private uint countMod;
        private uint countUsed;
        private uint growAt;
        private uint shrinkAt;

        public TaggedHashMap(uint size)
        {
            Resize(NextPow2(size), false);
            Clear();
        }

        private KVP[] Entries
        {
            get
            {
                var size = 0;
                for (uint i = 0; i < count; i++) if (buckets[i].hash != 0) size++;

                var result = new KVP[size];
                int j = 0;

                for (uint i = 0; i < count; i++){
                    if (buckets[i].hash == 0) continue;
                    result[j] = new KVP(buckets[i].key, buckets[i].value);
                    j++;
                }
                return result;
            }
        }

        private void Resize(uint newSize, bool auto = true)
        {
            var oldCount = count;
            var oldBuckets = buckets;

            count = newSize;
            countMod = newSize - 1;
            buckets = new Entry[newSize];

            growAt = auto ? (uint)(newSize*LOAD_FACTOR) : newSize;
            shrinkAt = auto ? newSize >> 2 : 0;

            if ((countUsed > 0) && (newSize != 0))
            {
                //Debug.Assert(countUsed <= newSize);
                //Debug.Assert(oldBuckets != null);

                countUsed = 0;

                for (uint i = 0; i < oldCount; i++)
                    if (oldBuckets[i].hash != 0)
                        PutInternal(oldBuckets[i], false, false);
            }
        }

        private bool Get(TKey key, out TValue value)
        {
            uint index;
            if (Find(key, out index))
            {
                value = buckets[index].value;
                return true;
            }

            value = default(TValue);
            return false;
        }

        private bool Put(TKey key, TValue val, bool canReplace)
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
                var
                    indexCurrent = (indexInit + i) & countMod;
                if (buckets[indexCurrent].hash == 0)
                {
                    countUsed++;
                    buckets[indexCurrent] = entry;
                    return true;
                }

                if (checkDuplicates && (entry.hash == buckets[indexCurrent].hash) &&
                    KeyComparer(entry.key, buckets[indexCurrent].key))
                {
                    if (!canReplace) return false; // TODO: error propagation
                        //throw new ArgumentException("An entry with the same key already exists", nameof(entry.key));

                    buckets[indexCurrent] = entry;
                    return true;
                }

                var
                    probeDistance = DistanceToInitIndex(indexCurrent);
                if (probeCurrent > probeDistance)
                {
                    probeCurrent = probeDistance;
                    Swap(ref buckets[indexCurrent], ref entry);
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
            if (countUsed > 0)
            {
                uint
                    hash = GetHash(key),
                    indexInit = hash & countMod,
                    probeDistance = 0;

                for (uint i = 0; i < count; i++)
                {
                    index = (indexInit + i) & countMod;

                    if ((hash == buckets[index].hash) && KeyComparer(key, buckets[index].key))
                        return true;

                    if (buckets[index].hash != 0)
                        probeDistance = DistanceToInitIndex(index);

                    if (i > probeDistance)
                        break;
                }
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

                    if ((buckets[nextIndex].hash == 0) || (DistanceToInitIndex(nextIndex) == 0))
                    {
                        buckets[curIndex] = default;

                        if (--countUsed == shrinkAt)
                            Resize(shrinkAt);

                        return true;
                    }

                    Swap(ref buckets[curIndex], ref buckets[nextIndex]);
                }
            }

            return false;
        }

        private uint DistanceToInitIndex(uint indexStored)
        {
            //Debug.Assert(buckets[indexStored].hash != 0);

            var indexInit = buckets[indexStored].hash & countMod;
            if (indexInit <= indexStored)
                return indexStored - indexInit;
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

        private static void Swap<T>(ref T first, ref T second)
        {
            var temp = first;
            first = second;
            second = temp;
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

        public TValue this[TKey key]
        {
            get
            {
                TValue result;
                if (!Get(key, out result)) return default;
                    //throw new KeyNotFoundException(key.ToString()); // TODO: error propagation

                return result;
            }
            set { Put(key, value, true); }
        }

        public void Clear()
        {
            Resize(0);
        }

        public int Count => (int) countUsed;

        public bool IsReadOnly => false;
    }
}