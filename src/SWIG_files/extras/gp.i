// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * gp: companion of the generated gp.i, included before its classes.
 * The value types are C# structs (src/NetOcc/gp/*.cs); these thunks cover the methods that stay
 * in OCCT (validation, normalization, transformations). Trivial math is managed.
 */

%{
// gp_Trsf's C# struct holds the form as an int
static_assert(sizeof(gp_TrsfForm) == 4, "gp_TrsfForm size");
%}

%inline %{
// gp_Pnt
inline double NetOcc_gp_Pnt_Distance(const gp_Pnt* theSelf, const gp_Pnt* theOther) { return theSelf->Distance(*theOther); }
inline void   NetOcc_gp_Pnt_Transform(gp_Pnt* theSelf, const gp_Trsf* theT) { theSelf->Transform(*theT); }
inline gp_Pnt NetOcc_gp_Pnt_Transformed(const gp_Pnt* theSelf, const gp_Trsf* theT) { return theSelf->Transformed(*theT); }
inline gp_Pnt NetOcc_gp_Pnt_Mirrored(const gp_Pnt* theSelf, const gp_Ax1* theA1) { return theSelf->Mirrored(*theA1); }
inline gp_Pnt NetOcc_gp_Pnt_Rotated(const gp_Pnt* theSelf, const gp_Ax1* theA1, double theAng) { return theSelf->Rotated(*theA1, theAng); }

// gp_Dir: constructors validate and normalize (Standard_ConstructionError on a null vector)
inline void   NetOcc_gp_Dir_Init(gp_Dir* theSelf) { *theSelf = gp_Dir(); }
inline void   NetOcc_gp_Dir_InitXYZ(gp_Dir* theSelf, double theX, double theY, double theZ) { *theSelf = gp_Dir(theX, theY, theZ); }
inline void   NetOcc_gp_Dir_InitVec(gp_Dir* theSelf, const gp_Vec* theV) { *theSelf = gp_Dir(*theV); }
inline gp_Dir NetOcc_gp_Dir_Crossed(const gp_Dir* theSelf, const gp_Dir* theOther) { return theSelf->Crossed(*theOther); }
inline double NetOcc_gp_Dir_Angle(const gp_Dir* theSelf, const gp_Dir* theOther) { return theSelf->Angle(*theOther); }
inline bool   NetOcc_gp_Dir_IsParallel(const gp_Dir* theSelf, const gp_Dir* theOther, double theAngTol) { return theSelf->IsParallel(*theOther, theAngTol); }
inline gp_Dir NetOcc_gp_Dir_Transformed(const gp_Dir* theSelf, const gp_Trsf* theT) { return theSelf->Transformed(*theT); }

// gp_Vec
inline gp_Vec NetOcc_gp_Vec_Normalized(const gp_Vec* theSelf) { return theSelf->Normalized(); }
inline gp_Vec NetOcc_gp_Vec_Transformed(const gp_Vec* theSelf, const gp_Trsf* theT) { return theSelf->Transformed(*theT); }

// gp_Ax1 / gp_Ax2
inline void NetOcc_gp_Ax1_Init(gp_Ax1* theSelf) { *theSelf = gp_Ax1(); }
inline void NetOcc_gp_Ax2_Init(gp_Ax2* theSelf) { *theSelf = gp_Ax2(); }
inline void NetOcc_gp_Ax2_InitPN(gp_Ax2* theSelf, const gp_Pnt* theP, const gp_Dir* theN) { *theSelf = gp_Ax2(*theP, *theN); }
inline void NetOcc_gp_Ax2_InitPNV(gp_Ax2* theSelf, const gp_Pnt* theP, const gp_Dir* theN, const gp_Dir* theVx) { *theSelf = gp_Ax2(*theP, *theN, *theVx); }

// gp_Trsf
inline void    NetOcc_gp_Trsf_Init(gp_Trsf* theSelf) { *theSelf = gp_Trsf(); }
inline void    NetOcc_gp_Trsf_SetTranslation(gp_Trsf* theSelf, const gp_Vec* theV) { theSelf->SetTranslation(*theV); }
inline void    NetOcc_gp_Trsf_SetRotation(gp_Trsf* theSelf, const gp_Ax1* theA1, double theAng) { theSelf->SetRotation(*theA1, theAng); }
inline void    NetOcc_gp_Trsf_SetScale(gp_Trsf* theSelf, const gp_Pnt* theP, double theS) { theSelf->SetScale(*theP, theS); }
inline gp_Trsf NetOcc_gp_Trsf_Multiplied(const gp_Trsf* theSelf, const gp_Trsf* theT) { return theSelf->Multiplied(*theT); }
inline gp_Trsf NetOcc_gp_Trsf_Inverted(const gp_Trsf* theSelf) { return theSelf->Inverted(); }
inline double  NetOcc_gp_Trsf_Value(const gp_Trsf* theSelf, int theRow, int theCol) { return theSelf->Value(theRow, theCol); }
%}
