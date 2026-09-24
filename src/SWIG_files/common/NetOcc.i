// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * NetOcc common SWIG setup. First %include of every module .i.
 *
 * C# layout mirrors pythonocc: one SWIG module per OCCT package, C# namespace
 * OCC.Core.<Package>, OCCT class and method names unchanged.
 */

%begin %{
#if defined(_MSC_VER)
// 4190: extern "C" wrappers return trivially copyable value types (gp_Pnt, ...) by value
#pragma warning(disable : 4190 4244 4267 4251 4275)
#elif defined(__clang__)
#pragma clang diagnostic ignored "-Wreturn-type-c-linkage"
#endif
%}

%{
#include <exception>
#include <functional>
#include <iterator>
#include <type_traits>
#include <Standard_Failure.hxx>
#include <Standard_OutOfRange.hxx>
#include <Standard_Transient.hxx>
#include <Standard_Type.hxx>
%}

// Constructors are wrapped only when declared explicitly (OCCT has many
// protected or abstract bases).
%nodefaultctor;

/*
 * C# usings of the generated files: System, plus what the module passes, the namespaces of its
 * %import closure. Every module calls %netocc_csimports after its imports; the last call wins.
 */
%define %netocc_csimports(USINGS)
%typemap(csimports) SWIGTYPE %{
using global::System;
using global::System.Runtime.InteropServices;
USINGS
%}
%pragma(csharp) imclassimports=%{
using global::System;
using global::System.Runtime.InteropServices;
USINGS
%}
%pragma(csharp) moduleimports=%{
using global::System;
using global::System.Runtime.InteropServices;
USINGS
%}
%enddef

%netocc_csimports()

// Module and intermediary classes are plumbing; the API is the proxy classes.
%pragma(csharp) moduleclassmodifiers="internal class"
%pragma(csharp) imclassclassmodifiers="internal class"

%include "Types.i"
%include "Exceptions.i"
%include "Strings.i"
%include "Guid.i"
%include "Handles.i"
%include "ValueTypes.i"
%include "Collections.i"
