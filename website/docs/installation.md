---
sidebar_position: 2
title: Installation
description: Add Reservoir as a runtime package dependency.
---

# Installation

Install Reservoir into a .NET Standard 2.0-compatible project:

```shell
dotnet add package Reservoir
```

The package provides compiled assemblies for .NET Standard 2.0, .NET 8, and .NET 10. NuGet selects the nearest compatible asset for your target framework.

## What appears in your build

Reservoir behaves like a conventional `PackageReference` dependency:

1. Your project compiles against Reservoir's public API.
2. Applications receive `Reservoir.dll` in their output.
3. Libraries expose one consistent Reservoir type identity to downstream consumers.
4. Reservoir appears normally in the application's dependency manifest.

Consumer language version is independent from the compiler used to build Reservoir. No C# 12 setting or build-transitive target is required.
