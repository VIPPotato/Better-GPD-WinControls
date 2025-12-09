# Better GPD WinControl

## Introduction

A few months ago, I became the happy owner of a GPD Win 4.

I am totally blind, and the original WinControls app is based on images and was made with DuiLib, which is not screenreader friendly. Because of this, blind GPD users are unable to remap gamepad buttons on their own if they are using Windows.

Recently, I stumbled upon a repository by @Cryolitia, who successfully reverse-engineered WinControls and documented the protocol it uses. Without their work, my implementation would be impossible.

So, since I can't code myself, I sat down with Gemini 3 Pro and made a nice implementation of WinControls based on Cryolitia's documentation and code. It took me lots of time to make sure remapping works properly, but I somehow succeeded.

And that's what this repo is: a command line app which works on Windows along with a GUI for easier access, both written in C#.

These apps were made with accessibility and user-friendliness in mind, though I'm not sure if they look fine visually.

## Caution

This software writes raw data directly to the Embedded Controller (EC) firmware of your device. Incorrect data or unexpected errors during this process could potentially brick your device or cause irreversible damage. While it works on my machine, I cannot guarantee it is safe for yours. **Use this software entirely at your own risk.**

## Features

- **Tiny and Accessible:** Designed to be lightweight and usable with screen readers.
- **Full Remapping:** Remap all controller buttons, including the back buttons (L4/R4).
- **Macros:** Support for mapping buttons to macro keys.
- **Settings:** Adjust stick deadzones, centering, rumble strength, and LED modes.
- **Profiles:** Create, edit, and load multiple configuration profiles on the fly.
- **Capture Key:** Easily map buttons by pressing them in the GUI.

## How to use the GUI

1.  Launch `GpdGui.exe`.
2.  **Buttons/Macros:** Navigate to the "Buttons" or "Macros" tabs. Select a control from the list, then choose a key from the dropdown or click "Capture Key" to press the button you want to map.
3.  **Settings:** Use the "Settings" tab to adjust deadzones, rumble, and LED color.
4.  **Profiles:** Use the "Profiles" tab to create new profiles from your current settings, or load existing ones.
5.  **Apply:** Click "Apply Changes" to write your configuration to the device.

## How to use the command line app

Open a terminal and run `GpdControl.exe` with one of the following commands:

- `list`: Shows the current device configuration.
- `reset`: Resets all mappings to defaults (asks for confirmation).
- `profile load <name>`: Loads a profile from the 'profiles' folder.
- `profile del <name>`: Deletes a profile.
- `apply <file>`: Applies a specific mapping text file.
- `set <key> <val>`: Sets a single key mapping immediately.

## How to compile

You need the C# compiler (`csc.exe`) usually found in the .NET Framework folder. Run these commands in the source directory:

```powershell
# Compile CLI
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /target:exe /out:GpdControl.exe GpdControl.cs

# Compile GUI
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /target:winexe /out:GpdGui.exe /main:GpdGui.Program GpdGui.cs GpdControl.cs
```

## License

GPLv3

## Thanks to

**Cryolitia** - For reverse-engineering the WinControls protocol and making this possible.