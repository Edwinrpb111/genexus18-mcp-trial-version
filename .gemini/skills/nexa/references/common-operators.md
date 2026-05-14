---
name: common-operators
description: Symbols and keywords for arithmetic, comparison, pattern matching, and logic operations
---

Defines operators available in GeneXus expressions and conditions

---

# DELIMITER OPERATORS
`( )`	: Groups ambiguous expressions

---

# LOGICAL OPERATORS
`NOT`	: Logical negation
`AND`	: Logical conjunction
`OR`	: Logical disjunction

Examples:
~~~
NOT &Boolean
&Left AND &Right
&Left OR &Right
~~~

---

# ARITHMETIC OPERATORS
`^`		: Numeric exponentiation (or power)
`*`		: Numeric multiplication
`/`		: Numeric division
`+`		: Numeric addition or string concatenation
`-`		: Numeric subtraction

Examples:
~~~
&Addition = &Number + &Addend
&Difference = &Number - &Minuend
&Product = &Number * &Multiplier
&Quotient = &Dividend / &Divisor
&Power = &Base ^ &Exponent
&Concat = &First + !" " + &Last
~~~

---

# COMPARISON OPERATORS
`>`		: Greater than
`<`		: Less than
`=`		: Equal to
`<>`	: Not equal to
`<=`	: Less than or equal to
`>=`	: Greater than or equal to
`IN`	: Membership test of element in collection
`LIKE`	: Pattern matching using wildcards `%` (any) or `_` (one)

Examples:
~~~
&Left <> &Right
&Number > &Min AND &Number <= &Max
&Number IN (1, 2, 3)
&Number IN &Collection
&Text LIKE !"%SMITH"
~~~

Notes:
- `LIKE` operator uses `%` as wildcard; trailing `%` are invalid
- `LIKE` is case-insensitive and interpreted as "contains" or "similar to"
- `IN` operator first operand must be a standalone attribute/variable
- `IN` operator second operand must be either of: collection variable, array variable, literal list (syntax: `(<elem-1>, …, <elem-N>)`)
- `IN` operator cannot be combined with `NOT` as `NOT IN`; always use `NOT (… IN …)` instead

---

# COMPOUND ASSIGNMENT OPERATORS
`+=`	: Numeric addition or concatenation assignment
`-=`	: Numeric subtraction

Examples:
~~~
&Counter += 1
&Counter += &Addend
&Counter -= &Minuend
&Text += NewLine()
&Text += &Suffix
~~~

Notes:
- Other compound operators are invalid: `*=`, `/=`, `^=`

---

# CONSTRAINTS
- Never use `EXISTS` operator from SQL in any clause or expression
- Operands must have compatible types
- Use parentheses to clarify precedence when needed
- Only operators listed in this section are available
