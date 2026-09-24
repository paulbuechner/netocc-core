// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;

namespace OCC.Core.TDF;

/// <summary>
/// Native references on a TDF_Data that label, attribute and TNaming_Builder proxies hold,
/// so the OCAF tree outlives them in any finalization order (src/SWIG_files/wrapper/TDF.i).
/// Each acquire returns the data with one reference added, or zero; release tolerates zero.
/// </summary>
internal static class DataReference
{
    // the native library with the TDF and TNaming wrappers, whose companions (extras/TDF.i, TNaming.i) define these
    private const string Library = "NetOccApplicationFramework";

    [DllImport(Library, EntryPoint = "NetOcc_TDF_AcquireLabelData")]
    public static extern IntPtr AcquireFromLabel(IntPtr label);

    [DllImport(Library, EntryPoint = "NetOcc_TDF_AcquireAttributeData")]
    public static extern IntPtr AcquireFromAttribute(IntPtr attribute);

    [DllImport(Library, EntryPoint = "NetOcc_TDF_AcquireBuilderData")]
    public static extern IntPtr AcquireFromBuilder(IntPtr builder);

    [DllImport(Library, EntryPoint = "NetOcc_TDF_ReleaseData")]
    public static extern void Release(IntPtr data);
}
