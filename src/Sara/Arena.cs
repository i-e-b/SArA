namespace Sara
{
    /// <summary>
    /// Structure of an arena (to be used later)
    /// </summary>
    public struct Arena {
        // Base is implied by index

        /// <summary>
        /// Offset of next pointer to allocate. Zero indicates an empty arena
        /// </summary>
        public ushort Head;

        /// <summary>
        /// Number of references 
        /// </summary>
        public ushort RefCount;
    }
}