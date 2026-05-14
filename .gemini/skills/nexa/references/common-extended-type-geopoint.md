---
name: common-extended-type-geopoint
description: Geographic point coordinates
---

Specialization of Geography for representing geographic points with latitude and longitude coordinates

---

# STATIC METHODS

## New
Creates a new geographic point from latitude and longitude

Syntax: `GeoPoint.New(<latitude>, <longitude>)`

Where:
- `<latitude>`: Vertical coordinate (Numeric)
- `<longitude>`: Horizontal coordinate (Numeric)

Example:
~~~
&GeoPoint = GeoPoint.New(-34.92, -56.16)
~~~

---

# PROPERTIES

## Latitude
Latitude coordinate (vertical)

Syntax: `<geopoint>.Latitude`

Example:
~~~
&Lat = &GeoPoint.Latitude
~~~

## Longitude
Longitude coordinate (horizontal)

Syntax: `<geopoint>.Longitude`

Example:
~~~
&Lon = &GeoPoint.Longitude
~~~

---

# METHODS

## ToGeography
Converts the geo point to a Geography type

Syntax: `<geopoint>.ToGeography()`

Example:
~~~
&Geography = &GeoPoint.ToGeography()
~~~

## Distance
Returns the distance in meters to another geo point

Syntax: `<geopoint>.Distance(<geopoint>)`

Where:
- `<geopoint>`: Another `GeoPoint` instance

Example:
~~~
&DistanceMeters = &GeoPoint1.Distance(&GeoPoint2)
~~~

## ToWkt
Returns WKT representation

Syntax: `<geopoint>.ToWkt()`

Example:
~~~
&WktString = &GeoPoint.ToWkt()
// Output: "POINT(-56.163740158081055 -34.92478600243492)"
~~~

## FromWkt
Loads a geo point from WKT format string

Syntax: `<geopoint>.FromWkt(<string-expression>)`

Where:
- `<string-expression>`: WKT format string

Example:
~~~
&GeoPoint.FromWkt(!"POINT(-56.16 -34.92)")
~~~

## ToGeoJSON
Returns GeoJSON representation

Syntax: `<geopoint>.ToGeoJSON()`

Example:
~~~
&JsonString = &GeoPoint.ToGeoJSON()
// Output: {"type":"Point","coordinates":[-56.163740158081055,-34.92478600243492]}
~~~

## FromGeoJSON
Loads a geo point from GeoJSON format string

Syntax: `<geopoint>.FromGeoJSON(<string-expression>)`

Where:
- `<string-expression>`: GeoJSON format string

Example:
~~~
&GeoPoint.FromGeoJSON(!'{"type":"Point","coordinates":[-56.16,-34.92]}')
~~~

---

# CONSTRAINTS
- Use static `New()` method to create instances; no `new()` operator needed
- Inherits all `Geography` methods except geometry type conversions
- Coordinates use WGS84 coordinate system
- WKT format: `POINT(longitude latitude)` - note longitude comes first
- GeoJSON format: `{"type":"Point","coordinates":[longitude,latitude]}`