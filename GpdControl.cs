using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GpdControl
{
    static class NativeMethods
    {
        [DllImport("hid.dll", SetLastError = true)]
        public static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, uint DeviceInterfaceDetailDataSize, out uint RequiredSize, IntPtr DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetAttributes(SafeFileHandle HidDeviceObject, ref HIDD_ATTRIBUTES Attributes);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetPreparsedData(SafeFileHandle HidDeviceObject, out IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_FreePreparsedData(IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern int HidP_GetCaps(IntPtr PreparsedData, ref HIDP_CAPS Capabilities);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_SetFeature(SafeFileHandle HidDeviceObject, byte[] ReportBuffer, int ReportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_SetOutputReport(SafeFileHandle HidDeviceObject, byte[] ReportBuffer, int ReportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetInputReport(SafeFileHandle HidDeviceObject, byte[] ReportBuffer, int ReportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetFeature(SafeFileHandle HidDeviceObject, byte[] ReportBuffer, int ReportBufferLength);

        public const uint DIGCF_PRESENT = 0x02;
        public const uint DIGCF_DEVICEINTERFACE = 0x10;
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_FLAG_OVERLAPPED = 0x40000000;

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public uint cbSize;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }
    }

    public class GpdDevice : IDisposable
    {
        private SafeFileHandle _handle;
        private const ushort VID = 0x2f24;
        private const ushort USAGE_PAGE = 0xff00;

        public void Open()
        {
            Guid hidGuid;
            NativeMethods.HidD_GetHidGuid(out hidGuid);
            IntPtr deviceInfoSet = NativeMethods.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);

            try
            {
                NativeMethods.SP_DEVICE_INTERFACE_DATA deviceInterfaceData = new NativeMethods.SP_DEVICE_INTERFACE_DATA();
                deviceInterfaceData.cbSize = (uint)Marshal.SizeOf(deviceInterfaceData);
                uint index = 0;

                while (NativeMethods.SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, index, ref deviceInterfaceData))
                {
                    uint requiredSize = 0;
                    NativeMethods.SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref deviceInterfaceData, IntPtr.Zero, 0, out requiredSize, IntPtr.Zero);

                    IntPtr detailDataBuffer = Marshal.AllocHGlobal((int)requiredSize);
                    try
                    {
                        Marshal.WriteInt32(detailDataBuffer, (IntPtr.Size == 4) ? (4 + Marshal.SystemDefaultCharSize) : 8); // cbSize

                        if (NativeMethods.SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref deviceInterfaceData, detailDataBuffer, requiredSize, out requiredSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = (IntPtr)((long)detailDataBuffer + 4); // Offset for DevicePath
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);
                            
                            // Console.WriteLine("Found Device Path: " + devicePath);

                            var handle = NativeMethods.CreateFile(devicePath, NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE, NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE, IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
                            if (!handle.IsInvalid)
                            {
                                NativeMethods.HIDD_ATTRIBUTES attributes = new NativeMethods.HIDD_ATTRIBUTES();
                                attributes.Size = Marshal.SizeOf(attributes);
                                if (NativeMethods.HidD_GetAttributes(handle, ref attributes))
                                {
                                    // Console.WriteLine(string.Format("  VendorID: 0x{0:X4}, ProductID: 0x{1:X4}", attributes.VendorID, attributes.ProductID));
                                    if (attributes.VendorID == VID)
                                    {
                                        IntPtr preparsedData;
                                        if (NativeMethods.HidD_GetPreparsedData(handle, out preparsedData))
                                        {
                                            NativeMethods.HIDP_CAPS caps = new NativeMethods.HIDP_CAPS();
                                            NativeMethods.HidP_GetCaps(preparsedData, ref caps);
                                            NativeMethods.HidD_FreePreparsedData(preparsedData);
                                            // Console.WriteLine(string.Format("  UsagePage: 0x{0:X4}", caps.UsagePage));

                                            if (caps.UsagePage == USAGE_PAGE)
                                            {
                                                _handle = handle;
                                                return; // Found it
                                            }
                                        }
                                    }
                                }
                                handle.Close();
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detailDataBuffer);
                    }
                    index++;
                }
            }
            finally
            {
                NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }

            throw new Exception("GPD Win Controller not found.");
        }

        public void Dispose()
        {
            if (_handle != null) _handle.Close();
        }

        private byte[] SendReq(byte id, byte minorSerial, byte[] data = null)
        {
            return SendReqWithRetry(id, minorSerial, data);
        }

        private byte[] SendReqWithRetry(byte id, byte minorSerial, byte[] data = null, int retries = 3)
        {
            Exception lastEx = null;
            for(int i=0; i<=retries; i++) 
            { 
                try 
                {
                    return SendReqInternal(id, minorSerial, data);
                }
                catch(Exception ex)
                {
                    lastEx = ex;
                    System.Threading.Thread.Sleep(50);
                }
            }
            throw lastEx;
        }

        private byte[] SendReqInternal(byte id, byte minorSerial, byte[] data = null)
        {
            byte[] buffer = new byte[33];
            buffer[0] = 0x01; // ID
            buffer[1] = 0xa5;
            buffer[2] = id;
            buffer[3] = 0x5a;
            buffer[4] = (byte)(id ^ 0xFF);
            buffer[5] = 0x00; // Reserved / Padding
            buffer[6] = minorSerial; // Index Low
            buffer[7] = 0x00; // Index High
            if (data != null) Array.Copy(data, 0, buffer, 8, Math.Min(data.Length, 25));

            bool writeSuccess = false;
            
            // Method 1: Feature ID 1 (PRIORITY for Config Write)
            if (NativeMethods.HidD_SetFeature(_handle, buffer, buffer.Length))
            {
                writeSuccess = true;
            }
            else
            {
                // Fallback: Output ID 1
                if (NativeMethods.HidD_SetOutputReport(_handle, buffer, buffer.Length)) 
                {
                    writeSuccess = true;
                }
                else
                {
                    // Fallback: Output ID 0 (Shifted Payload)
                    byte[] buffer0 = new byte[34];
                    buffer0[0] = 0x00; // Dummy
                    Array.Copy(buffer, 0, buffer0, 1, 33);
                    
                    if (NativeMethods.HidD_SetOutputReport(_handle, buffer0, buffer0.Length))
                    {
                        writeSuccess = true;
                    }
                }
            }

            if (!writeSuccess) throw new Exception("Failed to write to device (SetFeature/SetOutput failed).");

            if (id == 0x21 || id == 0x23) return null; // No response needed for writes (usually)
            
            // Wait for device to generate NEW report
            // Reduced from 100ms to 20ms to avoid timeout but allow processing
            System.Threading.Thread.Sleep(20); 
            
            // Read Response
            byte[] readBuffer = new byte[65];
            readBuffer[0] = 0x01;
            
            // PRIORITY: Try Feature Report (Control Transfer) first.
            // This bypasses the Input Buffer and gets the current state synchronously.
            if (NativeMethods.HidD_GetFeature(_handle, readBuffer, readBuffer.Length))
            {
                return readBuffer;
            }
            
            // Fallback: Input Report (Interrupt Transfer)
            // Only use this if GetFeature fails.
            if (NativeMethods.HidD_GetInputReport(_handle, readBuffer, readBuffer.Length))
            {
                return readBuffer;
            }
            
            throw new Exception("Failed to read response.");
        }
        
        public string FirmwareVersion { get; private set; }

        public byte[] ReadConfig()
        {
            WaitReady(0x10);
            
            // Read all minor serials (0-3) for Major 1.
            byte[] load0_raw = SendReq(0x11, 0x00, null); // Minor0
            System.Threading.Thread.Sleep(50);
            byte[] load1_raw = SendReq(0x11, 0x01, null); // Minor1
            System.Threading.Thread.Sleep(50);
            byte[] load2_raw = SendReq(0x11, 0x02, null); // Minor2
            System.Threading.Thread.Sleep(50);
            byte[] load3_raw = SendReq(0x11, 0x03, null); // Minor3
            System.Threading.Thread.Sleep(50);

            // Console.WriteLine("DEBUG LOAD0: " + BitConverter.ToString(load0_raw));
            // Console.WriteLine("DEBUG LOAD1: " + BitConverter.ToString(load1_raw));
            // Console.WriteLine("DEBUG LOAD2: " + BitConverter.ToString(load2_raw));
            // Console.WriteLine("DEBUG LOAD3: " + BitConverter.ToString(load3_raw));

            List<byte> configData = new List<byte>();
            
            // Extract the 64 byte payload from each response
            // DEBUG logs show data starts at index 0 (e.g. E8 00 ...), so no Report ID offset is needed.
            byte[] chunk0 = new byte[64];
            Array.Copy(load0_raw, 0, chunk0, 0, 64);
            configData.AddRange(chunk0);

            byte[] chunk1 = new byte[64];
            Array.Copy(load1_raw, 0, chunk1, 0, 64);
            configData.AddRange(chunk1);

            byte[] chunk2 = new byte[64];
            Array.Copy(load2_raw, 0, chunk2, 0, 64);
            configData.AddRange(chunk2);

            byte[] chunk3 = new byte[64];
            Array.Copy(load3_raw, 0, chunk3, 0, 64);
            configData.AddRange(chunk3);

            byte[] checkResponse = SendReq(0x12, 0x00, null); // Checksum command is Minor 0

            // Parse Firmware info
            byte xMaj = checkResponse[9];
            byte xMin = checkResponse[10];
            byte kMaj = checkResponse[11];
            byte kMin = checkResponse[12];
            
            FirmwareVersion = string.Format("X{0:x}{1:02x} K{2:x}{3:02x}", xMaj, xMin, kMaj, kMin);

            uint checksum = BitConverter.ToUInt32(checkResponse, 24);
            uint calculatedChecksum = (uint)configData.Sum(b => (long)b);

            if (checksum != calculatedChecksum)
            {
                Console.WriteLine(string.Format("Warning: Checksum mismatch! Device: {0}, Calc: {1}", checksum, calculatedChecksum));
            }

            return configData.ToArray();
        }

        public void WriteConfig(byte[] config)
        {
            if (config.Length != 256) throw new ArgumentException("Config must be 256 bytes");

            // Reference protocol writes 8 x 16-byte blocks with a 2-byte index in the payload.
            for (int block = 0; block < 8; block++)
            {
                byte[] payload = new byte[18];
                payload[0] = (byte)block; // index low
                payload[1] = 0x00;        // index high
                Array.Copy(config, block * 16, payload, 2, 16);
                SendReq(0x21, 0x00, payload);
            }

            byte[] response = SendReq(0x22, 0x00, null); // Checksum. Minor 0

            uint checksum = BitConverter.ToUInt32(response, 24);
            uint calculatedChecksum = (uint)config.Sum(b => (long)b);
            
            if (checksum == calculatedChecksum)
            {
                SendReq(0x23, 0x00, null); // Commit. Minor 0
            }
            else
            {
                throw new Exception(string.Format("Checksum mismatch while writing. Device: {0}, Calc: {1}", checksum, calculatedChecksum));
            }
        }



        private void WaitReady(byte id)
        {
            byte[] response = null;
            int safeguard = 0;
            do
            {
                response = SendReq(id, 0x00, null); // Minor 0 for WaitReady?
                safeguard++;
                if (safeguard > 100) throw new Exception("WaitReady timed out.");
            } while (response == null || response[8] != 0xaa);
        }
    }

    public class KeyCodes
    {
        public static readonly Dictionary<string, byte> Map = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            {"NONE", 0x00},
            {"A", 0x04}, {"B", 0x05}, {"C", 0x06}, {"D", 0x07}, {"E", 0x08},
            {"F", 0x09}, {"G", 0x0a}, {"H", 0x0b}, {"I", 0x0c}, {"J", 0x0d},
            {"K", 0x0e}, {"L", 0x0f}, {"M", 0x10}, {"N", 0x11}, {"O", 0x12},
            {"P", 0x13}, {"Q", 0x14}, {"R", 0x15}, {"S", 0x16}, {"T", 0x17},
            {"U", 0x18}, {"V", 0x19}, {"W", 0x1a}, {"X", 0x1b}, {"Y", 0x1c},
            {"Z", 0x1d},
            {"1", 0x1e}, {"2", 0x1f}, {"3", 0x20}, {"4", 0x21}, {"5", 0x22},
            {"6", 0x23}, {"7", 0x24}, {"8", 0x25}, {"9", 0x26}, {"0", 0x27},
            {"ENTER", 0x28}, {"ESC", 0x29}, {"BACKSPACE", 0x2a}, {"TAB", 0x2b},
            {"SPACE", 0x2c}, {"MINUS", 0x2d}, {"EQUAL", 0x2e}, {"LEFTBRACE", 0x2f},
            {"RIGHTBRACE", 0x30}, {"BACKSLASH", 0x31}, {"HASHTILDE", 0x32},
            {"SEMICOLON", 0x33}, {"APOSTROPHE", 0x34}, {"GRAVE", 0x35},
            {"COMMA", 0x36}, {"DOT", 0x37}, {"SLASH", 0x38}, {"CAPSLOCK", 0x39},
            {"F1", 0x3a}, {"F2", 0x3b}, {"F3", 0x3c}, {"F4", 0x3d}, {"F5", 0x3e},
            {"F6", 0x3f}, {"F7", 0x40}, {"F8", 0x41}, {"F9", 0x42}, {"F10", 0x43},
            {"F11", 0x44}, {"F12", 0x45},
            {"SYSRQ", 0x46}, {"SCROLLLOCK", 0x47}, {"PAUSE", 0x48}, {"INSERT", 0x49},
            {"HOME", 0x4a}, {"PAGEUP", 0x4b}, {"DELETE", 0x4c}, {"END", 0x4d},
            {"PAGEDOWN", 0x4e}, {"RIGHT", 0x4f}, {"LEFT", 0x50}, {"DOWN", 0x51},
            {"UP", 0x52}, {"NUMLOCK", 0x53}, {"KPSLASH", 0x54}, {"KPASTERISK", 0x55},
            {"KPMINUS", 0x56}, {"KPPLUS", 0x57}, {"KPENTER", 0x58},
            {"KP1", 0x59}, {"KP2", 0x5a}, {"KP3", 0x5b}, {"KP4", 0x5c},
            {"KP5", 0x5d}, {"KP6", 0x5e}, {"KP7", 0x5f}, {"KP8", 0x60},
            {"KP9", 0x61}, {"KP0", 0x62}, {"KPDOT", 0x63},
            {"APPLICATIONS", 0x65}, {"POWER", 0x66},
            {"MUTE", 0x7F}, {"VOLUP", 0x80}, {"VOLDN", 0x81},
            {"LEFTCTRL", 0xe0}, {"LEFTSHIFT", 0xe1}, {"LEFTALT", 0xe2}, {"LEFTMETA", 0xe3}, {"LWIN", 0xe3},
            {"RIGHTCTRL", 0xe4}, {"RIGHTSHIFT", 0xe5}, {"RIGHTALT", 0xe6}, {"RIGHTMETA", 0xe7}, {"RWIN", 0xe7},
            {"MOUSE_WHEELUP", 0xe8}, {"MOUSE_WHEELDOWN", 0xe9}, {"MOUSE_LEFT", 0xea},
            {"MOUSE_RIGHT", 0xeb}, {"MOUSE_MIDDLE", 0xec}, {"MOUSE_FAST", 0xed},
            {"PRINTSCREEN", 0x46}
        };

        public static string GetName(byte code)
        {
            foreach (var kvp in Map)
            {
                if (kvp.Value == code) return kvp.Key;
            }
            return string.Format("0x{0:X2}", code);
        }
    }

    public class Config
    {
        public byte[] Raw { get; private set; }

        // Definition of fields
        // Offset, Name, Description
        public class FieldDef { public int Offset; public string Name; public string Desc; public int Size; public string Type; }
        
        public static List<FieldDef> Fields = new List<FieldDef>
        {
            // Block 0: DPad + ABXY (0-15)
            new FieldDef{Offset=0, Name="du", Desc="dpad up", Size=2, Type="Key"},
            new FieldDef{Offset=2, Name="dd", Desc="dpad down", Size=2, Type="Key"},
            new FieldDef{Offset=4, Name="dl", Desc="dpad left", Size=2, Type="Key"},
            new FieldDef{Offset=6, Name="dr", Desc="dpad right", Size=2, Type="Key"},
            
            new FieldDef{Offset=8, Name="a", Desc="A button", Size=2, Type="Key"},
            new FieldDef{Offset=10, Name="b", Desc="B button", Size=2, Type="Key"},
            new FieldDef{Offset=12, Name="x", Desc="X button", Size=2, Type="Key"},
            new FieldDef{Offset=14, Name="y", Desc="Y button", Size=2, Type="Key"},
            
            // Block 1: Left Stick + Clicks + Start/Select (16-31)
            new FieldDef{Offset=16, Name="lu", Desc="left stick up", Size=2, Type="Key"},
            new FieldDef{Offset=18, Name="ld", Desc="left stick down", Size=2, Type="Key"},
            new FieldDef{Offset=20, Name="ll", Desc="left stick left", Size=2, Type="Key"},
            new FieldDef{Offset=22, Name="lr", Desc="left stick right", Size=2, Type="Key"},
            
            new FieldDef{Offset=24, Name="l3", Desc="left stick click", Size=2, Type="Key"},
            new FieldDef{Offset=26, Name="r3", Desc="right stick click", Size=2, Type="Key"},
            
            new FieldDef{Offset=28, Name="start", Desc="Start button", Size=2, Type="Key"},
            new FieldDef{Offset=30, Name="select", Desc="Select button", Size=2, Type="Key"},

            // Block 2: Menu + Shoulders (32-47)
            new FieldDef{Offset=32, Name="menu", Desc="Menu button", Size=2, Type="Key"},
            
            new FieldDef{Offset=34, Name="l1", Desc="L1 shoulder button", Size=2, Type="Key"},
            new FieldDef{Offset=36, Name="r1", Desc="R1 shoulder button", Size=2, Type="Key"},
            new FieldDef{Offset=38, Name="l2", Desc="L2 trigger", Size=2, Type="Key"},
            new FieldDef{Offset=40, Name="r2", Desc="R2 trigger", Size=2, Type="Key"},
            
            // Block 3/4: Back Keys (50-64)
            new FieldDef{Offset=50, Name="l41", Desc="L4 macro key 1", Size=2, Type="Key"},
            new FieldDef{Offset=52, Name="l42", Desc="L4 macro key 2", Size=2, Type="Key"},
            new FieldDef{Offset=54, Name="l43", Desc="L4 macro key 3", Size=2, Type="Key"},
            new FieldDef{Offset=56, Name="l44", Desc="L4 macro key 4", Size=2, Type="Key"},
            new FieldDef{Offset=58, Name="r41", Desc="R4 macro key 1", Size=2, Type="Key"},
            new FieldDef{Offset=60, Name="r42", Desc="R4 macro key 2", Size=2, Type="Key"},
            new FieldDef{Offset=62, Name="r43", Desc="R4 macro key 3", Size=2, Type="Key"},
            new FieldDef{Offset=64, Name="r44", Desc="R4 macro key 4", Size=2, Type="Key"},

            // Block 4/5: Misc + Delays (66-95)
            new FieldDef{Offset=66, Name="rumble", Desc="Rumble", Size=1, Type="Rumble"},
            new FieldDef{Offset=68, Name="ledmode", Desc="LED mode", Size=1, Type="LedMode"},
            new FieldDef{Offset=69, Name="colour", Desc="LED colour", Size=3, Type="Colour"},
            
            new FieldDef{Offset=72, Name="ldead", Desc="Left stick deadzone", Size=1, Type="Signed"},
            new FieldDef{Offset=73, Name="lcent", Desc="Left stick centering", Size=1, Type="Signed"},
            new FieldDef{Offset=74, Name="rdead", Desc="Right stick deadzone", Size=1, Type="Signed"},
            new FieldDef{Offset=75, Name="rcent", Desc="Right stick centering", Size=1, Type="Signed"},

            new FieldDef{Offset=80,Name="l4delay1",Desc="L4 macro delay 1",Size=2,Type="Millis"},
            new FieldDef{Offset=82,Name="l4delay2",Desc="L4 macro delay 2",Size=2,Type="Millis"},
            new FieldDef{Offset=84,Name="l4delay3",Desc="L4 macro delay 3",Size=2,Type="Millis"},
            new FieldDef{Offset=86,Name="l4delay4",Desc="L4 macro delay 4",Size=2,Type="Millis"},
            new FieldDef{Offset=88,Name="r4delay1",Desc="R4 macro delay 1",Size=2,Type="Millis"},
            new FieldDef{Offset=90,Name="r4delay2",Desc="R4 macro delay 2",Size=2,Type="Millis"},
            new FieldDef{Offset=92,Name="r4delay3",Desc="R4 macro delay 3",Size=2,Type="Millis"},
            new FieldDef{Offset=94,Name="r4delay4",Desc="R4 macro delay 4",Size=2,Type="Millis"},
        };

        public Config(byte[] raw)
        {
            Raw = raw;
        }

        public void Set(string key, string value)
        {
            var def = Fields.FirstOrDefault(d => d.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (def == null) throw new Exception(string.Format("Unknown setting: {0}", key));

            switch (def.Type)
            {
                case "Key":
                    byte code;
                    if (KeyCodes.Map.TryGetValue(value.ToUpper(), out code))
                    {
                        Raw[def.Offset] = code; // Write keycode to first byte (Little Endian)
                        Raw[def.Offset + 1] = 0x00; // Zero second byte
                    }
                    else
                    {
                        throw new Exception(string.Format("Invalid key: {0}", value));
                    }
                    break;
                case "Signed":
                    sbyte sb = sbyte.Parse(value);
                    Raw[def.Offset] = (byte)sb;
                    break;
                case "Rumble":
                    byte rb = byte.Parse(value);
                    if (rb > 2) throw new Exception("Rumble must be 0, 1, or 2");
                    Raw[def.Offset] = rb;
                    break;
                case "LedMode":
                    string mode = value.Trim().ToLowerInvariant();
                    if (mode == "off") Raw[def.Offset] = 0x00;
                    else if (mode == "solid") Raw[def.Offset] = 0x01;
                    else if (mode == "breathe") Raw[def.Offset] = 0x11;
                    else if (mode == "rotate") Raw[def.Offset] = 0x21;
                    else throw new Exception("LED mode must be off, solid, breathe, or rotate");
                    break;
                case "Colour":
                    string hex = value.Trim();
                    if (hex.StartsWith("#")) hex = hex.Substring(1);
                    if (hex.Length != 6) throw new Exception("Colour must be a 6-digit hex value (RRGGBB)");
                    int rgb;
                    if (!int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out rgb))
                    {
                        throw new Exception("Colour must be a valid hex value (RRGGBB)");
                    }

                    // Device format is B, G, R.
                    Raw[def.Offset] = (byte)(rgb & 0xFF);
                    Raw[def.Offset + 1] = (byte)((rgb >> 8) & 0xFF);
                    Raw[def.Offset + 2] = (byte)((rgb >> 16) & 0xFF);
                    break;
                case "Millis":
                    ushort ms = ushort.Parse(value);
                    BitConverter.GetBytes(ms).CopyTo(Raw, def.Offset);
                    break;
                 // Add other types...
            }
        }

        public string GetValue(FieldDef def)
        {
            switch (def.Type)
            {
                case "Key":
                    // Keycodes are stored as XX 00. The actual keycode is the first byte.
                    return KeyCodes.GetName(Raw[def.Offset]);
                case "Signed":
                    return ((sbyte)Raw[def.Offset]).ToString();
                case "Rumble":
                    return Raw[def.Offset].ToString();
                case "LedMode":
                     byte m = Raw[def.Offset];
                     if (m == 0) return "off";
                     if (m == 1) return "solid";
                     if (m == 0x11) return "breathe";
                     if (m == 0x21) return "rotate";
                     return string.Format("0x{0:X2}", m);
                case "Colour":
                    return string.Format("{0:X2}{1:X2}{2:X2}", Raw[def.Offset+2], Raw[def.Offset+1], Raw[def.Offset]);
                case "Millis":
                    return BitConverter.ToUInt16(Raw, def.Offset).ToString();
                default:
                    return "?";
            }
        }

        public string ToProfileString()
        {
            StringBuilder sb = new StringBuilder();
            foreach (var def in Fields)
            {
                sb.AppendLine(string.Format("{0}={1}", def.Name, GetValue(def)));
            }
            return sb.ToString();
        }

        public static void LoadFromProfile(Config config, string[] lines)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line) || line.Trim().StartsWith("#")) continue;
                string[] parts = line.Split(new char[] { '=' }, 2);
                if (parts.Length != 2)
                {
                    throw new Exception(string.Format("Invalid config line {0}: {1}", i + 1, line));
                }

                string key = parts[0].Trim();
                string val = parts[1].Trim();
                int parenIdx = val.IndexOf('(');
                if (parenIdx > 0) val = val.Substring(0, parenIdx).Trim();

                try
                {
                    config.Set(key, val);
                }
                catch (Exception ex)
                {
                    throw new Exception(string.Format("Invalid config line {0}: {1} ({2})", i + 1, line, ex.Message));
                }
            }
        }

        public void Dump()
        {
            foreach (var def in Fields)
            {
                Console.WriteLine(string.Format("{0} = {1} ({2})", def.Name, GetValue(def), def.Desc));
            }
        }
    }

    class CliProgram
    {
        private static bool TryResolveProfilePath(string profilesDir, string profileName, out string fullPath, out string error)
        {
            fullPath = null;
            error = null;

            if (string.IsNullOrWhiteSpace(profileName))
            {
                error = "Profile name cannot be empty.";
                return false;
            }

            if (profileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || profileName.Contains("\\") || profileName.Contains("/"))
            {
                error = "Profile name contains invalid characters.";
                return false;
            }

            string candidate = Path.GetFullPath(Path.Combine(profilesDir, profileName + ".txt"));
            string baseDir = Path.GetFullPath(profilesDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (!candidate.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            {
                error = "Profile path escapes the profiles directory.";
                return false;
            }

            fullPath = candidate;
            return true;
        }

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: GpdControl <command> [args]");
                Console.WriteLine("Commands:");
                Console.WriteLine("  list              Show current device configuration");
                Console.WriteLine("  reset             Reset to default mappings (requires confirmation)");
                Console.WriteLine("  profile load <name>  Load a profile from 'profiles' folder");
                Console.WriteLine("  profile del <name>   Delete a profile");
                Console.WriteLine("  apply <file>      Apply a specific mapping file");
                Console.WriteLine("  set <key> <val>   Set a single key");
                return;
            }

            try
            {
                using (var device = new GpdDevice())
                {
                    device.Open();
                    byte[] data = device.ReadConfig();
                    var config = new Config(data);

                    string command = args[0].ToLower();
                    
                    if (command == "list")
                    {
                        Console.WriteLine(string.Format("Firmware: {0}", device.FirmwareVersion));
                        config.Dump();
                    }
                    else if (command == "reset")
                    {
                        Console.Write("Are you sure you want to reset to defaults? Type 'yes' to confirm: ");
                        string confirm = Console.ReadLine();
                        if (confirm == "yes")
                        {
                            if (File.Exists("default_mappings.txt"))
                            {
                                string[] lines = File.ReadAllLines("default_mappings.txt");
                                Config.LoadFromProfile(config, lines);
                                device.WriteConfig(config.Raw);
                                Console.WriteLine("Reset complete. Defaults applied.");
                            }
                            else
                            {
                                Console.WriteLine("Error: default_mappings.txt not found.");
                            }
                        }
                        else
                        {
                            Console.WriteLine("Operation terminated.");
                        }
                    }
                    else if (command == "profile")
                    {
                        string profilesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles");
                        if (!Directory.Exists(profilesDir)) Directory.CreateDirectory(profilesDir);

                        if (args.Length < 2) 
                        { 
                            Console.WriteLine("Usage: profile <load|del> <name>");
                            return; 
                        }
                        
                        string subCmd = args[1].ToLower();
                        
                        if (subCmd == "load")
                        {
                            if (args.Length < 3) { Console.WriteLine("Usage: profile load <name>"); return; }
                            string path;
                            string err;
                            if (!TryResolveProfilePath(profilesDir, args[2], out path, out err))
                            {
                                Console.WriteLine("Error: " + err);
                                return;
                            }
                            if (File.Exists(path))
                            {
                                string[] lines = File.ReadAllLines(path);
                                Config.LoadFromProfile(config, lines);
                                device.WriteConfig(config.Raw);
                                Console.WriteLine("Profile '" + args[2] + "' loaded to device.");
                            }
                            else
                            {
                                Console.WriteLine("Profile not found: " + path);
                            }
                        }
                        else if (subCmd == "del")
                        {
                            if (args.Length < 3) { Console.WriteLine("Usage: profile del <name>"); return; }
                            string path;
                            string err;
                            if (!TryResolveProfilePath(profilesDir, args[2], out path, out err))
                            {
                                Console.WriteLine("Error: " + err);
                                return;
                            }
                            if (File.Exists(path))
                            {
                                File.Delete(path);
                                Console.WriteLine("Profile '" + args[2] + "' deleted.");
                            }
                            else
                            {
                                Console.WriteLine("Profile not found.");
                            }
                        }
                    }
                    else if (command == "listdump")
                    {
                         if (args.Length < 2) { Console.WriteLine("Usage: listdump <filename>"); return; }
                         using (StreamWriter sw = new StreamWriter(args[1]))
                         {
                             Console.SetOut(sw); // Redirect Console.WriteLine to file
                             Console.WriteLine(string.Format("Firmware: {0}", device.FirmwareVersion));
                             config.Dump();
                             
                             sw.WriteLine("RAW DATA DUMP:");
                             sw.WriteLine(BitConverter.ToString(data));
                         }
                         var standardOutput = new StreamWriter(Console.OpenStandardOutput());
                         standardOutput.AutoFlush = true;
                         Console.SetOut(standardOutput);
                         Console.WriteLine("Dump written to " + args[1]);
                    }
                    else if (command == "set")
                    {
                        if (args.Length < 3) { Console.WriteLine("Usage: set <key> <value>"); return; }
                        config.Set(args[1], args[2]);
                        device.WriteConfig(config.Raw);
                        Console.WriteLine("Applied.");
                    }
                    else if (command == "apply")
                    {
                        if (args.Length < 2) { Console.WriteLine("Usage: apply <filename>"); return; }
                        string[] lines = File.ReadAllLines(args[1]);
                        Config.LoadFromProfile(config, lines);
                        device.WriteConfig(config.Raw);
                        Console.WriteLine("Applied configuration from file.");
                    }
                    else if (command == "dumpraw")
                    {
                        if (args.Length < 2) { Console.WriteLine("Usage: dumpraw <filename>"); return; }
                        File.WriteAllBytes(args[1], data);
                        Console.WriteLine(string.Format("Raw config dumped to {0}", args[1]));
                        StringBuilder sb = new StringBuilder();
                        for (int i = 0; i < data.Length; i++)
                        {
                            sb.Append(string.Format("{0:X2} ", data[i]));
                            if ((i + 1) % 16 == 0) sb.AppendLine();
                        }
                        File.WriteAllText(args[1] + ".txt", sb.ToString());
                        Console.WriteLine(string.Format("Hex text dump written to {0}.txt", args[1]));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("Error: {0}", ex.Message));
            }
        }
    }
}
