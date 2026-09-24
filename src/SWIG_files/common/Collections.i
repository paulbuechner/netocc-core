// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * NCollection templates. Each instantiation gets a C# name (the pre-OCCT-8
 * alias, e.g. TopTools_ListOfShape) and IEnumerable<T>.
 */

%{
#include <NCollection_List.hxx>
%}

template <class TheItemType> class NCollection_List {
public:
  NCollection_List();
  int Extent() const;
  bool IsEmpty() const;
  const TheItemType& First() const;
  const TheItemType& Last() const;
  %extend {
    void Clear() { $self->Clear(); }
    void Append(const TheItemType& theItem) { $self->Append(theItem); }
    // 0-based; walks the list (spike: O(n) per call, enumeration is O(n^2))
    const TheItemType& Value(int theIndex) const {
      if (theIndex < 0 || theIndex >= $self->Extent()) {
        throw Standard_OutOfRange("NCollection_List::Value: index out of range");
      }
      auto it = $self->cbegin();
      std::advance(it, theIndex);
      return *it;
    }
  }
};

%define %occt_list(NAME, TYPE)
%typemap(csinterfaces) NCollection_List< TYPE > "global::System.IDisposable, global::System.Collections.Generic.IEnumerable<TYPE>"
%typemap(cscode) NCollection_List< TYPE > %{
  public int Count => Extent();

  public global::System.Collections.Generic.IEnumerator<TYPE> GetEnumerator() {
    int n = Extent();
    for (int i = 0; i < n; i++) {
      yield return Value(i);
    }
  }

  global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() {
    return GetEnumerator();
  }
%}
%occt_valueclass(NCollection_List< TYPE >)
%template(NAME) NCollection_List< TYPE >;
%enddef

/*
 * NCollection_Sequence: 1-based like OCCT (Value(1) .. Value(Length())); the
 * enumerator walks forward, which the sequence serves in O(1) per step.
 */
%{
#include <NCollection_Sequence.hxx>
%}

template <class TheItemType> class NCollection_Sequence {
public:
  NCollection_Sequence();
  int Length() const;
  bool IsEmpty() const;
  const TheItemType& First() const;
  const TheItemType& Last() const;
  const TheItemType& Value(const int theIndex) const;
  void Append(const TheItemType& theItem);
  %extend {
    void Clear() { $self->Clear(); }
  }
};

%define %occt_sequence(NAME, TYPE)
%typemap(csinterfaces) NCollection_Sequence< TYPE > "global::System.IDisposable, global::System.Collections.Generic.IEnumerable<TYPE>"
%typemap(cscode) NCollection_Sequence< TYPE > %{
  public int Count => Length();

  public global::System.Collections.Generic.IEnumerator<TYPE> GetEnumerator() {
    int n = Length();
    for (int i = 1; i <= n; i++) {
      yield return Value(i);
    }
  }

  global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() {
    return GetEnumerator();
  }
%}
%occt_valueclass(NCollection_Sequence< TYPE >)
%template(NAME) NCollection_Sequence< TYPE >;
%enddef
