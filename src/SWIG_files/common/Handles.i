// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * OCCT handles (Standard_Transient hierarchy).
 *
 * The C# proxy is the object: it holds the raw T* and owns exactly one
 * reference. SWIG's ref/unref features add that reference after construction
 * and drop it in Dispose/the finalizer; Handle(T) parameters and returns are
 * converted at the boundary. The counter is atomic, so the GC finalizer
 * thread may release.
 */

%{
#include <cstdint>
#include <Standard_Handle.hxx>

// In/out slot value until the call succeeds (C#: OCC.Core.NativeRef.Unchanged).
#define NETOCC_REF_UNCHANGED (reinterpret_cast<void*>(static_cast<std::intptr_t>(-1)))
%}

namespace opencascade { template <class T> class handle; }

%feature("ref")   Standard_Transient "$this->IncrementRefCounter();"
%feature("unref") Standard_Transient "if ($this->DecrementRefCounter() == 0) $this->Delete();"

// Handle(TYPE) <-> TYPE proxy. Use once per transient class, before its declaration.
%define %occt_handle(TYPE)
%typemap(ctype)  opencascade::handle< TYPE >, const opencascade::handle< TYPE >& "void *"
%typemap(imtype, out="global::System.IntPtr") opencascade::handle< TYPE >, const opencascade::handle< TYPE >& "global::System.Runtime.InteropServices.HandleRef"
%typemap(cstype) opencascade::handle< TYPE >, const opencascade::handle< TYPE >& "TYPE"
%typemap(csin)   opencascade::handle< TYPE >, const opencascade::handle< TYPE >& "TYPE.getCPtr($csinput)"
%typemap(in) opencascade::handle< TYPE > %{
  $1 = opencascade::handle< TYPE >(static_cast< TYPE* >($input));
%}
%typemap(in) const opencascade::handle< TYPE >& (opencascade::handle< TYPE > temp) %{
  temp = opencascade::handle< TYPE >(static_cast< TYPE* >($input));
  $1 = &temp;
%}
%typemap(out) opencascade::handle< TYPE > %{
  { TYPE* p = $1.get(); if (p) p->IncrementRefCounter(); $result = (void*)p; }
%}
%typemap(out) const opencascade::handle< TYPE >& %{
  { TYPE* p = $1->get(); if (p) p->IncrementRefCounter(); $result = (void*)p; }
%}
%typemap(csout, excode=SWIGEXCODE) opencascade::handle< TYPE >, const opencascade::handle< TYPE >& {
    global::System.IntPtr cPtr = $imcall;
    TYPE ret = (cPtr == global::System.IntPtr.Zero) ? null : new TYPE(cPtr, true);$excode
    return ret;
  }

// Handle(TYPE)& (in/out) -> ref TYPE. The wrapper takes the input into a local handle and
// parks a sentinel in the slot; argout replaces it with the result plus one reference for
// the new proxy. A C++ exception skips argout, so the caller's variable stays unchanged.
%typemap(ctype, out="void *") opencascade::handle< TYPE >& "void **"
%typemap(imtype, out="global::System.IntPtr") opencascade::handle< TYPE >& "ref global::System.IntPtr"
%typemap(cstype, out="TYPE") opencascade::handle< TYPE >& "ref TYPE"
%typemap(csin,
         pre="    TYPE keep$csinput = $csinput; global::System.IntPtr slot$csinput = TYPE.getCPtr(keep$csinput).Handle;",
         post="      global::System.GC.KeepAlive(keep$csinput);"
              " if (slot$csinput != global::OCC.Core.NativeRef.Unchanged) $csinput = (slot$csinput == global::System.IntPtr.Zero) ? null : new TYPE(slot$csinput, true);")
         opencascade::handle< TYPE >& "ref slot$csinput"
%typemap(in) opencascade::handle< TYPE >& (opencascade::handle< TYPE > temp) %{
  temp = opencascade::handle< TYPE >(static_cast< TYPE* >(*$input));
  *$input = NETOCC_REF_UNCHANGED;
  $1 = &temp;
%}
%typemap(argout) opencascade::handle< TYPE >& %{
  { TYPE* p = temp$argnum.get(); if (p) p->IncrementRefCounter(); *$input = (void*)p; }
%}
// SWIG falls back from const T& to T& for typemaps missing on const T&: keep argout off const handles.
%typemap(argout) const opencascade::handle< TYPE >& ""
// Non-const Handle(TYPE)& returns behave like const& returns.
%typemap(out) opencascade::handle< TYPE >& %{
  { TYPE* p = $1->get(); if (p) p->IncrementRefCounter(); $result = (void*)p; }
%}
%typemap(csout, excode=SWIGEXCODE) opencascade::handle< TYPE >& {
    global::System.IntPtr cPtr = $imcall;
    TYPE ret = (cPtr == global::System.IntPtr.Zero) ? null : new TYPE(cPtr, true);$excode
    return ret;
  }
%enddef

// Handle(TYPE)::DownCast as a static method: Geom_Plane.DownCast(surface), null on mismatch.
%define %occt_downcast(TYPE)
%extend TYPE {
  static opencascade::handle< TYPE > DownCast(const opencascade::handle< Standard_Transient >& theObject) {
    return opencascade::handle< TYPE >::DownCast(theObject);
  }
}
%enddef

%define %occt_transient(TYPE)
%occt_handle(TYPE)
%occt_downcast(TYPE)
%enddef
