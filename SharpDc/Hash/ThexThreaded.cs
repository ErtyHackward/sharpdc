/*
 * 
 * Tiger Tree Hash Threaded - by Gil Schmidt.
 * 
 *  - this code was writtin based on:
 *    "Tree Hash EXchange format (THEX)" 
 *    http://www.open-content.net/specs/draft-jchapweske-thex-02.html
 * 
 *  - the tiger hash class was converted from visual basic code called TigerNet:
 *    http://www.hotpixel.net/software.html
 * 
 *  - Base32 class was taken from:
 *    http://msdn.microsoft.com/msdnmag/issues/04/07/CustomPreferences/default.aspx
 *    didn't want to waste my time on writing a Base32 class.
 * 
 *  - along with the request for a version the return the full TTH tree and the need
 *    for a faster version i rewrote a thread base version of the ThexCS.
 *    i must say that the outcome wasn't as good as i thought it would be.
 *    after writing ThexOptimized i noticed that the major "speed barrier"
 *    was reading the data from the file so i decide to split it into threads
 *    that each one will read the data and will make the computing process shorter.
 *    in testing i found out that small files (about 50 mb) are being processed 
 *    faster but in big files (700 mb) it was slower, also the CPU is working better
 *    with more threads.
 *    
 *  - the update for the ThexThreaded is now including a Dispose() function to free
 *    some memory which is mostly taken by the TTH array, also i changed the way the
 *    data is pulled out of the file so it would read data block instead of data leaf 
 *    every time (reduced the i/o reads dramaticly and go easy on the hd) i used 1 MB
 *    blocks for each thread you can set it at DataBlockSize (put something like: 
 *    LeafSize * N). the method for copying bytes is change to Buffer.BlockCopy it's
 *    faster but you won't notice it too much. 
 * 
 *    (a lot of threads = slower but the cpu is working less, i recommend 3-5 threads)
 *
 * - fixed code for 0 byte file (thanks Flow84).
 *  
 * 
 *  if you use this code please add my name and email to the references!
 *  [ contact me at Gil_Smdt@hotmali.com ]
 */

using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.IO;
using System.Threading.Tasks;
using SharpDc.Helpers;
using SharpDc.Logging;
using ThreadPriority = System.Threading.ThreadPriority;

namespace SharpDc.Hash
{
    public interface IThexThreaded
    {
        byte[][][] GetTTHTree(string filename);

        HashAlgorithm Hasher { get; }
    }

    public class ThexThreaded<T> : IThexThreaded where T : HashAlgorithm, new()
	{
        private static readonly ILogger Logger = LogManager.GetLogger();

		const byte LeafHash = 0x00;
		const int  LeafSize = 1024;
		const int  DataBlockSize = LeafSize * 256; // 256 Kb
		const int  ThreadCount = 4;
        const int  ZERO_BYTE_FILE = 0;

		public byte[][][] TTH;
		public int LevelCount;
		string _filename;
		int _leafCount;
		FileStream _filePtr;

		FileBlock[] _fileParts = new FileBlock[ThreadCount];
		Thread[] _threadsList = new Thread[ThreadCount];

        public bool LowPriority { get; set; }

        public HashAlgorithm Hasher
        {
            get { return new T(); }
        }

        public byte[] GetTTHRoot(string filename)
		{
			GetTTH(filename);
			return TTH[LevelCount-1][0];
		}

		public byte[][][] GetTTHTree(string filename)
		{
			GetTTH(filename);
			return TTH;			
		}

		private void GetTTH(string filename)
		{
			_filename = filename;

			try
			{
				OpenFile();
                
                if (Initialize())
                {
                    SplitFile();
                    StartThreads();
                    CompressTree();
                }
			}
			catch (Exception e)
			{
                Logger.Error("error while trying to get TTH: " + e.Message);
				StopThreads();
			}

			if (_filePtr != null) 
                _filePtr.Close();
		}
		
		void Dispose()
		{
			TTH = null;
			_threadsList = null;
			_fileParts = null;
			GC.Collect();
		}

		void OpenFile()
		{
			if (!File.Exists(_filename))
				throw new Exception("file doesn't exists!");

		    _filePtr = new FileStream(_filename, FileMode.Open, FileAccess.Read, FileShare.Read);
		}

