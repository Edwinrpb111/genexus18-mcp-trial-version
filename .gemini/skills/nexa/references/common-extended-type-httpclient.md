---
name: common-extended-type-httpclient
description: HTTP client for building requests and receiving responses
---

Executes HTTP requests to external services with full control over headers, authentication, and request body

---

# PROPERTIES

## Host
Server hostname

Syntax: `<httpclient>.Host`

Example:
~~~
&HttpClient.Host = !"api.example.com"
~~~

## Port
Server port number

Syntax: `<httpclient>.Port`

Example:
~~~
&HttpClient.Port = 443
~~~

## BaseUrl
Server base URL path

Syntax: `<httpclient>.BaseUrl`

Example:
~~~
&HttpClient.BaseUrl = !"https://api.example.com"
~~~

## Secure
Indicates if the request uses HTTPS (1) or HTTP (0, default)

Syntax: `<httpclient>.Secure`

Example:
~~~
&HttpClient.Secure = 1
~~~

## Timeout
Maximum time to wait for responses in seconds

Syntax: `<httpclient>.Timeout`

Example:
~~~
&HttpClient.Timeout = 30
~~~

## IncludeCookies
Indicates if cookies are included in requests

Syntax: `<httpclient>.IncludeCookies`

Example:
~~~
&HttpClient.IncludeCookies = True
~~~

## StatusCode
HTTP status code returned by the server (read-only)

Syntax: `<httpclient>.StatusCode`

Example:
~~~
If &HttpClient.StatusCode = 200
	// Process successful response
EndIf
~~~

## ReasonLine
HTTP reason phrase returned by the server (read-only)

Syntax: `<httpclient>.ReasonLine`

Example:
~~~
&Reason = &HttpClient.ReasonLine
~~~

## EOF
Indicates if the data stream ended (read-only)

Syntax: `<httpclient>.EOF`

Constraints:
- Useful for reading chunks using the `ReadChunk` method

Example:
~~~
Do While NOT &HttpClient.EOF
	&Chunk = &HttpClient.ReadChunk()
EndDo
~~~

## ProxyServerHost
Host name of the proxy server

Syntax: `<httpclient>.ProxyServerHost`

Example:
~~~
&HttpClient.ProxyServerHost = !"proxy.example.com"
~~~

## ProxyServerPort
Port number of the proxy server

Syntax: `<httpclient>.ProxyServerPort`

Example:
~~~
&HttpClient.ProxyServerPort = 8080
~~~

## ErrCode
Numeric error code (read-only); 0 indicates success, any other code indicates failure

Syntax: `<httpclient>.ErrCode`

Example:
~~~
If &HttpClient.ErrCode <> 0
	msg(&HttpClient.ErrDescription)
EndIf
~~~

## ErrDescription
Text error description with details about the failure (read-only)

Syntax: `<httpclient>.ErrDescription`

Example:
~~~
&ErrorMsg = &HttpClient.ErrDescription
~~~

---

# METHODS

## Execute
Sends the request to source using specified HTTP verb

Syntax: `<httpclient>.Execute(<verb>, <source>)`

Where:
- `<verb>`: HTTP method from `HttpMethod` enum (`GET`, `POST`, `PUT`, `DELETE`)
- `<source>`: URL path to request

Example:
~~~
&HttpClient.Execute(HttpMethod.GET, !"/users")
&HttpClient.Execute(HttpMethod.POST, !"/orders")
~~~

## AddHeader
Adds an HTTP header with name and value to the request

Syntax: `<httpclient>.AddHeader(<name>, <value>)`

Where:
- `<name>`: Header name
- `<value>`: Header value

Example:
~~~
&HttpClient.AddHeader(!"Authorization", format(!"Bearer %1", &Token))
&HttpClient.AddHeader(!"Content-Type", !"application/json")
~~~

## AddString
Adds payload string to the request body

Syntax: `<httpclient>.AddString(<payload>)`

Where:
- `<payload>`: String content to add to request body

Example:
~~~
&HttpClient.AddString(&MySdt.ToJson())
~~~

## AddFile
Adds a file to the request body

Syntax: `<httpclient>.AddFile(<path>[, <name>])`

Where:
- `<path>`: File path string
- `<name>`: File name string (optional; mandatory for multipart/form-data requests)

Example:
~~~
&HttpClient.AddFile(&FilePath)
&HttpClient.AddFile(&FilePath, &FileName)
~~~

## AddVariable
Adds form-data to the request body

Syntax: `<httpclient>.AddVariable(<name>, <value>)`

Where:
- `<name>`: Variable name
- `<value>`: Variable value

Example:
~~~
&HttpClient.AddVariable(!"orderId", &OrderId.ToString())
~~~

## AddAuthentication
Adds HTTP authentication credentials

Syntax: `<httpclient>.AddAuthentication(<method>, <realm>, <username>, <password>)`

Where:
- `<method>`: Authentication method from `HttpAuthenticationType` enum (`Basic`, `Digest`, `NTLM`, `Kerberos`)
- `<realm>`: Authentication realm
- `<username>`: Username for authentication
- `<password>`: Password for authentication

Example:
~~~
&HttpClient.AddAuthentication(HttpAuthenticationType.Basic, !"api.example.com", !"user", !"pass123")
~~~

## AddProxyAuthentication
Adds proxy authentication credentials

Syntax: `<httpclient>.AddProxyAuthentication(<method>, <realm>, <username>, <password>)`

Where:
- `<method>`: Authentication method from `HttpAuthenticationType` enum
- `<realm>`: Proxy authentication realm
- `<username>`: Username for proxy authentication
- `<password>`: Password for proxy authentication

Example:
~~~
&HttpClient.AddProxyAuthentication(HttpAuthenticationType.Digest, !"proxy.example.com", !"admin", !"admin123")
~~~

## GetHeader
Returns the value of a response header

Syntax: `<httpclient>.GetHeader(<name>)`

Where:
- `<name>`: Header name to retrieve

Example:
~~~
&ContentType = &HttpClient.GetHeader(!"Content-Type")
~~~

## ToString
Returns the response body as a string

Syntax: `<httpclient>.ToString()`

Example:
~~~
&JsonData = &HttpClient.ToString()
~~~

## ToFile
Saves the response body to a file

Syntax: `<httpclient>.ToFile(<path>)`

Where:
- `<path>`: File path where response will be saved

Example:
~~~
&HttpClient.ToFile(&FilePath)
~~~

## ReadChunk
Reads a portion of the response body as string

Syntax: `<httpclient>.ReadChunk()`

Example:
~~~
&Chunk = &HttpClient.ReadChunk()
~~~

---

# CONSTRAINTS
- Always use `new()` to create HttpClient instances
- URL syntax follows: `[<protocol>://]<host>[:<port>]/<base>/<source>`
- Check `ErrCode` and `ErrDescription` after operations to handle errors
- Use `StatusCode` property to validate HTTP response status
- For streaming large responses, use `ReadChunk()` with `EOF` property