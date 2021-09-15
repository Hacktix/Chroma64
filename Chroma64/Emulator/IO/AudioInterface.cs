using Chroma.Audio;
using Chroma.Audio.Sources;
using Chroma64.Emulator.Memory;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Chroma64.Emulator.IO
{
    public enum AI
    {
        BASE_REG = 0x00,
        DRAM_ADDR_REG = 0x00,
        LEN_REG = 0x04,
        CONTROL_REG = 0x08,
        STATUS_REG = 0x0C,
        DACRATE_REG = 0x10,
        BITRATE_REG = 0x14,
    }

    class AudioInterface : BigEndianMemory
    {
        private Waveform wave;
        private MemoryBus bus;

        private int dmaCount = 0;
        private uint[] dmaAddr = new uint[2];
        private int[] dmaLen = new int[2];

        public AudioInterface(MemoryBus bus) : base(0x18)
        {
            this.bus = bus;
            wave = new Waveform(new AudioFormat(SampleFormat.S16, ByteOrder.BigEndian), PushSamples);
            wave.Play();
        }

        private void PushSamples(Span<byte> buffer, AudioFormat format)
        {
            buffer.Clear();

            if (dmaCount > 0)
            {
                int len = Math.Min(buffer.Length, dmaLen[0]);
                byte[] data = new byte[len];
                Array.Copy(bus.RDRAM.Bytes, bus.RDRAM.Bytes.Length - (int)dmaAddr[0] - len, data, 0, len);
                Array.Reverse(data);
                data.AsSpan().CopyTo(buffer);

                if (len < buffer.Length)
                {
                    // Very hacky sound stretching, *kinda* works but not really
                    Span<short> samples = MemoryMarshal.Cast<byte, short>(data.AsSpan());
                    Span<short> buf = MemoryMarshal.Cast<byte, short>(buffer);

                    int diff = buf.Length - samples.Length;
                    int doubleCounter = (int)Math.Ceiling((float)buf.Length / diff) - 1;
                    int realCounter = doubleCounter;
                    for (int i = 0, j = 0; i < buf.Length; i++)
                    {
                        buf[i] = samples[Math.Clamp(j, 0, samples.Length - 1)];
                        if ((--realCounter) < 0)
                            realCounter = doubleCounter;
                        else
                            j++;
                    }
                }

                dmaAddr[0] += (uint)len;
                dmaLen[0] -= len;

                if (dmaLen[0] == 0)
                {
                    bus.MI.SetRegister(MI.INTR_REG, bus.MI.GetRegister(MI.INTR_REG) | 0b100);
                    if (--dmaCount > 0)
                    {
                        dmaAddr[0] = dmaAddr[1];
                        dmaLen[0] = dmaLen[1];
                        SetRegister(AI.STATUS_REG, 0);
                    }
                    else
                        SetRegister(AI.STATUS_REG, 0);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public new T Read<T>(ulong addr) where T : unmanaged
        {
            // Addresses over 0x17 are unused
            if (addr > 0x17)
                return default;

            return base.Read<T>(addr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public new void Write<T>(ulong addr, T val) where T : unmanaged
        {
            if (addr >= (ulong)AI.DRAM_ADDR_REG && addr < (ulong)AI.DRAM_ADDR_REG + 4)
            {
                base.Write(addr, val);
                if (dmaCount < 2)
                    dmaAddr[dmaCount] = GetRegister(AI.DRAM_ADDR_REG);
                return;
            }

            if (addr >= (ulong)AI.LEN_REG && addr < (ulong)AI.LEN_REG + 4)
            {
                base.Write(addr, val);
                uint len = GetRegister(AI.LEN_REG) & 0x3FFF8;
                if (dmaCount < 2 && len > 0)
                {
                    dmaLen[dmaCount++] = (int)len;
                    SetRegister(AI.STATUS_REG, dmaCount > 1 ? 0xC1100001 : 0);
                }
                return;
            }

            if (addr >= (ulong)AI.DACRATE_REG && addr < (ulong)AI.DACRATE_REG + 4)
            {
                base.Write(addr, val);
                int dacrate = (int)(48681812 / 4 / ((GetRegister(AI.DACRATE_REG) & 0x3FFF) + 1));
                GameCore.AudioOut.Close();
                GameCore.AudioOut.Open(null, 48000, dacrate);
                wave = new Waveform(new AudioFormat(SampleFormat.S16, ByteOrder.BigEndian), PushSamples, ChannelMode.Stereo, 48000);
                wave.Play();
                return;
            }

            if (addr >= (ulong)AI.STATUS_REG && addr < (ulong)AI.STATUS_REG + 4)
            {
                bus.MI.SetRegister(MI.INTR_REG, (uint)(bus.MI.GetRegister(MI.INTR_REG) & ~0b100));
                return;
            }

            // Addresses over 0x17 are unused
            if (addr > 0x17)
                return;

            base.Write<T>(addr, val);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetRegister(AI reg)
        {
            return base.Read<uint>((ulong)reg);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetRegister(AI reg, uint value)
        {
            base.Write((ulong)reg, value);
        }
    }
}
