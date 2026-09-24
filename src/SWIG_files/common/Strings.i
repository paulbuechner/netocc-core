// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * Strings are UTF-8 in both directions. OCCT reads file paths as UTF-8, while
 * default P/Invoke char* marshaling is ANSI on Windows (breaks C:\Daten\Übersicht\...).
 * netstandard2.0 has no UnmanagedType.LPUTF8Str, so encode/decode by hand.
 */

// const char* parameters: null-terminated UTF-8 byte[] (pinned, no copy on the native side)
%typemap(ctype, out="void *") const char*, const char* const "char *"
%typemap(imtype, out="global::System.IntPtr") const char*, const char* const "byte[]"
%typemap(cstype) const char*, const char* const "string"
%typemap(csin) const char*, const char* const "global::OCC.Core.Utf8.Encode($csinput)"
%typemap(in) const char*, const char* const %{ $1 = ($1_ltype)$input; %}

// const char* returns: decoded immediately (the pointer is owned by OCCT)
%typemap(out) const char*, const char* const %{ $result = (void*)$1; %}
%typemap(csout, excode=SWIGEXCODE) const char*, const char* const {
    string ret = global::OCC.Core.Utf8.Decode($imcall);$excode
    return ret;
  }

/*
 * TCollection strings are C# strings; no proxy classes.
 *
 *   TCollection_AsciiString      bytes, read and written as UTF-8 (paths, entries)
 *   TCollection_ExtendedString   UTF-16, passed as LPWStr (char16_t on every platform)
 *
 *   TYPE / const TYPE&  parameter  -> string       (null = empty)
 *   TYPE& parameter                -> ref string   (in/out)
 *   TYPE / const TYPE&  return     -> string       (copied into a thread-local buffer)
 *
 * Returns are copied natively first: the owner of a const& result may be a proxy
 * that the GC finalizes as soon as the P/Invoke call returns.
 */
%{
#include <TCollection_AsciiString.hxx>
#include <TCollection_ExtendedString.hxx>
%}

%typemap(ctype, out="void *") TCollection_AsciiString, const TCollection_AsciiString& "const char *"
%typemap(imtype, out="global::System.IntPtr") TCollection_AsciiString, const TCollection_AsciiString& "byte[]"
%typemap(cstype) TCollection_AsciiString, const TCollection_AsciiString& "string"
%typemap(csin) TCollection_AsciiString, const TCollection_AsciiString& "global::OCC.Core.Utf8.Encode($csinput)"
%typemap(in) TCollection_AsciiString %{ if ($input) $1 = TCollection_AsciiString($input); %}
%typemap(in) const TCollection_AsciiString& (TCollection_AsciiString temp) %{
  if ($input) temp = TCollection_AsciiString($input);
  $1 = &temp;
%}
%typemap(out, null="0") TCollection_AsciiString %{
  { static thread_local TCollection_AsciiString netocc_ret; netocc_ret = $1; $result = (void*)netocc_ret.ToCString(); }
%}
%typemap(out, null="0") const TCollection_AsciiString& %{
  { static thread_local TCollection_AsciiString netocc_ret; netocc_ret = *$1; $result = (void*)netocc_ret.ToCString(); }
%}
%typemap(csout, excode=SWIGEXCODE) TCollection_AsciiString, const TCollection_AsciiString& {
    global::System.IntPtr ptr = $imcall;$excode
    return global::OCC.Core.Utf8.Decode(ptr);
  }

%typemap(ctype, out="void *") TCollection_ExtendedString, const TCollection_ExtendedString& "const char16_t *"
%typemap(imtype, out="global::System.IntPtr",
         inattributes="[global::System.Runtime.InteropServices.MarshalAs(global::System.Runtime.InteropServices.UnmanagedType.LPWStr)]")
         TCollection_ExtendedString, const TCollection_ExtendedString& "string"
