---
name: common-extended-type-smtpsession
description: SMTP session for sending emails
---

Sends emails through a configured SMTP mail server with authentication support

---

# PROPERTIES

## Host
SMTP server hostname

Syntax: `<smtpsession>.Host`

Example:
~~~
&SmtpSession.Host = !"smtp.gmail.com"
~~~

## Port
SMTP server port number

Syntax: `<smtpsession>.Port`

Example:
~~~
&SmtpSession.Port = 587
~~~

## UserName
Username for SMTP authentication

Syntax: `<smtpsession>.UserName`

Example:
~~~
&SmtpSession.UserName = !"user@example.com"
~~~

## Password
Password for SMTP authentication

Syntax: `<smtpsession>.Password`

Example:
~~~
&SmtpSession.Password = &AppPassword
~~~

## Secure
Indicates if connection uses SSL/TLS (1) or not (0, default)

Syntax: `<smtpsession>.Secure`

Example:
~~~
&SmtpSession.Secure = 1
~~~

## Sender
Defines the sender information

Syntax: `<smtpsession>.Sender`

Constraints:
- Value must be a `MailRecipient` instance

Example:
~~~
&Sender = new()
&Sender.Address = !"noreply@example.com"
&Sender.Name = !"System Notifications"
&SmtpSession.Sender = &Sender
~~~

## Timeout
Maximum time to wait for connection in seconds

Syntax: `<smtpsession>.Timeout`

Example:
~~~
&SmtpSession.Timeout = 60
~~~

## Authentication
Indicates if server requires authentication (1) or not (0, default)

Syntax: `<smtpsession>.Authentication`

Example:
~~~
&SmtpSession.Authentication = 1
~~~

## AuthenticationMethod
Specifies the authentication method

Syntax: `<smtpsession>.AuthenticationMethod`

Constraints:
- Values: `!"XOAUTH2"` (OAuth 2.0), `!""` (Basic, default)

Example:
~~~
&SmtpSession.AuthenticationMethod = !"XOAUTH2"
~~~

## AttachDir
Base directory for searching attached files (via `Attachments` property)

Syntax: `<smtpsession>.AttachDir`

Constraints:
- An empty value (`""`) will not perform any search

Example:
~~~
&SmtpSession.AttachDir = !"C:\Attachments"
~~~

## ErrCode
Numeric error code (read-only); 0 indicates success

Syntax: `<smtpsession>.ErrCode`

Example:
~~~
If &SmtpSession.ErrCode <> 0
	msg(&SmtpSession.ErrDescription)
EndIf
~~~

## ErrDescription
Text error description (read-only)

Syntax: `<smtpsession>.ErrDescription`

Example:
~~~
&ErrorMsg = &SmtpSession.ErrDescription
~~~

---

# METHODS

## Login
Establishes connection to the SMTP server

Syntax: `<smtpsession>.Login()`

Constraints:
- Returns status code; 0 indicates success

Example:
~~~
If &SmtpSession.Login() = 0
	// Connection successful
EndIf
~~~

## Send
Sends an email message

Syntax: `<smtpsession>.Send(<message>)`

Where:
- `<message>`: Instance of `MailMessage` to send

Constraints:
- Returns status code; 0 indicates success

Example:
~~~
If &SmtpSession.Send(&MailMessage) = 0
	// Email sent successfully
EndIf
~~~

## Logout
Closes the connection with the SMTP server

Syntax: `<smtpsession>.Logout()`

Constraints:
- Returns status code; 0 indicates success

Example:
~~~
&SmtpSession.Logout()
~~~

---

# CONSTRAINTS
- Always use `new()` to create SmtpSession instances
- Call `Login()` before `Send()`
- Call `Logout()` after finishing email operations
- Check return codes and `ErrCode`/`ErrDescription` for error handling
- Configure `Sender` property before sending emails