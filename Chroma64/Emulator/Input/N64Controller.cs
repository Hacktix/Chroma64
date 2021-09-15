using Chroma.Diagnostics.Logging;
using Chroma.Input;
using Chroma.Input.GameControllers;
using System;
using System.Collections.Generic;

namespace Chroma64.Emulator.Input
{
    public enum N64ControllerButton
    {
        ButtonA, ButtonB, ButtonZ, ButtonStart, DpadUp, DpadDown, DpadLeft, DpadRight, TriggerLeft, TriggerRight,
        CUp, CDown, CLeft, CRight, // Explicit Controller C-Button support, probably never used but whatever
    }

    public enum N64ControllerAxis
    {
        CX, CY, // C-Buttons are treated as axes because most modern controllers don't have two DPads
        AnalogStickX, AnalogStickY
    }

    public enum N64ControllerButtonAxis
    {
        CUp, CDown, CLeft, CRight, // C-Buttons are treated as axes because most modern controllers don't have two DPads
        AnalogUp, AnalogDown, AnalogLeft, AnalogRight
    }

    class N64Controller : ControllerDevice
    {
        /// <summary>
        /// Float value between 0 and 1 determining how far a controller axis needs to be tilted to be recognized as a C-Button input.
        /// </summary>
        public static float C_AXIS_SENSITIVITY = 0.35f;

        public Dictionary<ControllerButton, N64ControllerButton> ControllerButtonMapping = new Dictionary<ControllerButton, N64ControllerButton>();
        public Dictionary<ControllerAxis, N64ControllerAxis> ControllerAxisMapping = new Dictionary<ControllerAxis, N64ControllerAxis>();
        public Dictionary<KeyCode, N64ControllerButton> KeyboardButtonMapping = new Dictionary<KeyCode, N64ControllerButton>();
        public Dictionary<KeyCode, N64ControllerButtonAxis> KeyboardAxisMapping = new Dictionary<KeyCode, N64ControllerButtonAxis>();

        // Map of N64ControllerButtons to their state (whether they're pressed or not)
        private Dictionary<N64ControllerButton, bool> _btnState = new Dictionary<N64ControllerButton, bool>();

        // Separate C-Button state variables (for Controller Axis support)
        private bool _cUp = false;
        private bool _cDown = false;
        private bool _cLeft = false;
        private bool _cRight = false;

        // Analog Stick state variables
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

        #region Controller Input Handlers
        public void OnButtonPressed(ControllerButton button)
        {
            if (ControllerButtonMapping.ContainsKey(button))
            {
                switch (ControllerButtonMapping[button])
                {
                    case N64ControllerButton.CUp:
                        _cUp = true;
                        break;
                    case N64ControllerButton.CDown:
                        _cDown = true;
                        break;
                    case N64ControllerButton.CLeft:
                        _cLeft = true;
                        break;
                    case N64ControllerButton.CRight:
                        _cRight = true;
                        break;
                    default:
                        _btnState[ControllerButtonMapping[button]] = true;
                        break;
                }
            }
        }

        public void OnButtonReleased(ControllerButton button)
        {
            if (ControllerButtonMapping.ContainsKey(button))
            {
                switch (ControllerButtonMapping[button])
                {
                    case N64ControllerButton.CUp:
                        _cUp = false;
                        break;
                    case N64ControllerButton.CDown:
                        _cDown = false;
                        break;
                    case N64ControllerButton.CLeft:
                        _cLeft = false;
                        break;
                    case N64ControllerButton.CRight:
                        _cRight = false;
                        break;
                    default:
                        _btnState[ControllerButtonMapping[button]] = false;
                        break;
                }
            }
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
                    case N64ControllerAxis.CX:
                        if (Math.Abs(value) >= C_AXIS_SENSITIVITY)
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
                    case N64ControllerAxis.CY:
                        if (Math.Abs(value) >= C_AXIS_SENSITIVITY)
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
        #endregion

        #region Keyboard Input Handlers
        public void OnButtonPressed(KeyCode button)
        {
            if (KeyboardButtonMapping.ContainsKey(button))
                _btnState[KeyboardButtonMapping[button]] = true;
            else if (KeyboardAxisMapping.ContainsKey(button))
            {
                switch (KeyboardAxisMapping[button])
                {
                    case N64ControllerButtonAxis.AnalogDown:
                        _analogY = -125;
                        break;
                    case N64ControllerButtonAxis.AnalogUp:
                        _analogY = 125;
                        break;
                    case N64ControllerButtonAxis.AnalogLeft:
                        _analogX = -125;
                        break;
                    case N64ControllerButtonAxis.AnalogRight:
                        _analogX = 125;
                        break;
                    case N64ControllerButtonAxis.CDown:
                        _cDown = true;
                        break;
                    case N64ControllerButtonAxis.CUp:
                        _cUp = true;
                        break;
                    case N64ControllerButtonAxis.CLeft:
                        _cLeft = true;
                        break;
                    case N64ControllerButtonAxis.CRight:
                        _cRight = true;
                        break;
                }
            }
        }

        public void OnButtonReleased(KeyCode button)
        {
            if (KeyboardButtonMapping.ContainsKey(button))
                _btnState[KeyboardButtonMapping[button]] = false;
            else if (KeyboardAxisMapping.ContainsKey(button))
            {
                switch (KeyboardAxisMapping[button])
                {
                    case N64ControllerButtonAxis.AnalogDown:
                    case N64ControllerButtonAxis.AnalogUp:
                        _analogY = 0;
                        break;
                    case N64ControllerButtonAxis.AnalogLeft:
                    case N64ControllerButtonAxis.AnalogRight:
                        _analogX = 0;
                        break;
                    case N64ControllerButtonAxis.CDown:
                        _cDown = false;
                        break;
                    case N64ControllerButtonAxis.CUp:
                        _cUp = false;
                        break;
                    case N64ControllerButtonAxis.CLeft:
                        _cLeft = false;
                        break;
                    case N64ControllerButtonAxis.CRight:
                        _cRight = false;
                        break;
                }
            }
        }
        #endregion

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
