# DualSense (DS5): what works and what does not

## Works now

A real **DualSense or DualSense Edge pad is fully supported as an input device** (USB and Bluetooth): buttons, sticks, analog triggers, touchpad click, and rumble back to the pad. It can be presented to games as a virtual Xbox 360 or DualShock 4.

## Not yet: a *virtual* DualSense output

Presenting something to a game *as* a DualSense is a different problem. ViGEmBus can create Xbox 360 and DualShock 4 targets only, so a user-mode app alone cannot present a DualSense. That needs its own **virtual HID driver**.

I chose not to ship an untested kernel driver. A driver that cannot be compiled and tested on real Windows is a risk to your machine, and this release was written without a Windows build environment. DS4 output already works in nearly every game that supports PlayStation pads.

## What the driver would need

- **Framework:** KMDF plus the Virtual HID Framework (VHF), so Windows enumerates a HID device the normal way.
- **Identity:** VID `054C`, PID `0CE6`.
- **Report descriptor:** must match a real DualSense: input report `0x01`, output report `0x02` (rumble, adaptive triggers, lightbar, player LEDs), and the feature reports games probe (`0x05` calibration, `0x09` pairing info, `0x20` firmware info). Capture these from a real pad rather than writing them from memory.
- **User-mode channel:** an IOCTL interface so MaxiDodPS3 can submit input reports and receive output reports from games.
- **Mapping:** DS3/DS4 pads have no adaptive triggers or DualSense-format gyro, so those fields would be neutral at first.

## Plan

1. Build the driver in a VM with test-signing on. It never touches the Bluetooth stack.
2. Add a `Ds5Pad : IVirtualPad` in MaxiDodPS3 that talks to it. The `IVirtualPad` interface already isolates this, and a new `"ds5"` mode would sit beside `x360` and `ds4`.
3. Distribution to normal users needs Microsoft attestation signing (EV certificate and a Hardware Dev Center account).

If you want to start this, begin with step 1 and a captured DualSense descriptor.
