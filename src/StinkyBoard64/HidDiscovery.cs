using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace StinkyFootboard.Diagnostics;

/// <summary>Read-only enumeration of Windows HID interfaces. No HID input/output/feature reports are sent.</summary>
internal static class HidDiscovery
{
  public static IReadOnlyList<HidDescriptor> Enumerate()
  {
    if (!OperatingSystem.IsWindows())
      throw new PlatformNotSupportedException("HID discovery requires Windows.");

    Native.HidD_GetHidGuid(out Guid hidGuid);
    nint deviceSet = Native.SetupDiGetClassDevs(ref hidGuid, null, nint.Zero, 0x12); // PRESENT | DEVICEINTERFACE
    if (deviceSet == new nint(-1))
      throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

    var devices = new List<HidDescriptor>();
    try
    {
      for (uint i = 0; ; i++)
      {
        var interfaceData = new Native.SP_DEVICE_INTERFACE_DATA
        {
          Size = (uint)Marshal.SizeOf<Native.SP_DEVICE_INTERFACE_DATA>()
        };
        if (!Native.SetupDiEnumDeviceInterfaces(deviceSet, nint.Zero, ref hidGuid, i, ref interfaceData))
        {
          int error = Marshal.GetLastWin32Error();
          if (error != 259) // ERROR_NO_MORE_ITEMS
            throw new System.ComponentModel.Win32Exception(error);
          break;
        }

        Native.SetupDiGetDeviceInterfaceDetailProbe(deviceSet, ref interfaceData, nint.Zero, 0, out uint required, nint.Zero);
        if (required < 6 || required > 65536)
          continue;

        nint detail = Marshal.AllocHGlobal((int)required);
        try
        {
          // The cbSize value differs by process bitness, but the UTF-16
          // DevicePath field begins at offset 4 in both layouts.
          Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
          var devInfo = new Native.SP_DEVINFO_DATA
          {
            Size = (uint)Marshal.SizeOf<Native.SP_DEVINFO_DATA>()
          };
          if (!Native.SetupDiGetDeviceInterfaceDetail(deviceSet, ref interfaceData, detail, required, out _, ref devInfo))
            continue;

          string? path = Marshal.PtrToStringUni(nint.Add(detail, 4));
          if (string.IsNullOrEmpty(path))
            continue;

          Guid? containerId = GetContainerId(deviceSet, ref devInfo);
          // Open with zero access (metadata only), sharing with applications
          // already using the device. No ReadFile/WriteFile/SetFeature calls.
          using SafeFileHandle handle = Native.CreateFile(path, 0, 3, nint.Zero, 3, 0, nint.Zero);
          if (handle.IsInvalid)
          {
            devices.Add(new HidDescriptor(path, null, null, null, containerId,null, null, null, null, null, null, null,$"Cannot open for metadata: Win32 {Marshal.GetLastWin32Error()}"));
            continue;
          }

          var attributes = new Native.HIDD_ATTRIBUTES
          {
            Size = (uint)Marshal.SizeOf<Native.HIDD_ATTRIBUTES>()
          };
          if (!Native.HidD_GetAttributes(handle, ref attributes))
            continue;

          ushort? usagePage = null, usage = null;
          ushort? inputLength = null, outputLength = null, featureLength = null;
          if (Native.HidD_GetPreparsedData(handle, out nint preparsed))
          {
            try
            {
              if (Native.HidP_GetCaps(preparsed, out var caps) == 0x00110000)
              {
                usagePage = caps.UsagePage;
                usage = caps.Usage;
                inputLength = caps.InputReportByteLength;
                outputLength = caps.OutputReportByteLength;
                featureLength = caps.FeatureReportByteLength;
              }
            }
            finally { Native.HidD_FreePreparsedData(preparsed); }
          }

          devices.Add(new HidDescriptor(
              path, attributes.VendorId, attributes.ProductId,
              attributes.Version, containerId,
              ReadHidString(handle, Native.HidD_GetManufacturerString),
              ReadHidString(handle, Native.HidD_GetProductString),
              usagePage, usage, inputLength, outputLength, featureLength, null));
        }
        finally { Marshal.FreeHGlobal(detail); }
      }
    }
    finally { Native.SetupDiDestroyDeviceInfoList(deviceSet); }

    return devices;
  }

  private static Guid? GetContainerId(nint deviceSet, ref Native.SP_DEVINFO_DATA info)
  {
    var key = new Native.DEVPROPKEY
    {
      FormatId = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"),
      PropertyId = 2
    };
    var data = new byte[16];
    if (Native.SetupDiGetDeviceProperty(deviceSet, ref info, ref key, out _, data, (uint)data.Length, out uint actual, 0) && actual == 16)
      return new Guid(data);
    return null;
  }

  private delegate bool HidStringGetter(SafeFileHandle handle, nint buffer, uint length);
  private static string? ReadHidString(SafeFileHandle handle, HidStringGetter getter)
  {
    const int size = 512;
    nint buffer = Marshal.AllocHGlobal(size);
    try
    {
      Marshal.Copy(new byte[size], 0, buffer, size);
      return getter(handle, buffer, size) ? Marshal.PtrToStringUni(buffer)?.TrimEnd('\0') : null;
    }
    finally { Marshal.FreeHGlobal(buffer); }
  }

  private static class Native
  {
    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
      public uint Size;
      public Guid ClassGuid;
      public uint Flags;
      public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA
    {
      public uint Size;
      public Guid ClassGuid;
      public uint DevInst;
      public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DEVPROPKEY
    {
      public Guid FormatId;
      public uint PropertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES
    {
      public uint Size;
      public ushort VendorId, ProductId, Version;
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

    [DllImport("hid.dll")]
    public static extern void HidD_GetHidGuid(out Guid guid);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool HidD_GetAttributes(SafeFileHandle handle, ref HIDD_ATTRIBUTES attrs);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out nint preparsed);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool HidD_FreePreparsedData(nint preparsed);

    [DllImport("hid.dll")]
    public static extern int HidP_GetCaps(nint preparsed, out HIDP_CAPS caps);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool HidD_GetManufacturerString(SafeFileHandle handle, nint buffer, uint size);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool HidD_GetProductString(SafeFileHandle handle, nint buffer, uint size);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, nint parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiEnumDeviceInterfaces(nint set, nint device, ref Guid guid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA data);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceInterfaceDetailProbe(nint set, ref SP_DEVICE_INTERFACE_DATA data, nint detail, uint size, out uint required, nint info);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(nint set, ref SP_DEVICE_INTERFACE_DATA data, nint detail, uint size, out uint required, ref SP_DEVINFO_DATA info);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDevicePropertyW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceProperty(nint set, ref SP_DEVINFO_DATA info, ref DEVPROPKEY key, out uint propertyType, [Out] byte[] buffer, uint bufferSize, out uint required, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiDestroyDeviceInfoList(nint set);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFile(string path, uint access, uint sharing, nint security, uint disposition, uint flags, nint template);
  }
}

internal sealed record HidDescriptor(
    string Path,
    ushort? VendorId,
    ushort? ProductId,
    ushort? Version,
    Guid? ContainerId,
    string? Manufacturer,
    string? Product,
    ushort? UsagePage,
    ushort? Usage,
    ushort? InputReportLength,
    ushort? OutputReportLength,
    ushort? FeatureReportLength,
    string? Error);
