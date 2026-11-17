# Protocol notes used by MaxiDodPS3

Offsets include the HID report ID at index 0 (how HidSharp returns reports on Windows).
These follow public documentation of the PlayStation pad protocols (for example the Linux `hid-playstation` and `hid-sony` drivers). Verify against your own pad when something looks off; clones especially.

## DualShock 3 / Sixaxis (054C:0268)

**Enable (USB only):** feature report `0xF4`: `F4 42 03 00 00`. Without it a USB pad stays silent.

**Input report 0x01 (49 bytes)**

| Offset | Meaning |
|---|---|
| 2 | Select, L3, R3, Start, Up, Right, Down, Left (bits 0-7) |
| 3 | L2, R2, L1, R1, Triangle, Circle, Cross, Square (bits 0-7) |
| 4 | PS button (bit 0) |
| 6-9 | LX, LY, RX, RY (0-255, centre about 128, Y grows downward) |
| 18, 19 | L2, R2 analog pressure |

**Output report 0x01 (49 bytes, resized to the device's declared length)**

| Offset | Meaning |
|---|---|
| 1 | Weak motor duration (`0xFF` = until changed) |
| 2 | Weak motor on/off |
| 3 | Strong motor duration |
| 4 | Strong motor force 0-255 |
| 9 | LED mask (`0x02` LED1, `0x04` LED2, `0x08` LED3, `0x10` LED4) |
| 10-29 | Four 5-byte LED config blocks |

**Pairing (USB only):** feature report `0xF5`: `F5 01 00 <6-byte host Bluetooth address>`.

## DualShock 4 (054C:05C4, 054C:09CC)

**Input.** USB report `0x01` (64 bytes). Over Bluetooth the pad first sends a basic `0x01` report with the same field layout; reading feature report `0x02` makes it switch to the full report `0x11`, which is the same data starting two bytes later. Field offsets for `0x01` (add 2 for `0x11`):

| Offset | Meaning |
|---|---|
| 1-4 | LX, LY, RX, RY (0-255, Y grows downward) |
| 5 | bits 0-3 hat (0 = up, clockwise, 8 = released); bit 4 Square, 5 Cross, 6 Circle, 7 Triangle |
| 6 | L1, R1, L2 (digital), R2 (digital), Share, Options, L3, R3 (bits 0-7) |
| 7 | bit 0 PS, bit 1 touchpad click |
| 8, 9 | L2, R2 analog |

**Output.**

| Transport | Report | Layout |
|---|---|---|
| USB | `0x05`, 32 bytes | `[1]` flags (7 = rumble + lightbar + blink), `[4]` right/weak motor, `[5]` left/strong motor, `[6..8]` lightbar RGB |
| Bluetooth | `0x11`, 78 bytes | `[1]` = `0xC0`, `[3]` flags, `[6]` weak, `[7]` strong, `[8..10]` RGB, `[74..77]` CRC-32 (little-endian) |

The Bluetooth CRC is the standard CRC-32 of `0xA2` followed by bytes `0..73`.

## DualSense (054C:0CE6, 054C:0DF2 Edge)

**Input.** USB report `0x01` (64 bytes). Over Bluetooth the pad sends a basic `0x01` report (DS4 layout) until feature report `0x05` is read, then full report `0x31` (same data, starting at index 2). Offsets for USB `0x01` (add 1 for `0x31`):

| Offset | Meaning |
|---|---|
| 1-4 | LX, LY, RX, RY |
| 5, 6 | L2, R2 analog |
| 8 | hat + face buttons (same bits as DS4 byte 5) |
| 9 | L1, R1, L2, R2, Create, Options, L3, R3 |
| 10 | PS (bit 0), touchpad click (bit 1), mute (bit 2) |

**Output.** USB report `0x02` (63 bytes) or Bluetooth report `0x31` (78 bytes: `[1]` sequence in the high nibble, `[2]` = `0x10`, common block from index 3, CRC-32 at `[74..77]` over `0xA2` + bytes `0..73`). Common block (index `c` = 1 for USB, 3 for Bluetooth):

| Offset | Meaning |
|---|---|
| c | `0x01` compatible vibration, `0x02` haptics select |
| c+1 | `0x04` lightbar control, `0x10` player LED control |
| c+2 | right (weak) motor |
| c+3 | left (strong) motor |
| c+43 | player LED mask |
| c+44..46 | lightbar RGB |

## Rumble mapping

Games send two motors (0-255 each). DS4 and DualSense take both values directly. A DS3's strong motor takes its value directly and its weak motor is on/off, switching on at `SmallMotorThreshold`. Each value can be scaled with `LargeMotorScale` / `SmallMotorScale`.

Clones may need: a periodic re-send, the motors swapped, `HidD_SetOutputReport` instead of `WriteFile` (`WriteMethod`), a DS3 finite duration or no LED bytes, DS4 flags of 255, or DualSense without the haptics-select bit. `rumble-test` tries these.

## Not implemented yet

Motion sensors, touchpad coordinates, battery level, adaptive triggers/haptics, and forwarding a game's lightbar colour.
