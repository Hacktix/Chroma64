using Chroma.Graphics;
using Chroma64.Emulator.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chroma64.Emulator.IO
{
    public enum VI
    {
        BASE_REG = 0x00, STATUS_REG = 0x00, CONTROL_REG = 0x00,
        ORIGIN_REG = 0x04, DRAM_ADDR_REG = 0x04,
        WIDTH_REG = 0x08, H_WIDTH_REG = 0x08,
        INTR_REG = 0x0C, V_INTR_REG = 0x0C,
        CURRENT_REG = 0x10, V_CURRENT_LINE_REG = 0x10,
        BURST_REG = 0x14, TIMING_REG = 0x14,
        V_SYNC_REG = 0x18,
        H_SYNC_REG = 0x1C,
        LEAP_REG = 0x20, H_SYNC_LEAP_REG = 0x20,
        H_START_REG = 0x24, H_VIDEO_REG = 0x24,
        V_START_REG = 0x28, V_VIDEO_REG = 0x28,
        V_BURST_REG = 0x2C,
        X_SCALE_REG = 0x30,
        Y_SCALE_REG = 0x34,
    }

    class VideoInterface : BigEndianMemory
    {
        private MemoryBus bus;

        public VideoInterface(MemoryBus bus) : base(0x38)
        {
            this.bus = bus;
        }

        public new T Read<T>(ulong addr) where T : unmanaged
        {
            // Addresses over 0x37 are unused
            if (addr > 0x37)
                return default;

            return base.Read<T>(addr);
        }

        public new void Write<T>(ulong addr, T val) where T : unmanaged
        {
            // Addresses over 0x37 are unused
            if (addr > 0x37)
                return;

            base.Write<T>(addr, val);
        }

        public uint GetRegister(VI reg)
        {
            return base.Read<uint>((ulong)reg);
        }

        public void SetRegister(VI reg, uint value)
        {
            base.Write((ulong)reg, value);
        }

        public bool NeedsRender()
        {
            return (GetRegister(VI.CONTROL_REG) & 0b11) == 3;
        }

        public void SetFramebuffer(ref Texture tex)
        {
            int width = (int)(GetRegister(VI.WIDTH_REG) & 0xFFF);
            int yScale = (int)GetRegister(VI.Y_SCALE_REG);
            int height = ((15 * yScale) / 64);

            int origin = (int)(GetRegister(VI.DRAM_ADDR_REG) & 0x3FFFFF);
            byte[] data = new byte[width * height * 4];
            Array.Copy(bus.RDRAM.Bytes, bus.RDRAM.Bytes.Length - origin - data.Length, data, 0, data.Length);
            Array.Reverse(data);

            int cnt = 0;
            foreach (byte b in data)
                cnt += b != 0 ? 1 : 0;

            if (tex == null || tex.Width != width || tex.Height != height)
                tex = new Texture(width, height, PixelFormat.RGBA);
            tex.SetPixelData(data);
        }
    }
}
