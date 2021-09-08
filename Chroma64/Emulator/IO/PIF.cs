using Chroma64.Emulator.Memory;
using Chroma64.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chroma64.Emulator.IO
{
    class PIF : BigEndianMemory
    {
        public PIF() : base(0x40) { }

        public void Exec()
        {
            if (Bytes[0] == 0)
                return;

            byte t;
            byte r;
            int channel = 0;
            for (int i = 0x3F; i > 0; i--)
            {
                t = Bytes[i];
                if (t == 0)
                    channel++;
                else if ((t & 0x80) == 0)
                {
                    r = Bytes[--i];

                    byte[] cmdBuf = new byte[t];
                    Array.Copy(Bytes, i - t, cmdBuf, 0, t);
                    Array.Reverse(cmdBuf);
                    i -= t;

                    switch (cmdBuf[0])
                    {
                        // Info Command
                        case 0x00:
                            if (channel == 0)
                            {
                                Bytes[--i] = 0x05;
                                Bytes[--i] = 0x00;
                                Bytes[--i] = 0x01;
                            }
                            else
                            {
                                Bytes[--i] = 0x00;
                                Bytes[--i] = 0x00;
                                Bytes[--i] = 0x00;
                            }
                            break;

                        default:
                            Log.FatalError($"Unimplemented PIF Command 0x{cmdBuf[0]:X2} [t = {t} | r = {r} | {cmdBuf.Length} byte(s)]");
                            break;
                    }

                    channel++;
                }
                else if (t == 0xFE)
                    break;
            }
        }
    }
}
