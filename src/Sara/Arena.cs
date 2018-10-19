namespace Sara
{
    /// <summary>
    /// Structure of an arena
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
        public ushort RefCount; // this could change to a scope?
    }
}