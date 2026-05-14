---
name: common-extended-type-httpresponse
description: HTTP response builder for web objects
---

Builds HTTP responses with headers, body content, and file attachments

---

# METHODS

## AddString
Adds payload string to the response body

Syntax: `<httpresponse>.AddString(<payload>)`

Where:
- `<payload>`: String content to add to response body

Example:
~~~
&HttpResponse.AddString(&MySdt.ToJson())
~~~

## AddFile
Adds a file to the response body

Syntax: `<httpresponse>.AddFile(<path>[, <name>])`

Where:
- `<path>`: File path string
- `<name>`: File name string (optional; mandatory for multipart/form-data responses)

Example:
~~~
&HttpResponse.AddFile(&FilePath)
&HttpResponse.AddFile(&FilePath, &FileName)
~~~

## AddHeader
Adds an HTTP header with name and value to the response

Syntax: `<httpresponse>.AddHeader(<name>, <value>)`

Where:
- `<name>`: Header name
- `<value>`: Header value

Example:
~~~
&HttpResponse.AddHeader(!"Content-Type", !"application/json")
&HttpResponse.AddHeader(!"Authorization", format(!"Bearer %1", &Token))
~~~

---

# CONSTRAINTS
- HttpResponse is automatically available in web objects; no `new()` required
- Set headers before adding body content
- Multiple calls to `AddString` will concatenate content