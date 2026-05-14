---
name: common-extended-type-httprequest
description: Incoming HTTP request context in web objects
---

Provides access to headers, body, URL info, client, and server metadata for incoming HTTP requests

---

# PROPERTIES

## BaseUrl
Server base URL

Syntax: `<httprequest>.BaseUrl`

Example:
~~~
&BaseUrl = &HttpRequest.BaseUrl
~~~

## RemoteAddress
Client IP address

Syntax: `<httprequest>.RemoteAddress`

Example:
~~~
&ClientIp = &HttpRequest.RemoteAddress
~~~

## Method
HTTP method used for the request

Syntax: `<httprequest>.Method`

Constraints:
- Value is from `HttpMethod` enum (`GET`, `POST`, `PUT`, `DELETE`)

Example:
~~~
If &HttpRequest.Method = HttpMethod.POST
	// Process POST request
EndIf
~~~

## QueryString
Raw query string from the URL

Syntax: `<httprequest>.QueryString`

Example:
~~~
&Query = &HttpRequest.QueryString
~~~

## Referrer
Referring page URL

Syntax: `<httprequest>.Referrer`

Example:
~~~
&RefUrl = &HttpRequest.Referrer
~~~

## ScriptName
Name of the executed script or endpoint

Syntax: `<httprequest>.ScriptName`

Example:
~~~
&Script = &HttpRequest.ScriptName
~~~

## ScriptPath
Relative path to the script

Syntax: `<httprequest>.ScriptPath`

Example:
~~~
&Path = &HttpRequest.ScriptPath
~~~

## ServerHost
Server host name

Syntax: `<httprequest>.ServerHost`

Example:
~~~
&Host = &HttpRequest.ServerHost
~~~

## ServerPort
Server port number

Syntax: `<httprequest>.ServerPort`

Example:
~~~
&Port = &HttpRequest.ServerPort
~~~

## Secure
Indicates if the request used HTTPS (1) or HTTP (0)

Syntax: `<httprequest>.Secure`

Example:
~~~
If &HttpRequest.Secure = 1
	// Process secure request
EndIf
~~~

---

# METHODS

## GetHeader
Returns the value of the specified header

Syntax: `<httprequest>.GetHeader(<name>)`

Where:
- `<name>`: Header name to retrieve

Example:
~~~
&UserAgent = &HttpRequest.GetHeader(!"User-Agent")
&ContentType = &HttpRequest.GetHeader(!"Content-Type")
~~~

## GetVariable
Retrieves a form or query variable by name

Syntax: `<httprequest>.GetVariable(<name>)`

Where:
- `<name>`: Variable name to retrieve

Example:
~~~
&OrderId = &HttpRequest.GetVariable(!"order-id")
~~~

## ToString
Returns the raw request body as string

Syntax: `<httprequest>.ToString()`

Example:
~~~
&RequestBody = &HttpRequest.ToString()
~~~

## ToFile
Saves the request body to a file

Syntax: `<httprequest>.ToFile(<path>)`

Where:
- `<path>`: File path where request body will be saved

Example:
~~~
&HttpRequest.ToFile(&FilePath)
~~~

---

# CONSTRAINTS
- HttpRequest is automatically available in web objects; no `new()` required
- Access request metadata through properties, not methods
- Use `GetVariable` for form data and query parameters
- Use `GetHeader` for HTTP headers