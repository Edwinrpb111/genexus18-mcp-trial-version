---
name: common-external-object-dictionary
description: Dictionary external object for runtime key-value storage
---

Runtime key-value storage with customizable key and value types

---

# PROPERTIES (design-time)
Must be defined when creating Dictionary variables:
- `KeyType`: Type of keys (default: 'VarChar'); any `DataType` or `Domain` object name
- `KeyLength`: Length for VarChar keys (default: 255)
- `KeyDecimals`: Decimal places for Numeric keys (default: 0)
- `KeySigned`: Allows negative values for Numeric keys (default: False)
- `ValueType`: Type of values (default: 'VarChar'); any `DataType` or `Domain` object name
- `ValueLength`: Length for VarChar values (default: 255)
- `ValueDecimals`: Decimal places for Numeric values (default: 0)
- `ValueSigned`: Allows negative values for Numeric values (default: False)

Example:
~~~
&Config
[
	DataType = 'Dictionary',
	KeyType = 'VarChar',
	KeyLength = '50',
	ValueType = 'VarChar',
	ValueLength = '100'
]
~~~

---

# PROPERTIES (run-time)

## Count
Number of items in the dictionary

Syntax: `<dictionary>.Count`

Example:
~~~
&Count = &MyDict.Count
~~~

## Keys
Collection of stored keys (collection of `KeyType` referenced type, read-only)

Syntax: `<dictionary>.Keys`

Example:
~~~
For &Key in &MyDict.Keys
	// Process each key
EndFor
~~~

## Values
Collection of stored values (colection of `ValueType` referenced type, read-only)

Syntax: `<dictionary>.Values`

Example:
~~~
For &Value in &MyDict.Values
	// Process each value
EndFor
~~~

---

# METHODS

## Set
Adds or updates an entry with the specified key and value

Syntax: `<dictionary>.Set(<key>, <value>)`

Example:
~~~
&MyDict.Set("Name", "John")
&MyDict.Set("Age", 25)
~~~

## Get
Retrieves the value (with refencenced `ValueType` type) associated with the specified key (with referenced `KeyType` type)

Syntax: `<dictionary>.Get(<key>)`

Example:
~~~
&Val = &MyDict.Get("Age")
&Name = &MyDict.Get("Name")
~~~

## Contains
Checks if the key exists in the dictionary

Syntax: `<dictionary>.Contains(<key>)`

Example:
~~~
If &MyDict.Contains("Age")
	&Age = &MyDict.Get("Age")
EndIf
~~~

## Remove
Removes the entry with the specified key; returns True if success, False if failed

Syntax: `<dictionary>.Remove(<key>)`

Example:
~~~
If &MyDict.Remove("Name")
	// entry removed successfully
EndIf
~~~

## RemoveKeys(<keys>)
Removes multiple entries given a collection of keys

Syntax: `<dictionary>.RemoveKeys(<key-collection>)`

Example:
~~~
&MyDict.RemoveKeys(&KeyCollection)
~~~

## RemoveAll(<dictionary>)
Removes all entries whose keys are in the given dictionary

Syntax: `<dictionary>.RemoveAll(<other-dictionary>)`

Example:
~~~
&MyDict.RemoveAll(&OtherDict)
~~~

## SetDictionary
Merges entries from another dictionary

Syntax: `<dictionary>.SetDictionary(<other-dictionary>)`

Example:
~~~
&MyDict.SetDictionary(&OtherDict)
~~~

## Clear
Removes all entries from the dictionary

Syntax: `<dictionary>.Clear()`

Example:
~~~
&MyDict.Clear()
~~~

---

# CONSTRAINTS
- Dictionary are always defined as a variable; defined without `&`, referenced with `&`
- Both `KeyType` and `ValueType` must support XML/JSON conversion; otherwise runtime error is raised
- Always define `KeyType`, `ValueType`, and related properties when creating Dictionary variables
- Dictionary cannot have `Collection = True`; otherwise runtime error is raised
- Dictionary supports serialization via `ToJson`/`FromJson` when key and value types are serializable
- All property definitions must be set at variable declaration time, not at runtime