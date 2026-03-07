using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace VsBridge;

// == ComHelper — COM interop utilities == //
internal static class ComHelper
{
    // == P/Invoke declarations == //
    [DllImport("ole32.dll")]
    private static extern int CLSIDFromProgID(
        [MarshalAs(UnmanagedType.LPWStr)] string lpszProgID,
        out Guid lpclsid);

    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(
        ref Guid rclsid,
        IntPtr pvReserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable pprot);

    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(int reserved, out IBindCtx ppbc);

    // == GetActiveObject — replaces Marshal.GetActiveObject (removed in .NET Core) == //
    /// <summary>
    /// Replacement for Marshal.GetActiveObject which was removed in .NET Core.
    /// </summary>
    public static object GetActiveObject(string progId)
    {
        int hr = CLSIDFromProgID(progId, out Guid clsid);
        Marshal.ThrowExceptionForHR(hr);
        GetActiveObject(ref clsid, IntPtr.Zero, out object obj);
        return obj;
    }

    // == FindDteInstance — returns newest running VS DTE == //
    /// <summary>
    /// Finds the best available Visual Studio DTE instance.
    /// Prefers VS 2026 (18.0), falls back to VS 2022 (17.0).
    /// </summary>
    public static object? FindDteInstance()
    {
        var instances = EnumerateVsInstances();

        var best = instances
            .OrderByDescending(i => i.Version)      // Prefer VS 2026, then VS 2022
            .FirstOrDefault();

        return best?.Dte;
    }

    // == EnumerateVsInstances — walks ROT for VS DTE entries == //
    /// <summary>
    /// Enumerates all running Visual Studio instances from the Running Object Table.
    /// </summary>
    public static List<VsInstance> EnumerateVsInstances()
    {
        var results = new List<VsInstance>();

        int hr = GetRunningObjectTable(0, out IRunningObjectTable rot);
        if (hr != 0) return results;

        rot.EnumRunning(out IEnumMoniker enumMoniker);
        enumMoniker.Reset();

        var monikers = new IMoniker[1];
        while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
        {
            CreateBindCtx(0, out IBindCtx ctx);
            monikers[0].GetDisplayName(ctx, null, out string displayName);

            // ROT entries look like: !VisualStudio.DTE.17.0:12345
            if (!displayName.StartsWith("!VisualStudio.DTE.")) continue;

            try
            {
                rot.GetObject(monikers[0], out object obj);

                // Parse version from display name (e.g. "17.0" from "!VisualStudio.DTE.17.0:12345")
                var versionStr = displayName.Split(':')[0].Replace("!VisualStudio.DTE.", "");
                if (double.TryParse(versionStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double version))
                {
                    string solutionPath = "";
                    try
                    {
                        dynamic dte = obj;
                        solutionPath = dte.Solution.FullName ?? "";
                    }
                    catch { }

                    results.Add(new VsInstance(obj, version, displayName, solutionPath));
                }
            }
            catch { }
        }

        return results;
    }
}

// == VsInstance — lightweight record for a discovered VS instance == //
internal record VsInstance(object Dte, double Version, string DisplayName, string SolutionPath);
