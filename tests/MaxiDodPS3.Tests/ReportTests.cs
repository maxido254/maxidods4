using MaxiDodPS3;
using Xunit;

namespace MaxiDodPS3.Tests;

public class Crc32Tests
{
    [Fact]
    public void MatchesStandardCheckValue()
    {
        Assert.Equal(0xCBF43926u, Crc32.Compute("123456789"u8));
    }

    [Fact]
    public void SeededValueMatchesZlib()
    {
        // zlib.crc32(b"\xa2" + b"123456789") computed independently in Python.
        Assert.Equal(0x63DA9F12u, Crc32.Compute((byte)0xA2, "123456789"u8));
    }
}

public class Ds3Tests
{
    [Fact]
    public void ParsesButtonsSticksAndTriggers()
    {
        var r = new byte[49];
        r[0] = 0x01;
        r[2] = 0x08 | 0x10;          // Start + Up
        r[3] = 0x40 | 0x04;          // Cross + L1
        r[4] = 0x01;                 // PS
        r[6] = 10; r[7] = 20; r[8] = 30; r[9] = 40;
        r[18] = 200; r[19] = 90;

        Assert.True(Ps3Report.TryParse(r, out var s));
        Assert.True(s.Start); Assert.True(s.Up); Assert.False(s.Down);
        Assert.True(s.Cross); Assert.True(s.L1); Assert.False(s.Circle);
        Assert.True(s.Ps);
        Assert.Equal(10, s.LX); Assert.Equal(20, s.LY); Assert.Equal(30, s.RX); Assert.Equal(40, s.RY);
        Assert.Equal(200, s.L2); Assert.Equal(90, s.R2);
    }

    [Fact]
    public void RejectsWrongReportId()
    {
        var r = new byte[49];
        r[0] = 0x02;
        Assert.False(Ps3Report.TryParse(r, out _));
    }

    [Fact]
    public void OutputIs49BytesAndCarriesMotors()
    {
        var o = Ps3Report.BuildOutput(strong: 180, weakOn: true, ledSlot: 2);
        Assert.Equal(49, o.Length);
        Assert.Equal(0x01, o[0]);
        Assert.Equal(0x01, o[2]);    // weak on
        Assert.Equal(180, o[4]);     // strong force
        Assert.Equal(0x08, o[9]);    // LED3
    }

    [Fact]
    public void OutputIsResizedToDeviceLength()
    {
        Assert.Equal(51, Ps3Report.BuildOutput(255, false, 0, length: 51).Length);
    }
}

public class Ds4Tests
{
    private static byte[] UsbReport()
    {
        var r = new byte[64];
        r[0] = 0x01;
        r[1] = 11; r[2] = 22; r[3] = 33; r[4] = 44;   // sticks
        r[5] = 0x20 | 0x02;                            // Cross + hat East
        r[6] = 0x01 | 0x20;                            // L1 + Options
        r[7] = 0x01 | 0x02;                            // PS + touchpad
        r[8] = 150; r[9] = 60;                         // L2 R2
        return r;
    }

    [Fact]
    public void ParsesUsbReport()
    {
        Assert.True(Ds4Report.TryParse(UsbReport(), out var s));
        Assert.Equal(11, s.LX); Assert.Equal(22, s.LY); Assert.Equal(33, s.RX); Assert.Equal(44, s.RY);
        Assert.True(s.Cross); Assert.True(s.Right); Assert.False(s.Up);
        Assert.True(s.L1); Assert.True(s.Start); Assert.True(s.Ps); Assert.True(s.Touchpad);
        Assert.Equal(150, s.L2); Assert.Equal(60, s.R2);
    }

    [Fact]
    public void ParsesBluetoothReportWithTwoExtraHeaderBytes()
    {
        var usb = UsbReport();
        var bt = new byte[78];
        bt[0] = 0x11;
        Array.Copy(usb, 1, bt, 3, 9);

        Assert.True(Ds4Report.TryParse(bt, out var s));
        Assert.Equal(11, s.LX); Assert.True(s.Cross); Assert.True(s.Ps); Assert.Equal(150, s.L2);
    }

