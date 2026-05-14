---
name: common-extended-type-geopolygon
description: Geographic polygon representation
---

Specialization of Geography for representing geographic polygons (areas)

---

# METHODS

## ToGeography
Converts the geo polygon to a Geography type

Syntax: `<geopolygon>.ToGeography()`

Example:
~~~
&Geography = &GeoPolygon.ToGeography()
~~~

## Intersect
Checks if the current polygon intersects another geography

Syntax: `<geopolygon>.Intersect(<geo-expression>)`

Where:
- `<geo-expression>`: Another `Geography` instance

Example:
~~~
If &GeoPolygon1.Intersect(&GeoPolygon2)
	// Polygons intersect
EndIf
~~~

## ToWkt
Returns WKT representation

Syntax: `<geopolygon>.ToWkt()`

Example:
~~~
&WktString = &GeoPolygon.ToWkt()
// Output: "POLYGON((-56.248367 -34.873821, -56.266563 -34.876427, -56.248367 -34.873821))"
~~~

## FromWkt
Loads a geo polygon from WKT format string

Syntax: `<geopolygon>.FromWkt(<string-expression>)`

Where:
- `<string-expression>`: WKT format string

Example:
~~~
&GeoPolygon.FromWkt(!"POLYGON((-56.248 -34.873, -56.266 -34.876, -56.248 -34.873))")
~~~

## ToGeoJSON
Returns GeoJSON representation

Syntax: `<geopolygon>.ToGeoJSON()`

Example:
~~~
&JsonString = &GeoPolygon.ToGeoJSON()
// Output: {"type":"Polygon","coordinates":[[[-56.248,-34.873],[-56.266,-34.876],[-56.248,-34.873]]]}
~~~

## FromGeoJSON
Loads a geo polygon from GeoJSON format string

Syntax: `<geopolygon>.FromGeoJSON(<string-expression>)`

Where:
- `<string-expression>`: GeoJSON format string

Example:
~~~
&GeoPolygon.FromGeoJSON(!'{"type":"Polygon","coordinates":[[[-56.248,-34.873],[-56.266,-34.876],[-56.248,-34.873]]]}')
~~~

---

# CONSTRAINTS
- Always use `new()` to create GeoPolygon instances
- Inherits all `Geography` properties and methods except `Distance()`
- The `Distance()` method is not available for polygons
- Coordinates use WGS84 coordinate system
- Polygon must be closed (first point equals last point)
- WKT format: `POLYGON((lon1 lat1, lon2 lat2, …, lon1 lat1))`
- GeoJSON format: `{"type":"Polygon","coordinates":[[[lon1,lat1],[lon2,lat2],…,[lon1,lat1]]]}`
- Outer ring coordinates are counter-clockwise, inner rings (holes) are clockwise