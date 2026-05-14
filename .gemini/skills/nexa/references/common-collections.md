---
name: common-collections
description: Collection manipulation methods and properties
---

Operations for elements with Collection property set to True

---

# DEFINITION
Elements with `Collection = 'True'` property provide built-in properties and methods for data manipulation
All operations use one-based indexing (first element is at position 1)

---

# PROPERTIES

## Count
Returns number of elements in collection

Syntax: `<collection>.Count`
DataType: `Numeric(4.0)`, read-only

Example:
~~~
&TotalItems = &MyCollection.Count
~~~

---

# METHODS

## Add
Adds item to end of collection

Syntax: `<colelction>.Add(<item>)`

Where:
- `<item>`: An item with the same base type as `<collection>` elements

Example:
~~~
&Product = new()
&Product.ProductId = 123
&Product.ProductName = !"Widget"
&Products.Add(&Product)
~~~

## AddRange
Adds all elements from collection to target

Syntax: `<collection>.AddRange(<items> [, <index>])`

Where:
- `<items>`: Source collection to copy from with the same base type as `<collection>` elements
- `<index>`: Optional position to insert (if omitted, adds at end)

Example:
~~~
&Products.AddRange(&NewProducts)
&Products.AddRange(&MoreProducts, 5)
~~~

## Clear
Removes all elements from collection

Syntax: `<collection>.Clear()`

Example:
~~~
&Products.Clear()
~~~

## Clone
Creates deep copy of collection

Syntax: `<collection>.Clone()`

Example:
~~~
&ProductsCopy = &Products.Clone()
~~~

## IndexOf
Returns index of first occurrence of item
Returns 0 if not found

Syntax: `<collection>.IndexOf(<item>)`

Where:
- `<item>`: An item with the same base type as `<collection>` elements

Example:
~~~
&Position = &Products.IndexOf(&TargetProduct)
If &Position > 0
	msg(format(!"Found at position %1", &Position))
EndIf
~~~

## LastIndexOf
Returns index of last occurrence of item
Returns 0 if not found

Syntax: `<collection>.LastIndexOf(<item>)`

Where:
- `<item>`: An item with the same base type as `<collection>` elements

Example:
~~~
&LastPosition = &Products.LastIndexOf(&TargetProduct)
~~~

## Item
Returns element at specified index

Syntax: `<collection>.Item(<index>)`

Where:
- `<index>`: Position in the collection

Example:
~~~
&FirstProduct = &Products.Item(1)
&ThirdProduct = &Products.Item(3)
~~~

## Remove
Removes element at specified index

Syntax: `<collection>.Remove(<index>)`

Where:
- `<index>`: Position in the collection

Example:
~~~
&Products.Remove(2)
~~~

## RemoveRange
Removes range of elements starting at index

Syntax: `<collection>.RemoveRange(<index> [, <count>])`

Where:
- `<index>`: Starting position
- `<count>`: Optional number of elements (if omitted, removes all from index)

Example:
~~~
&Products.RemoveRange(3, 5)
&Products.RemoveRange(10)
~~~

## Set
Replaces element at index with item

Syntax: `<collection>.Set(<index>, <item>)`

Where:
- `<index>`: Collection position
- `<item>`: An item with the same base type as `<collection>` elements

Example:
~~~
&UpdatedProduct = new()
&UpdatedProduct.ProductName = !"Updated Widget"
&Products.Set(3, &UpdatedProduct)
~~~

## Sort
Sorts collection in ascending order by default

Syntax: `<collection>.Sort([<criteria>])`

Where:
- `<criteria>`: Optional sorting specification

Criteria syntax:
- Empty or omitted: ascending default order
- `"()"`: descending default order
- `"Member1, Member2"`: ascending by multiple members (SDT only)
- `"Member1, (Member2)"`: mixed order (SDT only)
- Parentheses indicate descending: `(MemberName)`

Examples:
~~~
&Numbers.Sort()
&Numbers.Sort("()")
&Products.Sort("ProductCategory, ProductName")
&Products.Sort("ProductPrice, (ProductName)")
~~~

## New
Initializes collection as empty

Syntax: `<collection> = new()`

Example:
~~~
&Products = new()
~~~

---

# MESSAGES BUILT IN TYPE

## Message built-in type
The `Messages` is a built-in SDT from `GeneXus.Common` representing a collection of `Messages.Message` items (one-based indexing)

Variable definition:
- `Messages [ DataType = 'Messages, GeneXus.Common' ]`

Notes:
- Messages is already a collection; never define a variable based on 'Messages, GeneXus.Common' with `Collection = 'True'`
- Messages are commonly used for error handling and debugging, often required by certain built-in functions or methods

## Messages.Message built-in type
Built-in SDT element member of `Message` SDT from `GeneXus.Common`

Variable definition:
- `Message [ DataType = 'Messages.Message, GeneXus.Common' ]`

Notes:
- Messages items with `Type = MessageTypes.Error` automatically trigger alerts in mobile objects

### Id
Message identifier (non-translatable constant, `!` prefix required)
DataType: `VarChar(128)`

Example:
~~~
&Message.Id = !"UPDATE_ERROR_0001"
~~~

### Type
Message severity level: Error, Warning, Info, Debug
DataType: `MessageTypes, GeneXus.Common`

Example:
~~~
&Message.Type = MessageTypes.Error
~~~

### Description
Descriptive text (can use `Format`)
DataType: `VarChar(256)`

Example:
~~~
&Message.Description = Format("Failed to update customer %1", &CustomerId)
~~~

---

# CONSTRAINTS
- Indexing is one-based (first element = 1)
- IndexOf/LastIndexOf return 0 when item not found
- Remove/Set/Item operations require valid index
- Sort with criteria only valid for SDT collections
- Collections of simple types sort by value
- Collections of SDT sort by default member or specified criteria
- AddRange preserves source collection order
- Clone creates independent deep copy
