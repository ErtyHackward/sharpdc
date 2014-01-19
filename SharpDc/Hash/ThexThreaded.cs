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
        byte[][][] GetTTHTree(string Filename);
    }

    public class ThexThreaded<T> : IThexThreaded where T : HashAlgorithm, new()
	{
        private static readonly ILogger Logger = LogManager.GetLogger();

		const byte LeafHash = 0x00;
		const byte InternalHash = 0x01;
		const int  LeafSize = 1024;
		const int  DataBlockSize = LeafSize * 1024; // 1 MB
		const int  ThreadCount = 4;
        const int  ZERO_BYTE_FILE = 0;

		public byte[][][] TTH;
		public int LevelCount;
		string Filename;
		int LeafCount;
		FileStream FilePtr;

		FileBlock[] FileParts = new FileBlock[ThreadCount];
		Thread[] ThreadsList = new Thread[ThreadCount];

		public byte[] GetTTHRoot(string Filename)
		{
			GetTTH(Filename);
			return TTH[LevelCount-1][0];
		}

		public byte[][][] GetTTHTree(string Filename)
		{
			GetTTH(Filename);
			return TTH;			
		}

		private void GetTTH(string Filename)
		{
			this.Filename = Filename;

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

			if (FilePtr != null) 
                FilePtr.Close();
		}
		
		void Dispose()
		{
			TTH = null;
			ThreadsList = null;
			FileParts = null;
			GC.Collect();
		}

		void OpenFile()
		{
			if (!File.Exists(Filename))
				throw new Exception("file doesn't exists!");

			FilePtr = new FileStream(Filename,FileMode.Open,FileAccess.Read,FileShare.Read); //,DataBlockSize);
		}

		bool Initialize()
		{
            if (FilePtr.Length == ZERO_BYTE_FILE)
            {
                T TG = new T();

                LevelCount = 1;

                TTH = new byte[1][][];
                TTH[0] = new byte[1][];

                TTH[0][0] = TG.ComputeHash(new byte[] { 0 });

                return false;
            }
            else
            {
                int i = 1;
                LevelCount = 1;

                LeafCount = (int)(FilePtr.Length / LeafSize);
                if ((FilePtr.Length % LeafSize) > 0) LeafCount++;

                while (i < LeafCount) { i *= 2; LevelCount++; }

                TTH = new byte[LevelCount][][];
                TTH[0] = new byte[LeafCount][];
            }

            return true;
		}

		void SplitFile()
		{
			long LeafsInPart = LeafCount / ThreadCount;

			// check if file is bigger then 1 MB or don't use threads
			if (FilePtr.Length > 1024 * 1024) 
				for (int i = 0; i < ThreadCount; i++)
					FileParts[i] = new FileBlock(LeafsInPart * LeafSize * i,
												 LeafsInPart * LeafSize * (i + 1));

			FileParts[ThreadCount - 1].End = FilePtr.Length;
		}

		void StartThreads()
		{
			for (int i = 0; i < ThreadCount; i++)
			{
				ThreadsList[i] = new Thread(ProcessLeafs);
				ThreadsList[i].IsBackground = true;
                ThreadsList[i].Priority = (ThreadPriority)ProcessPriorityClass.Idle;
				ThreadsList[i].Start(i);
			}

			while (true)
            {
				Thread.Sleep(0);
			    if (ThreadsList.All(t => !t.IsAlive))
			        break;
			}
		}

		void StopThreads()
		{
			for (int i = 0; i < ThreadCount; i++)
				if (ThreadsList[i] != null && ThreadsList[i].IsAlive) 
					ThreadsList[i].Abort();
		}

		void ProcessLeafs(object threadId)
		{
            using (ThreadUtility.EnterBackgroundProcessingMode())
            using (var ThreadFilePtr = new FileStream(Filename, FileMode.Open, FileAccess.Read, FileShare.Read))
		    {
		        FileBlock ThreadFileBlock = FileParts[(int)threadId];
		        var TG = new T();
		        byte[] DataBlock;
		        byte[] Data = new byte[LeafSize + 1];
		        int LeafIndex, BlockLeafs;
		        int i;

		        ThreadFilePtr.Position = ThreadFileBlock.Start;

		        while (ThreadFilePtr.Position < ThreadFileBlock.End)
		        {
		            LeafIndex = (int)(ThreadFilePtr.Position / 1024);

		            if (ThreadFileBlock.End - ThreadFilePtr.Position < DataBlockSize)
		                DataBlock = new byte[ThreadFileBlock.End - ThreadFilePtr.Position];
		            else
		                DataBlock = new byte[DataBlockSize];

		            ThreadFilePtr.Read(DataBlock, 0, DataBlock.Length); //read block

		            BlockLeafs = DataBlock.Length / 1024;

		            for (i = 0; i < BlockLeafs; i++)
		            {
		                Buffer.BlockCopy(DataBlock, i * LeafSize, Data, 1, LeafSize);

		                TG.Initialize();
		                TTH[0][LeafIndex++] = TG.ComputeHash(Data);
		            }

		            if (i * LeafSize < DataBlock.Length)
		            {
		                Data = new byte[DataBlock.Length - BlockLeafs * LeafSize + 1];
		                Data[0] = LeafHash;

		                Buffer.BlockCopy(DataBlock, BlockLeafs * LeafSize, Data, 1, ( Data.Length - 1 ));

		                TG.Initialize();
		                TTH[0][LeafIndex++] = TG.ComputeHash(Data);

		                Data = new byte[LeafSize + 1];
		                Data[0] = LeafHash;
		            }
		        }

		        DataBlock = null;
		        Data = null;
		    }
		}

		void CompressTree()
		{
			int InternalLeafCount;
			int Level = 0,i,LeafIndex;

			while (Level + 1 < LevelCount)
			{
				LeafIndex = 0;
				InternalLeafCount = (LeafCount / 2) + (LeafCount % 2);
				TTH[Level + 1] = new byte[InternalLeafCount][];

			    if (LeafCount > 128)
			    {
			        Parallel.For(0, LeafCount / 2,
			                     ind => ProcessInternalLeaf(Level + 1, ind, TTH[Level][ind*2], TTH[Level][ind*2+1]));
			        LeafIndex = LeafCount / 2;
			    }
			    else
			    {
			        for (i = 1; i < LeafCount; i += 2)
			            ProcessInternalLeaf(Level + 1, LeafIndex++, TTH[Level][i - 1], TTH[Level][i]);
			    }

			    if (LeafIndex < InternalLeafCount) 
					TTH[Level + 1][LeafIndex] = TTH[Level][LeafCount - 1];

				Level++;
				LeafCount = InternalLeafCount;
			}
		}

		void ProcessInternalLeaf(int Level,int Index,byte[] LeafA,byte[] LeafB)
		{
			T TG = new T();
			byte[] Data = new byte[LeafA.Length + LeafB.Length + 1];

			Data[0] = InternalHash;

			Buffer.BlockCopy(LeafA,0,Data,1,LeafA.Length);
			Buffer.BlockCopy(LeafB,0,Data,LeafA.Length + 1,LeafA.Length);

			TG.Initialize();
			TTH[Level][Index] = TG.ComputeHash(Data);
		}
	}

	struct FileBlock
	{
		public long Start,End;

		public FileBlock(long Start,long End)
		{
			this.Start = Start;
			this.End = End;
		}
	}
}
