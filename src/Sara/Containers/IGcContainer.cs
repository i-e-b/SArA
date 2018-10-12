namespace Sara
{
    /// <summary>
    /// Container elements' ability to expose internal pointers
    /// for GC tracing
    /// </summary>
    public interface IGcContainer
    {
        /// <summary>
        /// Return all allocated pointers. Can be used for a GC scan.
        /// The container should be usable after this call.
        /// </summary>
        ulong[] References();

        /// <summary>
        /// Free all memory. The container MUST NOT be used after calling.
        /// This is for use with manual management.
        /// </summary>
        void Deallocate();
    }
}