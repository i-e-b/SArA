namespace Sara
{
    /// <summary>
    /// Container elements' ability to expose internal pointers
    /// for GC tracing
    /// </summary>
    public interface IGcContainer
    {
        long[] References();
    }
}