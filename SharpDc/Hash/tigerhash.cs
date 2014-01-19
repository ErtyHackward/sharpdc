//
//       Title: Tiger Hash for C#
//      Author: mastamac of Software Union
// ---------------------------------------
// Description: A speed-optimized native C# implementation of the cryptographic 
//              TIGER hash algorithm of 1995. Based on and usable through
//              .net Framework's HashAlgorithm class.
//     License: Common Development and Distribution License (CDDL)
// ############################################################################


using System;
using System.Runtime.CompilerServices;
using HashAlgorithm=System.Security.Cryptography.HashAlgorithm;


namespace softwareunion
{

	abstract public class BlockHashAlgorithm : HashAlgorithm
	{

		private byte[] ba_PartialBlockBuffer;
		private int i_PartialBlockFill;
		
		protected int i_InputBlockSize;
		protected long l_TotalBytesProcessed;


		/// <summary>Initializes a new instance of the BlockHashAlgorithm class.</summary>
		/// <param name="blockSize">The size in bytes of an individual block.</param>
		protected BlockHashAlgorithm(int blockSize,int hashSize) : base()
		{
			this.i_InputBlockSize = blockSize;
			this.HashSizeValue = hashSize;
			ba_PartialBlockBuffer = new byte[BlockSize];
		}


		/// <summary>Initializes the algorithm.</summary>
		/// <remarks>If this function is overriden in a derived class, the new function should call back to
		/// this function or you could risk garbage being carried over from one calculation to the next.</remarks>
		public override void Initialize()
		{	//abstract: base.Initialize();
			l_TotalBytesProcessed = 0;
			i_PartialBlockFill = 0;
			if(ba_PartialBlockBuffer==null) ba_PartialBlockBuffer=new byte[BlockSize];
		}


		/// <summary>The size in bytes of an individual block.</summary>
		public int BlockSize
		{
			get { return i_InputBlockSize; }
		}

		/// <summary>The number of bytes currently in the buffer waiting to be processed.</summary>
		public int BufferFill
		{
			get { return i_PartialBlockFill; }
		}

		
		/// <summary>Performs the hash algorithm on the data provided.</summary>
		/// <param name="array">The array containing the data.</param>
		/// <param name="ibStart">The position in the array to begin reading from.</param>
		/// <param name="cbSize">How many bytes in the array to read.</param>
		protected override void HashCore(byte[] array,int ibStart,int cbSize)
		{
			int i;

			// Use what may already be in the buffer.
			if(BufferFill > 0)
			{
				if(cbSize+BufferFill < BlockSize)
				{
					// Still don't have enough for a full block, just store it.
					Array.Copy(array,ibStart,ba_PartialBlockBuffer,BufferFill,cbSize);
					i_PartialBlockFill += cbSize;
					return;
				}
				else
				{
					// Fill out the buffer to make a full block, and then process it.
					i = BlockSize - BufferFill;
					Array.Copy(array,ibStart,ba_PartialBlockBuffer,BufferFill,i);
					ProcessBlock(ba_PartialBlockBuffer,0,1); l_TotalBytesProcessed += BlockSize;
					i_PartialBlockFill = 0; ibStart += i; cbSize -= i;
				}
			}

			// For as long as we have full blocks, process them.
			if(cbSize>=BlockSize)
			{	ProcessBlock(array,ibStart,cbSize/BlockSize);
				l_TotalBytesProcessed+=cbSize-cbSize%BlockSize;
			}
			/*for(i=0; i < (cbSize-cbSize%BlockSize); i+=BlockSize)
			{	ProcessBlock(array,ibStart + i,1);
				count += BlockSize;
			}*/

			// If we still have some bytes left, store them for later.
			int bytesLeft = cbSize % BlockSize;
			if(bytesLeft != 0)
			{
				Array.Copy(array,((cbSize - bytesLeft) + ibStart),ba_PartialBlockBuffer,0,bytesLeft);
				i_PartialBlockFill = bytesLeft;
			}
		}


		/// <summary>Performs any final activities required by the hash algorithm.</summary>
		/// <returns>The final hash value.</returns>
		protected override byte[] HashFinal()
		{
			return ProcessFinalBlock(ba_PartialBlockBuffer,0,i_PartialBlockFill);
		}


