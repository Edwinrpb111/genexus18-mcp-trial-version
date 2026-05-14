---
name: common-external-object-log
description: Log external object for writing log messages at various levels
---

System logging with severity levels and topic channels referenced as a static object

---

# METHODS

## Write
Logs a message optionally specifying topic and log level

Syntax: `Log.Write(<message>[, <topic>, <log-level>])`

Where:
- `<message>`: Message to log (required)
- `<topic>`: Log topic/channel (optional)
- `<log-level>`: Severity level from `LogLevel` enum (optional)

Constraints:
- Topic can be an empty string when specifying a log level

Example:
~~~
Log.Write(!"Process started", !"$MyApp.Process", LogLevel.INFO)
Log.Write(!"Simple message")
Log.Write(!"", !"", LogLevel.DEBUG)
~~~

## Fatal
Logs a fatal error message optionally specifying a topic

Syntax: `Fatal(<message>[, <topic>])`

Where:
- `<message>`: Message to log (required)
- `<topic>`: Log topic/channel (optional)

Example:
~~~
Log.Fatal(!"System crash", !"$MyApp.Core")
Log.Fatal(!"Critical failure")
~~~

## Error
Logs an error message optionally specifying a topic

Syntax: `Error(<message>[, <topic>])`

Where:
- `<message>`: Message to log (required)
- `<topic>`: Log topic/channel (optional)

Example:
~~~
Log.Error(!"Failed to save", !"$MyApp.DB")
Log.Error(format(!"Database error: %1", &ErrorMsg), !"$MyApp.Data")
~~~

## Warning
Logs a warning message optionally specifying a topic

Syntax: `Warning(<message>[, <topic>])`

Where:
- `<message>`: Message to log (required)
- `<topic>`: Log topic/channel (optional)

Example:
~~~
Log.Warning(!"Low disk space", !"$MyApp.Storage")
Log.Warning(!"Connection timeout", !"$MyApp.Network")
~~~

## Info
Logs an informational message optionally specifying a topic

Syntax: `Info(<message>[, <topic>])`

Where:
- `<message>`: Message to log (required)
- `<topic>`: Log topic/channel (optional)

Example:
~~~
Log.Info(!"User logged in", !"MyApp.Auth")
Log.Info(!"Application started", !"$MyApp.Core")
~~~

## Debug
Logs a debug message optionally specifying a topic

Syntax: `Debug(<message>[, <topic>])`

Where:
- `<message>`: Message to log (required)
- `<topic>`: Log topic/channel (optional)

Example:
~~~
Log.Debug(format(!"New user: %1", &UserId), !"$MyApp.Debug")
Log.Debug(!"Processing batch", !"$MyApp.Batch")
~~~

---

# REFERENCES

## LogLevel enum
Enum domain defined in 'GeneXus.Common' module providing severity levels

Values:
- `LogLevel.FATAL`: Fatal level
- `LogLevel.ERROR`: Error level
- `LogLevel.WARNING`: Warning level
- `LogLevel.INFO`: Informational level
- `LogLevel.DEBUG`: Debug level
- `LogLevel.OFF`: Log disabled

Example:
~~~
Log.Write(!"Starting process", !"$MyApp", LogLevel.INFO)
If &LogLevel > LogLevel.OFF
	Log.Write(!"Debug info", !"$MyApp", LogLevel.DEBUG)
EndIf
~~~

## Topics handling
Topics provide channel organization for log messages

Notes:
- If topic starts with dollar sign (`$`), value is used as-is
- Otherwise, topic is prefixed with `GeneXusUserLog` string

Examples:
~~~
Log.Info(!"User login", !"$MyApp.Auth")	// Topic will be: "MyApp.Auth" (as-is)
Log.Info(!"User login", !"MyApp.Auth")	// Topic will be: "GeneXusUserLog.MyApp.Auth"
~~~

---

# CONSTRAINTS
- Topic parameter can be omitted but is recommended for better log organization
- All logging methods are static and do not require object instantiation (as variable)