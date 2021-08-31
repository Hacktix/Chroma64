using Chroma;
using Chroma.Diagnostics;
using Chroma64.Emulator;
using System.IO;

namespace Chroma64
{
    internal class GameCore : Game
    {
        private EmulatorCore emu;

        public GameCore(string[] args) : base(new(false, false))
        {
            if (args.Length >= 0 && File.Exists(args[0]))
            {
                emu = new EmulatorCore(args[0]);
            }
        }

        protected override void Update(float delta)
        {
            Window.Title = $"Chroma64 - {PerformanceCounter.FPS.ToString("F0")} FPS";

            if (emu != null)
            {
                emu.TickFrame();
            }
        }
    }
}