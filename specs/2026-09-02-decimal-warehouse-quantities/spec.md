# Decimal warehouse quantities

Status: **Active**

## Outcome

WMS stores, calculates, compares, and exchanges warehouse quantities as exact
decimal values. C# uses its regular `decimal` type; application validation and
persistence enforce the current 1C document boundary of 15 total digits and 3
fractional digits.

## Scope

- planned and actual receiving and shipping quantities;
- inventory movement, balance, and turnover quantities;
- inventory-count expected and counted quantities;
- direct, transit, picking, and putaway movement quantities;
- application read models, WebApp inputs, Mobile V1 contracts, and Android
  view state carrying those quantities;
- the receiving and shipping 1C integration boundary;
- EF Core mappings and a migration from SQL Server `float` to
  `decimal(15,3)`.

Weight, volume, dimensions, coordinates, and conversion coefficients remain
binary floating-point values and are outside this change.

## Rules

1. The domain and application model use C# `decimal` for warehouse quantities.
2. Persisted warehouse-quantity columns use SQL Server `decimal(15,3)`.
3. Mobile V1 represents warehouse quantities as JSON numbers backed by C#
   `decimal` contracts.
4. The 1C OData transport models use `decimal` for the current `Number(15,3)`
   quantity fields even though 1C metadata publishes fractional numbers as
   `Edm.Double`; JSON numeric values are read into and written from the exact
   WMS decimal representation.
5. Existing business validation remains unchanged: positive movement
   quantities, nonnegative facts, and plan/availability limits.
6. Values with more than three fractional digits are rejected at editable and
   HTTP boundaries rather than silently rounded.
7. Values outside the nonnegative `Number(15,3)` range are rejected before
   persistence. Signed turnover deltas use the corresponding signed range.
8. Existing data does not require a compatibility migration strategy because
   the current database may be cleared before applying the migration.

## Acceptance criteria

- no warehouse quantity in current domain, application, Mobile V1, WebApp,
  Mobile, or 1C document models remains `double`;
- all warehouse-quantity EF properties are mapped to `decimal(15,3)`;
- fractional arithmetic and equality use `decimal` without tolerance helpers;
- physical measurements remain `double`;
- the affected projects and solution build without adding or running tests.
