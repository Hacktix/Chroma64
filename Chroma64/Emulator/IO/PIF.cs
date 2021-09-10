using Chroma.Diagnostics.Logging;
using Chroma64.Emulator.Memory;
using System;

namespace Chroma64.Emulator.IO
{
    public enum ControllerButton
    {
        A, B, Z, Start, Up, Down, Left, Right, LT, RT, UpC, DownC, LeftC, RightC
    }

    class PIF : BigEndianMemory
    {
        private Log log = LogManager.GetForCurrentAssembly();

        public bool[] ControllerState = new bool[14];

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

                        // TODO: Controller State
                        case 0x01:
                            Bytes[--i] = (byte)(
                                (ControllerState[(int)ControllerButton.A] ? 0x80 : 0) |
                                (ControllerState[(int)ControllerButton.B] ? 0x40 : 0) |
                                (ControllerState[(int)ControllerButton.Z] ? 0x20 : 0) |
                                (ControllerState[(int)ControllerButton.Start] ? 0x10 : 0) |
                                (ControllerState[(int)ControllerButton.Up] ? 0x08 : 0) |
                                (ControllerState[(int)ControllerButton.Down] ? 0x04 : 0) |
                                (ControllerState[(int)ControllerButton.Left] ? 0x02 : 0) |
                                (ControllerState[(int)ControllerButton.Right] ? 0x01 : 0)
                            );
                            Bytes[--i] = (byte)(
                                (ControllerState[(int)ControllerButton.UpC] ? 0x08 : 0) |
                                (ControllerState[(int)ControllerButton.DownC] ? 0x04 : 0) |
                                (ControllerState[(int)ControllerButton.LeftC] ? 0x02 : 0) |
                                (ControllerState[(int)ControllerButton.RightC] ? 0x01 : 0)
                            );
                            Bytes[--i] = 0x00;
                            Bytes[--i] = 0x00;
                            break;

                        // TODO: Mempack R/W 
                        case 0x02:
                        case 0x03:
                            i -= r;
                            break;

                        default:
                            log.Error($"Unimplemented PIF Command 0x{cmdBuf[0]:X2} [t = {t} | r = {r} | {cmdBuf.Length} byte(s)]");
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
