---
name: common-serialization
description: Methods for serializing and deserializing SDT, BC, and Collections to/from JSON and XML
---

Every element based on SDT, TRN as BC, or Collection supports serialization and deserialization methods

---

# SERIALIZATION

## ToJson
Serializes object/collection to JSON string

Syntax: `<variable>.ToJson([<flag>])`

Where:
- `<flag>`: Optional serialization flag

Returns: `String` (JSON representation)

Examples:
~~~
&JsonString = &MySdt.ToJson()
&JsonString = &MySdt.ToJson(0)
&JsonString = &MyCollection.ToJson()
~~~

## ToXml
Serializes object/collection to XML string

Syntax: `<variable>.ToXml([<flag>])`

Where:
- `<flag>`: Optional serialization flag

Returns: `String` (XML representation)

Examples:
~~~
&XmlString = &MySdt.ToXml()
&XmlString = &MySdt.ToXml(0)
&XmlString = &MyCollection.ToXml()
~~~

---

# DESERIALIZATION

## FromJson
Loads object/collection from JSON string and returns success status

Syntax: `<variable>.FromJson(<input>[, <messages>])`

Where:
- `<input>`: JSON string to deserialize
- `<messages>`: Optional Messages SDT to fill with details

Returns: `Boolean` (success status)

Examples:
~~~
&MySdt.FromJson(&JsonString)
&MySdt.FromJson(&JsonString, &Messages)
&IsOk = &MySdt.FromJson(&JsonString)
&IsOk = &MySdt.FromJson(&JsonString, &Messages)
~~~

## FromXml
Loads object/collection from XML string and returns success status

Syntax: `<variable>.FromXml(<input>[, <messages>])`

Where:
- `<input>`: XML string to deserialize
- `<messages>`: Optional Messages SDT to fill with details

Returns: `Boolean` (success status)

Examples:
~~~
&MySdt.FromXml(&XmlString)
&MySdt.FromXml(&XmlString, &Messages)
&IsOk = &MySdt.FromXml(&XmlString)
&IsOk = &MySdt.FromXml(&XmlString, &Messages)
~~~

---

# CONSTRAINTS
- Serialization flag includes (True, default) or excludes (False) extra data like:
	* `initialized`: BC initialization flag (`0`/`1`)
	* `gx_mode`: Operation mode (`INS`, `UPD`, `DLT`)
	* `<field>_z`: Old value per field for change detection
	* `<field>_GXI`: Image field metadata (with `_z` variant for old value)
- Serialization flag is only available for BC instances of TRNs
