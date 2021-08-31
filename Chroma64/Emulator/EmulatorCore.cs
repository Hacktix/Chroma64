using Chroma64.Emulator.Memory;

namespace Chroma64.Emulator
{
    class EmulatorCore
    {
        private static readonly int TICKS_PER_FRAME = 1562500;

        private ROM _rom;

        public EmulatorCore(string romPath)
        {
            _rom = new ROM(romPath);
        }

        public void TickFrame()
        {
            for (int i = 0; i < TICKS_PER_FRAME; i++)
            {
                // TODO: Tick components here
            }
        }
    }
}
