// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * Quantity: companion of the generated Quantity.i, included before its classes.
 * Quantity_Color is a C# struct (src/NetOcc/Quantity/Quantity_Color.cs) holding linear RGB floats;
 * the thunks cover what stays in OCCT (color-space conversion and range checks, the tolerant
 * IsEqual with its global epsilon).
 */

%inline %{
inline void NetOcc_Quantity_Color_Init(Quantity_Color* theSelf) { *theSelf = Quantity_Color(); }
inline void NetOcc_Quantity_Color_InitValues(Quantity_Color* theSelf, double theC1, double theC2, double theC3, Quantity_TypeOfColor theType) { *theSelf = Quantity_Color(theC1, theC2, theC3, theType); }
inline void NetOcc_Quantity_Color_Values(const Quantity_Color* theSelf, double& theC1, double& theC2, double& theC3, Quantity_TypeOfColor theType) { theSelf->Values(theC1, theC2, theC3, theType); }
inline bool NetOcc_Quantity_Color_IsEqual(const Quantity_Color* theSelf, const Quantity_Color* theOther) { return theSelf->IsEqual(*theOther); }
%}
