---
name: common-extended-type-pop3session
description: POP3 session for receiving emails
---

Receives emails from a POP3 mail server with authentication support

---

# PROPERTIES

## Host
POP3 server hostname or IP address

Syntax: `<pop3session>.Host`

Example:
~~~
&Pop3Session.Host = !"pop3.example.com"
~~~

## Port
POP3 server port number

Syntax: `<pop3session>.Port`

Example:
~~~
&Pop3Session.Port = 110
~~~

## UserName
Username for POP3 authentication

Syntax: `<pop3session>.UserName`

Example:
~~~
&Pop3Session.UserName = !"user@example.com"
~~~

## Password
Password for POP3 authentication

Syntax: `<pop3session>.Password`

Example:
~~~
&Pop3Session.Password = &MyPassword
~~~

## Secure
Indicates if connection uses SSL/TLS (1) or not (0, default)

Syntax: `<pop3session>.Secure`

Example:
~~~
&Pop3Session.Secure = 1
~~~

## Timeout
Maximum wait time for responses in seconds

Syntax: `<pop3session>.Timeout`

Example:
~~~
&Pop3Session.Timeout = 30
~~~

## AttachDir
Base directory for saving received attachments

Syntax: `<pop3session>.AttachDir`

Constraints:
- An empty value (`""`) will not perform any save

Example:
~~~
&Pop3Session.AttachDir = !"C:\Attachments"
~~~

## NewMessages
Specifies whether to receive only new (unread) messages (1) or all messages (0)

Syntax: `<pop3session>.NewMessages`

Example:
~~~
&Pop3Session.NewMessages = 1
~~~

## Count
Number of messages in the inbox (read-only)

Syntax: `<pop3session>.Count`

Example:
~~~
&MessageCount = &Pop3Session.Count
~~~

## ErrDisplay
Indicates whether error messages are displayed (1) or not (0)

Syntax: `<pop3session>.ErrDisplay`

Example:
~~~
&Pop3Session.ErrDisplay = 1
~~~

## AuthenticationMethod
Specifies the authentication method

Syntax: `<pop3session>.AuthenticationMethod`

Constraints:
- Values: `!"XOAUTH2"` (OAuth 2.0), `!""` (Basic, default)

Example:
~~~
&Pop3Session.AuthenticationMethod = !"XOAUTH2"
~~~

## ErrCode
Numeric error code (read-only); 0 indicates success

Syntax: `<pop3session>.ErrCode`

Example:
~~~
If &Pop3Session.ErrCode <> 0
	msg(&Pop3Session.ErrDescription)
EndIf
~~~

## ErrDescription
Text error description (read-only)

Syntax: `<pop3session>.ErrDescription`

Example:
~~~
&ErrorMsg = &Pop3Session.ErrDescription
~~~

---

# METHODS

## Login
Initiates the POP3 session

Syntax: `<pop3session>.Login()`

Constraints:
- Returns status code; 0 indicates success

Example:
~~~
If &Pop3Session.Login() = 0
	// Login successful
EndIf
~~~

## Receive
Receives a message identified by UID

Syntax: `<pop3session>.Receive(<message>)`

Where:
- `<message>`: Instance of `MailMessage` to populate with received email

Constraints:
- Returns status code; 0 indicates success

Example:
~~~
If &Pop3Session.Receive(&MailMessage) = 0
	// Message received successfully
EndIf
~~~

## GetNextUID
Retrieves the UID of the next message

Syntax: `<pop3session>.GetNextUID()`

Constraints:
- Returns numeric UID of next message

Example:
~~~
&NextUID = &Pop3Session.GetNextUID()
~~~

## Delete
Deletes a message identified by UID

Syntax: `<pop3session>.Delete(<uid>)`

Where:
- `<uid>`: Message UID to delete

Constraints:
- Returns status code; 0 indicates success

Example:
~~~
&Pop3Session.Delete(&MessageUID)
~~~

## Skip
Skips the current message

Syntax: `<pop3session>.Skip()`

Constraints:
- Returns status code; 0 indicates success

Example:
~~~
&Pop3Session.Skip()
~~~

## Logout
Ends the POP3 session

Syntax: `<pop3session>.Logout()`

Constraints:
- Returns status code; 0 indicates success

Example:
~~~
&Pop3Session.Logout()
~~~

---

# CONSTRAINTS
- Always use `new()` to create Pop3Session instances
- Call `Login()` before receiving messages
- Call `Logout()` after finishing email operations
- Check return codes and `ErrCode`/`ErrDescription` for error handling
- Use `GetNextUID()` to iterate through messages