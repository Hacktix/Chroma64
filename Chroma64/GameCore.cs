using Chroma;
using Chroma.Diagnostics;
using Chroma.Graphics;
using Chroma.Input;
using Chroma.Input.GameControllers;
using Chroma64.Emulator;
using Chroma64.Emulator.Input;
using Chroma64.Emulator.IO;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Numerics;

namespace Chroma64
{
    internal class GameCore : Game
    {
        private EmulatorCore emu;

        private Texture _tex = null;

        private Dictionary<int, int> playerIdPortMap = new Dictionary<int, int>();

        public GameCore(string[] args) : base(new(false, false))
        {
            Audio.Output.Open(null, 48000, 1024);
            if (args.Length >= 0 && File.Exists(args[0]))
            {
                emu = new EmulatorCore(args[0]);
            }
            Window.CanResize = true;
            Window.Size = new Size(640, 480);
        }

        protected override void ControllerConnected(ControllerEventArgs e)
        {
            if (emu != null)
            {
                // TODO: Fetch custom mappings
                N64Controller controller = new N64Controller();

                controller.ControllerButtonMapping[ControllerButton.A] = N64ControllerButton.ButtonA;
                controller.ControllerButtonMapping[ControllerButton.B] = N64ControllerButton.ButtonB;
                controller.ControllerButtonMapping[ControllerButton.X] = N64ControllerButton.ButtonZ;
                controller.ControllerButtonMapping[ControllerButton.Menu] = N64ControllerButton.ButtonStart;
                controller.ControllerButtonMapping[ControllerButton.DpadDown] = N64ControllerButton.DpadDown;
                controller.ControllerButtonMapping[ControllerButton.DpadUp] = N64ControllerButton.DpadUp;
                controller.ControllerButtonMapping[ControllerButton.DpadLeft] = N64ControllerButton.DpadLeft;
                controller.ControllerButtonMapping[ControllerButton.DpadRight] = N64ControllerButton.DpadRight;
                controller.ControllerButtonMapping[ControllerButton.LeftBumper] = N64ControllerButton.TriggerLeft;
                controller.ControllerButtonMapping[ControllerButton.RightBumper] = N64ControllerButton.TriggerRight;

                controller.ControllerAxisMapping[ControllerAxis.LeftStickX] = N64ControllerAxis.AnalogStickX;
                controller.ControllerAxisMapping[ControllerAxis.LeftStickY] = N64ControllerAxis.AnalogStickY;
                controller.ControllerAxisMapping[ControllerAxis.RightStickX] = N64ControllerAxis.CStickX;
                controller.ControllerAxisMapping[ControllerAxis.RightStickY] = N64ControllerAxis.CStickY;

                int port = emu.RegisterController(controller);
                playerIdPortMap[e.Controller.Info.PlayerIndex] = port;
            }
        }

        protected override void ControllerDisconnected(ControllerEventArgs e)
        {
            if(emu != null)
            {
                if(playerIdPortMap.ContainsKey(e.Controller.Info.PlayerIndex))
                {
                    emu.UnregisterController(playerIdPortMap[e.Controller.Info.PlayerIndex]);
                    playerIdPortMap.Remove(e.Controller.Info.PlayerIndex);
                }
            }
        }

        protected override void ControllerButtonPressed(ControllerButtonEventArgs e)
        {
            if (emu != null)
                emu.OnButtonPressed(e.Controller.Info.PlayerIndex, e.Button);
        }

        protected override void ControllerButtonReleased(ControllerButtonEventArgs e)
        {
            if (emu != null)
                emu.OnButtonReleased(e.Controller.Info.PlayerIndex, e.Button);
        }

        protected override void ControllerAxisMoved(ControllerAxisEventArgs e)
        {
            if (emu != null)
            {
                float val = Math.Clamp(e.Value / (float)short.MaxValue, -1, 1);
                emu.OnAxisChanged(e.Controller.Info.PlayerIndex, e.Axis, val);
            }
        }

        protected override void KeyPressed(KeyEventArgs e)
        {
            // TODO: Mapping keyboard to custom port
            if (emu != null)
                emu.OnButtonPressed(0, e.KeyCode);
        }

        protected override void KeyReleased(KeyEventArgs e)
        {
            // TODO: Mapping keyboard to custom port
            if (emu != null)
                emu.OnButtonReleased(0, e.KeyCode);
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

                    float widthScale = (float)Window.Size.Width / _tex.Width;
                    float heightScale = (float)Window.Size.Height / _tex.Height;
                    Vector2 scale = new Vector2(
                        Math.Min(widthScale, heightScale),
                        Math.Min(widthScale, heightScale)
                    );
                    Vector2 pos = new Vector2(
                        Window.Size.Width / 2 - (scale.X * _tex.Width) / 2,
                        Window.Size.Height / 2 - (scale.X * _tex.Height) / 2
                    );

                    _tex.Flush();
                    context.DrawTexture(
                        _tex,
                        pos,
                        scale
                    );
                }
            }
        }
    }
}