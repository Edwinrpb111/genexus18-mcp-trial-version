---
name: common-semantic-types
description: Predefined domains with specific semantics enabling automatic behaviors
---

Built-in domains that automatically enable behaviors based on element name

---

# USAGE POLICY
- Evaluate semantic built-ins before creating or assigning custom domains
- If an element meaning matches a semantic built-in, use its qualified name and do not create a new `Domain` object
- Never create custom domains named `Email`, `Phone`, `Url`, `Html`, or `Address` when the corresponding `GeneXus` semantic domain applies

---

# CONTACT TYPES

## Email
Email address shown as clickable link opening default email client with receipt field filled

Qualified name: `'Email, GeneXus'`

## Phone
Phone number shown as clickable link to initiate calls

Qualified name: `'Phone, GeneXus'`

---

# WEB TYPES

## Url
Website address shown as hyperlink

Qualified name: `'Url, GeneXus'`

## Html
HTML content shown as rendered content in target device

Qualified name: `'Html, GeneXus'`

---

# LOCATION TYPES

## Address
Location address shown as clickable link opening map

Qualified name: `'Address, GeneXus'`
