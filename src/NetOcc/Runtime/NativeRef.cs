// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;

namespace OCC.Core;

/// <summary>In/out slots of <c>ref</c> handle parameters (src/SWIG_files/common/Handles.i).</summary>
internal static class NativeRef
{
    /// <summary>
    /// The wrapper parks this in the slot before the C++ call and replaces it on success;
    /// still there afterwards means the call threw and the caller's variable is left as it was.
    /// </summary>
    public static readonly IntPtr Unchanged = new(-1);
}
