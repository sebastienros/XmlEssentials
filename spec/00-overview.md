# Overview

Part of the [XmlEssentials spec](README.md).

| Field | Value |
| --- | --- |
| **Title** | XmlEssentials: a System.Text.Json-shaped XML stack for .NET |
| **Author** | CloudNimble / Grok |
| **Date** | 2026-11-21 |
| **Status** | Draft |
| **Product** | XmlEssentials |
| **Assembly** | CloudNimble.XmlEssentials |
| **Root namespace** | CloudNimble.Text.Xml |
| **Repo** | `D:\GitHub\XmlEssentials` (https://github.com/CloudNimble/XmlEssentials) |
| **License** | MIT, CloudNimble 2026 |

This directory is the implementation contract. An engineer should be able to implement the product from these pages without inventing product decisions.

## Overview

Microsoft declined to ship `System.Text.Xml` ([dotnet/runtime#125299](https://github.com/dotnet/runtime/issues/125299), closed as not planned, March 2026). The stated reason was not that the API was wrong; it was that a second XML subsystem in the BCL has Newtonsoft→STJ-scale cost and Microsoft would rather augment `System.Xml` someday. That someday is not a Native AOT story today: `System.Xml.Serialization.XmlSerializer` is `[RequiresDynamicCode]` / `[RequiresUnreferencedCode]` on every public entry point, `Microsoft.XmlSerializer.Generator` (sgen) emits a sidecar assembly rather than an incremental Roslyn generator, and AOT work on the in-box serializer (#106580) has slipped.

XmlEssentials is the product Microsoft sketched and then refused: a fully standalone .NET XML stack whose public surface is a direct parallel of `System.Text.Json` (STJ). High-level type `XmlSerializer`, options/context/converters/source-gen, UTF-8 `ref struct` reader, `IBufferWriter<byte>` writer, DOM. Zero dependency on any BCL XML assembly. Own attribute family. Own `XmlException`. Namespace `CloudNimble.Text.Xml` so a future `using` swap to `System.Text.Xml` remains a hedge, while the NuGet id is `XmlEssentials` (CloudNimble Essentials packages omit the `CloudNimble.` prefix).

XML subset: well-formed UTF-8 XML **1.0 and 1.1** with namespaces, comments, CDATA, PIs, and `xsi:nil`. No DTD (XXE mitigation). One tokenizer; version from the declaration (read) or `XmlVersion` (write). BCL never implemented 1.1.

## Background & motivation

### Current state

| Stack | What it is | Why it is not this product |
| --- | --- | --- |
| `System.Xml.Serialization.XmlSerializer` | JIT IL-emit mapper, public types only, XSD-shaped, `[RequiresDynamicCode]` | Unusable on Native AOT; sgen is not a Roslyn generator; inheritance order is XSD extension (base then derived) |
| `System.Xml.XmlReader` / `XmlWriter` / `XDocument` | Allocating, `string`-heavy, not span/`IBufferWriter` | AOT-safe parser, not an object mapper; pulling it in reintroduces `System.Private.Xml` |
| `Microsoft.XmlSerializer.Generator` | Post-build sgen sidecar | Not incremental, not in-process, unused by Native AOT |
| Community: StackXML, XmlSourceGenerator, ExtendedXmlSerializer, ABForge.Xml | Partial AOT or fluent/reflection | Not BCL-quality, not STJ-shaped, not a full reader/writer/DOM/serializer stack |
| **ModernXmlSerializer** (`D:\GitHub\ModernXmlSerializer`) | 2019 CloudNimble reflection helper | **Abandoned. Do not revive. Do not depend on it.** Prior art for what not to copy (see below) |

### Pain points

1. Native AOT and trimming cannot use in-box `XmlSerializer` for real object graphs.
2. STJ taught the ecosystem a shape (`Serialize<T>`, `JsonSerializerContext`, `Utf8JsonReader`, converters) that XML still lacks.
3. BCL XML attributes (`[XmlElement]`, `[XmlRoot]`, …) force `using System.Xml.Serialization`, which makes `XmlSerializer` ambiguous and pulls the old contract into DTOs.
4. XML still matters for the first consumers of this stack: Restier, PortableRest, EDMX/xscgen, OData, SOAP-ish payloads, config, Office/OpenXML-adjacent tools. EasyAF may `PackageReference` later (Better Together); this is not an EasyAF component.

### Prior art: ModernXmlSerializer (what not to copy)

`D:\GitHub\ModernXmlSerializer` is a netstandard2.0 helper in namespace `System` (`src/ModernXmlSerializer/ObjectExtensions.cs`). It reflects **only** public instance properties with `[XmlElement]`, writes `GetValue()` as element text, deserializes one-level `XDocument` + `TypeConverter`, and uses **`Encoding.Unicode`** (UTF-16) for the XML stream. No nested objects, no collections, no attributes, no namespaces, no `xsi:nil`. Cache writes swallow exceptions. Benchmarks constructed a new BCL `XmlSerializer` per iteration (dishonest) and the deserialize benchmarks did not even call the library.

XmlEssentials does not share code, attributes, namespace, or package id with that repo.

### Why a new stack rather than a mapper on `XmlReader`

#125299's most important ideas are span reader/writer, context source-gen, `OmitXmlDeclaration`, namespace-aware name comparison, `WriteNil`, converters, and `IBufferWriter<byte>`. Those ideas do not require the BCL parser. They require **not** taking a dependency on `System.Private.Xml` if the product is going to be the STJ of XML. Microsoft's counter-proposal ("source-generate onto existing `XmlReader`/`XmlWriter`") is a smaller AOT mapper, not a modern stack. That is a different product. This one writes its own UTF-8 reader/writer.

## Goals & non-goals

### Goals

- Ship the STJ-parallel product: `Utf8XmlReader`, `Utf8XmlWriter`, DOM, `XmlSerializer`, converters, source generator (generated metadata read by the one runtime engine, with a write fast path; `Default`, `Metadata` and `Serialization` modes as System.Text.Json has them), options (including `Default` / `Web` / `Strict`), naming policies, dictionaries, `IAsyncEnumerable` / `DeserializeAsyncEnumerable` / `SerializeAsyncEnumerable`, `PipeReader`/`PipeWriter`, `ReferenceHandler`, interceptors.
- Direct public-surface parallel of STJ: same type roles, same method shapes, same options/context/converter/source-gen pattern, `Xml*` names. Authoritative overload list: [07](07-api-serializer.md).
- Zero dependency on `System.Xml`, `System.Xml.Linq`, `System.Xml.Serialization`, `System.Xml.XDocument`, `System.Private.Xml`. Not even internally.
- UTF-8, spans, `ReadOnlySequence<byte>`, `IBufferWriter<byte>`, `ref struct` reader.
- Own attribute family in `CloudNimble.Text.Xml.Serialization`.
- Source generation as the first-class AOT path; reflection optional and gated like STJ (`IsReflectionEnabledByDefault`).
- XSD → types source generator (`AdditionalFiles` `*.xsd`) that feeds the serializer generator. No runtime schema validation.
- Internal types serializable via source-gen (improvement over BCL `XmlSerializer`, which requires public types).
- Deterministic, documented XML subset ([XML fidelity](02-xml-fidelity.md)).
- XPath 1.0 over the read-only `XmlDocument`, in full: the 13 axes, every node test, the operators, variables and the 27 core functions, evaluated over the UTF-8 rows without a copy of the document ([XPath](27-xpath.md)).
- `net10.0` and `net11.0` TFMs; `IsAotCompatible=true`; `EnableTrimAnalyzer=true`.
- **100% line** on `CloudNimble.XmlEssentials.dll` as a **CI fail gate**: MTP `Microsoft.Testing.Extensions.CodeCoverage` **collects** (`dotnet test --coverage`); `coverage-gate.cs` **fails** the build if line-rate or branch-rate &lt; 1.00 ([K21](01-identity-and-decisions.md#key-decisions)). Not coverlet.runsettings. Breakdance public API baselines.
- Fair BenchmarkDotNet comparison vs cached BCL `XmlSerializer` and vs STJ on the same object graph.

### Non-goals

- DTD (internal or external). `<!DOCTYPE` is an error. This **is** the XXE mitigation.
- External entities, parameter entities, general entity declarations beyond the five XML predefined entities.
- Runtime XSD / DTD **validation** (`XmlSchemaSet`, streaming schema-validating reader). XSD is compile-time codegen only ([11](11-xsd-source-generation.md)).
- SOAP encoding (`soap-enc:arrayType`, `href`/`id` graphs). `ReferenceHandler` is the STJ `$id`/`$ref` analogue, not SOAP encoding.
- XInclude.
- Significant-whitespace preservation as a **serializer** feature (writer may indent; reader may skip insignificant whitespace).
- Reuse of `System.Xml.Serialization` attributes.
- Honoring BCL `[XmlInclude]` semantics (known types). See [disambiguation](08-api-converters-and-attributes.md#disambiguation).
- Strong-name signing (match current ClaudeEssentials: unsigned).
- Living in EasyAF. Own repo.
- Reviving ModernXmlSerializer.
- `System.Text.Xml` namespace and `XmlData*` type prefix from #125299.
- Python/Node/Perl tooling. Scripts are .NET 10+ C# file-based apps.
- Binary XML / non-UTF-8 encodings.

---

[Index](README.md) · [Next: Identity and decisions](01-identity-and-decisions.md)
