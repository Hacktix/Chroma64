using Chroma64.Util;
using System.Runtime.InteropServices;

namespace Chroma64.Emulator.CPU
{
    public enum RoundMode
    {
        /// <summary>
        /// Rounding towards nearest representable value.
        /// </summary>
        FE_TONEAREST = 0x00000000,

        /// <summary>
        /// Rounding towards negative infinity.
        /// </summary>
        FE_DOWNWARD = 0x00000100,

        /// <summary>
        /// Rounding towards positive infinity.
        /// </summary>
        FE_UPWARD = 0x00000200,

        /// <summary>
        /// Rounding towards zero.
        /// </summary>
        FE_TOWARDZERO = 0x00000300,
    }

    class COP1
    {
        private int fcr31 = 0;
        public int FCR31
        {
            get { return fcr31; }
            set
            {
                fcr31 = value;

                // Set Rounding Mode
                switch (value & 0b11)
                {
                    case 0b00:
                        SetRound(RoundMode.FE_TONEAREST);
                        break;
                    case 0b01:
                        SetRound(RoundMode.FE_TOWARDZERO);
                        break;
                    case 0b10:
                        SetRound(RoundMode.FE_UPWARD);
                        break;
                    case 0b11:
                        SetRound(RoundMode.FE_DOWNWARD);
                        break;
                }
            }
        }

        private byte[] bytes = new byte[32 * 8];
        private MainCPU parent;

        public COP1(MainCPU parent)
        {
            this.parent = parent;
        }

        #region Rounding Mode Functions
        [DllImport("ucrtbase.dll", EntryPoint = "fegetround", CallingConvention = CallingConvention.Cdecl)]
        public static extern RoundMode GetRound();

        [DllImport("ucrtbase.dll", EntryPoint = "fesetround", CallingConvention = CallingConvention.Cdecl)]
        public static extern int SetRound(RoundMode roundingMode);
        #endregion

        public unsafe void SetFGR<T>(int index, T value) where T : unmanaged
        {
            fixed (byte* ptr = bytes)
            {
                if ((parent.COP0.GetReg(COP0REG.Status) & (1 << 26)) == 0)
                {
                    ulong val = sizeof(T) == 8 ? (*(ulong*)&value) : (*(uint*)&value);
                    uint hi = (uint)((val & 0xFFFFFFFF00000000) >> 32);
                    uint lo = (uint)(val & 0xFFFFFFFF);
                    *(uint*)(ptr + 8 * index) = lo;
                    *(uint*)(ptr + 8 * (index + 1)) = hi;
                }
                else
                {
                    if (sizeof(T) == 4)
                    {
                        uint val = *(uint*)&value;
                        *(uint*)(ptr + 8 * index) = val;
                    }
                    else
                    {
                        ulong val = *(ulong*)&value;
                        *(ulong*)(ptr + 8 * index) = val;
                    }
                }
            }
        }

        public unsafe T GetFGR<T>(int index) where T : unmanaged
        {
            fixed (byte* ptr = bytes)
            {
                if ((parent.COP0.GetReg(COP0REG.Status) & (1 << 26)) == 0)
                {
                    uint lo = *(uint*)(ptr + 8 * index);
                    uint hi = *(uint*)(ptr + 8 * (index + 1));
                    ulong val = lo | (((ulong)hi) << 32);
                    return *(T*)&val;
                }
                else
                    return *(T*)(ptr + 8 * index);
            }
        }

        #region CVT Instructions

        // CVT.D.fmt
        public void CVT_D_S(int src, int dest) { SetFGR(dest, (double)GetFGR<float>(src)); }
        public void CVT_D_W(int src, int dest) { SetFGR(dest, (double)GetFGR<int>(src)); }
        public void CVT_D_L(int src, int dest) { SetFGR(dest, (double)GetFGR<long>(src)); }

        // CVT.S.fmt
        public void CVT_S_D(int src, int dest) { SetFGR(dest, (float)GetFGR<double>(src)); }
        public void CVT_S_W(int src, int dest) { SetFGR(dest, (float)GetFGR<int>(src)); }
        public void CVT_S_L(int src, int dest) { SetFGR(dest, (float)GetFGR<long>(src)); }

        // CVT.W.fmt
        public void CVT_W_D(int src, int dest) { SetFGR(dest, (int)GetFGR<double>(src)); }
        public void CVT_W_S(int src, int dest) { SetFGR(dest, (int)GetFGR<float>(src)); }

        // CVT.L.fmt
        public void CVT_L_D(int src, int dest) { SetFGR(dest, (long)GetFGR<double>(src)); }
        public void CVT_L_S(int src, int dest) { SetFGR(dest, (long)GetFGR<float>(src)); }

        #endregion
    }
}