    [Fact]
    public void HatReleasedMeansNoDirection()
    {
        var r = UsbReport();
        r[5] = 0x08;
        Assert.True(Ds4Report.TryParse(r, out var s));
        Assert.False(s.Up || s.Down || s.Left || s.Right);
    }

    [Fact]
    public void UsbOutputPlacesMotorsAndFlags()
    {
        var o = Ds4Report.BuildOutput(strong: 200, weak: 100, slot: 0, bluetooth: false, outLen: 0, flags: 0x07, lightbar: true);
        Assert.Equal(32, o.Length);
        Assert.Equal(0x05, o[0]);
        Assert.Equal(0x07, o[1]);
        Assert.Equal(100, o[4]);   // right / weak
        Assert.Equal(200, o[5]);   // left / strong
        Assert.Equal(0x40, o[8]);  // slot 1 = blue
    }

    [Fact]
    public void BluetoothOutputHasValidCrc()
    {
        var o = Ds4Report.BuildOutput(200, 100, 0, bluetooth: true, outLen: 0, flags: 0x07, lightbar: true);
        Assert.Equal(78, o.Length);
        Assert.Equal(0x11, o[0]);
        Assert.Equal(0xC0, o[1]);
        Assert.Equal(100, o[6]);
        Assert.Equal(200, o[7]);

        uint stored = BitConverter.ToUInt32(o, 74);
        Assert.Equal(Crc32.Compute((byte)0xA2, o.AsSpan(0, 74)), stored);
        // Expected value computed independently with Python's zlib for these exact inputs.
        Assert.Equal(0xC9020BC3u, stored);
    }
}

public class DualSenseTests
{
    [Fact]
    public void ParsesUsbReport()
    {
        var r = new byte[64];
        r[0] = 0x01;
        r[1] = 1; r[2] = 2; r[3] = 3; r[4] = 4;
        r[5] = 250; r[6] = 120;                 // L2 R2
        r[8] = 0x80 | 0x00;                     // Triangle + hat North
        r[9] = 0x02 | 0x10;                     // R1 + Create
        r[10] = 0x01 | 0x04;                    // PS + mute

        Assert.True(Ds5Report.TryParse(r, bluetooth: false, out var s));
        Assert.Equal(1, s.LX); Assert.Equal(4, s.RY);
        Assert.Equal(250, s.L2); Assert.Equal(120, s.R2);
        Assert.True(s.Triangle); Assert.True(s.Up);
        Assert.True(s.R1); Assert.True(s.Select); Assert.True(s.Ps);
    }

    [Fact]
    public void ParsesFullBluetoothReport()
    {
        var r = new byte[78];
        r[0] = 0x31;
        r[2] = 7; r[3] = 8; r[4] = 9; r[5] = 10;
        r[6] = 33; r[7] = 44;
        r[9] = 0x40;                            // Circle
        Assert.True(Ds5Report.TryParse(r, bluetooth: true, out var s));
        Assert.Equal(7, s.LX); Assert.Equal(33, s.L2); Assert.Equal(44, s.R2);
        Assert.True(s.Circle);
    }

    [Fact]
    public void BasicBluetoothReportUsesDs4Layout()
    {
        var r = new byte[78];
        r[0] = 0x01;
        r[1] = 5; r[2] = 6; r[3] = 7; r[4] = 8;
        r[5] = 0x20 | 0x08;                     // Cross, hat released
        r[8] = 77; r[9] = 88;
        Assert.True(Ds5Report.TryParse(r, bluetooth: true, out var s));
        Assert.Equal(5, s.LX); Assert.True(s.Cross); Assert.Equal(77, s.L2); Assert.Equal(88, s.R2);
    }

    [Fact]
    public void UsbOutputIs63BytesWithRumble()
    {
        var o = Ds5Report.BuildOutput(200, 100, 0, bluetooth: false, outLen: 0, hapticsSelect: true, lightbar: true, seq: 0);
        Assert.Equal(63, o.Length);
        Assert.Equal(0x02, o[0]);
        Assert.Equal(0x03, o[1]);   // compatible vibration + haptics select
        Assert.Equal(100, o[3]);    // right / weak
        Assert.Equal(200, o[4]);    // left / strong
    }

