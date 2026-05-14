---
name: common-formulas
description: Declarative formulas for calculating values over attributes
---

Simplifies logic by avoiding explicit code for aggregations, lookups, and conditional evaluations

---

# AGGREGATION FORMULAS

## Average
Calculates average

Syntax: `Average(<attribute>, <condition>, <default>)`

Where:
- `<attribute>`: Attribute to average
- `<condition>`: Condition over target attribute
- `<default>`: Default value if no match

Example:
~~~
Average(EmployeeSalary, EmployeeAge > 30 AND EmployeeAge < 40, 0)
~~~

## Count
Returns count

Syntax: `Count(<attribute>, <condition>, <default>)`

Where:
- `<attribute>`: Attribute to count
- `<condition>`: Condition over target attribute
- `<default>`: Default value if no match

Notes:
- Existence validation: `Count(<attribute>, <condition>, 0) > 0`

Example:
~~~
Count(TaskId, TaskStatus = TaskStatusType.Open, 0)
~~~

## Sum
Calculates sum

Syntax: `Sum(<attribute>, <condition>, <default>)`

Where:
- `<attribute>`: Attribute to sum
- `<condition>`: Condition over target attribute
- `<default>`: Default value if no match

Example:
~~~
Sum(OrderAmount, OrderStatus = OrderStatusType.Complete, 0)
~~~

---

# LOOKUP FORMULAS

## Find
Finds first

Syntax: `Find(<attribute>, <condition>, <default>)`

Where:
- `<attribute>`: Attribute to find
- `<condition>`: Condition over target attribute
- `<default>`: Default value if no match

Example:
~~~
Find(CustomerName, CustomerId = 123, '<none>')
~~~

## Max
Finds maximum

Syntax: `Max(<attribute>, <condition>, <default>, <output>)`

Where:
- `<attribute>`: Attribute to find maximum value
- `<condition>`: Condition over target attribute
- `<default>`: Default value if no match
- `<output>`: Attribute (other or same) value for matching record

Example:
~~~
Max(ProductPrice, CategoryId = 12, "<none>", ProductName)
~~~

## Min
Finds minimum

Syntax: `Min(<attribute>, <condition>, <default>, <output>)`

Where:
- `<attribute>`: Attribute to find maximum value
- `<condition>`: Condition over target attribute
- `<default>`: Default value if no match
- `<output>`: Attribute (other or same) value for matching record

Example:
~~~
Min(EmployeeSalary, DepartmentId = 3, "<none>", EmployeeName)
~~~

---

# CONSTRAINTS
- The `Exists` formula must never be used in any expression or clause
- Only formulas listed here are available
