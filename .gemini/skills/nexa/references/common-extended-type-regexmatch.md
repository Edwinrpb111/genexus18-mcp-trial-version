---
name: common-extended-type-regexmatch
description: Regular expression match result type
---

Represents one match returned by regular expression operations over `Character`, `VarChar`, or `LongVarChar` values

---

# PROPERTIES

## Value
Full text matched by the regular expression (read-only)

Syntax: `<regex-match>.Value`

Example:
~~~
&Text = &Match.Value
~~~

## Groups
Captured groups matched by the regular expression (read-only collection)

Syntax: `<regex-match>.Groups`

DataType: `VarChar(64)` collection

Example:
~~~
For &Group in &Match.Groups
	msg(&Group, status)
EndFor
~~~

---

# USAGE

## Match collection
String regex methods return collection of `RegExMatch`:
- `<str>.Matches(<regex>)`

Example:
~~~
For &Match in &EmailText.Matches(!"([\\w\\.-]+)@([\\w\\.-]+)")
	&CompleteEmail = &Match.Value
	For &Group in &Match.Groups
		msg(&Group, status)
	EndFor
EndFor
~~~

## Variable definitions
~~~
Match [ DataType = 'RegExMatch' ]
Matches [ DataType = 'RegExMatch', Collection = 'True' ]
Group [ DataType = 'VarChar(64)' ]
~~~

---

# CONSTRAINTS
- Use `RegExMatch` only as variable type (not as attribute, domain, or SDT member)
- Use `Matches()` method when `RegExMatch` details (`Value` and `Groups`) are needed
- Keep regex patterns as non-translatable strings using `!` prefix
