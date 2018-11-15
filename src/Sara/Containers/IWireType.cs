namespace Sara
{
    /// <summary>
    /// Marker for types that can be transformed to and from a single compact bytestream
    /// </summary>
    public interface IWireType
    {
        /// <summary>
        /// Build a byte vector that can be used to transmit the instance
        /// </summary>
        Vector<byte> Serialise();

        /// <summary>
        /// Reconstruct an instance from a byte vector.
        /// Note: this would be static or constructor, but C# is limited
        /// </summary>
        Result<Unit> Deserialise(Vector<byte> data);
    }
}