// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2014
// licensed under the LGPL
// -------------------------------------------------------------

using System.Runtime.InteropServices;
using System.IO;

namespace SharpDc.Hash
{
    /// <summary>
    /// Header structure for alternative NTFS file stream
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TthStreamHeader
    {
        public const int G_MAGIC = 724266087;
        public uint magic;
        public uint checksum;  // xor of other TTHStreamHeader DWORDs
        public ulong fileSize;
        public ulong timeStamp;
        public ulong blockSize;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 24)]
        public byte[] root;

        public static TthStreamHeader Read(BinaryReader br)
        {
            var header = new TthStreamHeader();
            header.magic = br.ReadUInt32();
            header.checksum = br.ReadUInt32();
            header.fileSize = br.ReadUInt64();
            header.timeStamp = br.ReadUInt64();
            header.blockSize = br.ReadUInt64();
            header.root = br.ReadBytes(24);
            return header;
        }

        public void Write(BinaryWriter bw)
        {
            bw.Write(G_MAGIC);
            bw.Write(this.checksum);
            bw.Write(this.fileSize);
            bw.Write(this.timeStamp);
            bw.Write(this.blockSize);
            bw.Write(this.root);
            bw.Flush();
        }

        public void SetChecksum()
        {
            //p_header.magic = g_MAGIC;
            //uint32_t l_sum = 0;
            //for (size_t i = 0; i < sizeof(TTHStreamHeader) / sizeof(uint32_t); i++)
            //    l_sum ^= ((uint32_t*)&p_header)[i];
            //p_header.checksum ^= l_sum;
            this.magic = G_MAGIC;
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                Write(bw);
                ms.Position = 0;
                uint checksum = 0;
                using (var br = new BinaryReader(ms))
                {
                    for (var i = 0; i < Marshal.SizeOf(this) / sizeof(int); i++)
                    {
                        checksum ^= br.ReadUInt32();
                    }
                }
                bw.Close();
                this.checksum ^= checksum;
            }
        }

        public bool Validate()
        {
            if (this.magic != G_MAGIC)
                return false;
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                Write(bw);
                ms.Position = 0;
                uint checksum = 0;
                using (var br = new BinaryReader(ms))
                {
                    for (var i = 0; i < Marshal.SizeOf(this) / sizeof(int); i++)
                    {
                        checksum ^= br.ReadUInt32();
                    }
                }
                bw.Close();
                return checksum == 0;
            }
        }
    }
}
