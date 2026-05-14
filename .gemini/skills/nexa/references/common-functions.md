---
name: common-functions
description: Performs operations returning values based on input arguments
---

Functions for calculations, date/time handling, string formatting, and system operations

---

# MATHEMATICAL FUNCTIONS

## Mod
Returns remainder of division

Syntax: `Mod(<dividend>, <divisor>)`
Method: N/A

Examples:
~~~
Mod(10, 3) // Returns 1
Mod(15, 4) // Returns 3
~~~

## Abs
Returns absolute value

Syntax: `Abs(<value>)`
Method: N/A

Examples:
~~~
Abs(-5)   // Returns 5
Abs(10.5) // Returns 10.5
~~~

## Round
Rounds number to specified decimals

Syntax: `Round(<expression>, <decimals>)`
Method: `<expression>.Round(<decimals>)`

Examples:
~~~
Round(10.567, 2) // Returns 10.57
&Value.Round(2)  // Method alternative
~~~

## Int
Returns integer part of number

Syntax: `Int(<expression>)`
Method: `<expression>.Int()`

Examples:
~~~
Int(10.9)    // Returns 10
&Value.Int() // Method alternative
~~~

## Trunc
Truncates number

Syntax: `Trunc(<expression>)`
Method: `<expression>.Truncate()`

Examples:
~~~
Trunc(10.9)       // Returns 10
&Value.Truncate() // Method alternative
~~~

## Random
Returns random number between 0 and 1

Syntax: `Random()`
Method: N/A

Examples:
~~~
&RandomValue = Random()
&RandomInt = Int(Random() * 100)
~~~

---

# TIME FUNCTIONS

## Today
Returns current date

Syntax: `Today()`
Method: N/A

Examples:
~~~
&CurrentDate = Today()
~~~

## Now
Returns current datetime

Syntax: `Now()`
Method: N/A

Examples:
~~~
&CurrentDateTime = Now()
~~~

## Year
Returns year component

Syntax: `Year(<expression>)`
Method: `<expression>.Year()`

