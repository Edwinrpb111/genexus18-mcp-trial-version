---
name: common-extended-types
description: Built-in extended types for system integration and advanced functionality
---

System-provided types for HTTP, email, XML, geography, file management, and other advanced operations

---

# HTTP TYPES

## HttpClient
See [common-extended-type-httpclient](common-extended-type-httpclient.md)

## HttpRequest
See [common-extended-type-httprequest](common-extended-type-httprequest.md)

## HttpResponse
See [common-extended-type-httpresponse](common-extended-type-httpresponse.md)

---

# EMAIL TYPES

## SmtpSession
See [common-extended-type-smtpsession](common-extended-type-smtpsession.md)

## Pop3Session
See [common-extended-type-pop3session](common-extended-type-pop3session.md)

## MailMessage
See [common-extended-type-mailmessage](common-extended-type-mailmessage.md)

## MailRecipient
See [common-extended-type-mailrecipient](common-extended-type-mailrecipient.md)

---

# FILE SYSTEM TYPES

## File
See [common-extended-type-file](common-extended-type-file.md)

## Directory
See [common-extended-type-directory](common-extended-type-directory.md)

---

# GEOGRAPHY TYPES

## Geography
See [common-extended-type-geography](common-extended-type-geography.md)

## GeoPoint
See [common-extended-type-geopoint](common-extended-type-geopoint.md)

## GeoLine
See [common-extended-type-geoline](common-extended-type-geoline.md)

## GeoPolygon
See [common-extended-type-geopolygon](common-extended-type-geopolygon.md)

---

# XML TYPES

## XmlReader
See [common-extended-type-xmlreader](common-extended-type-xmlreader.md)

## XmlWriter
See [common-extended-type-xmlwriter](common-extended-type-xmlwriter.md)

---

# OTHER TYPES

## Expression
See [common-extended-type-expression](common-extended-type-expression.md)

## RegExMatch
See [common-extended-type-regexmatch](common-extended-type-regexmatch.md)

## Dictionary
See [common-external-object-dictionary](common-external-object-dictionary.md)

## Log
See [common-external-object-log](common-external-object-log.md)

---

# CONSTRAINTS
All extended types share these common characteristics:
- Initialization: Use `new()` function to create instances; e.g. `&MyType = new()`
- Constant strings: Use exclamation mark (`!`) prefix for non-translatable strings; e.g. `!"constant-value"`
- Error handling: Check `ErrCode` and `ErrDescription` properties after operations
	* `ErrCode = 0` indicates success
	* Any other code indicates failure
- Variable-only usage: Extended types can only be used for variables, not for attributes or SDT members
- Resource cleanup: Close resources when finished (files, connections, readers, writers)
- Cross-platform compatibility: Use `Separator` property from File instances when building paths
