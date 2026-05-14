---
name: common-extended-type-geography
description: Geographic entities representation
---

Represents geographic entities such as points, lines, or polygons using spatial data types with WKT and GeoJSON support

---

# PROPERTIES

## SRID
Spatial Reference System Identifier of the geographic object (read-only)

Syntax: `<geography>.SRID`

Example:
~~~
&SridValue = &Geography.SRID
~~~

## FeatureType
Geometry type string (read-only)

Syntax: `<geography>.FeatureType`

Constraints:
- Values: `!"POINT"`, `!"LINE"`, `!"POLYGON"`
- Empty string if null or unsupported

Example:
~~~
If &Geography.FeatureType = !"POINT"
	// Process as point
EndIf
~~~

---

# METHODS

## Distance
Returns the distance in meters to another geography

Syntax: `<geography>.Distance(<geo-expression>)`

Where:
- `<geo-expression>`: Another `Geography` or `GeoPoint` instance

Constraints:
- Only valid for `GeoPoint` or `Geography` containing a point

Example:
~~~
&DistanceMeters = &GeoPoint1.Distance(&GeoPoint2)
~~~

## Intersect
Checks if the current geography intersects another geography

Syntax: `<geography>.Intersect(<geo-expression>)`

Where:
- `<geo-expression>`: Another `Geography` instance

Example:
~~~
If &Geography1.Intersect(&Geography2)
	// Geographies intersect
EndIf
~~~

## ToWkt
Returns WKT (Well-Known Text) representation

Syntax: `<geography>.ToWkt()`

Example:
~~~
&WktString = &Geography.ToWkt()
// Example output: "POINT(-56.16 -34.92)"
~~~

## FromWkt
Loads a geography instance from WKT format string

Syntax: `<geography>.FromWkt(<string-expression>)`

Where:
- `<string-expression>`: WKT format string

Example:
~~~
&Geography.FromWkt(!"POINT(-56.16 -34.92)")
~~~

## ToGeoJSON
Returns GeoJSON representation

Syntax: `<geography>.ToGeoJSON()`

Example:
~~~
&JsonString = &Geography.ToGeoJSON()
// Example output: {"type":"Point","coordinates":[-56.16,-34.92]}
~~~

## FromGeoJSON
Loads a geography instance from GeoJSON format string

Syntax: `<geography>.FromGeoJSON(<string-expression>)`

Where:
- `<string-expression>`: GeoJSON format string

Example:
~~~
&Geography.FromGeoJSON(!'{"type":"Point","coordinates":[-56.16,-34.92]}')
~~~

## ToGeoPoint
Converts the current geography to a GeoPoint

Syntax: `<geography>.ToGeoPoint()`

Constraints:
- Only valid for point geometries

Example:
~~~
&GeoPoint = &Geography.ToGeoPoint()
~~~

## ToGeoLine
Converts the current geography to a GeoLine

Syntax: `<geography>.ToGeoLine()`

Constraints:
- Only valid for line geometries

Example:
~~~
&GeoLine = &Geography.ToGeoLine()
~~~

## ToGeoPolygon
Converts the current geography to a GeoPolygon

Syntax: `<geography>.ToGeoPolygon()`

Constraints:
- Only valid for polygon geometries

Example:
~~~
&GeoPolygon = &Geography.ToGeoPolygon()
~~~

---

# CONSTRAINTS
- Always use `new()` to create Geography instances
- Geography coordinates use WGS84 coordinate system
- Supports serialization via `ToJson`/`FromJson`
- WKT and GeoJSON are standard interchange formats