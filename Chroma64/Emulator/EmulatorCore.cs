using Chroma64.Emulator.Memory;
using Chroma64.Emulator.CPU;
using Chroma.Graphics;
using Chroma64.Util;

namespace Chroma64.Emulator
{
    class EmulatorCore
    {
        private static readonly int TICKS_PER_FRAME = 1562500;

        private ROM rom;
        private MemoryBus bus;
        private MainCPU cpu;

        public EmulatorCore(string romPath)
        {
            rom = new ROM(romPath);
            bus = new MemoryBus(rom);
            cpu = new MainCPU(bus);
        }

        public void TickFrame()
        {
            cpu.Tick(TICKS_PER_FRAME);

            // VI Interrupt
            if(bus.VI.NeedsRender())
            {
                bus.MI.SetRegister(IO.MI.INTR_REG, bus.MI.GetRegister(IO.MI.INTR_REG) | 0b1000);
            }

            // TODO: Tick components here
        }

        public bool NeedsRender()
        {
            return bus.VI.NeedsRender();
        }

        public void SetFramebufferTexture(ref Texture tex)
        {
            bus.VI.SetFramebuffer(ref tex);
        }
    }
}
