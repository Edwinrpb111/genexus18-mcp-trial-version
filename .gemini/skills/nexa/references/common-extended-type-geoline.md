---
name: common-extended-type-geoline
description: Geographic line representation
---

Specialization of Geography for representing geographic lines (polylines)

---

# METHODS

## ToGeography
Converts the geo line to a Geography type

Syntax: `<geoline>.ToGeography()`

Example:
~~~
&Geography = &GeoLine.ToGeography()
~~~

## Intersect
Checks if the current line intersects another geography

Syntax: `<geoline>.Intersect(<geo-expression>)`

Where:
- `<geo-expression>`: Another `Geography` instance

Example:
~~~
If &GeoLine.Intersect(&GeoPolygon)
	// Line intersects with polygon
EndIf
~~~

## ToWkt
Returns WKT representation

Syntax: `<geoline>.ToWkt()`

Example:
~~~
&WktString = &GeoLine.ToWkt()
// Output: "LINESTRING(-56.18528366088867 -34.90571271703311, -56.17850303649902 -34.90641660705113)"
~~~

## FromWkt
Loads a geo line from WKT format string

Syntax: `<geoline>.FromWkt(<string-expression>)`

Where:
- `<string-expression>`: WKT format string

Example:
~~~
&GeoLine.FromWkt(!"LINESTRING(-56.185 -34.905, -56.178 -34.906)")
~~~

## ToGeoJSON
Returns GeoJSON representation

Syntax: `<geoline>.ToGeoJSON()`

Example:
~~~
&JsonString = &GeoLine.ToGeoJSON()
// Output: {"type":"LineString","coordinates":[[-56.185,-34.905],[-56.178,-34.906]]}
~~~

## FromGeoJSON
Loads a geo line from GeoJSON format string

Syntax: `<geoline>.FromGeoJSON(<string-expression>)`

Where:
- `<string-expression>`: GeoJSON format string

Example:
~~~
&GeoLine.FromGeoJSON(!'{"type":"LineString","coordinates":[[-56.185,-34.905],[-56.178,-34.906]]}')
~~~

---

# CONSTRAINTS
- Always use `new()` to create GeoLine instances
- Inherits all `Geography` properties and methods except `Distance()`
- The `Distance()` method is not available for lines
- Coordinates use WGS84 coordinate system
- WKT format: `LINESTRING(lon1 lat1, lon2 lat2, …)`
- GeoJSON format: `{"type":"LineString","coordinates":[[lon1,lat1],[lon2,lat2],…]}`