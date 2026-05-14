---
name: common-events
description: Dynamic responsive logic through declarative event handlers
---

Declarative code that remains inactive until triggered by specific events

---

# GENERAL DEFINITION

## SYNTAX
~~~
Event <event-name>
	<event-code>
EndEvent
~~~

Where:
- `<event-name>`: Predefined keyword or quoted string according to object type
- `<event-code>`: Business logic statements on separate lines without semicolons

---

## QUALIFIERS
Context restrictions:
- `[WEB]`: Runs only in Web (HTML) interface
- `[WIN]`: Runs only in Windows GUI
- `[TEXT]`: Runs only in text-based interface
- `[BC]`: Runs only as Business Component

Example:
~~~
[BC]
{
	Event Start
		msg(!"[INFO] BC started", status)
	EndEvent
}
~~~

---

## CONSTRAINTS
- Use only predefined event names supported by each object type
- Qualifiers must use block form for grouped event definitions
- Event code must not use semicolons at statement end
- For `API` object events, never use direct DB access commands (`For each`, `New`, `Delete`, `Commit`, `Rollback`)
