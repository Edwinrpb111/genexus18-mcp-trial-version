---
name: common-extended-type-expression
description: Mathematical expression evaluator
---

Evaluates complex mathematical formulas from string expressions with variables and functions

---

# PROPERTIES

## Expression
Formula string to evaluate (write-only)

Syntax: `<expression>.Expression`

Constraints:
- Always use constant string (non-translatable) with exclamation mark (`!`) prefix
- Always use parentheses to disambiguate operation order
- Optimize expressions using supported elements

Example:
~~~
&ConeVolume.Expression = !"pi * pow(r, 2) * h / 3"
~~~

## Variables
Assigns argument values for the expression (write-only)

Syntax: `<expression>.Variables.Set(<arg-name>, <arg-value>)`

Where:
- `<arg-name>`: Variable name (constant string with `!` prefix)
- `<arg-value>`: Variable value (must be string; use `.ToString()` for numeric values)

Constraints:
- Never use `Set` method over predefined constants (e.g. `pi`)

Example:
~~~
&ConeVolume.Variables.Set(!"r", &Radius.ToString())
&ConeVolume.Variables.Set(!"h", &Height.ToString())
~~~

## ErrCode
Numeric error code (read-only); 0 indicates success

Syntax: `<expression>.ErrCode`

Example:
~~~
If &ConeVolume.ErrCode <> 0
	msg(&ConeVolume.ErrDescription)
EndIf
~~~

## ErrDescription
Text error description (read-only)

Syntax: `<expression>.ErrDescription`

Example:
~~~
&ErrorMsg = &ConeVolume.ErrDescription
~~~

---

# METHODS

## Evaluate
Executes the formula and returns the numeric result

Syntax: `<expression>.Evaluate()`

Example:
~~~
&Result = &ConeVolume.Evaluate()
If &ConeVolume.ErrCode = 0
	msg(format(!"Result: %1", &Result), status)
EndIf
~~~

---

# SUPPORTED ELEMENTS

## Constants
- `pi`: π value (3.14159…)

## Arithmetic Operators
- `+`: Addition
- `-`: Subtraction
- `*`: Multiplication
- `/`: Division

## Logical Operators
- `!`: Logical NOT
- `&&`, `and`: Logical AND
- `||`, `or`: Logical OR

## Comparison Operators
- `>`: Greater than
- `<`: Less than
- `>=`: Greater than or equal
- `<=`: Less than or equal
- `<>`: Not equal
- `==`: Equal

## Functions
- `abs`: Absolute value
- `int`: Integer part
- `frac`: Fractional part
- `sin`, `asin`: Sine and arcsine
- `cos`, `acos`: Cosine and arccosine
- `tan`, `atan`: Tangent and arctangent
- `floor`: Floor function
- `round`: Rounding
- `ln`: Natural logarithm
- `log`: Base-10 logarithm
- `exp`: Exponential
- `sqrt`: Square root
- `pow`: Power function
- `max`: Maximum value
- `min`: Minimum value
- `rnd`: Random number
- `iif`: Conditional expression

## Procedures
- Only procedures that receive character/numeric parameters and return `Numeric(-10.2)`
- Procedure names are case-sensitive

Example:
~~~
&DiscountAmount.Expression = !"total * (1 - DiscountRate(id))"

/*
	Being DiscountRate procedure defined with:
	- parm(in:&CustomerId, out:&Discount)
	- CustomerId [ DataType = 'Attribute:CustomerId' ]
	- Discount [ DataType = 'Numeric(-10.2)' ]
*/

&DiscountAmount.Variables.Set(!"total", &PurchaseTotal.ToString())
&DiscountAmount.Variables.Set(!"id", &CustomerId.ToString())
~~~

---

# CONSTRAINTS
- Always use `new()` to create Expression instances
- Set `Expression` property before setting `Variables`
- All variable values must be converted to strings using `.ToString()`
- Always check `ErrCode` after `Evaluate()` to handle errors
- Use parentheses to ensure correct operation order
- Predefined constants (like `pi`) cannot be overridden via `Set(…)`; e.g. `&ConeVolume.Set("pi", "3.14")` is invalid