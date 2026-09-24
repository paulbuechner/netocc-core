// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * OCCT primitive typedefs (Standard_TypeDef.hxx) and by-reference primitives.
 *
 * Contract: a non-const C++ reference is a C# `ref` parameter, never `out`,
 * because some OCCT reference parameters are read as well as written.
 */

typedef int            Standard_Integer;
typedef unsigned int   Standard_UInteger;
typedef double         Standard_Real;
typedef bool           Standard_Boolean;
typedef float          Standard_ShortReal;
typedef char           Standard_Character;
typedef const char*    Standard_CString;
typedef void*          Standard_Address;

%include <typemaps.i>
%apply double& INOUT { double& };
%apply float&  INOUT { float& };
%apply int&    INOUT { int& };
%apply bool&   INOUT { bool& };

// Non-const enum& -> ref Enum, through an int slot (OCCT enums are int-sized). const enum& stays by value.
%typemap(ctype) enum SWIGTYPE & "int *"
%typemap(imtype) enum SWIGTYPE & "ref int"
%typemap(cstype) enum SWIGTYPE & "ref $*csclassname"
%typemap(csin, pre="    int temp$csinput = (int)$csinput;", post="      $csinput = ($*csclassname)temp$csinput;") enum SWIGTYPE & "ref temp$csinput"
%typemap(in) enum SWIGTYPE & %{
  static_assert(sizeof(*$1) == sizeof(int), "enum reference needs an int-sized enum");
  $1 = ($1_ltype)$input;
%}

/*
 * size_t / Standard_Size: SWIG's C# default is a 32-bit uint. Keep it
 * pointer-sized at the P/Invoke layer (win-x86 is 32-bit) and expose ulong.
 */
%typemap(ctype)  size_t, Standard_Size "size_t"
%typemap(imtype) size_t, Standard_Size "global::System.UIntPtr"
%typemap(cstype) size_t, Standard_Size "ulong"
%typemap(csin)   size_t, Standard_Size "new global::System.UIntPtr($csinput)"
%typemap(in)     size_t, Standard_Size %{ $1 = ($1_ltype)$input; %}
%typemap(out)    size_t, Standard_Size %{ $result = $1; %}
%typemap(csout, excode=SWIGEXCODE) size_t, Standard_Size {
    ulong ret = (ulong)$imcall;$excode
    return ret;
  }
typedef size_t Standard_Size;
