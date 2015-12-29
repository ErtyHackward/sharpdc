namespace SharpDc.Connections
{
    /// <summary>
    /// Describes storage for the hyper server
    /// </summary>
    public interface IHyperStorage
    {
        void EnqueueTask(HyperServerTask task);

        bool Contains(string path);

        /// <summary>
        /// Displays debug information for this storage
        /// </summary>
        /// <returns></returns>
        string DebugLine();

        /// <summary>
        /// Starts the reader if applicable (create background threads etc)
        /// </summary>
        void StartAsync();
    }
}