---
name: common-extended-type-xmlreader
description: XML file and string reader
---

Reads XML files or strings by navigating through nodes with support for attributes, namespaces, and validation

---

# PROPERTIES

## Name
Name of the current element (read-only)

Syntax: `<xmlreader>.Name`

Example:
~~~
If &XmlReader.Name = !"customer"
	// Process customer element
EndIf
~~~

## LocalName
Local name of the current element without namespace (read-only)

Syntax: `<xmlreader>.LocalName`

Example:
~~~
&LocalName = &XmlReader.LocalName
~~~

## Value
Value of the current element (read-only)

Syntax: `<xmlreader>.Value`

Constraints:
- Only valid for these node types: Text, Comment, ProcessingInstruction, DocumentType, Element

Example:
~~~
&ElementValue = &XmlReader.Value
~~~

## AttributeCount
Number of attributes in the current element (read-only)

Syntax: `<xmlreader>.AttributeCount`

Example:
~~~
&AttrCount = &XmlReader.AttributeCount
~~~

## EOF
Indicates if the end of XML data has been reached (read-only)

Syntax: `<xmlreader>.EOF`

Example:
~~~
Do While NOT &XmlReader.EOF
	&XmlReader.Read()
EndDo
~~~

## IsSimple
Indicates if the current element is a simple element (leaf node) (read-only)

Syntax: `<xmlreader>.IsSimple`

Example:
~~~
If &XmlReader.IsSimple
	// Process simple element
EndIf
~~~

## Prefix
Prefix of the current element's namespace (read-only)

Syntax: `<xmlreader>.Prefix`

Example:
~~~
&NamespacePrefix = &XmlReader.Prefix
~~~

## NamespaceUri
Namespace URI of the current element (read-only)

Syntax: `<xmlreader>.NamespaceUri`

Example:
~~~
&NamespaceUri = &XmlReader.NamespaceUri
~~~

## ReadExternalEntities
Indicates if external entities are read

Syntax: `<xmlreader>.ReadExternalEntities`

Example:
~~~
&XmlReader.ReadExternalEntities = True
~~~

## RemoveWhiteNodes
Indicates if white space nodes are removed

Syntax: `<xmlreader>.RemoveWhiteNodes`

Example:
~~~
&XmlReader.RemoveWhiteNodes = True
~~~

## RemoveWhiteSpaces
Indicates if white space characters are removed

Syntax: `<xmlreader>.RemoveWhiteSpaces`

Example:
~~~
&XmlReader.RemoveWhiteSpaces = True
~~~

## SimpleElements
Indicates if simple elements are read as text

Syntax: `<xmlreader>.SimpleElements`

Example:
~~~
&XmlReader.SimpleElements = True
~~~

## ValidationType
Specifies the type of validation to apply

Syntax: `<xmlreader>.ValidationType`

Constraints:
- Values: 0 (not carried), 1 (automatic), 2 (by DTD), 3 (by Schema), 4 (by XDR)

Example:
~~~
&XmlReader.ValidationType = 2
~~~

## ErrCode
Numeric error code (read-only); 0 indicates success

Syntax: `<xmlreader>.ErrCode`

Example:
~~~
If &XmlReader.ErrCode <> 0
	msg(&XmlReader.ErrDescription)
EndIf
~~~

## ErrDescription
Text error description (read-only)

Syntax: `<xmlreader>.ErrDescription`

Example:
~~~
&ErrorMsg = &XmlReader.ErrDescription
~~~

---

# METHODS

## Open
Opens an XML file for reading

Syntax: `<xmlreader>.Open(<file>)`

Where:
- `<file>`: File instance to read

Example:
~~~
&XmlReader.Open(&XmlFile)
~~~

## OpenFromString
Opens an XML string for reading

Syntax: `<xmlreader>.OpenFromString(<string>)`

Where:
- `<string>`: XML content as string

Example:
~~~
&XmlReader.OpenFromString(&XmlString)
~~~

## OpenResponse
Opens an XML response from an HTTP request

Syntax: `<xmlreader>.OpenResponse(<http-request>)`

Where:
- `<http-request>`: HttpRequest instance

Example:
~~~
&XmlReader.OpenResponse(&HttpRequest)
~~~

## Close
Closes the XMLReader

Syntax: `<xmlreader>.Close()`

Example:
~~~
&XmlReader.Close()
~~~

## Read
Reads the next node in the XML data

Syntax: `<xmlreader>.Read()`

Constraints:
- Returns Boolean; True if node was read successfully

Example:
~~~
Do While &XmlReader.Read()
	If &XmlReader.Name = !"item"
		// Process item element
	EndIf
EndDo
~~~

## Skip
Skips the current node

Syntax: `<xmlreader>.Skip()`

Example:
~~~
&XmlReader.Skip()
~~~

## GetAttributeByName
Gets the attribute value by name

Syntax: `<xmlreader>.GetAttributeByName(<name>)`

Where:
- `<name>`: Attribute name

