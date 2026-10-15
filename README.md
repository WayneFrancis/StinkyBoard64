# StinkyBoard64

A Windows x64 (.NET 10 / WinForms) configuration editor for Stinky Gaming Footboards, plus an **optional Windows Macro Host** for executing macros triggered by the footboard or an ordinary keyboard.

The two programs are independent: the configuration editor can read/write the board, but the **Macro Host does not open HID write handles, flash firmware, or change EEPROM**. No interception starts until you explicitly select **Start intercepting** in the Macro Host.

## Configuration editor

- Detect attached Stinky boards and read the four stored key assignments independently.
- Assign a keyboard HID key to each direction. Existing button modes, modifier bits and timing are displayed and preserved by this simplified editor.
- Save and load per-board JSON configuration files.
- A hardware write requires a successful baseline read and an explicit warning/confirmation (No is the default); the editor then reads back and verifies the result.
- **Open Macro Host** launches the separately built host; from a source checkout it runs the current source project, avoiding stale Debug binaries.

## Windows Macro Host

Macro Host is a host-side macro engine, not an on-board firmware macro editor. It supports:

- Arbitrary keyboard trigger keys and as many trigger mappings as you need, including **left/right/both/either** Ctrl, Alt, Shift and Windows-key modifiers.
- A **Text** macro (Unicode text with no intentional per-character delay) or an ordered **Steps** macro.
- Keyboard down/up/press, explicit millisecond delays, left/right/middle mouse button down/up/click/double-click, wheel up/down, and **Beep** with adjustable frequency and duration.
- Individual macro JSON import/export **inside the macro editor** and whole-profile JSON import/export from the main Macro Host window.
- Playback speed control for default key/mouse hold time, diagnostic test output, keyboard-hook identification, and Windows tray controls.
- Numeric keypad scan-code normalization, including the Windows synthetic-Shift workaround for combos such as **Left Ctrl + Right Alt + Right Shift + Numpad 7**.
- Backward compatibility with host version-2 JSON profiles containing F5–F12 text macros.

The input hook is **global** to physical keyboards and cannot identify which USB keyboard sent a key. Configured trigger keys are suppressed while interception is active. Modifier key events themselves are not suppressed, and reserved Windows shortcuts may still have operating-system side effects. Input sent through Windows `SendInput` may not be accepted by applications using protected or device-specific Raw Input. Test carefully in Notepad before gaming.

## Run from source

Install the **.NET 10 SDK** on Windows.

```powershell
git clone https://github.com/WayneFrancis/StinkyBoard64.git
cd StinkyBoard64
dotnet run --project src/StinkyBoard64
```

Click **Open Macro Host** in the editor, or run Macro Host directly:

```powershell
dotnet run --project src/StinkyBoard64.MacroHost
```

You do **not** need a board connected to run Macro Host or test a macro's simulated output. You do need a footboard to program the physical board's trigger keys. Press **Start intercepting** explicitly to arm the host; stopping interception returns configured key combinations to normal.

## Portable Windows build

The [GitHub Actions build](https://github.com/WayneFrancis/StinkyBoard64/actions) produces a `StinkyBoard64-Windows-x64` artifact containing a ZIP with this layout:

```
Editor/StinkyBoard64.exe
MacroHost/StinkyBoard64.MacroHost.exe
```

The ZIP includes the self-contained .NET runtime. Extract **both directories side by side** so the editor can find the Macro Host. You may also run the host executable directly without the editor.

To build the two applications yourself:

```powershell
dotnet publish src/StinkyBoard64 -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish src/StinkyBoard64.MacroHost -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
dotnet test tests/StinkyBoard64.MacroTests -c Release
```

## Risk and limitations

**USE AT YOUR OWN RISK.** StinkyBoard64 is independent of the original manufacturer, provided without warranty. A board write **replaces all four 64-byte key/sequence EEPROM pages** on the selected board, even when changing one key. Existing firmware macros, alternate-bank data, and bytes that cannot be completely read may be lost. Saving JSON preserves a simple configuration, **not a full EEPROM backup**. Preserve the original utility for recovery, use the read-back verification, and don't repeatedly retry failed writes.

The Macro Host itself never writes to the board. Its global input hook can suppress keyboard shortcuts from any keyboard while armed; use **Stop interception** or **Exit** from the window or tray to release them. Windows-generated input does not guarantee compatibility with all games.

StinkyBoard64's hardware editor is a separate, simplified implementation from the private development project; the host-side macro engine is included here as an independent Windows component.
