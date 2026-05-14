---
name: common-extended-type-mailmessage
description: Email message structure
---

Represents an email message with recipients, subject, body, and attachments for sending or receiving

---

# PROPERTIES

## From
Sender of the message (read-only)

Syntax: `<mailmessage>.From`

Constraints:
- Value is a `MailRecipient` instance

Example:
~~~
&SenderAddress = &MailMessage.From.Address
~~~

## To
List of primary recipients (collection)

Syntax: `<mailmessage>.To`

Constraints:
- Collection of `MailRecipient` instances

Example:
~~~
&Recipient = new()
&Recipient.Address = &CustomerEmail
&Recipient.Name = &CustomerName
&MailMessage.To.Add(&Recipient)
~~~

## Cc
List of carbon copy recipients (collection)

Syntax: `<mailmessage>.Cc`

Constraints:
- Collection of `MailRecipient` instances

Example:
~~~
&MailMessage.Cc.Add(&CcRecipient)
~~~

## Bcc
List of blind carbon copy recipients (collection)

Syntax: `<mailmessage>.Bcc`

Constraints:
- Collection of `MailRecipient` instances

Example:
~~~
&MailMessage.Bcc.Add(&BccRecipient)
~~~

## Subject
Subject line of the email

Syntax: `<mailmessage>.Subject`

Example:
~~~
&MailMessage.Subject = !"Order Confirmation"
~~~

## Text
Plain text body of the message

Syntax: `<mailmessage>.Text`

Example:
~~~
&MailMessage.Text = format(!"Order %1 confirmed", &OrderId)
~~~

## HTMLText
HTML-formatted body of the message

Syntax: `<mailmessage>.HTMLText`

Example:
~~~
&MailMessage.HTMLText = format(!"<h1>Order %1</h1><p>Confirmed</p>", &OrderId)
~~~

## Attachments
Collection of attachment file paths (collection)

Syntax: `<mailmessage>.Attachments`

Constraints:
- Collection of file path strings

Example:
~~~
&MailMessage.Attachments.Add(&InvoicePath)
~~~

## DateSent
Timestamp when the email was sent (read-only)

Syntax: `<mailmessage>.DateSent`

Example:
~~~
&SentDate = &MailMessage.DateSent
~~~

## DateReceived
Timestamp when the email was received (read-only)

Syntax: `<mailmessage>.DateReceived`

Example:
~~~
&ReceivedDate = &MailMessage.DateReceived
~~~

---

# CONSTRAINTS
- Always use `new()` to create MailMessage instances
- `From` property is read-only; use `SmtpSession.Sender` for outgoing emails
- Recipients must be added using `MailRecipient` instances
- Attachments are file paths, not file contents
- Both `Text` and `HTMLText` can be set; client determines which to display