using Chroma.Diagnostics.Logging;
using System;
using System.IO;

namespace Chroma64.Emulator.Memory
{
    unsafe class ROM : BigEndianMemory
    {
        private Log log = LogManager.GetForCurrentAssembly();

        public ROM(string filePath) : base(File.ReadAllBytes(filePath))
        {
            fixed (byte* romPtr = Bytes)
            {
                // Get 32 bit identifier and re-order bytes depending on value
                uint formatIdent = *(uint*)romPtr;
                switch (formatIdent)
                {
                    // Native big endian format (ABCD)
                    case 0x40123780:
                        log.Info("Format: Native");
                        break;

                    // Byte-swapped format (BADC)
                    case 0x12408037:
                        for (int i = 0; i < Bytes.Length; i += 2)
                        {
                            byte tmp = romPtr[i];
                            romPtr[i] = romPtr[i + 1];
                            romPtr[i + 1] = tmp;
                        }
                        log.Info("Format: Byte-swapped");
                        break;

                    // Little endian format (DCBA)
                    case 0x80371240:
                        for (int i = 0; i < Bytes.Length; i += 4)
                        {
                            byte tmp = romPtr[i];
                            romPtr[i] = romPtr[i + 3];
                            romPtr[i + 3] = tmp;
                            tmp = romPtr[i + 2];
                            romPtr[i + 2] = romPtr[i + 1];
                            romPtr[i + 1] = tmp;
                        }
                        log.Info("Format: Little-endian");
                        break;

                    // Some other unknown format or a file that's not a ROM, error
                    default:
                        log.Error("Unknown ROM format");
                        break;
                }

                // Reverse ROM byte array for BE-LE Mapping
                Array.Reverse(Bytes);

                // Extra validation + logging
                log.Info($"Header: 0x{Read<uint>(0).ToString("X4")}");
                if ((*(uint*)(romPtr + Bytes.Length - 4)) != 0x80371240)
                    log.Error("Invalid ROM File!");

                log.Info($"CRC1: 0x{Read<uint>(0x10):X8}");
                log.Info($"CRC2: 0x{Read<uint>(0x14):X8}");
            }
        }
    }
}