Examples:
~~~
Year(#2025-07-11#)     // Returns 2025
&Date.Year()           // Method alternative
~~~

## Month
Returns month component

Syntax: `Month(<expression>)`
Method: `<expression>.Month()`

Examples:
~~~
Month(#2025-07-11#)    // Returns 7
&Date.Month()          // Method alternative
~~~

## Day
Returns day component

Syntax: `Day(<expression>)`
Method: `<expression>.Day()`

Examples:
~~~
Day(#2025-07-11#)      // Returns 11
&Date.Day()            // Method alternative
~~~

## Hour
Returns hour component

Syntax: `Hour(<expression>)`
Method: `<expression>.Hour()`

Examples:
~~~
Hour(#2025-07-11 14:35:00#)  // Returns 14
&DateTime.Hour()             // Method alternative
~~~

## Minute
Returns minute component

Syntax: `Minute(<expression>)`
Method: `<expression>.Minute()`

Examples:
~~~
Minute(#2025-07-11 14:35:00#)  // Returns 35
&DateTime.Minute()             // Method alternative
~~~

## Second
Returns second component

Syntax: `Second(<expression>)`
Method: `<expression>.Second()`

Examples:
~~~
Second(#2025-07-11 14:35:26#)  // Returns 26
&DateTime.Second()             // Method alternative
~~~

## Age
Returns age based on date

Syntax: `Age(<expression>[, <reference-expression>])`
Method: `<expression>.Age([<reference-expression>])`

Examples:
~~~
Age(#1990-05-15#)              // Age from today
Age(#1990-05-15#, &RefDate)    // Age from reference
&BirthDate.Age()               // Method alternative
~~~

## YMDtoD
Creates date from components

Syntax: `YMDtoD(<year>, <month>, <day>)`
Method: `<expression>.Set(<year>, <month>, <day>)`

Examples:
~~~
YMDtoD(2025, 7, 11)            // Returns #2025-07-11#
&Date.Set(2025, 7, 11)         // Method alternative
~~~

## YMDHMStoT
Creates datetime from components

Syntax: `YMDHMStoT(<year>, <month>, <day>[, <hour>, <minute>, <second>])`
Method: `<expression>.Set(<year>, <month>, <day>[, <hour>[, <minute>[, <second>]]])`

Examples:
~~~
YMDHMStoT(2025, 7, 11, 14, 30, 26)  // Returns #2025-07-11 14:30:26#
&DateTime.Set(2025, 7, 11, 14, 30)  // Method alternative
~~~

---

# STRING FUNCTIONS

## Len
Returns string length

Syntax: `Len(<expression>)`
Method: `<expression>.Length()`

Examples:
~~~
Len(!"Hello")                  // Returns 5
&Text.Length()                 // Method alternative
~~~

## Trim
Removes leading and trailing spaces

Syntax: `Trim(<expression>)`
Method: `<expression>.Trim()`

Examples:
~~~
Trim(!" Hello ")               // Returns "Hello"
&Text.Trim()                   // Method alternative
~~~

## Upper
Converts to uppercase

Syntax: `Upper(<expression>)`
Method: `<expression>.ToUpper()`

Examples:
~~~
Upper(!"hello")                // Returns "HELLO"
&Text.ToUpper()                // Method alternative
~~~

## Lower
Converts to lowercase

Syntax: `Lower(<expression>)`
Method: `<expression>.ToLower()`

Examples:
~~~
Lower(!"HELLO")                // Returns "hello"
&Text.ToLower()                // Method alternative
~~~

## Str
Converts number to string

Syntax: `Str(<expression>[, <length>[, <decimals>]])`
Method: `<expression>.ToString()`

Examples:
~~~
Str(123)                       // Returns "123"
Str(123.45, 10, 2)             // Returns "    123.45"
&Number.ToString()             // Method alternative
~~~

## Val
Converts string to number

Syntax: `Val(<expression>)`
Method: `<expression>.ToNumeric()`

Examples:
~~~
Val(!"123")                    // Returns 123
&Text.ToNumeric()              // Method alternative
~~~

## Concat
Concatenates strings with optional separator

Syntax: `Concat(<expression-1>, <expression-2>[, <separator>])`
Method: N/A

Examples:
~~~
Concat(!"Hello", !"World")             // Returns "HelloWorld"
Concat(!"Hello", !"World", !" ")       // Returns "Hello World"
~~~

## Format
Formats string with interpolated values

Syntax: `Format(<template>, <value-1>, …, <value-9>)`
Method: N/A

Examples:
~~~
Format(!"Hello %1", &Name)
Format(!"Total: %1, Tax: %2", &Total, &Tax)
Format(!"Result: %1 / %2", &Num, &Den)
~~~

Notes:
- Prefer `Format` over concatenation for cleaner code
- Never define `Format` without interpolated arguments

## Space
Returns string with specified spaces

Syntax: `Space(<count>)`
Method: N/A

Examples:
~~~
Space(5)                       // Returns "     "
~~~

## Chr
Returns character from ASCII code

Syntax: `Chr(<ascii-code>)`
Method: N/A

Examples:
~~~
Chr(65)                        // Returns "A"
Chr(10)                        // Returns newline
~~~

## Asc
Returns ASCII code from character

Syntax: `Asc(<expression>)`
Method: N/A

Examples:
~~~
Asc(!"A")                      // Returns 65
~~~

## UrlEncode
Encodes text as URL-safe string

Syntax: `UrlEncode(<expression>)`
Method: N/A

Examples:
~~~
UrlEncode(!"Hello World")      // Returns "Hello%20World"
~~~

## UrlDecode
Decodes URL-encoded text

Syntax: `UrlDecode(<expression>)`
Method: N/A

Examples:
~~~
UrlDecode(!"Hello%20World")    // Returns "Hello World"
~~~

---

# UTILITY FUNCTIONS

## Iif
Conditional inline expression

Syntax: `Iif(<condition>, <true-value>, <false-value>)`
Method: N/A

Examples:
~~~
&Status = Iif(&Age >= 18, !"Adult", !"Minor")
&Discount = Iif(&IsMember, 0.10, 0.05)
~~~

## Null
Checks if value is null or empty

Syntax: `Null(<expression>)`
Method: `<expression>.IsEmpty()`

Examples:
~~~
If Null(&Value)
If &Value.IsEmpty()            // Method alternative
~~~

## Old
Returns attribute value before modification

Syntax: `Old(<attribute>)`
Method: `<attribute>.GetOldValue()`

Examples:
~~~
&OldPrice = Old(ProductPrice)
&OldPrice = ProductPrice.GetOldValue()  // Method alternative
~~~

## Compare
Compares two values with operator

Syntax: `Compare(<value1>, <operator>, <value2>)`
Method: N/A

Examples:
~~~
Compare(&Age, !">=", 18)
Compare(&Name, !"=", !"John")
~~~

## Link
Generates link to object

Syntax: `Link(<object>[, <param-1>, …, <param-N>])`
Method: N/A

Examples:
~~~
&Url = Link(ProductDetail, &ProductId)
~~~

## Shell
Executes external program and returns exit code

Syntax: `Shell(<program>[, <modal>[, <redirectOutput>]])`
Method: N/A

Examples:
~~~
&Ret = Shell(!"notepad.exe")
&Ret = Shell(!"curl -s https://example.com/health", 1, 1)
~~~

## Sleep
Pauses execution

Syntax: `Sleep(<seconds>)`
Method: N/A

Examples:
~~~
Sleep(5) // Pause 5 seconds
~~~

---

# CONSTRAINTS
- Propose `Procedure` object creation only when no listed function can fulfill the request
- Prefer methods over functions when available