    [Fact]
    public void BluetoothOutputHasValidCrc()
    {
        var o = Ds5Report.BuildOutput(200, 100, 1, bluetooth: true, outLen: 0, hapticsSelect: true, lightbar: true, seq: 3);
        Assert.Equal(78, o.Length);
        Assert.Equal(0x31, o[0]);
        Assert.Equal(0x30, o[1]);   // sequence 3 in the high nibble
        Assert.Equal(0x10, o[2]);
        Assert.Equal(0x03, o[3]);
        Assert.Equal(100, o[5]);
        Assert.Equal(200, o[6]);
        Assert.Equal(0x0A, o[46]);  // player 2 LEDs
        Assert.Equal(0x40, o[47]);  // slot 2 = red

        uint stored = BitConverter.ToUInt32(o, 74);
        Assert.Equal(Crc32.Compute((byte)0xA2, o.AsSpan(0, 74)), stored);
        Assert.Equal(0x00541E0Cu, stored);
    }

    [Fact]
    public void HapticsSelectCanBeDisabled()
    {
        var o = Ds5Report.BuildOutput(1, 1, 0, false, 0, hapticsSelect: false, lightbar: false, seq: 0);
        Assert.Equal(0x01, o[1]);
        Assert.Equal(0x00, o[2]);   // lightbar control off
    }
}

public class ConfigTests
{
    [Fact]
    public void DefaultsAreValid()
    {
        var c = new Config();
        c.Normalize();
        Assert.Equal("x360", c.ModeFor(PadKind.Ps3));
        Assert.Equal("x360", c.ModeFor(PadKind.Ds4));
        Assert.Equal("x360", c.ModeFor(PadKind.DualSense));
        Assert.True(c.HideSourcePads);
    }

    [Fact]
    public void BadModeIsRejectedNotSilentlyChanged()
    {
        var c = new Config { Ds4Mode = "xinput" };
        Assert.Throws<ArgumentException>(() => c.Normalize());
    }

    [Fact]
    public void ModesAreIndependentPerKind()
    {
        var c = new Config { Ps3Mode = "x360", Ds4Mode = "native", DualSenseMode = "ds4" };
        c.Normalize();
        Assert.Equal("x360", c.ModeFor(PadKind.Ps3));
        Assert.Equal("native", c.ModeFor(PadKind.Ds4));
        Assert.Equal("ds4", c.ModeFor(PadKind.DualSense));
    }

    [Fact]
    public void ExtraDevicesAddCloneKinds()
    {
        var c = new Config { ExtraDevices = new List<string> { "1234:ABCD:ds4", "1111:2222", "3333:4444:ds5", "bad", "5555:6666:nope" } };
        var all = c.AllKnown();
        Assert.Contains(all, k => k.Vid == 0x1234 && k.Pid == 0xABCD && k.Kind == PadKind.Ds4);
        Assert.Contains(all, k => k.Vid == 0x1111 && k.Pid == 0x2222 && k.Kind == PadKind.Ps3);
        Assert.Contains(all, k => k.Vid == 0x3333 && k.Pid == 0x4444 && k.Kind == PadKind.DualSense);
        Assert.DoesNotContain(all, k => k.Vid == 0x5555);
    }
}

public class HidHideTests
{
    [Fact]
    public void ConvertsUsbInterfacePathToInstanceId()
    {
        string path = @"\\?\hid#vid_054c&pid_09cc#7&2a1b3c4d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
        Assert.Equal(@"HID\VID_054C&PID_09CC\7&2A1B3C4D&0&0000", HidHideManager.ToInstanceId(path));
    }

    [Fact]
    public void ConvertsBluetoothInterfacePathToInstanceId()
    {
        string path = @"\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&09cc#8&abc&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
        Assert.Equal(@"HID\{00001124-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&09CC\8&ABC&0&0000",
            HidHideManager.ToInstanceId(path));
    }
}