%typemap(cstype) TCollection_ExtendedString, const TCollection_ExtendedString& "string"
%typemap(csin) TCollection_ExtendedString, const TCollection_ExtendedString& "$csinput"
%typemap(in) TCollection_ExtendedString %{ if ($input) $1 = TCollection_ExtendedString($input); %}
%typemap(in) const TCollection_ExtendedString& (TCollection_ExtendedString temp) %{
  if ($input) temp = TCollection_ExtendedString($input);
  $1 = &temp;
%}
%typemap(out, null="0") TCollection_ExtendedString %{
  { static thread_local TCollection_ExtendedString netocc_ret; netocc_ret = $1; $result = (void*)netocc_ret.ToExtString(); }
%}
%typemap(out, null="0") const TCollection_ExtendedString& %{
  { static thread_local TCollection_ExtendedString netocc_ret; netocc_ret = *$1; $result = (void*)netocc_ret.ToExtString(); }
%}
%typemap(csout, excode=SWIGEXCODE) TCollection_ExtendedString, const TCollection_ExtendedString& {
    global::System.IntPtr ptr = $imcall;$excode
    return global::System.Runtime.InteropServices.Marshal.PtrToStringUni(ptr);
  }

/*
 * TYPE& -> ref string. The slot carries the input string in and the address of a
 * thread-local copy of the result out; C# decodes it right after the call. After
 * a C++ exception the slot still holds the input, so the variable keeps its value.
 */
%typemap(ctype, out="void *") TCollection_AsciiString&, TCollection_ExtendedString& "void **"
%typemap(imtype, out="global::System.IntPtr") TCollection_AsciiString&, TCollection_ExtendedString& "ref global::System.IntPtr"
%typemap(cstype, out="string") TCollection_AsciiString&, TCollection_ExtendedString& "ref string"
%typemap(csin,
         pre="    global::System.IntPtr buffer$csinput = global::OCC.Core.Utf8.Alloc($csinput); global::System.IntPtr result$csinput = buffer$csinput;",
         post="      $csinput = global::OCC.Core.Utf8.Decode(result$csinput); global::System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer$csinput);")
         TCollection_AsciiString& "ref result$csinput"
%typemap(csin,
         pre="    global::System.IntPtr buffer$csinput = global::System.Runtime.InteropServices.Marshal.StringToHGlobalUni($csinput); global::System.IntPtr result$csinput = buffer$csinput;",
         post="      $csinput = global::System.Runtime.InteropServices.Marshal.PtrToStringUni(result$csinput); global::System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer$csinput);")
         TCollection_ExtendedString& "ref result$csinput"
%typemap(in) TCollection_AsciiString& (TCollection_AsciiString temp) %{
  if (*$input) temp = TCollection_AsciiString((const char*)*$input);
  $1 = &temp;
%}
%typemap(in) TCollection_ExtendedString& (TCollection_ExtendedString temp) %{
  if (*$input) temp = TCollection_ExtendedString((const char16_t*)*$input);
  $1 = &temp;
%}
%typemap(argout) TCollection_AsciiString& %{
  { static thread_local TCollection_AsciiString netocc_out; netocc_out = temp$argnum; *$input = (void*)netocc_out.ToCString(); }
%}
%typemap(argout) TCollection_ExtendedString& %{
  { static thread_local TCollection_ExtendedString netocc_out; netocc_out = temp$argnum; *$input = (void*)netocc_out.ToExtString(); }
%}

// SWIG falls back from const T& to T& for typemaps missing on const T&: keep argout off const strings.
%typemap(argout) const TCollection_AsciiString&, const TCollection_ExtendedString& ""

// Non-const TYPE& returns are read-only copies, like const& returns.
%typemap(out, null="0") TCollection_AsciiString& %{
  { static thread_local TCollection_AsciiString netocc_ret; netocc_ret = *$1; $result = (void*)netocc_ret.ToCString(); }
%}
%typemap(out, null="0") TCollection_ExtendedString& %{
  { static thread_local TCollection_ExtendedString netocc_ret; netocc_ret = *$1; $result = (void*)netocc_ret.ToExtString(); }
%}
%typemap(csout, excode=SWIGEXCODE) TCollection_AsciiString& {
    global::System.IntPtr ptr = $imcall;$excode
    return global::OCC.Core.Utf8.Decode(ptr);
  }
%typemap(csout, excode=SWIGEXCODE) TCollection_ExtendedString& {
    global::System.IntPtr ptr = $imcall;$excode
    return global::System.Runtime.InteropServices.Marshal.PtrToStringUni(ptr);
  }
