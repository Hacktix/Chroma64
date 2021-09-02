using Chroma64.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chroma64.Emulator.Memory
{
    unsafe class ROM : BigEndianMemory
    {
        public ROM(string filePath) : base(File.ReadAllBytes(filePath))
        {
            fixed (byte* romPtr = bytes)
            {
                // Get 32 bit identifier and re-order bytes depending on value
                uint formatIdent = *(uint*)romPtr;
                switch (formatIdent)
                {
                    // Native big endian format (ABCD)
                    case 0x40123780:
                        Log.Info("Format: Native");
                        break;

                    // Byte-swapped format (BADC)
                    case 0x12408037:
                        for (int i = 0; i < bytes.Length; i += 2)
                        {
                            byte tmp = romPtr[i];
                            romPtr[i] = romPtr[i + 1];
                            romPtr[i + 1] = tmp;
                        }
                        Log.Info("Format: Byte-swapped");
                        break;

                    // Little endian format (DCBA)
                    case 0x80371240:
                        for (int i = 0; i < bytes.Length; i += 4)
                        {
                            byte tmp = romPtr[i];
                            romPtr[i] = romPtr[i + 3];
                            romPtr[i + 3] = tmp;
                            tmp = romPtr[i + 2];
                            romPtr[i + 2] = romPtr[i + 1];
                            romPtr[i + 1] = tmp;
                        }
                        Log.Info("Format: Little-endian");
                        break;

                    // Some other unknown format or a file that's not a ROM, error
                    default:
                        Log.Error("Unknown ROM format");
                        break;
                }

                // Reverse ROM byte array for BE-LE Mapping
                Array.Reverse(bytes);

                // Extra validation + logging
                Log.Info($"Header: 0x{Read<uint>(0).ToString("X4")}");
                if ((*(uint*)(romPtr + bytes.Length - 4)) != 0x80371240)
                    Log.FatalError("Invalid ROM File!");

                Log.Info($"CRC1: 0x{Read<uint>(0x10):X8}");
                Log.Info($"CRC2: 0x{Read<uint>(0x14):X8}");
            }
        }
    }
}
