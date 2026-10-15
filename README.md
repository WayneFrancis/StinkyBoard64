# StinkyBoard64

A small independent Windows (.NET 10 / WinForms) key configurator for up to two Stinky Gaming Footboards (USB VID:PID 27E0:0101, original firmware protocol). No extra libraries, macro editor, firmware flasher, reset commands, or developer tools.

## What it does

- Detect attached boards and read each board's four saved key bindings automatically; select a board to view it and edit independently.
- Assign one HID keyboard key to each of the four rocker directions. Existing button modes, modifier bits and millisecond timing are displayed and kept unchanged.
- **Save JSON** for the selected board or **Load JSON** into its unsent draft; never modifies the hardware on load.
- Show **USE AT YOUR OWN RISK** warning on every startup requiring Yes, with **No selected by default**.
- Use a single **Yes / No confirmation with No as the default** before any hardware write.
- Re-read settings from the target after a write to verify saved key/mode/modifier/interval values. No automatic retries.

## Run

Close original `Stinky.exe`; with .NET 10 SDK installed on Windows, run:

```powershell
git clone https://github.com/WayneFrancis/StinkyBoard64.git
cd StinkyBoard64
dotnet run --project src/StinkyBoard64
```

To publish a portable Windows x64 self-contained build:

```powershell
dotnet publish src/StinkyBoard64 -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Risk and limitations

**Using this software and writing to the boards is entirely at your own risk.** This software is independent of the Stinky manufacturer, without guarantees or warranty.

A write uses the known 12-report protocol and **replaces all four entire 64-byte key/sequence EEPROM pages** on the selected board, even when only one key changes. **Existing on-board macros and alternate-bank settings may be erased**. The last alternate-bank bytes cannot be read by the observed protocol. JSON is a *simple key-profile backup*, not a complete EEPROM backup. Save a JSON file for each board and preserve the original utility before editing.

Attachment numbers/Container IDs are not permanent physical serial numbers. Verify the target after moving cables. Writes stop if the connected topology changes, the original Stinky.exe is running, or no successful device read established a baseline. If a write fails, do not repeatedly retry it; USB disconnect/reconnect once and recover with the original utility if needed. Unsupported/unknown mode flags cause a read to fail rather than silently writing guesses. Multi-step macros are **not** supported: they were observed to replay only the first key with the old firmware. This tool deliberately writes only single-key mappings.

The HID protocol was physically validated in the separate StinkyFootboardRedone project, including independent writes to two attached boards. This new simplified UI requires physical testing in its own right.
