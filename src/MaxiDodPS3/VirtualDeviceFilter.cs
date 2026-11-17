using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace MaxiDodPS3;

/// <summary>
/// Recognises the virtual pads that ViGEmBus creates (including the virtual DualShock 4 this program makes in "ds4" mode),
/// so MaxiDodPS3 never mistakes its own output for a real controller and feeds it back in.
///
/// It asks Windows (Configuration Manager, read-only) whether the HID device sits under the ViGEmBus bus driver.
/// </summary>
public static class VirtualDeviceFilter
{
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(out uint parent, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_DevNode_Registry_PropertyW(
        uint devInst, uint property, out uint regDataType, byte[]? buffer, ref uint length, uint flags);

    private const uint CM_DRP_SERVICE = 0x00000005;

    // Only definite answers are cached; "could not tell yet" is re-checked on the next scan.
    private static readonly ConcurrentDictionary<string, bool> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True if the device is a ViGEm virtual pad. If Windows cannot answer yet (a virtual pad that has only just appeared),
    /// it is treated as virtual for now and checked again on the next scan, which errs on the side of never looping.
    /// </summary>
    public static bool IsVirtual(string hidDevicePath)
    {
        if (Cache.TryGetValue(hidDevicePath, out bool known)) return known;

        try
        {
            string instanceId = HidHideManager.ToInstanceId(hidDevicePath);
            if (CM_Locate_DevNodeW(out uint node, instanceId, 0) != 0) return true; // not ready yet; do not cache

            bool virtualPad = false;
            for (int depth = 0; depth < 6; depth++)
            {
                string? service = GetService(node);
                if (service != null && service.Equals("ViGEmBus", StringComparison.OrdinalIgnoreCase))
                {
                    virtualPad = true;
                    break;
                }
                if (CM_Get_Parent(out uint parent, node, 0) != 0) break;
                node = parent;
            }

            Cache[hidDevicePath] = virtualPad;
            return virtualPad;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false; // cannot check on this system: assume it is a real pad
        }
    }

    private static string? GetService(uint node)
    {
        uint len = 0;
        CM_Get_DevNode_Registry_PropertyW(node, CM_DRP_SERVICE, out _, null, ref len, 0);
        if (len == 0) return null;

        var buf = new byte[len];
        if (CM_Get_DevNode_Registry_PropertyW(node, CM_DRP_SERVICE, out _, buf, ref len, 0) != 0) return null;
        return Encoding.Unicode.GetString(buf, 0, (int)Math.Min(len, (uint)buf.Length)).TrimEnd('\0');
    }
}
