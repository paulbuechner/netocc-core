// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace OCC.Core.gp;

/// <summary>
/// OCCT <c>gp_Trsf</c>: non-degenerate rigid/scale transformation, same layout as C++
/// (<c>scale</c>, <c>shape</c>, 4 bytes padding, <c>matrix</c>, <c>loc</c>).
/// Setters and composition run in OCCT. <c>default(gp_Trsf)</c> has scale 0 and is invalid.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 112)]
public struct gp_Trsf
{
    [FieldOffset(0)] private double scale;
    [FieldOffset(8)] private gp_TrsfForm shape;
    [FieldOffset(16)] private gp_Mat matrix;
    [FieldOffset(88)] private gp_XYZ loc;

    /// <summary>Identity, like OCCT.</summary>
    public gp_Trsf() => gpModule.NetOcc_gp_Trsf_Init(ref this);

    public void SetTranslation(in gp_Vec theV) => gpModule.NetOcc_gp_Trsf_SetTranslation(ref this, in theV);
    public void SetRotation(in gp_Ax1 theA1, double theAng) => gpModule.NetOcc_gp_Trsf_SetRotation(ref this, in theA1, theAng);
    public void SetScale(in gp_Pnt theP, double theS) => gpModule.NetOcc_gp_Trsf_SetScale(ref this, in theP, theS);
    public readonly gp_Trsf Multiplied(in gp_Trsf theT) => gpModule.NetOcc_gp_Trsf_Multiplied(in this, in theT);
    public readonly gp_Trsf Inverted() => gpModule.NetOcc_gp_Trsf_Inverted(in this);
    public readonly double Value(int theRow, int theCol) => gpModule.NetOcc_gp_Trsf_Value(in this, theRow, theCol);

    public readonly double ScaleFactor() => scale;
    public readonly gp_TrsfForm Form() => shape;
    public readonly gp_XYZ TranslationPart() => loc;
    public readonly gp_Mat HVectorialPart() => matrix;
}