		/// <summary>Process a block of data.</summary>
		/// <param name="inputBuffer">The block of data to process.</param>
		/// <param name="inputOffset">Where to start in the block.</param>
		protected abstract void ProcessBlock(byte[] inputBuffer,int inputOffset,int inputLength);


		/// <summary>Process the last block of data.</summary>
		/// <param name="inputBuffer">The block of data to process.</param>
		/// <param name="inputOffset">Where to start in the block.</param>
		/// <param name="inputCount">How many bytes need to be processed.</param>
		/// <returns>The results of the completed hash calculation.</returns>
		protected abstract byte[] ProcessFinalBlock(byte[] inputBuffer,int inputOffset,int inputCount);
	}

	public partial class Tiger:BlockHashAlgorithm
	{
		// registers
		private ulong[] accu, x;

		public Tiger():base(64,192)
		{	
            Initialize();
		}

		public override void Initialize()
		{
			base.Initialize();

			accu=new[] { 0x0123456789ABCDEFUL, 0xFEDCBA9876543210UL, 0xF096A5B4C3B2E187UL };

			if(x==null) x=new ulong[8];
			else Array.Resize(ref x,8);
			Array.Clear(x,0,8);
		}

		protected override void ProcessBlock(byte[] inputBuffer,int inputOffset,int iBlkCount)
		{	
            ulong a=accu[0], b=accu[1], c=accu[2],
			      x0, x1, x2, x3, x4, x5, x6, x7;

            int i;
			
			for(i=-1;iBlkCount>0;--iBlkCount,inputOffset+=i_InputBlockSize)
			{
                x0 = BitConverter.ToUInt64(inputBuffer, ++i * 8);
                x1 = BitConverter.ToUInt64(inputBuffer, ++i * 8);
                x2 = BitConverter.ToUInt64(inputBuffer, ++i * 8);
                x3 = BitConverter.ToUInt64(inputBuffer, ++i * 8);
                x4 = BitConverter.ToUInt64(inputBuffer, ++i * 8);
                x5 = BitConverter.ToUInt64(inputBuffer, ++i * 8);
                x6 = BitConverter.ToUInt64(inputBuffer, ++i * 8);
                x7 = BitConverter.ToUInt64(inputBuffer, ++i * 8);

				// rounds and schedule
				c^=x0;
                var zh = (uint)(c >> 32);
                var zl = (uint)c;
                a -= t1[(byte)zl] ^ t2[(byte)(zl >> 16)] ^ t3[(byte)zh] ^ t4[(byte)(zh >> 16)];
                b += t4[(byte)(zl >> 8)] ^ t3[(byte)(zl >> 24)] ^ t2[(byte)(zh >> 8)] ^ t1[(byte)(zh >> 24)];
                b*=5;

				a^=x1;
                zh = (uint)(a >> 32);
                zl = (uint)a;
                b -= t1[(byte)zl] ^ t2[(byte)(zl >> 16)] ^ t3[(byte)zh] ^ t4[(byte)(zh >> 16)];
                c += t4[(byte)(zl >> 8)] ^ t3[(byte)(zl >> 24)] ^ t2[(byte)(zh >> 8)] ^ t1[(byte)(zh >> 24)];
                c*=5;

				b^=x2;
                zh = (uint)(b >> 32);
                zl = (uint)b;
                c -= t1[(byte)zl] ^ t2[(byte)(zl >> 16)] ^ t3[(byte)zh] ^ t4[(byte)(zh >> 16)];
                a += t4[(byte)(zl >> 8)] ^ t3[(byte)(zl >> 24)] ^ t2[(byte)(zh >> 8)] ^ t1[(byte)(zh >> 24)];
                a*=5;

				c^=x3;
                zh = (uint)(c >> 32);
                zl = (uint)c;
                a -= t1[(byte)zl] ^ t2[(byte)(zl >> 16)] ^ t3[(byte)zh] ^ t4[(byte)(zh >> 16)];
                b += t4[(byte)(zl >> 8)] ^ t3[(byte)(zl >> 24)] ^ t2[(byte)(zh >> 8)] ^ t1[(byte)(zh >> 24)];
                b*=5;

				a^=x4;
                zh = (uint)(a >> 32);
                zl = (uint)a;
                b -= t1[(byte)zl] ^ t2[(byte)(zl >> 16)] ^ t3[(byte)zh] ^ t4[(byte)(zh >> 16)];
                c += t4[(byte)(zl >> 8)] ^ t3[(byte)(zl >> 24)] ^ t2[(byte)(zh >> 8)] ^ t1[(byte)(zh >> 24)];
                c*=5;

				b^=x5;
                zh = (uint)(b >> 32);
                zl = (uint)b;
                c -= t1[(byte)zl] ^ t2[(byte)(zl >> 16)] ^ t3[(byte)zh] ^ t4[(byte)(zh >> 16)];
                a += t4[(byte)(zl >> 8)] ^ t3[(byte)(zl >> 24)] ^ t2[(byte)(zh >> 8)] ^ t1[(byte)(zh >> 24)];
                a*=5;

				c^=x6;
                zh = (uint)(c >> 32);
                zl = (uint)c;
                a -= t1[(byte)zl] ^ t2[(byte)(zl >> 16)] ^ t3[(byte)zh] ^ t4[(byte)(zh >> 16)];
                b += t4[(byte)(zl >> 8)] ^ t3[(byte)(zl >> 24)] ^ t2[(byte)(zh >> 8)] ^ t1[(byte)(zh >> 24)];
                b*=5;

				a^=x7;
                zh = (uint)(a >> 32);
                zl = (uint)a;
                b -= t1[(byte)zl] ^ t2[(byte)(zl >> 16)] ^ t3[(byte)zh] ^ t4[(byte)(zh >> 16)];
                c += t4[(byte)(zl >> 8)] ^ t3[(byte)(zl >> 24)] ^ t2[(byte)(zh >> 8)] ^ t1[(byte)(zh >> 24)];
                c*=5;

                x0 -= x7 ^ 0xA5A5A5A5A5A5A5A5UL;
                x1 ^= x0;
                x2 += x1;
                x3 -= x2 ^ ((~x1) << 19);
                x4 ^= x3;
                x5 += x4;
                x6 -= x5 ^ (~x4 >> 23);
                x7 ^= x6;
                x0 += x7;
                x1 -= x0 ^ ((~x7) << 19);
                x2 ^= x1;
                x3 += x2;
                x4 -= x3 ^ (~x2 >> 23);
                x5 ^= x4;
                x6 += x5;
                x7 -= x6 ^ 0x0123456789ABCDEFUL;


				b^=x0;
                zh = (uint)(b >> 32);
                zl = (uint)b;
                c -= t1[(byte)zl] ^ t2[(byte)(zl >> 16)] ^ t3[(byte)zh] ^ t4[(byte)(zh >> 16)];
                a += t4[(byte)(zl >> 8)] ^ t3[(byte)(zl >> 24)] ^ t2[(byte)(zh >> 8)] ^ t1[(byte)(zh >> 24)];
                a*=7;

				c^=x1;
                zh = (uint)(c >> 32);
                zl = (uint)c;
                a -= t1[(byte)zl] ^ t2[(byte)(zl >> 16)] ^ t3[(byte)zh] ^ t4[(byte)(zh >> 16)];
                b += t4[(byte)(zl >> 8)] ^ t3[(byte)(zl >> 24)] ^ t2[(byte)(zh >> 8)] ^ t1[(byte)(zh >> 24)];
                b*=7;

				a^=x2;
                zh = (uint)(a >> 32);
                zl = (uint)a;
                b -= t1[(byte)zl] ^ t2[(byte)(zl >> 16)] ^ t3[(byte)zh] ^ t4[(byte)(zh >> 16)];
                c += t4[(byte)(zl >> 8)] ^ t3[(byte)(zl >> 24)] ^ t2[(byte)(zh >> 8)] ^ t1[(byte)(zh >> 24)];
                c*=7;

				b^=x3;
                zh = (uint)(b >> 32);
                zl = (uint)b;
                c -= t1[(byte)zl] ^ t2[(byte)(zl >> 16)] ^ t3[(byte)zh] ^ t4[(byte)(zh >> 16)];
                a += t4[(byte)(zl >> 8)] ^ t3[(byte)(zl >> 24)] ^ t2[(byte)(zh >> 8)] ^ t1[(byte)(zh >> 24)];
                a*=7;

				c^=x4;
                zh = (uint)(c >> 32);
                zl = (uint)c;
                a -= t1[(byte)zl] ^ t2[(byte)(zl >> 16)] ^ t3[(byte)zh] ^ t4[(byte)(zh >> 16)];
                b += t4[(byte)(zl >> 8)] ^ t3[(byte)(zl >> 24)] ^ t2[(byte)(zh >> 8)] ^ t1[(byte)(zh >> 24)];
                b*=7;

				a^=x5;
                zh = (uint)(a >> 32);
                zl = (uint)a;
                b -= t1[(byte)zl] ^ t2[(byte)(zl >> 16)] ^ t3[(byte)zh] ^ t4[(byte)(zh >> 16)];
                c += t4[(byte)(zl >> 8)] ^ t3[(byte)(zl >> 24)] ^ t2[(byte)(zh >> 8)] ^ t1[(byte)(zh >> 24)];
                c*=7;

				b^=x6;
                zh = (uint)(b >> 32);
                zl = (uint)b;
                c -= t1[(byte)zl] ^ t2[(byte)(zl >> 16)] ^ t3[(byte)zh] ^ t4[(byte)(zh >> 16)];
                a += t4[(byte)(zl >> 8)] ^ t3[(byte)(zl >> 24)] ^ t2[(byte)(zh >> 8)] ^ t1[(byte)(zh >> 24)];
                a*=7;

				c^=x7;
                zh = (uint)(c >> 32);
                zl = (uint)c;
                a -= t1[(byte)zl] ^ t2[(byte)(zl >> 16)] ^ t3[(byte)zh] ^ t4[(byte)(zh >> 16)];
                b += t4[(byte)(zl >> 8)] ^ t3[(byte)(zl >> 24)] ^ t2[(byte)(zh >> 8)] ^ t1[(byte)(zh >> 24)];
                b*=7;

                x0 -= x7 ^ 0xA5A5A5A5A5A5A5A5UL;
                x1 ^= x0;
                x2 += x1;
                x3 -= x2 ^ ((~x1) << 19);
                x4 ^= x3;
                x5 += x4;
                x6 -= x5 ^ (~x4 >> 23);
                x7 ^= x6;
                x0 += x7;
                x1 -= x0 ^ ((~x7) << 19);
                x2 ^= x1;
                x3 += x2;
                x4 -= x3 ^ (~x2 >> 23);
                x5 ^= x4;
                x6 += x5;
                x7 -= x6 ^ 0x0123456789ABCDEFUL;

				a^=x0;
                zh = (uint)(a >> 32);
                zl = (uint)a;
                b -= t1[(byte)zl] ^ t2[(byte)(zl >> 16)] ^ t3[(byte)zh] ^ t4[(byte)(zh >> 16)];
                c += t4[(byte)(zl >> 8)] ^ t3[(byte)(zl >> 24)] ^ t2[(byte)(zh >> 8)] ^ t1[(byte)(zh >> 24)];
                c*=9;

				b^=x1;
                zh = (uint)(b >> 32);
                zl = (uint)b;
                c -= t1[(byte)zl] ^ t2[(byte)(zl >> 16)] ^ t3[(byte)zh] ^ t4[(byte)(zh >> 16)];
                a += t4[(byte)(zl >> 8)] ^ t3[(byte)(zl >> 24)] ^ t2[(byte)(zh >> 8)] ^ t1[(byte)(zh >> 24)];
                a*=9;

				c^=x2;
                zh = (uint)(c >> 32);
                zl = (uint)c;
                a -= t1[(byte)zl] ^ t2[(byte)(zl >> 16)] ^ t3[(byte)zh] ^ t4[(byte)(zh >> 16)];
                b += t4[(byte)(zl >> 8)] ^ t3[(byte)(zl >> 24)] ^ t2[(byte)(zh >> 8)] ^ t1[(byte)(zh >> 24)];
                b*=9;

				a^=x3;
                zh = (uint)(a >> 32);
                zl = (uint)a;
                b -= t1[(byte)zl] ^ t2[(byte)(zl >> 16)] ^ t3[(byte)zh] ^ t4[(byte)(zh >> 16)];
                c += t4[(byte)(zl >> 8)] ^ t3[(byte)(zl >> 24)] ^ t2[(byte)(zh >> 8)] ^ t1[(byte)(zh >> 24)];
                c*=9;

				b^=x4;
                zh = (uint)(b >> 32);
                zl = (uint)b;
                c -= t1[(byte)zl] ^ t2[(byte)(zl >> 16)] ^ t3[(byte)zh] ^ t4[(byte)(zh >> 16)];
                a += t4[(byte)(zl >> 8)] ^ t3[(byte)(zl >> 24)] ^ t2[(byte)(zh >> 8)] ^ t1[(byte)(zh >> 24)];
                a*=9;

				c^=x5;
                zh = (uint)(c >> 32);
                zl = (uint)c;
                a -= t1[(byte)zl] ^ t2[(byte)(zl >> 16)] ^ t3[(byte)zh] ^ t4[(byte)(zh >> 16)];
                b += t4[(byte)(zl >> 8)] ^ t3[(byte)(zl >> 24)] ^ t2[(byte)(zh >> 8)] ^ t1[(byte)(zh >> 24)];
                b*=9;

				a^=x6;
                zh = (uint)(a >> 32);
                zl = (uint)a;
                b -= t1[(byte)zl] ^ t2[(byte)(zl >> 16)] ^ t3[(byte)zh] ^ t4[(byte)(zh >> 16)];
                c += t4[(byte)(zl >> 8)] ^ t3[(byte)(zl >> 24)] ^ t2[(byte)(zh >> 8)] ^ t1[(byte)(zh >> 24)];
                c*=9;

				b^=x7;
                zh = (uint)(b >> 32);
                zl = (uint)b;
                c -= t1[(byte)zl] ^ t2[(byte)(zl >> 16)] ^ t3[(byte)zh] ^ t4[(byte)(zh >> 16)];
                a += t4[(byte)(zl >> 8)] ^ t3[(byte)(zl >> 24)] ^ t2[(byte)(zh >> 8)] ^ t1[(byte)(zh >> 24)];
                a*=9;

				// feed forward
				a=accu[0]^=a; b-=accu[1]; accu[1]=b; c=accu[2]+=c;
			}
		}

