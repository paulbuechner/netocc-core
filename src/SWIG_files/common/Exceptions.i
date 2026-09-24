// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * C++ exceptions never cross the P/Invoke boundary: every wrapper catches them
 * and hands type + message to a per-module C# callback, which queues an
 * OCC.Core.OcctException that the generated code throws right after the call.
 *
 * OCCT 8: Standard_Failure derives from std::exception (not Standard_Transient);
 * its type name comes from ExceptionType(), the message from what()
 * (GetMessageString() is deprecated).
 */

%insert(runtime) %{
typedef void (SWIGSTDCALL* NetOccExceptionCallback_t)(const char* theType, const char* theMessage);
static NetOccExceptionCallback_t NetOcc_exceptionCallback = 0;

#ifdef __cplusplus
extern "C"
#endif
SWIGEXPORT void SWIGSTDCALL NetOccRegisterExceptionCallback_$module(NetOccExceptionCallback_t theCallback) {
  NetOcc_exceptionCallback = theCallback;
}

static void NetOcc_SetPendingException(const char* theType, const char* theMessage) {
  if (NetOcc_exceptionCallback) {
    NetOcc_exceptionCallback(theType, theMessage ? theMessage : "");
  }
}
%}

%pragma(csharp) imclasscode=%{
  class NetOccExceptionHelper {
    public delegate void ExceptionDelegate(global::System.IntPtr theType, global::System.IntPtr theMessage);
    static readonly ExceptionDelegate exceptionDelegate = new ExceptionDelegate(SetPending);

    [global::System.Runtime.InteropServices.DllImport("$dllimport", EntryPoint="NetOccRegisterExceptionCallback_$module")]
    public static extern void NetOccRegisterExceptionCallback(ExceptionDelegate theCallback);

    static void SetPending(global::System.IntPtr theType, global::System.IntPtr theMessage) {
      SWIGPendingException.Set(new global::OCC.Core.OcctException(
        global::OCC.Core.Utf8.Decode(theType), global::OCC.Core.Utf8.Decode(theMessage)));
    }

    static NetOccExceptionHelper() {
      NetOccRegisterExceptionCallback(exceptionDelegate);
    }
  }
  static readonly NetOccExceptionHelper netOccExceptionHelper = new NetOccExceptionHelper();
%}

%exception {
  try {
    $action
  } catch (const Standard_Failure& e) {
    NetOcc_SetPendingException(e.ExceptionType(), e.what());
    return $null;
  } catch (const std::exception& e) {
    NetOcc_SetPendingException("std::exception", e.what());
    return $null;
  } catch (...) {
    NetOcc_SetPendingException("unknown", "non-standard C++ exception");
    return $null;
  }
}
