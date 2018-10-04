namespace Sara
{
    public class Allocator
    {
        /// <summary>
        /// Start a new allocator.
        /// This tracks memory usage, but does not do any physical work
        /// </summary>
        /// <param name="start">Start of allocation space (bytes)</param>
        /// <param name="limit">Maximum memory before an out-of-memory condition is flagged (bytes)</param>
        public Allocator(long start, long limit)
        {
            
        }

        /// <summary>
        /// Allocate some bytes. Returns pointer to start of memory.
        /// Returns negative if failed.
        /// </summary>
        public long Alloc(int byteCount)
        {
            return 0;
        }
    }
}
