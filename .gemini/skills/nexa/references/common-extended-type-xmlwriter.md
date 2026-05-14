---
name: common-extended-type-xmlwriter
description: XML file and string writer
---

Creates and writes XML files or strings with support for elements, attributes, namespaces, and formatting

---

# PROPERTIES

## Indentation
Specifies the number of IndentChar to use for indentation

Syntax: `<xmlwriter>.Indentation`

Example:
~~~
&XmlWriter.Indentation = 2
~~~

## IndentChar
Specifies the character to use for indentation

Syntax: `<xmlwriter>.IndentChar`

Constraints:
- Common values: space (`!" "`) or tab (`!"\t"`)

Example:
~~~
&XmlWriter.IndentChar = !" "
~~~

## ResultingString
Contains the generated XML string when using OpenToString() (read-only)

Syntax: `<xmlwriter>.ResultingString`

Example:
~~~
&XmlString = &XmlWriter.ResultingString
~~~

## ErrCode
Numeric error code (read-only); 0 indicates success

Syntax: `<xmlwriter>.ErrCode`

Example:
~~~
If &XmlWriter.ErrCode <> 0
	msg(&XmlWriter.ErrDescription)
EndIf
~~~

## ErrDescription
Text error description (read-only)

Syntax: `<xmlwriter>.ErrDescription`

Example:
~~~
&ErrorMsg = &XmlWriter.ErrDescription
~~~

---

# METHODS

## Open
Opens a file for writing XML

Syntax: `<xmlwriter>.Open(<file>)`

Where:
- `<file>`: File path or File instance

Example:
~~~
&XmlWriter.Open(!"output.xml")
~~~

## OpenToString
Starts writing XML to a memory buffer

Syntax: `<xmlwriter>.OpenToString()`

Example:
~~~
&XmlWriter.OpenToString()
~~~

## OpenRequest
Starts writing XML to an HTTP request

Syntax: `<xmlwriter>.OpenRequest(<http-request>)`

Where:
- `<http-request>`: HttpRequest instance

Example:
~~~
&XmlWriter.OpenRequest(&HttpRequest)
~~~

## OpenResponse
Starts writing XML to an HTTP response

Syntax: `<xmlwriter>.OpenResponse(<http-response>)`

Where:
- `<http-response>`: HttpResponse instance

Example:
~~~
&XmlWriter.OpenResponse(&HttpResponse)
~~~

## Close
Closes the XMLWriter and returns the resulting string (if OpenToString was used)

Syntax: `<xmlwriter>.Close()`

Example:
~~~
&XmlString = &XmlWriter.Close()
~~~

## WriteStartDocument
Writes the XML declaration and starts the document

Syntax: `<xmlwriter>.WriteStartDocument()`

Example:
~~~
&XmlWriter.WriteStartDocument()
~~~

## WriteStartElement
Writes the start tag of an element

Syntax: `<xmlwriter>.WriteStartElement(<name>)`

Where:
- `<name>`: Element name

Example:
~~~
&XmlWriter.WriteStartElement(!"customers")
~~~

## WriteEndElement
Closes the current element

Syntax: `<xmlwriter>.WriteEndElement()`

Example:
~~~
&XmlWriter.WriteEndElement()
~~~

## WriteElement
Writes a complete element with name and value

Syntax: `<xmlwriter>.WriteElement(<name>, <value>)`

Where:
- `<name>`: Element name
- `<value>`: Element value

Example:
~~~
&XmlWriter.WriteElement(!"name", &CustomerName)
~~~

## WriteAttribute
Writes an attribute to the current element

Syntax: `<xmlwriter>.WriteAttribute(<name>, <value>)`

Where:
- `<name>`: Attribute name
- `<value>`: Attribute value

Constraints:
- Must be called after WriteStartElement and before any content

Example:
~~~
&XmlWriter.WriteStartElement(!"customer")
&XmlWriter.WriteAttribute(!"id", &CustomerId.ToString())
~~~

