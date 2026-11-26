# PR Plan

Part of the [XmlEssentials spec](README.md).

Incremental, mergeable PRs. Each PR must compile, include tests for the new surface, and not depend on later PRs except as listed.

## Status

| PR | Title | Status |
| --- | --- | --- |
| 1 | Repo scaffolding | **Done** (`bd364fb`) |
| 2a | Utf8XmlReader skeleton | **Done** (`bd364fb`) |
| 2b | Namespaces | **Done** (`47e7fd1`) |
| 2c | Resume | **Done** (`47e7fd1`) |
| 2d | xmlconf subset | **Done** (`47e7fd1`) |
| 3 | Utf8XmlWriter | **Done** (`e211820`) |
| 4 | DOM | **Done** (`7a7d8da`; real-document speed work in `26a0d03`) |
| 5 | Options, converters, primitive serialize | **Done** (`c24bee9`; small-element speed work in `97b91b2`); scalar roots are 0.06 to 0.26 of BCL; the two streaming rows are 0.79 and 0.65 of BCL ([optimization](23-optimization.md)) |
| 6a | POCO element mapping | **Done**; object rows are 0.23 to 0.68 of BCL ([optimization](23-optimization.md)) |
| 6b | XmlText / simple content | **Done** |
| 6c | Collections and dictionaries | **Done** |
| 6d | Polymorphism / extension data | **Done** |
| 6e | Mixed content, references, async enumerable | **Done** |
| 7 | Source generator + context | Implemented. Reworked to emit metadata for the one runtime engine ([single engine](25-single-engine-metadata.md), K52); the rework is uncommitted. |
| 7b | XSD generator | **Preview.** Implemented for the constructs in [11](11-xsd-source-generation.md); seventeen open problems are listed there, deferred until after the first release. Emits the same metadata and write fast path (uncommitted). |
| 8 | AOT / trimming tests | Implemented. `Tests.Aot/EngineFeatures.cs` checks the engine's features through generated metadata (uncommitted). |
| 9 | Benchmarks vs BCL XmlSerializer vs STJ | Implemented (uncommitted): `XmlSerializerGraphBenchmarks` adds first-call reflection and source-gen context on the Customer graph; existing object/scalar/sequence/reader/writer/DOM rows remain. |
| 10 | DotNetDocs site | Partial (reader/writer conceptual pages exist) |
| 11 | Breakdance freeze + coverage slogan check | Not started |
| 12 | Doc findings resolution | Slices 1 and 2 (F1 to F40), N1 to N10 and D1 to D4 and D6 are **done** and committed (decisions 43 to 77 in [doc findings resolution](24-doc-findings-resolution.md)); the test count, the gate and the Native AOT smoke are recorded there. D5: `Reset()` discarding pending bytes and non-public setters needing `[XmlInclude]` are **done** (decisions 78 and 79), the `1.x` version check is closed with no change, and the last two are **done** by the owner's decision: a value serialized as its own derived type has no discriminator (decision 80), and the constructor is chosen as System.Text.Json chooses it (decision 81). Nothing in the doc findings is open. No benchmark has been run since the serializer work; that is the last step, by the owner's instruction. |
| 13 | Exception path tracking in the serializer | **Done** (decision 61 in [doc findings](24-doc-findings-resolution.md), [reader API](04-api-reader.md)). `XmlException.Path` is set for an exception thrown while the serializer reads or writes, in the reflection resolver and in generated contexts alike. Item positions count from 0 and the message ends with the path and the line, as System.Text.Json writes them. |
| 14 | Expanded encoding support | **Designed, not started** ([expanded encoding support](26-expanded-encoding-support.md)). Reads UTF-16, UTF-32, US-ASCII and ISO-8859-1 by transcoding to UTF-8 at the entry points, and adds `XmlTranscoder.ToUtf8`. The parameter rename that goes with it (`utf8Xml` to `xml` on the byte-taking `Parse` and `Deserialize` entry points) is done. |
| 15 | XPath 1.0 | **Done** ([XPath 1.0](27-xpath.md)), tasks 1 to 9 committed. XPath 1.0 in full over the read-only `XmlDocument` in `CloudNimble.Text.Xml.XPath`: a node is a 64-bit id in document order, the navigator reads names and values from the row store as UTF-8, and a parent table is built for a document the first time an axis needs it. The grammar, conversions and core functions are ported from Xemele. The points where the implementation corrected the design are listed in [changes from the first draft](27-xpath.md#changes-from-the-first-draft). The Breakdance baseline for the new surface is not made: the repository has none yet (PR 11). The five open decisions of the design are the owner's ([open questions](20-open-questions.md)). |

Reader and writer speed work is tracked in [optimization](23-optimization.md), not as a new PR. Its final pass (2026-11-25) is recorded there with direct-loop numbers; the full BenchmarkDotNet run that followed it (2026-11-25, 229 benchmarks) is the last section there and is what the documentation tables are made from. Prefix resolution is done. Still open there: per-reader allocations on real documents (80 B to 10504 B per drain).

TreatWarningsAsErrors is on from PR 1. **CS1591** is in `NoWarn` until the runtime library's first public type (PR 2), then XML comments are required on every new public member in that same PR ([K40](01-identity-and-decisions.md#key-decisions)).

Coverage **line 100%** fail starts in **PR 2**, not PR 11. Branch coverage was reported against an 80% floor until `26a0d03`; `coverage-gate.cs` now fails below **100% branch** as well. Breakdance baselines update on every public-surface PR.

CI (GitHub Actions, McpEssentials process: `pr-validation.yml` / `build-and-deploy.yml` / `publish-ci-build.yml`), **MTP host**:

```bash
dotnet test src/CloudNimble.XmlEssentials.slnx -c Release --no-build -f net10.0
dotnet test src/CloudNimble.XmlEssentials.slnx -c Release --no-build -f net11.0 --coverage --coverage-output-format cobertura --coverage-output artifacts/coverage/coverage.cobertura.xml
dotnet run src/coverage-gate.cs -- artifacts/coverage/coverage.cobertura.xml
```

Do **not** use `dotnet test --settings coverlet.runsettings` (VSTest DataCollector; ignored by MTP). See [packaging](13-repo-and-packaging.md#coverage-fail-gate-mtp--not-coverletrunsettings).

`src/CloudNimble.XmlEssentials.slnx` and `src/CloudNimble.XmlEssentials.Docs/` **already exist**. PR 1 keeps them. Namespace in docs usings stays `CloudNimble.Text.Xml`.

## PR 1: Repo scaffolding

- **Status:** Done (`bd364fb` on `dev`)
- **Title:** `chore: scaffold XmlEssentials solution, props, tests, benchmarks`
- **Depends on:** none
- **Files:**
  - `src/Directory.Build.props` (PackageId strip `CloudNimble.`; `IsSourceGeneratorProject` / `IsDocumentationProject` excluded from `IsPrimaryProject`; IVT **only** on `CloudNimble.XmlEssentials` → `CloudNimble.XmlEssentials.Tests`; CS1591 NoWarn until PR 2; `TreatWarningsAsErrors`)
  - `src/Directory.Build.targets`
  - `src/global.json` (SDK 11.0.x, **`test.runner`: Microsoft.Testing.Platform**)
  - `src/.editorconfig`
  - `src/coverage-gate.cs` (parse Cobertura; fail line &lt; 100% on `CloudNimble.XmlEssentials`; PR 1: exit 0 if zero instrumented lines but Cobertura file must exist)
  - `.github/workflows/pr-validation.yml`, `build-and-deploy.yml`, `publish-ci-build.yml` (McpEssentials process; exact commands above)
  - empty runtime / generator / tests / performance projects
  - **Keep** existing `CloudNimble.XmlEssentials.slnx` and `CloudNimble.XmlEssentials.Docs/`
  - root `README.md`
- **Description:** ClaudeEssentials-shaped tree. CI invokes MTP `--coverage` and `coverage-gate.cs` on the empty library (collector must run).

## PR 2a: Utf8XmlReader skeleton

- **Status:** Done (`bd364fb` on `dev`)
- **Title:** `feat: Utf8XmlReader skeleton, empty elements, text, XmlException`
- **Depends on:** PR 1
- **Files:** reader, options/state snapshot contract, `XmlException`, `XmlName`, `XmlTokenType`, DoS 0-semantics, tests, **coverage-gate.cs now fails on line &lt; 100%**, XML comments, Breakdance baseline
- **Description:** UTF-8, BOM, declaration, empty vs start+end, text, five entities, DTD reject. No namespaces yet.

## PR 2b: Namespaces

- **Status:** Done (`47e7fd1` on `dev`)
- **Title:** `feat: Utf8XmlReader namespace stack`
- **Depends on:** PR 2a
- **Description:** Prefix/default xmlns, `xmlns=""`, unprefixed attributes vs default xmlns.

## PR 2c: Resume

- **Status:** Done (`47e7fd1` on `dev`)
- **Title:** `feat: Utf8XmlReader IsFinalBlock resume`
- **Depends on:** PR 2b
- **Description:** `XmlReaderState` snapshot tests: split inside namespaced start tag, attribute, text.

## PR 2d: xmlconf subset

- **Status:** Done (`47e7fd1` on `dev`)
- **Title:** `test: UTF-8 XML 1.0 and 1.1 conformance subset`
- **Depends on:** PR 2c
- **Description:** Curated W3C xmlconf without DTD. 1.0 and 1.1. EOL (NEL/LS), RestrictedChar, `xmlns:p=""`, NameStartChar.

(PRs 3–4 still depend on the completed reader = 2d.)

## PR 3: Utf8XmlWriter

- **Status:** Done (`e211820` on `dev`)
- **Title:** `feat: Utf8XmlWriter and reader/writer round-trip`
- **Depends on:** PR 2d
- **Files:** writer (`EmitByteOrderMark` default false; `MaxAttributeCount` / `MaxNameByteCount` on options), `XmlEncodedText`, round-trip tests, baseline
- **Description:** `IBufferWriter<byte>` + Stream. Escaping, indent, xmlns auto-declare, `WriteNilValue`/`WriteNilElement`.

## PR 4: DOM

- **Status:** Done (`7a7d8da` on `dev`; real-document reader and writer speed work followed in `26a0d03`)
- **Title:** `feat: XmlDocument, XmlElement, and mutable XmlNode tree`
- **Depends on:** PR 3
- **Description:** ArrayPool rental + Dispose. Mixed content. Nested `XmlElement.Enumerator`. Immutable `XmlDeclarationInfo` on `XmlDocument`.

## PR 5: Options, converters, primitive serialize

- **Status:** Done (`c24bee9` on `dev`; small-element reader and writer speed work followed in `97b91b2`)
- **Title:** `feat: XmlSerializerOptions, XmlConverter, primitive XmlSerializer`
- **Depends on:** PR 4
- **Description:** Scalars. Root name via policy (`<int>42</int>` identity). Null root = xsi:nil. Full [07](07-api-serializer.md) overload families including TValue+context deserialize.

## PR 6a: POCO elements

- **Status:** Done
- **Title:** `feat: POCO element mapping`
- **Depends on:** PR 5
- **Description:** Public properties serialize as child elements by convention (STJ map). `DefaultPropertyMapping` (`Element` / `Attribute`) is the blanket switch: `Attribute` writes unannotated scalars as attributes; nested objects stay elements and their simple members are attributes too. `[XmlAttribute]` is honored (simple types only). Naming policy, nested objects, records/init/required/constructor, `[XmlRoot]` / `[XmlPropertyName]` / `[XmlIgnore]` / `[XmlInclude]` / `[XmlRequired]` / `[XmlPropertyOrder]` / `[XmlElement]` name/ns/order/nillable. `[XmlText]` and typed/multiple `[XmlElement]` throw `NotSupportedException` at type-info bind (6b / 6d).

## PR 6b: Attributes and simple content

- **Status:** Done
- **Title:** `feat: XmlAttribute and XmlText mapping`
- **Depends on:** PR 6a
- **Description:** `[XmlText]` / `[XmlCData]` simple content. `[XmlAttribute]` and `DefaultPropertyMapping.Attribute` shipped with 6a.

## PR 6c: Collections

- **Status:** Done
- **Title:** `feat: collection mapping (unwrapped default, XmlArray wrap)`
- **Depends on:** PR 6b
- **Description:** Unwrapped default, `[XmlArray]` wrap, `Dictionary<,>` maps ([K16](01-identity-and-decisions.md#key-decisions)).

## PR 6d: Nil, polymorphism, extension data

- **Status:** Done
- **Title:** `feat: xsi:nil, polymorphism, XmlExtensionData`
- **Depends on:** PR 6c
- **Description:** `[XmlDerivedType<T>]` element-name discriminator; typed `[XmlElement<T>]`. `[XmlPolymorphic]` `DiscriminatorPlacement` (`ElementName`, `Attribute`, `XsiType`). `[XmlExtensionData]` captures unmapped attributes and elements. `[XmlPropertyOrder]` interleave shipped with 6a.

## PR 6e: Mixed content, references, async enumerable

- **Status:** Done
- **Title:** `feat: XmlMixed, ReferenceHandler, SerializeAsyncEnumerable`
- **Depends on:** PR 6d
- **Description:** `[XmlMixed]` text and child elements (`XmlMixedText` / `XmlMixedElement`). `IgnoreCycles` / `Preserve` write and read (`id`/`ref` in `urn:CloudNimble.Text.Xml:refs`). `DeserializeAsyncEnumerable` / `SerializeAsyncEnumerable` shipped with PR 5.

## PR 7: Source generator + context

- **Status:** Implemented (uncommitted)
- **Title:** `feat: XmlSerializerContext source generator packed in XmlEssentials`
- **Depends on:** PR 6e
- **Files:** generator (`IsSourceGeneratorProject`, `IsRoslynComponent`, pack both analyzer folders), `[XmlSerializable]`, `Default` singleton, XMLESS007 = BCL `XmlIncludeAttribute`
- **Description:** **Metadata and Serialization** modes (`Default` = both). Interceptors for `XmlSerializer` calls that pass no options ([K42](01-identity-and-decisions.md#key-decisions)).

## PR 7b: XSD generator

- **Status:** Implemented (uncommitted)
- **Title:** `feat: XSD AdditionalFiles generator emitting types and AOT serialize`
- **Depends on:** PR 7
- **Files:** XsdGenerator in the same analyzer DLL, AdditionalFiles tests, XMLESS010–013, [11](11-xsd-source-generation.md)
- **Description:** `.xsd` → POCOs + context + ser/deser via shared emit. No runtime schema. Import only AdditionalFiles.

## PR 8: AOT / trimming tests

- **Status:** Implemented (uncommitted)
- **Title:** `test: Native AOT smoke using XmlSerializerContext`
- **Depends on:** PR 7b
- **Files:** `CloudNimble.XmlEssentials.Tests.Aot` (`PublishAot=true`, `IsReflectionEnabledByDefault=false`, XSD AdditionalFiles + `[XmlSerializable]` context), `CloudNimble.XmlEssentials.Samples.Cli` (McMaster.Extensions.CommandLineUtils 5.x, Native AOT)
- **Description:** Publish and run the smoke executable. Reflection overloads throw. Context and XSD-generated types round-trip. CI job `aot` on windows-latest.

## PR 9: Benchmarks vs BCL XmlSerializer vs STJ

- **Status:** Implemented (uncommitted)
- **Title:** `bench: XmlEssentials vs cached XmlSerializer vs System.Text.Json`
- **Depends on:** PR 7
- **Files:** `XmlSerializerGraphBenchmarks`, `GraphXmlContext`, Performance ProjectReference to the generator (no `InterceptorsNamespaces`, so reflection rows stay reflection)
- **Description:** Cache BCL instance. Label “BCL defaults (wrapped collections)” unless attributes pin infoset. Match indent/declaration. STJ is sidecar. First-call rows create new `XmlSerializerOptions` each iteration. Source-gen rows use `GraphXmlContext.Default.Customer`.

## PR 10: DotNetDocs site

- **Title:** `docs: Mintlify DotNetDocs site and conceptual guides`
- **Depends on:** PR 6e; ideally after PR 7
- **Files:** **existing** `CloudNimble.XmlEssentials.Docs` (keep; expand guides), `why-xmlessentials.mdx`, quickstart
- **Description:** XML comments already required from PR 2.

## PR 11: Breakdance freeze + coverage slogan check

- **Title:** `test: freeze public API baseline; confirm 100% line and branch gate`
- **Depends on:** PRs 2–8
- **Description:** Not the first time coverage is measured. Confirms `coverage-gate.cs` still fails the build below 100% line or 100% branch. Subsequent API changes update baselines in the **same** PR.

Preview packages (`-preview`) **after PR 11**, not after PR 7 ([rollout](19-operability.md)).

---

[Index](README.md) · [Previous: References](21-references.md) · [Next: Optimization](23-optimization.md)