		bool Initialize()
		{
            if (_filePtr.Length == ZERO_BYTE_FILE)
            {
                var tg = new T();

                LevelCount = 1;

                TTH = new byte[1][][];
                TTH[0] = new byte[1][];

                TTH[0][0] = tg.ComputeHash(new byte[] { 0 });

                return false;
            }
            else
            {
                int i = 1;
                LevelCount = 1;

                _leafCount = (int)(_filePtr.Length / LeafSize);
                if ((_filePtr.Length % LeafSize) > 0) _leafCount++;

                while (i < _leafCount) { i *= 2; LevelCount++; }

                TTH = new byte[LevelCount][][];
                TTH[0] = new byte[_leafCount][];
            }

            return true;
		}

		void SplitFile()
		{
			long leafsInPart = _leafCount / ThreadCount;

			// check if file is bigger then 1 MB or don't use threads
			if (_filePtr.Length > 1024 * 1024) 
				for (int i = 0; i < ThreadCount; i++)
					_fileParts[i] = new FileBlock(leafsInPart * LeafSize * i,
												  leafsInPart * LeafSize * (i + 1));

			_fileParts[ThreadCount - 1].End = _filePtr.Length;
		}

		void StartThreads()
		{
			for (int i = 0; i < ThreadCount; i++)
			{
				_threadsList[i] = new Thread(ProcessLeafs);
				_threadsList[i].IsBackground = true;
				_threadsList[i].Start(i);
			}

			while (true)
            {
				Thread.Sleep(0);
			    if (_threadsList.All(t => !t.IsAlive))
			        break;
			}
		}

		void StopThreads()
		{
			for (int i = 0; i < ThreadCount; i++)
				if (_threadsList[i] != null && _threadsList[i].IsAlive) 
					_threadsList[i].Abort();
		}

		void ProcessLeafs(object threadId)
		{
            using (LowPriority ? ThreadUtility.EnterBackgroundProcessingMode() : null)
            using (var threadFilePtr = new FileStream(_filename, FileMode.Open, FileAccess.Read, FileShare.Read))
		    {
		        var threadFileBlock = _fileParts[(int)threadId];
		        var tg = new T();
		        var data = new byte[LeafSize + 1];

		        threadFilePtr.Position = threadFileBlock.Start;

                var dataBlock = new byte[DataBlockSize];

		        while (threadFilePtr.Position < threadFileBlock.End)
		        {
		            var leafIndex = (int)(threadFilePtr.Position / 1024);

                    var dataBlockSize = (int)Math.Min(threadFileBlock.End - threadFilePtr.Position, DataBlockSize);

                    threadFilePtr.Read(dataBlock, 0, dataBlockSize); //read block

                    var blockLeafs = dataBlockSize / 1024;

		            int i;
		            for (i = 0; i < blockLeafs; i++)
		            {
		                Buffer.BlockCopy(dataBlock, i * LeafSize, data, 1, LeafSize);

		                tg.Initialize();
		                TTH[0][leafIndex++] = tg.ComputeHash(data);
		            }

                    if (i * LeafSize < dataBlockSize)
		            {
		                data = new byte[dataBlockSize - blockLeafs * LeafSize + 1];
		                data[0] = LeafHash;

		                Buffer.BlockCopy(dataBlock, blockLeafs * LeafSize, data, 1, (data.Length - 1));

		                tg.Initialize();
		                TTH[0][leafIndex] = tg.ComputeHash(data);

		                data = new byte[LeafSize + 1];
		                data[0] = LeafHash;
		            }
		        }
		    }
		}
        
		void CompressTree()
		{
		    int level = 0;

		    while (level + 1 < LevelCount)
			{
				int leafIndex = 0;
				var internalLeafCount = (_leafCount / 2) + (_leafCount % 2);
				TTH[level + 1] = new byte[internalLeafCount][];

			    if (_leafCount > 128)
			    {
			        Parallel.For(0, _leafCount / 2,
			                     ind => ProcessInternalLeaf(level + 1, ind, TTH[level][ind*2], TTH[level][ind*2+1]));
			        leafIndex = _leafCount / 2;
			    }
			    else
			    {
			        for (var i = 1; i < _leafCount; i += 2)
			            ProcessInternalLeaf(level + 1, leafIndex++, TTH[level][i - 1], TTH[level][i]);
			    }

			    if (leafIndex < internalLeafCount) 
					TTH[level + 1][leafIndex] = TTH[level][_leafCount - 1];

				level++;
				_leafCount = internalLeafCount;
			}
		}