## WriteText
Writes text content to the current element

Syntax: `<xmlwriter>.WriteText(<text>)`

Where:
- `<text>`: Text content

Example:
~~~
&XmlWriter.WriteText(&CustomerName)
~~~

## WriteComment
Writes a comment

Syntax: `<xmlwriter>.WriteComment(<text>)`

Where:
- `<text>`: Comment text

Example:
~~~
&XmlWriter.WriteComment(!"This is a comment")
~~~

## WriteCData
Writes a CDATA section

Syntax: `<xmlwriter>.WriteCData(<text>)`

Where:
- `<text>`: CDATA content

Example:
~~~
&XmlWriter.WriteCData(!"Some <CDATA> content")
~~~

## WriteRawText
Writes raw text without any escaping

Syntax: `<xmlwriter>.WriteRawText(<text>)`

Where:
- `<text>`: Raw text content

Example:
~~~
&XmlWriter.WriteRawText(!"<raw>content</raw>")
~~~

## WriteEntityReference
Writes an entity reference

Syntax: `<xmlwriter>.WriteEntityReference(<name>)`

Where:
- `<name>`: Entity name

Example:
~~~
&XmlWriter.WriteEntityReference(!"amp")
~~~

## WriteProcessingInstruction
Writes a processing instruction

Syntax: `<xmlwriter>.WriteProcessingInstruction(<target>, <data>)`

Where:
- `<target>`: Processing instruction target
- `<data>`: Processing instruction data

Example:
~~~
&XmlWriter.WriteProcessingInstruction(!"xml-stylesheet", !"type='text/xsl' href='style.xsl'")
~~~

## WriteNsStartElement
Writes the start tag of a namespaced element

Syntax: `<xmlwriter>.WriteNsStartElement(<prefix>, <name>)`

Where:
- `<prefix>`: Namespace prefix
- `<name>`: Element name

Example:
~~~
&XmlWriter.WriteNsStartElement(!"ns", !"item")
~~~

## WriteNsElement
Writes a complete namespaced element

Syntax: `<xmlwriter>.WriteNsElement(<prefix>, <name>, <value>)`

Where:
- `<prefix>`: Namespace prefix
- `<name>`: Element name
- `<value>`: Element value

Example:
~~~
&XmlWriter.WriteNsElement(!"ns", !"item", &ItemValue)
~~~

## WriteDocType
Writes a document type declaration

Syntax: `<xmlwriter>.WriteDocType(<name>[, <subset>])`

Where:
- `<name>`: Document type name
- `<subset>`: Optional internal subset

Example:
~~~
&XmlWriter.WriteDocType(!"book")
~~~

## WriteDocTypePublic
Writes a document type with public identifier

Syntax: `<xmlwriter>.WriteDocTypePublic(<name>, <public-id>, <uri>[, <subset>])`

Where:
- `<name>`: Document type name
- `<public-id>`: Public identifier
- `<uri>`: System URI
- `<subset>`: Optional internal subset

Example:
~~~
&XmlWriter.WriteDocTypePublic(!"book", !"PublicId", !"http://example.com/dtd")
~~~

## WriteDocTypeSystem
Writes a document type with system identifier

Syntax: `<xmlwriter>.WriteDocTypeSystem(<name>, <uri>[, <subset>])`

Where:
- `<name>`: Document type name
- `<uri>`: System URI
- `<subset>`: Optional internal subset

Example:
~~~
&XmlWriter.WriteDocTypeSystem(!"book", !"file.dtd")
~~~

---

# CONSTRAINTS
- Always use `new()` to create XmlWriter instances
- Always call `Close()` after writing XML
- Call `WriteStartElement()` before `WriteAttribute()`
- Match every `WriteStartElement()` with `WriteEndElement()`
- Use `WriteStartDocument()` at the beginning for well-formed XML
- When using `OpenToString()`, retrieve result via `Close()` or `ResultingString` property