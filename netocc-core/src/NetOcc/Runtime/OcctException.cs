// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

//
using OCC.Core.Standard;

namespace OCC.Core;

/// <summary>
/// A C++ exception raised inside OCCT (<c>Standard_Failure</c> and subclasses, or
/// <c>std::exception</c>), caught at the native boundary and rethrown in .NET.
/// </summary>
/// <param name="occtType">OCCT exception class, e.g. <c>Standard_ConstructionError</c>.</param>
/// <param name="occtMessage">Message as reported by OCCT.</param>
public class OcctException(string occtType, string occtMessage)
    : System.Exception(string.IsNullOrEmpty(occtMessage) ? occtType : $"{occtType}: {occtMessage}")
{
    /// <summary>OCCT exception class, e.g. <c>Standard_ConstructionError</c>.</summary>
    public string OcctType { get; } = occtType;

    /// <summary>Message as reported by OCCT.</summary>
    public string OcctMessage { get; } = occtMessage;

    /// <summary>
    /// Whether OCCT raised <typeparamref name="T"/> or a subclass of it: <c>Is&lt;Standard_RangeError&gt;()</c> holds for a
    /// <c>Standard_OutOfRange</c>. The proxies of OCCT's exception classes carry its class hierarchy.
    /// </summary>
    public bool Is<T>() where T : Standard_Failure => Raised is { } raised && typeof(T).IsAssignableFrom(raised);

    // the proxy of the raised class: OCC.Core.<Package>.<Class>, the package its name's prefix; none for std::exception
    // or a class outside the wrapped packages
    private System.Type Raised =>
        OcctType.IndexOf('_') is var end and > 0 ? typeof(OcctException).Assembly.GetType($"OCC.Core.{OcctType.Substring(0, end)}.{OcctType}") : null;
}
