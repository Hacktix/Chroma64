using Chroma;
using Chroma.Diagnostics;
using Chroma.Graphics;
using Chroma.Input;
using Chroma64.Emulator;
using Chroma64.Emulator.IO;
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
            Window.CanResize = true;
            Window.Size = new Size(640, 480);
        }

        protected override void KeyPressed(KeyEventArgs e) { KeyStateChanged(e.KeyCode, true); }

        protected override void KeyReleased(KeyEventArgs e) { KeyStateChanged(e.KeyCode, false); }

        private void KeyStateChanged(KeyCode key, bool pressed)
        {
            if (emu == null)
                return;

            switch (key)
            {
                case KeyCode.A: emu.HandleInput(ControllerButton.A, pressed); break;
                case KeyCode.B: emu.HandleInput(ControllerButton.B, pressed); break;
                case KeyCode.Z: emu.HandleInput(ControllerButton.Z, pressed); break;
                case KeyCode.Return: emu.HandleInput(ControllerButton.Start, pressed); break;
                case KeyCode.Left: emu.HandleInput(ControllerButton.Left, pressed); break;
                case KeyCode.Right: emu.HandleInput(ControllerButton.Right, pressed); break;
                case KeyCode.Up: emu.HandleInput(ControllerButton.Up, pressed); break;
                case KeyCode.Down: emu.HandleInput(ControllerButton.Down, pressed); break;
                case KeyCode.Numpad4: emu.HandleInput(ControllerButton.LeftC, pressed); break;
                case KeyCode.Numpad6: emu.HandleInput(ControllerButton.RightC, pressed); break;
                case KeyCode.Numpad8: emu.HandleInput(ControllerButton.UpC, pressed); break;
                case KeyCode.Numpad2: emu.HandleInput(ControllerButton.DownC, pressed); break;
            }
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
                    context.DrawTexture(
                        _tex,
                        Vector2.Zero,
                        new Vector2(
                            (float)Window.Size.Width / _tex.Width,
                            (float)Window.Size.Height / _tex.Height
                        )
                    );
                }
            }
        }
    }
}