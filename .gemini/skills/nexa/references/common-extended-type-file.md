---
name: common-extended-type-file
description: File system operations for managing files
---

Manages system files including create, move, delete, get attributes, and handle text content

---

# PROPERTIES

## Source
Specifies the file path to work with

Syntax: `<file>.Source`

Example:
~~~
&File.Source = &FilePath
~~~

## Separator
Directory separator character depending on the OS (read-only)

Syntax: `<file>.Separator`

Constraints:
- Returns platform-specific separator (`\` for Windows, `/` for Unix-based)

Example:
~~~
&Path = &BaseDir + &File.Separator + &FileName
~~~

## ErrCode
Numeric error code (read-only); 0 indicates success

Syntax: `<file>.ErrCode`

Example:
~~~
If &File.ErrCode <> 0
	msg(&File.ErrDescription)
EndIf
~~~

## ErrDescription
Text error description (read-only)

Syntax: `<file>.ErrDescription`

Example:
~~~
&ErrorMsg = &File.ErrDescription
~~~

---

# METHODS

## Exists
Checks if the file exists

Syntax: `<file>.Exists()`

Example:
~~~
If &File.Exists()
	// File exists
EndIf
~~~

## Copy
Copies the file to specified path; overwrites if exists

Syntax: `<file>.Copy(<path>)`

Where:
- `<path>`: Destination file path

Example:
~~~
&File.Copy(&NewPath)
~~~

## Delete
Deletes the file

Syntax: `<file>.Delete()`

Example:
~~~
&File.Delete()
~~~

## Rename
Renames or moves the file

Syntax: `<file>.Rename(<input>)`

Where:
- `<input>`: New file name or path

Example:
~~~
&File.Rename(&NewFileName)
~~~

## GetName
Returns the file name

Syntax: `<file>.GetName()`

Example:
~~~
&FileName = &File.GetName()
~~~

## GetAbsoluteName
Returns the full absolute path

Syntax: `<file>.GetAbsoluteName()`

Example:
~~~
&FullPath = &File.GetAbsoluteName()
~~~

## GetPath
Returns the parent directory path

Syntax: `<file>.GetPath()`

Example:
~~~
&DirPath = &File.GetPath()
~~~

## GetUri
Returns the file URI for local or external storage

Syntax: `<file>.GetUri()`

Example:
~~~
&FileUri = &File.GetUri()
~~~

## GetLastModified
Returns the last modified date and time

Syntax: `<file>.GetLastModified()`

Example:
~~~
&ModifiedDate = &File.GetLastModified()
~~~

## GetLength
Returns the file size in bytes

Syntax: `<file>.GetLength()`

Example:
~~~
&FileSize = &File.GetLength()
~~~

## FromBase64String
Writes decoded Base64 content into the file

Syntax: `<file>.FromBase64String(<base64>)`

Where:
- `<base64>`: Base64-encoded string

Constraints:
- Returns Boolean; True if success

Example:
~~~
If &File.FromBase64String(&Base64Data)
	// File written successfully
EndIf
~~~

## Open
Opens the file for text read/write operations

Syntax: `<file>.Open([<encoding>])`

Where:
- `<encoding>`: Optional encoding string (e.g. `!"UTF-8"`)

Example:
~~~
&File.Open(!"UTF-8")
~~~

## OpenRead
Opens the file for reading text

Syntax: `<file>.OpenRead([<encoding>])`

Where:
- `<encoding>`: Optional encoding string

Example:
~~~
&File.OpenRead(!"UTF-8")
~~~

## OpenWrite
Opens the file for writing text

Syntax: `<file>.OpenWrite([<encoding>])`

Where:
- `<encoding>`: Optional encoding string

Example:
~~~
&File.OpenWrite(!"UTF-8")
~~~

## Close
Closes the file

Syntax: `<file>.Close()`

Example:
~~~
&File.Close()
~~~

## ReadLine
Reads the next line without the line break

Syntax: `<file>.ReadLine()`

Constraints:
- File must be opened with `Open()` or `OpenRead()` first

Example:
~~~
&File.OpenRead()
Do While NOT &File.EOF
	&Line = &File.ReadLine()
EndDo
&File.Close()
~~~

## WriteLine
Writes text followed by line break

Syntax: `<file>.WriteLine(<text>)`

Where:
- `<text>`: Text to write

Constraints:
- File must be opened with `Open()` or `OpenWrite()` first

Example:
~~~
&File.OpenWrite()
&File.WriteLine(!"Log entry: " + &Timestamp.ToString())
&File.Close()
~~~

## ReadAllText
Reads entire file content as text

Syntax: `<file>.ReadAllText([<encoding>])`

Where:
- `<encoding>`: Optional encoding string

Example:
~~~
&FileContent = &File.ReadAllText(!"UTF-8")
~~~

## ReadAllLines
Reads all lines into a string collection

Syntax: `<file>.ReadAllLines([<encoding>])`

Where:
- `<encoding>`: Optional encoding string

Constraints:
- Returns collection of strings

Example:
~~~
&Lines = &File.ReadAllLines()
For &Line in &Lines
	// Process each line
EndFor
~~~

## WriteAllText
Writes and overwrites entire file content

Syntax: `<file>.WriteAllText(<text>[, <encoding>])`

Where:
- `<text>`: Text content to write
- `<encoding>`: Optional encoding string

Example:
~~~
&File.WriteAllText(&Content, !"UTF-8")
~~~

## WriteAllLines
Writes a collection of lines to file

Syntax: `<file>.WriteAllLines(<lines>[, <encoding>])`

Where:
- `<lines>`: Collection of strings
- `<encoding>`: Optional encoding string

Example:
~~~
&File.WriteAllLines(&LineCollection)
~~~

## AppendAllText
Appends text to the file

Syntax: `<file>.AppendAllText(<text>[, <encoding>])`

Where:
- `<text>`: Text to append
- `<encoding>`: Optional encoding string

Example:
~~~
&File.AppendAllText(&LogEntry, !"UTF-8")
~~~

## AppendAllLines
Appends a collection of lines to file

Syntax: `<file>.AppendAllLines(<lines>[, <encoding>])`

Where:
- `<lines>`: Collection of strings
- `<encoding>`: Optional encoding string

Example:
~~~
&File.AppendAllLines(&NewLines)
~~~

---

# CONSTRAINTS
- Always use `new()` to create File instances
- Set `Source` property before performing operations
- Use `Separator` property when building file paths for cross-platform compatibility
- Call `Close()` after `Open()`, `OpenRead()`, or `OpenWrite()` operations
- Check `ErrCode` and `ErrDescription` for error handling
- Methods like `ReadAllText` and `WriteAllText` handle file opening/closing automatically