		protected override byte[] ProcessFinalBlock(byte[] inputBuffer,int inputOffset,int inputCount)
		{	int paddingSize;

			// Figure out how much padding is needed between the last byte and the size.
			paddingSize = (int)(((ulong)inputCount + (ulong)l_TotalBytesProcessed) % (ulong)BlockSize);
			paddingSize = (BlockSize - 8) - paddingSize;
			if(paddingSize < 1) { paddingSize += BlockSize; }

			// Create the final, padded block(s).
			if(inputOffset>0&&inputCount>0) Array.Copy(inputBuffer,inputOffset,inputBuffer,0,inputCount);
			inputOffset=0;

			Array.Clear(inputBuffer,inputCount,BlockSize-inputCount);
			inputBuffer[inputCount] = 0x01; //0x80;
			ulong msg_bit_length = ((ulong)l_TotalBytesProcessed + (ulong)inputCount)<<3;

			if(inputCount+8 >= BlockSize)
			{
				if(inputBuffer.Length < 2*BlockSize) Array.Resize(ref inputBuffer,2*BlockSize);
				ProcessBlock(inputBuffer,inputOffset,1);
				inputOffset+=BlockSize; inputCount-=BlockSize;
			}

			for(inputCount=inputOffset+BlockSize-sizeof(ulong);msg_bit_length!=0;
					inputBuffer[inputCount]=(byte)msg_bit_length,msg_bit_length>>=8,++inputCount) ;
			ProcessBlock(inputBuffer,inputOffset,1);


			HashValue=new byte[HashSizeValue/8];
            Buffer.BlockCopy(accu, 0, HashValue, 0, 3 * sizeof(ulong));
            return HashValue;
		}

	}

}
