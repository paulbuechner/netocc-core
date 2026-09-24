// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * Value types: blittable C# structs with the exact C++ layout
 * (hand-written in src/NetOcc/<Package>/, generated later). SWIG never builds
 * proxies for them; these typemaps marshal them in other classes' signatures.
 *
 *   TYPE / const TYPE&   parameter  -> TYPE / in TYPE   (pointer to the C# struct)
 *   TYPE& / TYPE*        parameter  -> ref TYPE         (callee writes the caller's struct)
 *   TYPE / const TYPE&   return     -> TYPE             (via a thread-local buffer, see below)
 *   TYPE* NETOCC_ARRAY   parameter  -> TYPE[]           (pinned array for bulk copies)
 *
 * Returns never pass a struct by value across the C ABI: on x86 __stdcall the hidden
 * return pointer is part of the export's _Name@N decoration, which .NET computes from
 * the managed signature (without it) -> EntryPointNotFoundException. The wrapper copies
 * the result into a per-function thread-local buffer and returns its address; C# checks
 * for a pending exception first, then reads the struct (one unmanaged copy).
 */
%define %occt_valuetype(TYPE)
%typemap(ctype, out="void *") TYPE, const TYPE&, TYPE&, TYPE*, const TYPE* "TYPE *"
%typemap(imtype, out="global::System.IntPtr") TYPE, TYPE&, TYPE* "ref TYPE"
%typemap(imtype, out="global::System.IntPtr") const TYPE&, const TYPE* "in TYPE"
%typemap(cstype, out="TYPE") TYPE "TYPE"
%typemap(cstype, out="TYPE") const TYPE&, const TYPE* "in TYPE"
%typemap(cstype, out="TYPE") TYPE&, TYPE* "ref TYPE"
%typemap(csin) TYPE, TYPE&, TYPE* "ref $csinput"
%typemap(csin) const TYPE&, const TYPE* "in $csinput"
%typemap(in) TYPE %{ $1 = *($&1_ltype)$input; %}
%typemap(in) const TYPE&, TYPE&, TYPE*, const TYPE* %{ $1 = ($1_ltype)$input; %}
%typemap(out, null="0") TYPE %{
  { static thread_local TYPE netocc_ret; netocc_ret = $1; $result = (void*)&netocc_ret; }
%}
%typemap(out, null="0") const TYPE&, TYPE& %{
  { static thread_local TYPE netocc_ret; netocc_ret = *$1; $result = (void*)&netocc_ret; }
%}
%typemap(csout, excode=SWIGEXCODE) TYPE, const TYPE&, TYPE& {
    global::System.IntPtr ptr = $imcall;$excode
    return global::OCC.Core.NativeStruct.Read<TYPE>(ptr);
  }

%typemap(ctype)  TYPE* NETOCC_ARRAY "TYPE *"
%typemap(imtype) TYPE* NETOCC_ARRAY "[global::System.Runtime.InteropServices.Out] TYPE[]"
%typemap(cstype) TYPE* NETOCC_ARRAY "TYPE[]"
%typemap(csin)   TYPE* NETOCC_ARRAY "$csinput"
%typemap(in)     TYPE* NETOCC_ARRAY %{ $1 = ($1_ltype)$input; %}
%enddef

/*
 * Value classes: copyable non-transient classes wrapped as proxies
 * (TopoDS_Shape, TopLoc_Location, NCollection_List<T>, ...). A const& return
 * points into its parent, so it is copied into a proxy that owns the copy.
 */
%define %occt_valueclass(TYPE)
%typemap(out) const TYPE& %{ $result = (void*)new TYPE(*$1); %}
%typemap(csout, excode=SWIGEXCODE) const TYPE& {
    global::System.IntPtr cPtr = $imcall;
    $csclassname ret = (cPtr == global::System.IntPtr.Zero) ? null : new $csclassname(cPtr, true);$excode
    return ret;
  }
%enddef
