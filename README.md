# MaxiDodPS3

One app for **PS3, PS4 and PS5 controllers** on Windows (genuine or clone, USB or Bluetooth). Each pad can become a virtual **Xbox 360 (XInput)** pad, a virtual **DualShock 4**, or be left alone, and **rumble is passed back to the real pad**. Real pads are hidden from games so nothing shows up twice.

## Runs in the background (tray) and starts with Windows

MaxiDodPS3 is now a tray app, like WhatsApp: no window, an icon near the clock, and it keeps your pads working while you play.

- **Starts with Windows** (turned on the first time you run it, toggle it in the tray menu). It starts hidden with `--background`.
- **Closing a window only hides it.** The one way to quit is tray icon > **Exit** (that also un-hides your real pads).
- **One copy only.** Starting it again just brings up the running copy.
- **Waits for drivers.** If ViGEmBus is not ready yet at boot, it keeps retrying instead of quitting.
- Tray balloons tell you when a controller connects or disconnects. The log is in `%LOCALAPPDATA%\MaxiDodPS3\log.txt`.
- HidHide needs administrator rights. Run MaxiDodPS3 once as administrator and switch *Start with Windows* off and on: it then registers a scheduled task that starts it elevated at sign-in, with no UAC prompt each time.

## Debugger window (temporary)

Builds include a debugger so you can find the problem first and send me a screenshot. Start MaxiDodPS3 by hand (or tray icon > *Open debugger*). It shows drivers found, each pad live (input reports, buttons, rumble requests from games, whether writes to the pad succeed), a log, and three rumble tests:

1. **Direct rumble** (app to pad)
2. **Through XInput** (the path a game uses)
3. **Sweep all write methods** (tells you which letters you felt)

The **Controllers** tab shows a grey picture of each connected pad (up to `MaxPads`, for local multiplayer). A pressed button turns **red** and fades out 3 seconds after release, so you can test the others meanwhile; sticks and triggers react too, and the two motor circles turn red and shake while the pad is told to vibrate. Three buttons there rumble the large motor, the small motor, or both on every pad.

Press **F12** or the **Screenshot** button: it saves a PNG to `Pictures\MaxiDodPS3`, copies it to the clipboard and opens its folder. **Save report** writes everything as text.

**To remove the debugger:** in `src/MaxiDodPS3/MaxiDodPS3.csproj` set `<EnableDebugger>false</EnableDebugger>` (or build with `-p:EnableDebugger=false`; CI already publishes a `-release` artifact without it). To delete it for good, also delete the `src/MaxiDodPS3/Debugger/` folder.

> **Status: first release, not yet compiled or tested on Windows.** It was written without a Windows/.NET build environment. The GitHub Actions workflow builds it and runs the unit tests on every push, so expect to fix a few small compile or protocol issues on the first run, and test on real hardware. See "Known limits".

## What it does

