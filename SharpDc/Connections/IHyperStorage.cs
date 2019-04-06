using System;

namespace SharpDc.Connections
{
    /// <summary>
    /// Describes storage for the hyper server
    /// </summary>
    public interface IHyperStorage
    {
        void EnqueueTask(HyperServerTask task);

        /// <summary>
        /// Occurs when storage can't read file on the disk, it was probably moved to another storage or was deleted
        /// </summary>
        event EventHandler<FileGoneEventArgs> FileGone;

        bool Contains(string relativePath);

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

    public class FileGoneEventArgs : EventArgs
    {
        public string SystemPath { get; set; }

        public string RelativePath { get; set; }
    }
}