        private void ProcessInternalLeaf(int level, int index, byte[] leafA, byte[] leafB)
        {
            var tg = new T();
            TTH[level][index] = ThexHelper.InternalHash(tg, leafA, leafB);
        }
	}

    public static class ThexHelper
    {
        const byte InternalHashMark = 0x01;
        const byte LeafHash = 0x00;

        public static byte[] InternalHash(HashAlgorithm hash, byte[] leafA, byte[] leafB)
        {
            var data = new byte[leafA.Length + leafB.Length + 1];

            data[0] = InternalHashMark;
            
            leafA.CopyTo(data, 1);
            leafB.CopyTo(data, leafA.Length + 1);
            
            return hash.ComputeHash(data);
        }

        public static bool HashesEquals(byte[] hash1, byte[] hash2)
        {
            for (var i = 0; i < hash1.Length; i++)
            {
                if (hash1[i] != hash2[i])
                    return false;
            }
            return true;
        }

        public static byte[] CompressHashBlock(HashAlgorithm hasher, byte[][] hashBlock)
        {
            var hashCount = hashBlock.GetUpperBound(1);

            while (hashCount > 1) //until there's only 1 hash.
            {
                var tempBlockSize = hashCount / 2;
                if (hashCount % 2 > 0) tempBlockSize++;

                var tempBlock = new byte[tempBlockSize][];

                var hashIndex = 0;
                for (int i = 0; i < hashCount / 2; i++) //makes hash from pairs.
                {
                    tempBlock[i] = InternalHash(hasher, hashBlock[hashIndex], hashBlock[hashIndex + 1]);
                    hashIndex += 2;
                }

                //this one doesn't have a pair :(
                if (hashCount % 2 > 0)
                    tempBlock[tempBlockSize - 1] = hashBlock[hashCount - 1];

                hashBlock = tempBlock;
                hashCount = tempBlockSize;
            }
            return hashBlock[0];
        }

        private static byte[][] ReadLeafs(HashAlgorithm tg, string filePath, long start, long end)
        {
            using (ThreadUtility.EnterBackgroundProcessingMode())
            using (var threadFilePtr = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var threadFileBlock = new FileBlock(start, end);
                var LeafSize = 1024;
                var DataBlockSize = LeafSize * 1024;
                var data = new byte[LeafSize + 1];

                var totalLeafs = threadFileBlock.Length / LeafSize + threadFileBlock.Length % LeafSize > 0 ? 1 : 0;

                var result = new byte[totalLeafs][];

                threadFilePtr.Position = threadFileBlock.Start;

                while (threadFilePtr.Position < threadFileBlock.End)
                {
                    var leafIndex = (int)((threadFilePtr.Position - start) / 1024);

                    var dataBlock = new byte[Math.Min(threadFileBlock.End - threadFilePtr.Position, DataBlockSize)];

                    threadFilePtr.Read(dataBlock, 0, dataBlock.Length); //read block

                    var blockLeafs = dataBlock.Length / LeafSize;

                    int i;
                    for (i = 0; i < blockLeafs; i++)
                    {
                        Buffer.BlockCopy(dataBlock, i * LeafSize, data, 1, LeafSize);

                        tg.Initialize();
                        result[leafIndex++] = tg.ComputeHash(data);
                    }

                    if (i * LeafSize < dataBlock.Length)
                    {
                        data = new byte[dataBlock.Length - blockLeafs * LeafSize + 1];
                        data[0] = LeafHash;

                        Buffer.BlockCopy(dataBlock, blockLeafs * LeafSize, data, 1, (data.Length - 1));

                        tg.Initialize();
                        result[leafIndex] = tg.ComputeHash(data);

                        data = new byte[LeafSize + 1];
                        data[0] = LeafHash;
                    }
                }
                return result;
            }
        }

        public static bool VerifySegment(HashAlgorithm tg, byte[] correctHash, string filePath, long start, int length)
        {
            if (correctHash == null) return false;
            if (correctHash.Length != 24) return false;

            return HashesEquals(CompressHashBlock(tg, ReadLeafs(tg, filePath, start, start + length)), correctHash);
        }
    }

    struct FileBlock
	{
	    public long Start;
        public long End;

	    public int Length
	    {
	        get { return (int)(End - Start); }
	    }

	    public FileBlock(long start,long end)
		{
			Start = start;
			End = end;
		}
	}
}
