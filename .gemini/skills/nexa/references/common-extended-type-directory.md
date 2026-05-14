---
name: common-extended-type-directory
description: File system operations for managing directories
---

Manages system directories including create, delete, list contents, and access platform-specific paths

---

# PROPERTIES

## Source
Specifies the directory path to work with

Syntax: `<directory>.Source`

Example:
~~~
&Directory.Source = &DirPath
~~~

## ApplicationDataPath
Application data folder on all platforms (static, read-only)

Syntax: `Directory.ApplicationDataPath`

Example:
~~~
&AppDataPath = Directory.ApplicationDataPath
~~~

## TemporaryFilesPath
Temporary files folder (static, read-only)

Syntax: `Directory.TemporaryFilesPath`

Constraints:
- Returns `%TEMP%` in Windows, `/tmp` in Linux/macOS

Example:
~~~
&TempPath = Directory.TemporaryFilesPath
~~~

## ExternalFilesPath
External storage path or ApplicationDataPath if none (static, read-only)

Syntax: `Directory.ExternalFilesPath`

Example:
~~~
&ExtPath = Directory.ExternalFilesPath
~~~

## CacheFilesPath
Cache folder for iOS and Android environments (static, read-only)

Syntax: `Directory.CacheFilesPath`

Example:
~~~
&CachePath = Directory.CacheFilesPath
~~~

## ErrCode
Numeric error code (read-only); 0 indicates success

Syntax: `<directory>.ErrCode`

Example:
~~~
If &Directory.ErrCode <> 0
	msg(&Directory.ErrDescription)
EndIf
~~~

## ErrDescription
Text error description (read-only)

Syntax: `<directory>.ErrDescription`

Example:
~~~
&ErrorMsg = &Directory.ErrDescription
~~~

---

# METHODS

## Exists
Checks if the directory exists

Syntax: `<directory>.Exists()`

Example:
~~~
If &Directory.Exists()
	// Directory exists
EndIf
~~~

## Create
Creates the directory at Source path

Syntax: `<directory>.Create()`

Example:
~~~
&Directory.Create()
~~~

## Delete
Deletes the directory and all its contents

Syntax: `<directory>.Delete()`

Example:
~~~
&Directory.Delete()
~~~

## Rename
Renames or moves the directory

Syntax: `<directory>.Rename(<input>)`

Where:
- `<input>`: New directory name or path

Example:
~~~
&Directory.Rename(&NewDirPath)
~~~

## GetName
Returns the directory name

Syntax: `<directory>.GetName()`

Example:
~~~
&DirName = &Directory.GetName()
~~~

## GetAbsoluteName
Returns the full absolute path

Syntax: `<directory>.GetAbsoluteName()`

Example:
~~~
&FullPath = &Directory.GetAbsoluteName()
~~~

## GetFiles
Returns a collection of files in the directory

Syntax: `<directory>.GetFiles([<filter>])`

Where:
- `<filter>`: Optional file filter string (e.g. `!"*.txt"`)

Constraints:
- Returns collection of `File` instances

Example:
~~~
For &File in &Directory.GetFiles(!"*.txt")
	// Process each text file
EndFor
~~~

## GetDirectories
Returns a collection of subdirectories

Syntax: `<directory>.GetDirectories()`

Constraints:
- Returns collection of `Directory` instances

Example:
~~~
For &SubDir in &Directory.GetDirectories()
	// Process each subdirectory
EndFor
~~~

---

# CONSTRAINTS
- Always use `new()` to create Directory instances
- Set `Source` property before performing operations
- Static properties (`ApplicationDataPath`, etc.) are accessed directly without instantiation
- Check `ErrCode` and `ErrDescription` for error handling
- `Delete()` removes directory and all contents recursively