- **DualShock 3, DualShock 4 and DualSense** input over USB and Bluetooth, up to 4 pads at once, hot-plug, player-number lightbar/LEDs
- **You choose the output per kind of pad:** `x360` (XInput), `ds4` (not XInput) or `native` (don't touch it)
- **No duplicate pads:** the real pad is hidden from games with HidHide, one pad on both USB and Bluetooth counts once, and `native` mode creates nothing virtual
- **Rumble to the real pad:** DS3 (strong variable, weak on/off), DS4 and DualSense (both motors variable)
- **Clone pads:** extra VID:PID entries with a pad kind, plus tunable rumble methods and a `rumble-test` tool that finds the one your pad accepts
- `doctor` (read-only check), `pair` (point a USB DS3 at your PC's Bluetooth radio), `unhide` (undo hiding)

## Does it corrupt Bluetooth drivers?

No. MaxiDodPS3 is a normal user-mode program. It never installs, replaces or modifies any driver, and it only reads your Bluetooth radio address. The old tools that broke Bluetooth did so by swapping the radio's driver; this does not.

## Choosing XInput or not

In `maxidodps3.json` (next to the exe), or on the command line:

```
MaxiDodPS3 --mode x360                 everything as Xbox 360 (XInput)
MaxiDodPS3 --mode ds4                  everything as DualShock 4
MaxiDodPS3 --ps3-mode x360 --ds4-mode native --ds5-mode native
```

| Mode | What games see | Use it when |
|---|---|---|
| `x360` | A virtual Xbox 360 pad (XInput) | Almost any PC game, including FIFA/FC |
| `ds4` | A virtual DualShock 4 | Games or emulators that want PlayStation pads and not XInput |
| `native` | Nothing from MaxiDodPS3: the pad is left alone | The pad already works in the game (a DS4/DualSense with Steam Input, or DS3 through another driver) |

## No more duplicate controllers (the FIFA / FC problem)

Early DsHidMini showed a DS3 twice in games like FIFA and FC: once as the real pad, once as XInput. MaxiDodPS3 avoids this four ways:

1. **Hides the real pad.** While a virtual pad is active the real one is hidden from games with [HidHide](https://github.com/nefarius/HidHide), so a game only sees the virtual pad. MaxiDodPS3 whitelists itself so it can still read the real pad. Hidden pads are un-hidden when you quit, and `MaxiDodPS3 unhide` fixes things if a run ever crashes.
2. **Never reads its own output.** In `ds4` mode the virtual DualShock 4 looks like a real DS4, so MaxiDodPS3 asks Windows whether a device sits under the ViGEmBus driver and ignores those. (Virtual pads from other tools, such as DS4Windows, are not recognised, so don't run them at the same time.)
3. **Counts one pad once.** A DS4/DualSense that is on USB and Bluetooth at the same time is taken over USB only.
4. **`native` mode** creates nothing virtual, so there is nothing to duplicate.

You need HidHide installed (and usually an administrator prompt the first time) for #1. Without it MaxiDodPS3 still works and tells you duplicates are possible. `MaxiDodPS3 doctor` shows what it found. If you already use DsHidMini or Steam Input on a pad, set that kind to `native`, or turn the other tool's XInput/DS4 emulation off.

## Fake / clone pads and rumble

Clone pads often rumble over USB but not wirelessly on PC, even when they work wirelessly on a phone. That means the pad is fine and the PC-side way of sending the rumble command is the problem. Clones differ in what they accept, so:

```
MaxiDodPS3 rumble-test
```

It tries several ways of sending rumble for your kind of pad (re-sending while active, swapped motors, a different write method, and per-kind flags: DS3 duration/LED bytes, DS4 flags, DualSense haptics-select), asks which one you felt, and prints the settings to paste into `maxidodps3.json`. Run it once over USB and once wirelessly. If your clone shows up under a different VID/PID, add it to `ExtraDevices` as `"VID:PID:ds4"` (or `ps3`, `ds5`).

If USB works but nothing works wirelessly, the wireless driver is not forwarding output reports to the pad, which no setting here can fix.

## Connecting pads

- **DS4 / DualSense:** pair in Windows Bluetooth settings (DS4: hold PS + Share; DualSense: hold Create + PS). No extra driver needed.
- **DS3 wireless:** install the signed [BthPS3](https://github.com/nefarius/BthPS3) profile driver (it sits on top of the Microsoft Bluetooth stack and leaves the radio driver alone), plug the pad in over USB, run `MaxiDodPS3 pair`, unplug, press the PS button. MaxiDodPS3 deliberately does not reinvent that kernel driver.
- **Any pad over USB:** just plug it in.

## Install and run

Requires Windows 10/11, the .NET 8 runtime and [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases). HidHide is recommended.

```
dotnet build src/MaxiDodPS3 -c Release
dotnet run --project src/MaxiDodPS3 -- doctor   # command-line tools print to the console you ran them from
dotnet run --project src/MaxiDodPS3 -- --mode x360   # starts the tray app
```

The included GitHub Actions workflow builds a Windows artifact and runs the tests on every push.

## Known limits

- **Not yet compiled or tested on Windows or real pads.** Report layouts follow the public DS3/DS4/DualSense documentation (see [docs/PROTOCOL.md](docs/PROTOCOL.md)), and the unit tests check them, but hardware may differ, clones especially.
- **No DualSense virtual output (DS5 mode).** ViGEm has no DualSense target, so it needs a custom virtual HID driver. DualSense pads are fully supported as *input*; see [docs/DS5.md](docs/DS5.md). Use `ds4` mode for PlayStation-style output.
- **No settings window.** Settings are in `maxidodps3.json`; the only windows are the tray menu and the temporary debugger.
- Not implemented yet: motion sensors, touchpad coordinates, battery level, adaptive triggers, forwarding the game's lightbar colour, DS4 USB Wireless Adapter dongle.
- HidHide's command-line usage in this code follows its documented options but is untested here.
- ViGEmBus was officially retired by its author in 2023. It still works, but check its status.

## Layout

```
src/MaxiDodPS3/          C# source
tests/MaxiDodPS3.Tests/  unit tests (report parsing, CRC, config, HidHide paths)
docs/                    protocol notes and the DualSense output design
maxidodps3.json          settings
```

MIT licensed. ViGEmBus, HidHide and BthPS3 are separate projects with their own licences and are not bundled.
