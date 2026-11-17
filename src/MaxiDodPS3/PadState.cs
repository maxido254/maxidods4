namespace MaxiDodPS3;

public enum PadKind { Ps3, Ds4, DualSense }

/// <summary>Controller state shared by every supported pad type (DS3, DS4, DualSense).</summary>
public struct PadState
{
    public byte LX, LY, RX, RY, L2, R2;

    /// <summary>Select (DS3), Share (DS4) or Create (DualSense).</summary>
    public bool Select;

    /// <summary>Start (DS3) or Options (DS4/DualSense).</summary>
    public bool Start;

    public bool L3, R3, Up, Right, Down, Left;
    public bool L1, R1, Triangle, Circle, Cross, Square, Ps, Touchpad;
}