Example:
~~~
&Id = &XmlReader.GetAttributeByName(!"id")
~~~

## GetAttributeByIndex
Gets the attribute value by index

Syntax: `<xmlreader>.GetAttributeByIndex(<index>)`

Where:
- `<index>`: Attribute index (1-based)

Example:
~~~
&AttrValue = &XmlReader.GetAttributeByIndex(1)
~~~

## ExistsAttribute
Checks if an attribute exists in the current element

Syntax: `<xmlreader>.ExistsAttribute(<name>)`

Where:
- `<name>`: Attribute name

Example:
~~~
If &XmlReader.ExistsAttribute(!"id")
	&Id = &XmlReader.GetAttributeByName(!"id")
EndIf
~~~

## GetAttributeName
Gets the full name of an attribute by index

Syntax: `<xmlreader>.GetAttributeName(<index>)`

Where:
- `<index>`: Attribute index (1-based)

Example:
~~~
&AttrName = &XmlReader.GetAttributeName(1)
~~~

## GetAttributeLocalName
Gets the local name of an attribute by index

Syntax: `<xmlreader>.GetAttributeLocalName(<index>)`

Where:
- `<index>`: Attribute index (1-based)

Example:
~~~
&LocalName = &XmlReader.GetAttributeLocalName(1)
~~~

## GetAttributePrefix
Gets the prefix of an attribute by index

Syntax: `<xmlreader>.GetAttributePrefix(<index>)`

Where:
- `<index>`: Attribute index (1-based)

Example:
~~~
&Prefix = &XmlReader.GetAttributePrefix(1)
~~~

## GetAttributeUri
Gets the URI of an attribute by index

Syntax: `<xmlreader>.GetAttributeUri(<index>)`

Where:
- `<index>`: Attribute index (1-based)

Example:
~~~
&Uri = &XmlReader.GetAttributeUri(1)
~~~

## GetAttEntityNotationByIndex
Gets the entity notation of an attribute by index

Syntax: `<xmlreader>.GetAttEntityNotationByIndex(<index>)`

Where:
- `<index>`: Attribute index (1-based)

Example:
~~~
&EntityNotation = &XmlReader.GetAttEntityNotationByIndex(1)
~~~

## GetAttEntityNotationByName
Gets the entity notation of an attribute by name

Syntax: `<xmlreader>.GetAttEntityNotationByName(<name>)`

Where:
- `<name>`: Attribute name

Example:
~~~
&EntityNotation = &XmlReader.GetAttEntityNotationByName(!"id")
~~~

## GetAttEntityValueByIndex
Gets the entity value of an attribute by index

Syntax: `<xmlreader>.GetAttEntityValueByIndex(<index>)`

Where:
- `<index>`: Attribute index (1-based)

Example:
~~~
&EntityValue = &XmlReader.GetAttEntityValueByIndex(1)
~~~

## GetAttEntityValueByName
Gets the entity value of an attribute by name

Syntax: `<xmlreader>.GetAttEntityValueByName(<name>)`

Where:
- `<name>`: Attribute name

Example:
~~~
&EntityValue = &XmlReader.GetAttEntityValueByName(!"id")
~~~

## ReadRawXML
Reads raw XML data

Syntax: `<xmlreader>.ReadRawXML()`

Example:
~~~
&RawXml = &XmlReader.ReadRawXML()
~~~

## ReadType
Reads nodes of specific types

Syntax: `<xmlreader>.ReadType(<types>[, <name>])`

Where:
- `<types>`: Type flags combined using `+` (bitwise OR)
- `<name>`: Optional element name (only for Element or EndTag types)

Constraints:
- Type values: 001 (Element), 002 (EndTag), 004 (Text), 008 (Comment), 016 (WhiteSpace), 032 (CData), 064 (ProcessingInstruction), 128 (Doctype)

Example:
~~~
&Count = &XmlReader.ReadType(1 + 4, !"item")
~~~

## AddSchema
Adds a schema to the XMLReader

Syntax: `<xmlreader>.AddSchema(<schema>)`

Where:
- `<schema>`: Schema definition

Example:
~~~
&XmlReader.AddSchema(&Schema)
~~~

## SetDocEncoding
Sets the document encoding

Syntax: `<xmlreader>.SetDocEncoding(<encoding>)`

Where:
- `<encoding>`: Encoding string (e.g. `!"UTF-8"`)

Example:
~~~
&XmlReader.SetDocEncoding(!"UTF-8")
~~~

## SetNodeEncoding
Sets the encoding for the current node

Syntax: `<xmlreader>.SetNodeEncoding(<encoding>)`

Where:
- `<encoding>`: Encoding string

Example:
~~~
&XmlReader.SetNodeEncoding(!"UTF-8")
~~~

---

# CONSTRAINTS
- Always use `new()` to create XmlReader instances
- Always call `Close()` after opening and reading XML
- Check `EOF` property in loops to avoid infinite iterations
- Use `Read()` method to navigate through nodes
- Attribute indices are 1-based, not 0-based