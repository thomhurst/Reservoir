---
sidebar_position: 2
title: Installation
description: Add Reservoir as a source-only development dependency.
---

# Installation

Install Reservoir into a .NET Standard 2.0-compatible project:

```shell
dotnet add package Reservoir
```

NuGet records Reservoir as a development dependency. With `PackageReference`, that makes `PrivateAssets="all"` automatic: a library that uses Reservoir does not force its downstream consumers to reference the package.

## What appears in your build

The package contains C# files under `contentFiles` and a small build-transitive validation target. During compilation:

1. Reservoir source files join your project's compilation.
2. Reservoir types are `internal` by default, so they remain an implementation detail of your assembly.
3. No `Reservoir.dll` is copied to the output directory.
4. No runtime package dependency is added to your `.deps.json`.

Each project that directly uses Reservoir should reference the package. Because each project compiles its own copy, objects from one copy must be returned to the pool that created them.

Reservoir requires C# 12 or later. Set `<LangVersion>12.0</LangVersion>` or newer when the target framework's default language version is older.

## Make types public

Define `RESERVOIR_PUBLIC` when Reservoir types need to appear in your assembly's public API:

```xml
<PropertyGroup>
  <DefineConstants>$(DefineConstants);RESERVOIR_PUBLIC</DefineConstants>
</PropertyGroup>
```

Keep the default internal visibility when Reservoir is only an implementation detail. Public visibility does not turn Reservoir back into a runtime dependency; the same source still compiles into your assembly.

## Enable diagnostics outside Debug builds

Debug configurations include ownership diagnostics automatically. To enable them in a Release or staging configuration, define `RESERVOIR_DIAGNOSTICS`:

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Staging'">
  <DefineConstants>$(DefineConstants);RESERVOIR_DIAGNOSTICS</DefineConstants>
</PropertyGroup>
```

Diagnostics add tracking work and are intended for correctness testing, not production throughput measurements.
