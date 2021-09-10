using Chroma.Diagnostics.Logging;
using Chroma.Input;
using Chroma.Input.GameControllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chroma64.Emulator.Input
{
    public enum N64ControllerButton
    {
        ButtonA, ButtonB, ButtonZ, ButtonStart, DpadUp, DpadDown, DpadLeft, DpadRight, TriggerLeft, TriggerRight
    }

    public enum N64ControllerAxis
    {
        CStickX, CStickY, AnalogStickX, AnalogStickY
    }

    public enum N64ControllerButtonAxis
    {
        CStickUp, CStickDown, CStickLeft, CStickRight, AnalogUp, AnalogDown, AnalogLeft, AnalogRight
    }

    class N64Controller : ControllerDevice
    {
        public static float CSTICK_SENSITIVITY = 0.35f;

        public Dictionary<ControllerButton, N64ControllerButton> ControllerButtonMapping = new Dictionary<ControllerButton, N64ControllerButton>();
        public Dictionary<ControllerAxis, N64ControllerAxis> ControllerAxisMapping = new Dictionary<ControllerAxis, N64ControllerAxis>();
        public Dictionary<KeyCode, N64ControllerButton> KeyboardButtonMapping = new Dictionary<KeyCode, N64ControllerButton>();
        public Dictionary<KeyCode, N64ControllerButtonAxis> KeyboardAxisMapping = new Dictionary<KeyCode, N64ControllerButtonAxis>();

        private Dictionary<N64ControllerButton, bool> _btnState = new Dictionary<N64ControllerButton, bool>();
        private bool _cUp = false;
        private bool _cDown = false;
        private bool _cLeft = false;
        private bool _cRight = false;
        private sbyte _analogX = 0;
        private sbyte _analogY = 0;

        private Log _log = LogManager.GetForCurrentAssembly();

        public N64Controller()
        {
            _btnState[N64ControllerButton.ButtonA] = false;
            _btnState[N64ControllerButton.ButtonB] = false;
            _btnState[N64ControllerButton.ButtonZ] = false;
            _btnState[N64ControllerButton.ButtonStart] = false;
            _btnState[N64ControllerButton.DpadUp] = false;
            _btnState[N64ControllerButton.DpadDown] = false;
            _btnState[N64ControllerButton.DpadLeft] = false;
            _btnState[N64ControllerButton.DpadRight] = false;
            _btnState[N64ControllerButton.TriggerLeft] = false;
            _btnState[N64ControllerButton.TriggerRight] = false;
        }

        public void OnAxisChanged(ControllerAxis axis, float value)
        {
            if (ControllerAxisMapping.ContainsKey(axis))
            {
                switch (ControllerAxisMapping[axis])
                {
                    case N64ControllerAxis.AnalogStickX:
                        _analogX = (sbyte)(value * sbyte.MaxValue);
                        break;
                    case N64ControllerAxis.AnalogStickY:
                        _analogY = (sbyte)(-value * sbyte.MaxValue);
                        break;
                    case N64ControllerAxis.CStickX:
                        if (Math.Abs(value) >= CSTICK_SENSITIVITY)
                        {
                            if (value < 0)
                                _cLeft = true;
                            else
                                _cRight = true;
                        }
                        else
                        {
                            _cLeft = false;
                            _cRight = false;
                        }
                        break;
                    case N64ControllerAxis.CStickY:
                        if (Math.Abs(value) >= CSTICK_SENSITIVITY)
                        {
                            if (value < 0)
                                _cUp = true;
                            else
                                _cDown = true;
                        }
                        else
                        {
                            _cUp = false;
                            _cDown = false;
                        }
                        break;
                }
            }
        }

        public void OnButtonPressed(ControllerButton button)
        {
            if (ControllerButtonMapping.ContainsKey(button))
                _btnState[ControllerButtonMapping[button]] = true;
        }

        public void OnButtonPressed(KeyCode button)
        {
            throw new NotImplementedException();
        }

        public void OnButtonReleased(ControllerButton button)
        {
            if (ControllerButtonMapping.ContainsKey(button))
                _btnState[ControllerButtonMapping[button]] = false;
        }

        public void OnButtonReleased(KeyCode button)
        {
            throw new NotImplementedException();
        }

        public byte[] ExecPIF(byte[] cmdBuffer)
        {
            switch (cmdBuffer[0])
            {
                // Info
                case 0x00:
                    return new byte[] { 0x05, 0x00, 0x01 };

                // Controller State
                case 0x01:
                    return new byte[]
                    {
                        (byte)(
                            (_btnState[N64ControllerButton.ButtonA] ? 0x80 : 0) |
                            (_btnState[N64ControllerButton.ButtonB] ? 0x40 : 0) |
                            (_btnState[N64ControllerButton.ButtonZ] ? 0x20 : 0) |
                            (_btnState[N64ControllerButton.ButtonStart] ? 0x10 : 0) |
                            (_btnState[N64ControllerButton.DpadUp] ? 0x08 : 0) |
                            (_btnState[N64ControllerButton.DpadDown] ? 0x04 : 0) |
                            (_btnState[N64ControllerButton.DpadLeft] ? 0x02 : 0) |
                            (_btnState[N64ControllerButton.DpadRight] ? 0x01 : 0)
                        ),
                        (byte)(
                            (_btnState[N64ControllerButton.TriggerLeft] ? 0x20 : 0) |
                            (_btnState[N64ControllerButton.TriggerRight] ? 0x10 : 0) |
                            (_cUp ? 0x08 : 0) |
                            (_cDown ? 0x04 : 0) |
                            (_cLeft ? 0x02 : 0) |
                            (_cRight ? 0x01 : 0)
                        ),
                        (byte)_analogX,
                        (byte)_analogY,
                    };

                // Read Controller Accessory
                case 0x02:
                    return new byte[33];

                // Write Controller Accessory
                case 0x03:
                    return new byte[] { 0xFF };

                // Unknown
                default:
                    _log.Error($"Encountered unknown PIF Command {cmdBuffer[0]}");
                    Console.ReadKey();
                    Environment.Exit(-1);
                    return new byte[0];
            }
        }
    }
}
