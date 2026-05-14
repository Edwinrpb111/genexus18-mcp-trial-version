---
name: common-extended-type-mailrecipient
description: Email recipient information
---

Defines the name and email address of an email message recipient

---

# PROPERTIES

## Address
Email address of the recipient

Syntax: `<mailrecipient>.Address`

Example:
~~~
&MailRecipient.Address = !"user@example.com"
~~~

## Name
Display name of the recipient

Syntax: `<mailrecipient>.Name`

Constraints:
- Defaults to address if empty

Example:
~~~
&MailRecipient.Name = !"John Doe"
~~~

---

# CONSTRAINTS
- Always use `new()` to create MailRecipient instances
- `Address` is required; `Name` is optional
- Used with `MailMessage` for `To`, `Cc`, `Bcc`, and `From` properties