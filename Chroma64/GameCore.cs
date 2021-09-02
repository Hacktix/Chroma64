using Chroma;
using Chroma.Diagnostics;
using Chroma.Graphics;
using Chroma64.Emulator;
using System.Drawing;
using System.IO;
using System.Numerics;

namespace Chroma64
{
    internal class GameCore : Game
    {
        private EmulatorCore emu;

        private Texture _tex = null;

        public GameCore(string[] args) : base(new(false, false))
        {
            if (args.Length >= 0 && File.Exists(args[0]))
            {
                emu = new EmulatorCore(args[0]);
            }
            Window.CanResize = false;
            Window.Size = new Size(640, 480);
        }

        protected override void Draw(RenderContext context)
        {
            if (emu != null)
            {
                Window.Title = $"Chroma64 - {PerformanceCounter.FPS.ToString("F0")} FPS";
                emu.TickFrame();
                if (emu.NeedsRender())
                {
                    emu.SetFramebufferTexture(ref _tex);
                    _tex.Flush();
                    context.DrawTexture(_tex, Vector2.Zero);
                }
            }
        }
    }
}