# XmlEssentials spec

This directory is the implementation contract for **XmlEssentials**.

## Identity

| Item | Value |
| --- | --- |
| Package ID | `XmlEssentials` |
| Assembly | `CloudNimble.XmlEssentials` |
| Namespace | `CloudNimble.Text.Xml` |
| High-level type | `XmlSerializer` (static, STJ-shaped) |
| BCL XML | **Zero** dependency, including internally |
| Home | This repo. Not EasyAF. Not ModernXmlSerializer. |
| Scaffold | `src/CloudNimble.XmlEssentials.slnx` and `src/CloudNimble.XmlEssentials.Docs/` already exist. |

```bash
dotnet add package XmlEssentials
```

```csharp
using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.Serialization;
```

## Documents

| Document | What it is |
| --- | --- |
| [00-overview.md](00-overview.md) | Overview, background, goals |
| [01-identity-and-decisions.md](01-identity-and-decisions.md) | Product identity and key decisions |
| [02-xml-fidelity.md](02-xml-fidelity.md) | XML subset, DoS limits, lexical rules |
| [03-architecture.md](03-architecture.md) | Layers and flows |
| [04-api-reader.md](04-api-reader.md) | Utf8XmlReader and XmlReaderState snapshot |
| [05-api-writer.md](05-api-writer.md) | Utf8XmlWriter |
| [06-api-dom.md](06-api-dom.md) | XmlDocument / XmlElement / XmlNode |
| [07-api-serializer.md](07-api-serializer.md) | XmlSerializer (authoritative rectangular overloads) |
| [08-api-converters-and-attributes.md](08-api-converters-and-attributes.md) | Converters and attributes |
| [09-object-mapping.md](09-object-mapping.md) | Mapping rules |
| [10-source-generation.md](10-source-generation.md) | Serializer source generator |
| [11-xsd-source-generation.md](11-xsd-source-generation.md) | XSD → types (AOT, no runtime schema) |
| [12-aot-and-trimming.md](12-aot-and-trimming.md) | AOT |
| [13-repo-and-packaging.md](13-repo-and-packaging.md) | Packaging; **MTP coverage CI commands** |
| [14-testing.md](14-testing.md) | Tests |
| [15-benchmarks.md](15-benchmarks.md) | Benchmarks |
| [16-documentation.md](16-documentation.md) | DotNetDocs |
| [17-alternatives.md](17-alternatives.md) | Alternatives |
| [18-security.md](18-security.md) | Security |
| [19-operability.md](19-operability.md) | Rollout |
| [20-open-questions.md](20-open-questions.md) | Open questions |
| [21-references.md](21-references.md) | References |
| [22-pr-plan.md](22-pr-plan.md) | PR plan |
| [23-optimization.md](23-optimization.md) | Tokenizer/writer optimization playbook and backlog |
| [24-doc-findings-resolution.md](24-doc-findings-resolution.md) | Defects found by the documentation pass, and their resolution |
| [25-single-engine-metadata.md](25-single-engine-metadata.md) | Generated metadata and one runtime engine: design, public API, task list |
| [26-expanded-encoding-support.md](26-expanded-encoding-support.md) | Reading UTF-16, UTF-32, US-ASCII and ISO-8859-1, and converting to UTF-8: design and task list |
| [27-xpath.md](27-xpath.md) | XPath 1.0 over the read-only `XmlDocument`: the navigator, the public API and the task list |

## How to read the spec

1. [Key decisions](01-identity-and-decisions.md#key-decisions) and [XML fidelity](02-xml-fidelity.md).
2. Public API [04](04-api-reader.md)–[08](08-api-converters-and-attributes.md); [07](07-api-serializer.md) is the overload list.
3. [Object mapping](09-object-mapping.md).
4. [PR Plan](22-pr-plan.md).

## Out of scope reminders

- Do not revive ModernXmlSerializer.
- Do not reuse `System.Xml.Serialization` attributes.
- `[XmlInclude]` = STJ non-public members; known types = `[XmlDerivedType]`.
- Mixed content: parser, DOM, **and** `[XmlMixed]` mapper.
- `Dictionary<,>` is mapped (STJ object analogue). Extension-data / xmlns bags remain bags.
- XML 1.0 and 1.1: one tokenizer; version from the declaration.
- XSD: compile-time codegen only. No runtime validation. No DTD.
