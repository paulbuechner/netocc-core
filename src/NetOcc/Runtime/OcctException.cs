// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

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
}
