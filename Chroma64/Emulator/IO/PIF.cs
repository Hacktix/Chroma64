using Chroma64.Emulator.Input;
using Chroma64.Emulator.Memory;
using System;

namespace Chroma64.Emulator.IO
{
    class PIF : BigEndianMemory
    {
        public ControllerDevice[] Controllers = new ControllerDevice[4];

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

                    if (Controllers[channel] == null)
                    {
                        for (; r > 0; r--)
                            Bytes[--i] = 0;
                    }
                    else
                    {
                        byte[] res = Controllers[channel].ExecPIF(cmdBuf);
                        for (int j = 0; j < res.Length; j++)
                            Bytes[--i] = res[j];
                    }

                    channel++;
                }
                else if (t == 0xFE)
                    break;
            }
        }
    }
}
