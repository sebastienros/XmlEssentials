# Optimization

Part of the [XmlEssentials spec](README.md).

This page is the playbook for making `Utf8XmlReader` / `Utf8XmlWriter` as fast as STJ, and for beating BCL `XmlReader` / `XmlWriter` on the same payloads. It records **how** this repo has been optimizing, **what already shipped** in PR 2–3, and **what the next pass should do**.

Read [benchmarks](15-benchmarks.md) for serializer-column rules. Reader/writer benches live in `src/CloudNimble.XmlEssentials.Performance/` and are the source of truth for tokenizer numbers.

## Constraints that survive every pass

- Zero `System.Xml*` in runtime and tests.
- Official [dotnet/runtime](https://github.com/dotnet/runtime) source at a tagged release (`v11.0.0` while TFMs are net10/net11). **Not** reflection, ILSpy, or decompilation.
- STJ toolbox: spans, `SearchValues`, `Utf8Formatter` / `IUtf8SpanFormattable`, `Ascii.FromUtf16`, `ArrayPool`, `IBufferWriter.GetMemory` / `Advance`. No string concat on the hot path.
- CloudNimble C# layout ([AGENTS.md](../AGENTS.md)): block namespaces, internals not `private`, no nested types, XML docs on internals.
- Coverage: **100% line and 100% branch** fail gate on `CloudNimble.XmlEssentials.dll`. After a hot-path change, run tests **and** `coverage-gate.cs` before declaring a win.
- Do not commit until asked. Do not special-case a benchmark payload (no `if (prefix is "p")`).
- Report reader/writer benches as a markdown table: **Method, Category, Mean, Ratio, Allocated, Alloc ratio**. Integer nanoseconds and integer bytes. `Allocated` `-` from BenchmarkDotNet is **0 B**.

## Process (repeat this)

Do the steps in order. A naive port of one BCL trick without a probe has already **regressed** catalog write time; revert immediately when that happens.

### 1. Measure the real suite

```bash
dotnet run -c Release --project src/CloudNimble.XmlEssentials.Performance -f net11.0 -- --filter *
```

ShortRun + `InProcessEmitToolchain` is what `Program.cs` already uses. Categories today:

| Category | What it is |
| --- | --- |
| `TinyWrite` / `TinyDrain` | `<root id="1"><n>42</n></root>` |
| `CatalogWrite` / `CatalogDrain` | 200 unprefixed items (`item` / `name` / `qty` / `price`) |
| `CatalogGetInt32` | Same catalog; parse integer text (`TryGetInt32` vs BCL `int.TryParse(Value)`) |
| `NamespacedDrain` | 200 items with `xmlns:p="urn:catalog"` and `p:*` names |
| `RealDrain` / `RealDomParse` / `RealDomWrite` | Four fixture documents (RSS, OpenDocument, WordprocessingML, RPM metadata), whitespace and comments kept |
| `DomParseTiny` / `DomParseCatalog` / `DomParseNamespaced` | `XmlDocument.Parse` + `Dispose` on the three payloads above |
| `DomNavigate` | Parsed catalog; each item's `id` attribute and `qty` child as integers |
| `DomWrite` | Parsed catalog; `WriteTo` a reused writer |

XmlEssentials writer benches **reuse** one `Utf8XmlWriter` via `Reset()` (STJ `Utf8JsonWriter.Reset`). BCL `XmlWriter` has no reset; those benches construct per operation. Reader benches construct a `Utf8XmlReader` on a `byte[]` span; BCL reuses a `MemoryStream` with `XmlReaderSettings.CloseInput = false`.

### 2. Split “our object” from “the bench’s strings”

`MemoryDiagnoser` counts everything the iteration allocates, including `i.ToString()` and `"Widget" + i` in the bench body. Before changing the library, run a `GC.GetAllocatedBytesForCurrentThread()` probe (file-based `dotnet run app.cs`, not Python) that measures:

1. `new Utf8XmlWriter` / drain ctor only
2. The catalog loop’s own strings
3. Catalog write/read with numbers formatted into the buffer (`WriteAttributeNumber`, `WriteNumberValue`, `WriteStringValue("Widget")` + number — same XML, no concat)

Catalog write was **872 B writer + 8000 B caller strings**. The 8000 B was not the writer. Changing only the library cannot remove it.

### 3. Read the BCL (and STJ) source that actually runs

Fetch tagged files, for example:

- `https://raw.githubusercontent.com/dotnet/runtime/v11.0.0/src/libraries/System.Private.Xml/src/System/Xml/Core/XmlUtf8RawTextWriter.cs`
- `.../XmlWellFormedWriter.cs`
- `.../XmlWellFormedWriterHelpers.cs` (`ElementScope`, `CheckNCName`, `AttrName`)
- `.../System.Text.Json/src/System/Text/Json/Writer/Utf8JsonWriter.cs` (`GetMemory` / `Advance`, `Reset`)

NameTable / interned prefix compare lives with the reader (`XmlNameTable`, `XmlTextReaderImpl`). That was the namespaced-drain lesson; the reader now gets the same effect from a prefix-to-binding cache ([backlog item 1](#1-prefix-resolution-on-the-reader-done-real-documents-071-to-087)).

### 4. Port the technique, not the extra scans

BCL `RawText` writes UTF-16 ASCII as bytes in **one** loop. A port that does `Ascii.IsValid` + `Latin1.GetBytes` + `AppendNameFromChars` (three scans of `"item"`) **lost** catalog time (1.41× → 1.70×). Revert that class of change.

What **did** work:

| Technique | Where BCL / STJ does it | What we did |
| --- | --- | --- |
| Names of open elements kept for the end tag | `ElementScope` keeps `localName`/`prefix` strings; end tag re-`RawText`s them | Every name, however it was given, is kept as UTF-8 in the writer's name buffer (`XmlWriterFrame.Local` / `Prefix`), and let go when the element ends |
| Complete leaf elements | (we went further than BCL) | `WriteElementString` / `WriteElementNumber` write `<n>v</n>` without a frame |
| ASCII names as bytes | `XmlUtf8RawTextWriter.RawText` | `Ascii.FromUtf16`, `WriteRawByte` for markup |
| Direct `IBufferWriter` | `Utf8JsonWriter` `GetMemory`/`Advance` | No 16 KB staging copy on the `IBufferWriter` ctor |
| Numbers without `ToString` | STJ `Utf8Formatter` | `IUtf8SpanFormattable.TryFormat` into the output span; `WriteAttributeNumber` |
| Static `\n` / `\r\n` | — | Ctor does not `Encoding.UTF8.GetBytes` a newline |
| `Reset` | `Utf8JsonWriter.Reset` | Pool the writer in benches; 0 B after warmup |
| Binding slots, not intern+offset | — | Reader copies `NamespaceBinding` URI start/length onto the element |
| 1-byte prefix | BCL interned `string` pointer | `TryLookupBinding` length filter + 1-byte compare + last-prefix cache |

### 5. Prove it

After **each** behavioral or hot-path change:

```bash
dotnet test src/CloudNimble.XmlEssentials.slnx -c Release --coverage --coverage-output-format cobertura --coverage-output artifacts/coverage/coverage.cobertura.xml
dotnet run src/coverage-gate.cs -- artifacts/coverage/coverage.cobertura.xml
dotnet run -c Release --project src/CloudNimble.XmlEssentials.Performance -f net11.0 -- --filter *Utf8XmlWriterBenchmarks*
# or *Utf8XmlReaderBenchmarks* / --filter *
```

If line coverage drops, add internals tests for the new branch **before** more speed work. If a bench regresses, revert the last trick; do not stack a second trick on a regression.

A `GC.GetAllocatedBytesForCurrentThread` test (see `Utf8XmlWriterAllocationTests.Reset_WriteCatalog_DoesNotAllocate`) is required for any claim of 0 B.

## Baseline (2026-11-23, net11.0 ShortRun, Hyper-V)

Integer ns and bytes. Ratio is XmlEssentials / BCL on that category. These numbers were taken **after** the standards work (strict declaration, reserved namespaces, `]]>` detection, whitespace significance, shared XSD lexical routines, namespace-preserving `WriteTo`) and after backlog item 1. Write benches reset their `ArrayBufferWriter` with `ResetWrittenCount()`, which does not zero the buffer.

### Writer (`Reset` reuse)

| Method | Category | Mean | Ratio | Allocated | Alloc ratio |
| --- | --- | --- | --- | --- | --- |
| BclXmlWriter_WriteCatalog | CatalogWrite | 33137 ns | 1.00 | 13728 B | 1.00 |
| XmlEssentials_WriteCatalog | CatalogWrite | 23343 ns | 0.70 | 0 B | 0.00 |
| BclXmlWriter_WriteTiny | TinyWrite | 325 ns | 1.00 | 7328 B | 1.00 |
| XmlEssentials_WriteTiny | TinyWrite | 55 ns | 0.17 | 0 B | 0.00 |

### Reader, synthetic (new `Utf8XmlReader` per iteration on a `byte[]`)

| Method | Category | Mean | Ratio | Allocated | Alloc ratio |
| --- | --- | --- | --- | --- | --- |
| BclXmlReader_DrainCatalog | CatalogDrain | 25674 ns | 1.00 | 15088 B | 1.00 |
| XmlEssentials_DrainCatalog | CatalogDrain | 18737 ns | 0.73 | 0 B | 0.00 |
| BclXmlReader_DrainCatalogGetInt32 | CatalogGetInt32 | 29652 ns | 1.00 | 35808 B | 1.00 |
| XmlEssentials_DrainCatalogGetInt32 | CatalogGetInt32 | 19168 ns | 0.65 | 0 B | 0.00 |
| BclXmlReader_DrainNamespaced | NamespacedDrain | 20491 ns | 1.00 | 15200 B | 1.00 |
| XmlEssentials_DrainNamespaced | NamespacedDrain | 15507 ns | 0.76 | 0 B | 0.00 |
| BclXmlReader_DrainTiny | TinyDrain | 315 ns | 1.00 | 2472 B | 1.00 |
| XmlEssentials_DrainTiny | TinyDrain | 69 ns | 0.22 | 0 B | 0.00 |

### Reader, DOM, and writer on real documents (`RealDocumentBenchmarks`)

Four fixtures from the quick-xml test suite. Both sides keep whitespace and comments. `RealDomWrite` replays a parsed document into a reused writer. **This is the table that matters.**

| Method | Category | File | Mean | Ratio | Allocated | Alloc ratio |
| --- | --- | --- | --- | --- | --- | --- |
| BclXmlReader_Drain | RealDrain | document.xml | 19580 ns | 1.00 | 25144 B | 1.00 |
| XmlEssentials_Drain | RealDrain | document.xml | 14700 ns | 0.75 | 944 B | 0.04 |
| XmlEssentials_DrainWithScratch | RealDrain | document.xml | 14780 ns | 0.75 | 0 B | 0.00 |
| BclXmlReader_Drain | RealDrain | libreoffice_document.fodt | 79360 ns | 1.00 | 56017 B | 1.00 |
| XmlEssentials_Drain | RealDrain | libreoffice_document.fodt | 50830 ns | 0.64 | 6952 B | 0.12 |
| XmlEssentials_DrainWithScratch | RealDrain | libreoffice_document.fodt | 50620 ns | 0.64 | 0 B | 0.00 |
| BclXmlReader_Drain | RealDrain | rpm_primary.xml | 31420 ns | 1.00 | 20064 B | 1.000 |
| XmlEssentials_Drain | RealDrain | rpm_primary.xml | 20790 ns | 0.66 | 80 B | 0.004 |
| XmlEssentials_DrainWithScratch | RealDrain | rpm_primary.xml | 17690 ns | 0.56 | 0 B | 0.000 |
| BclXmlReader_Drain | RealDrain | sample_rss.xml | 187340 ns | 1.00 | 47137 B | 1.00 |
| XmlEssentials_Drain | RealDrain | sample_rss.xml | 83850 ns | 0.45 | 1441 B | 0.03 |
| XmlEssentials_DrainWithScratch | RealDrain | sample_rss.xml | 84730 ns | 0.45 | 0 B | 0.00 |
| BclXmlDocument_Parse | RealDomParse | document.xml | 50340 ns | 1.00 | 127176 B | 1.000 |
| XmlEssentials_Parse | RealDomParse | document.xml | 21180 ns | 0.42 | 104 B | 0.001 |
| BclXmlDocument_Parse | RealDomParse | libreoffice_document.fodt | 191240 ns | 1.00 | 353889 B | 1.000 |
| XmlEssentials_Parse | RealDomParse | libreoffice_document.fodt | 68560 ns | 0.36 | 105 B | 0.000 |
| BclXmlDocument_Parse | RealDomParse | rpm_primary.xml | 71070 ns | 1.00 | 170953 B | 1.000 |
| XmlEssentials_Parse | RealDomParse | rpm_primary.xml | 25310 ns | 0.36 | 104 B | 0.001 |
| BclXmlDocument_Parse | RealDomParse | sample_rss.xml | 331240 ns | 1.00 | 709082 B | 1.000 |
| XmlEssentials_Parse | RealDomParse | sample_rss.xml | 112880 ns | 0.34 | 105 B | 0.000 |
| BclXmlDocument_Write | RealDomWrite | document.xml | 28670 ns | 1.00 | 11032 B | 1.00 |
| XmlEssentials_Write | RealDomWrite | document.xml | 22200 ns | 0.77 | 0 B | 0.00 |
| BclXmlDocument_Write | RealDomWrite | libreoffice_document.fodt | 95810 ns | 1.00 | 23273 B | 1.00 |
| XmlEssentials_Write | RealDomWrite | libreoffice_document.fodt | 71450 ns | 0.75 | 0 B | 0.00 |
| BclXmlDocument_Write | RealDomWrite | rpm_primary.xml | 32270 ns | 1.00 | 8016 B | 1.00 |
| XmlEssentials_Write | RealDomWrite | rpm_primary.xml | 21270 ns | 0.66 | 0 B | 0.00 |
| BclXmlDocument_Write | RealDomWrite | sample_rss.xml | 149190 ns | 1.00 | 8017 B | 1.00 |
| XmlEssentials_Write | RealDomWrite | sample_rss.xml | 80340 ns | 0.54 | 0 B | 0.00 |

What the real documents show:

- Every row is under BCL. Before backlog item 1 the namespace-heavy drains were 1.27× (WordprocessingML) and 1.79× (OpenDocument), and `WriteTo` on the OpenDocument file was 1.22×. The synthetic catalog had hidden all of it.
- **Drain is 0 B on real documents when the reader is given a scratch buffer** (`XmlEssentials_DrainWithScratch`, pinned by `Drain_RealDocumentWithScratch_AllocatesNothing`). Without one the reader allocates a few arrays per instance, not per token: namespace bindings, element stacks and the attribute table once they outgrow their eight inline slots, and one buffer each for decoded attribute values and decoded text. See the fourth pass below and [reader](04-api-reader.md#scratch-buffer).
- `RealDomWrite` is 0 B on all four, pinned by `XmlDocumentAllocationTests.WriteTo_RealDocument_DoesNotAllocate`. That test found 200 B per write on the RSS file: sibling elements that declare different prefixes reuse one binding slot in turn, so the slot's remembered bytes never matched. The writer now keeps the first 64 distinct prefixes and URIs it has seen (`InternNamespaceBytes`).

### DOM, synthetic (`XmlDocument` / `XmlElement`, PR 4)

Parse rows build and dispose one document per operation. Navigate and write rows reuse one parsed catalog, so they measure the tree. BCL is `System.Xml.XmlDocument` loaded from a reused `MemoryStream`.

| Method | Category | Mean | Ratio | Allocated | Alloc ratio |
| --- | --- | --- | --- | --- | --- |
| BclXmlDocument_NavigateCatalog | DomNavigate | 3494 ns | 1.00 | 0 B | - |
| XmlEssentials_NavigateCatalog | DomNavigate | 2801 ns | 0.80 | 0 B | - |
| BclXmlDocument_ParseCatalog | DomParseCatalog | 54082 ns | 1.00 | 136128 B | 1.000 |
| XmlEssentials_ParseCatalog | DomParseCatalog | 26826 ns | 0.50 | 104 B | 0.001 |
| BclXmlDocument_ParseNamespaced | DomParseNamespaced | 43320 ns | 1.00 | 110760 B | 1.000 |
| XmlEssentials_ParseNamespaced | DomParseNamespaced | 21709 ns | 0.50 | 104 B | 0.001 |
| BclXmlDocument_ParseTiny | DomParseTiny | 650 ns | 1.00 | 5176 B | 1.00 |
| XmlEssentials_ParseTiny | DomParseTiny | 120 ns | 0.18 | 104 B | 0.02 |
| BclXmlDocument_WriteCatalog | DomWrite | 28184 ns | 1.00 | 7368 B | 1.00 |
| XmlEssentials_WriteCatalog | DomWrite | 15628 ns | 0.55 | 0 B | 0.00 |

The 104 B on parse is the `XmlDocument` object itself. [DOM](06-api-dom.md) rules out a document-instance cache, so parse cannot reach 0 B; everything the document holds is pooled. Before the row-store rewrite the same benches read 0.94 / 1.07 / 0.31 (parse), 3.71 with 59040 B (navigate), and 1.02 (write).

### Serializer, scalar roots (`XmlSerializerBenchmarks`, PR 5)

Each library reads the document it wrote. BCL is a cached `System.Xml.Serialization.XmlSerializer` with reused reader and writer settings; it has no call that returns bytes, so its write rows reuse one `MemoryStream` and copy it out. `System.Text.Json` is a sidecar, not a baseline. XmlEssentials uses `XmlSerializerOptions.Default` (reflection resolver, declaration written).

| Method | Category | Mean | Ratio | Allocated | Alloc ratio |
| --- | --- | --- | --- | --- | --- |
| Bcl_WriteDateTime | WriteDateTime | 348 ns | 1.00 | 7784 B | 1.00 |
| XmlEssentials_WriteDateTime | WriteDateTime | 68 ns | 0.20 | 104 B | 0.01 |
| Bcl_WriteEnum | WriteEnum | 355 ns | 1.00 | 7744 B | 1.00 |
| XmlEssentials_WriteEnum | WriteEnum | 64 ns | 0.18 | 96 B | 0.01 |
| Bcl_WriteInt32 | WriteInt32 | 331 ns | 1.00 | 7728 B | 1.000 |
| Stj_WriteInt32 | WriteInt32 | 31 ns | 0.09 | 32 B | 0.004 |
| XmlEssentials_WriteInt32 | WriteInt32 | 56 ns | 0.17 | 88 B | 0.011 |
| Bcl_WriteInt32Buffer | WriteInt32Buffer | 322 ns | 1.00 | 7648 B | 1.00 |
| XmlEssentials_WriteInt32Buffer | WriteInt32Buffer | 48 ns | 0.15 | 0 B | 0.00 |
| Bcl_WriteInt32String | WriteInt32String | 336 ns | 1.00 | 7784 B | 1.000 |
| Stj_WriteInt32String | WriteInt32String | 35 ns | 0.10 | 32 B | 0.004 |
| XmlEssentials_WriteInt32String | WriteInt32String | 65 ns | 0.19 | 144 B | 0.018 |
| Bcl_WriteNullable | WriteNullable | 600 ns | 1.00 | 8024 B | 1.00 |
| XmlEssentials_WriteNullable | WriteNullable | 78 ns | 0.13 | 144 B | 0.02 |
| Bcl_WriteString | WriteString | 356 ns | 1.00 | 7888 B | 1.00 |
| Stj_WriteString | WriteString | 66 ns | 0.18 | 80 B | 0.01 |
| XmlEssentials_WriteString | WriteString | 90 ns | 0.25 | 128 B | 0.02 |
| Bcl_ReadDateTime | ReadDateTime | 1114 ns | 1.00 | 6040 B | 1.00 |
| XmlEssentials_ReadDateTime | ReadDateTime | 71 ns | 0.06 | 0 B | 0.00 |
| Bcl_ReadEnum | ReadEnum | 813 ns | 1.00 | 4480 B | 1.00 |
| XmlEssentials_ReadEnum | ReadEnum | 59 ns | 0.07 | 0 B | 0.00 |
| Bcl_ReadInt32 | ReadInt32 | 1030 ns | 1.00 | 5968 B | 1.00 |
| Stj_ReadInt32 | ReadInt32 | 25 ns | 0.02 | 0 B | 0.00 |
| XmlEssentials_ReadInt32 | ReadInt32 | 55 ns | 0.05 | 0 B | 0.00 |
| Bcl_ReadInt32Stream | ReadInt32Stream | 1036 ns | 1.00 | 5968 B | 1.00 |
| XmlEssentials_ReadInt32Stream | ReadInt32Stream | 68 ns | 0.07 | 0 B | 0.00 |
| Bcl_ReadString | ReadString | 1200 ns | 1.00 | 6120 B | 1.00 |
| Stj_ReadString | ReadString | 77 ns | 0.06 | 96 B | 0.02 |
| XmlEssentials_ReadString | ReadString | 122 ns | 0.10 | 96 B | 0.02 |

- Every write row allocates only what it returns (the array or the string); `WriteInt32Buffer` writes into the caller's buffer and is 0 B. Pinned by `XmlSerializerAllocationTests` (`Serialize_ToBufferWriter_DoesNotAllocate`, `Serialize_Enum_ToBufferWriter_DoesNotAllocate`, `Serialize_ToUtf8Bytes_AllocatesOnlyTheResult`).
- The 0 B read rows are pinned by `Deserialize_FromUtf8_DoesNotAllocate`, `Deserialize_FromStream_DoesNotAllocate`, `Deserialize_DateTime_FromUtf8_DoesNotAllocate`, and `Deserialize_Enum_FromUtf8_DoesNotAllocate`. `ReadString` allocates the string it returns.
- Before this pass the write rows were **1.40 to 1.65 of BCL and about 17 KB per call**: each call allocated a 16 KB `ArrayBufferWriter`, a `Utf8XmlWriter`, and a copy of the result (twice for a string). Reads were 0.14 to 0.21; `ReadEnum` allocated 24 B and `ReadInt32Stream` 432 B.
- What changed it: one `Utf8XmlWriter` and one pooled buffer per thread (`XmlWriterCache`, used through `XmlWriterLease`, the same idea as STJ's `Utf8JsonWriterCache`); the string and array are produced from the pooled span; streams are read into a pooled array with the size limit checked as bytes arrive; the last type info is checked before the dictionary; enums are read and written from tables of UTF-8 names built once per enum type, with no boxing and no string; dates, times, and GUIDs are formatted on the stack; the root start tag is written from UTF-8 bytes held by the type info (16 ns per element against 22 ns for the `string` overload).

#### Where a single read spends its time (`XmlSerializerSequenceBenchmarks`, `ReadInt32Parts`)

Same value, `<Int32>12345</Int32>`, with and without the 38-byte XML declaration. No baseline: the rows are compared with each other.

| Method | Category | Mean | Allocated |
| --- | --- | --- | --- |
| Reader_Declared | ReadInt32Parts | 46 ns | 0 B |
| Reader_Plain | ReadInt32Parts | 41 ns | 0 B |
| Serializer_Declared | ReadInt32Parts | 56 ns | 0 B |
| Serializer_Plain | ReadInt32Parts | 51 ns | 0 B |
| Serializer_Plain_TypeInfo | ReadInt32Parts | 50 ns | 0 B |

Before the three reader changes below: `Reader_Plain` 57 ns, `Reader_Declared` 121 ns, `Serializer_Plain_TypeInfo` 67 ns, `Serializer_Plain` 78 ns, `Serializer_Declared` 146 ns. A single declared read went from 146 ns to 68 ns.

- **The serializer adds about 6 ns to the tokenizer** (47 against 41) and resolving type info from options adds about 8 ns more.
- **The declaration cost 65 ns and now costs 5 ns.** `ReadPiOrDeclaration` validated the data of a processing instruction one character at a time; a sampling profile had it at 33% of the declared read. Two steps fixed it. It finds `?>` with one search and checks the run against `XmlChars.PlainAsciiChars` with another (65 ns to 22 ns). And the declaration `Utf8XmlWriter` writes, `<?xml version="1.0" encoding="utf-8"?>`, is recognized with one comparison before any parsing (22 ns to 5 ns); every other spelling takes the general path. `System.Xml` does the same with one `StartsWith`.
- **Constructing the reader cost 34 ns and now costs 17 ns.** It was clearing its inline slots. Leaving out the clearing of only the eight small stacks saved 7 ns, which showed that the clearing was the cost and not the size. The slots are guarded by counts and a mask, so they need no clearing, but a slot holding an object reference must be cleared for the collector. So `AttributeRecord` and `NamespaceBinding` no longer hold references: a decoded attribute value is an offset into the one buffer the start tag's attributes share (which now keeps its contents when it grows), and an owned prefix or URI is a negative start that `Utf8XmlReader.Owned` resolves to a well-known namespace or an interned array. The reader went from 1320 to 1064 bytes. Two stacks are still cleared, the element-namespace ones: a slot there is written only once a namespace is in scope and is read as "no namespace" before that. Two earlier attempts were reverted: `this = default` (45 ns, it builds a temporary and copies it) and leaving the fields to the compiler's automatic defaults (no change).
- What `System.Text.Json` does differently, from its source: `Utf8JsonReader` holds a span, a few counters, its options, and a `BitStack`, so there is nothing to clear, and a JSON number is one token where `<Int32>1</Int32>` is three. 25 ns is not reachable for XML; the tokenizer alone is 44 ns here.

#### Sequences (`XmlSerializerSequenceBenchmarks`, 1,000 integers)

BCL has no streaming call, so its baseline is a cached `XmlSerializer` over `int[]`, read and written whole. STJ and XmlEssentials use their asynchronous sequence calls over a rewound `MemoryStream`.

| Method | Category | Mean | Ratio | Allocated | Alloc ratio |
| --- | --- | --- | --- | --- | --- |
| Bcl_ReadArray | ReadSequence | 41532 ns | 1.00 | 61152 B | 1.00 |
| Stj_ReadSequence | ReadSequence | 28113 ns | 0.68 | 8968 B | 0.15 |
| XmlEssentials_ReadSequence | ReadSequence | 31877 ns | 0.77 | 9552 B | 0.16 |
| Bcl_WriteArray | WriteSequence | 33014 ns | 1.00 | 30408 B | 1.00 |
| Stj_WriteSequence | WriteSequence | 13899 ns | 0.42 | 376 B | 0.01 |
| XmlEssentials_WriteSequence | WriteSequence | 21519 ns | 0.65 | 928 B | 0.03 |

- **Both rows are under BCL: 0.79 reading and 0.65 writing.** They were level with it (0.98 to 1.07) until the fixed cost of a small element was cut, see "The fixed cost of a small element" below, and 0.93 to 0.98 and 0.85 until a scalar element was read and written as a leaf, see "Second pass: the element itself".
- `ReadSequence` history: 221981 ns and 545561 B (5.40 of BCL) when streaming first shipped, with a reader state captured and two readers built per value; 65638 ns (1.60) with one state per buffer and every value tokenized twice; 51849 ns (1.26) with one pass and the reader copied after each value; about 43000 ns (1.00 to 1.05) now.
- How it reads in one pass. The built-in value converters have an internal `TryRead` that returns `false` when the reader's block ends before the element does, instead of throwing. A converter that cannot stop part way gets a read-ahead: the element is skipped first, then read from a copy of the reader, which is what `System.Text.Json` does for converters that cannot resume. `Utf8XmlReader.TrySkip` now puts the reader back on the start tag when it returns `false`, as `Utf8JsonReader.TrySkip` does.
- How it resumes without a copy per value. `Utf8JsonReader` is small enough to checkpoint by assignment; ours is 1064 bytes and holds references, so each copy goes through the write-barrier block move. A profile had that copy as the largest single cost. But the same elements are open and the same namespaces are in scope before every child of the document element, so the state is captured once, at the document element's start tag, and moved to where the last complete value ended (`XmlReaderStateSnapshot.At`). That costs three numbers per value.
- Where the time went before a scalar element was read as a leaf (43 µs then, 32 µs now), measured apart: 22 µs in the tokenizer (three tokens per value), 11 µs in the asynchronous iterator itself (a bare iterator of 1,000 integers costs that; STJ pays it too, the BCL array call does not), 1 µs in stream reads, the rest in the loop.
- `WriteSequence` was 33 ns per value before a scalar element was written as a leaf (21 ns now): about 17 ns in the writer per element (20 ns measured alone with a span name, before the next change), 11 ns in the asynchronous enumeration, the rest in per-value checks. One writer change came out of profiling it: `PushFrame` took a whole `XmlWriterFrame` by value and stored it, and because the frame holds two string references that store went through the collector's block move on every start tag. The frame is now built in its slot (`NextFrame`); 3 ns per element, 39.4 to 36.3 µs per 1,000 in a direct timing. Indexing the inline frames through `Unsafe.Add` instead of the indexer was tried and made no difference, so it was not kept. STJ's converter does not suspend per element either (it loops "while elements are being returned synchronously"); its advantage is that a JSON number and a comma cost less than an XML element.

Summary: every category, synthetic and real, is under BCL. Synthetic categories are 0 B (apart from the one object per parsed document, and the result a serializer call returns). Real-document drains allocate 80 B to 6920 B per reader.

#### The fixed cost of a small element

`<Int32>1</Int32>` is three tokens to read and three calls to write, and none of them has enough bytes for a vectorized search to pay for itself. Timed directly on 1,000 such elements (no serializer, no stream): reading went from 21.9 to 19.7 µs and writing from 20.0 to 12.3 µs.

- **Reader: short text is read one byte at a time.** `ReadText` ran three vectorized searches over a text node of one to three bytes. It now reads up to `ShortTextBytes` (8) bytes itself, taking only bytes that mean the same in both XML versions (printable ASCII other than `<`, `&` and `>`, the blank, the tab, the line feed), and hands anything else or anything longer to the existing loop. This also covers the indentation between two tags. The limit was 16 at first; that cost a 20-byte value 4 ns (1,000 date-times: 22.8 µs with no head, 27.0 µs at 16, 24.0 µs at 8) and showed up as `ReadDateTime` going from 77 to 80 ns. At 8 the integers, `rpm_primary.xml`, and the OpenDocument file keep all of the gain.
- **Checking a suspect row.** Five rows looked a few percent slower after this work. Three reruns settled them: `WriteInt32Buffer` and `ReadString` were noise; `ReadDateTime` was the head limit above; the two `sample_rss.xml` rows were the machine, because BCL moved with them and the ratios stayed at 0.48 and 0.37. Compare ratios, not times, across runs made at different moments.
- **Writer: short names are checked one byte at a time.** `ValidateNcName` ran one vectorized search for name characters and a second for a colon. `XmlChars.IsShortAsciiNcName` decides an ASCII name of up to 16 bytes in one loop (20.0 to 17.8 µs).
- **Writer: a tag goes out in one step.** A start tag was `<` and the name in two writes; an end tag was `</`, the name, and `>` in three. `WriteTagOpen` and `WriteTagClose` reserve once and write once (17.8 to 12.1 µs). This was the largest single step.
- **Tried and not kept**, each measured on the same loop: caching the array behind the output `Memory<byte>` so `Memory<T>.Span` is not asked again (12.1 to 15.6 µs, slower); writing the `>` that closes a start tag together with a number (12.1 to 13.1 µs, slower); indexing the inline frames through `Unsafe.Add` (no change).
- The CPU sampling profile was no help here: it charged most samples to whichever native frame was on the stack. What worked was the direct loop and one change at a time.

#### A document that was only fast when the JIT was lucky

The first full run after the reader changes had `sample_rss.xml` at 256 µs to drain (1.36 of BCL) where it had been 161 µs. The committed code still gave 161 µs on the same machine, so the change caused it, yet none of the changed code runs more than three times on that file. Outside BenchmarkDotNet the same build gave 155 µs with tiered compilation and 296 µs with it off. That was the finding: the file is mostly CDATA, `ReadCData` validated one character at a time through `AdvanceChar`, and it was fast only when profile-guided optimization inlined that call. The unrelated change had tipped the decision inside the benchmark process.

`ReadCData` and `ReadComment` now skip a run of plain ASCII with one search (`XmlChars.CDataPlainChars`, `XmlChars.CommentPlainChars`: tab and printable ASCII without the character that can start the terminator) and leave line ends and everything else to `AdvanceChar`. The drain is 89 µs in the benchmark (0.48), 83 µs outside it, and 109 µs with tiering off.

The tests written for that skip found a conformance gap that had been there since the reader shipped: line ends inside CDATA sections, comments, and processing instructions were returned as written, where XML 2.11 (and `spec/02-xml-fidelity.md`) say `#xD#xA` and `#xD` become `#xA` everywhere, and NEL and LS too in XML 1.1. `DecodeValueSpan` now takes `expandReferences` and all four token kinds go through it; a value with no carriage return (or 1.1 line end) still stays in the input, at the cost of one vectorized search per token.

Process rule that follows: **when a row moves by a large amount for no visible reason, time it outside BenchmarkDotNet with `DOTNET_TieredCompilation=0`.** If the untiered number is much worse than the tiered one, the hot path depends on a JIT decision and should be fixed so it does not.

### Serializer, graph columns (`XmlSerializerGraphBenchmarks`, PR 9)

The missing first-call and source-gen columns live on the Customer graph (two addresses, 20 line items, four tags). Numbers: [benchmarks](15-benchmarks.md#graph-xmlserializergraphbenchmarks-net110-shortrun-2026-11-23). Cached reflection write is 0 B and 0.39 of BCL. Source-gen write was 1.09 of BCL and 9 KB per call because every scalar went through `TypeInfoResolver.GetTypeInfo`; generated converters now call `WriteNumberValue` / `WriteStringValue` / `WriteBooleanValue` and `ReadInt32Element` / `ReadDecimalElement` for those primitives. Source-gen write is 0 B and **0.31 of BCL** (faster than cached reflection) after generated converters call `WriteLeafStart`/`WriteLeafEnd` with prebuilt `"<Name>"u8` tags, matching the reflection binder's `LeafOpenTag`. Source-gen read is slightly ahead of cached reflection on this graph. Interceptors are emitted only when `InterceptorsNamespaces` includes `CloudNimble.Text.Xml.Generated`, so the Performance project can reference the generator without rewriting its reflection rows.

The source-gen read numbers here and in the passes below describe generated read code that [single engine](25-single-engine-metadata.md) (K52) removed. A generated context now reads through the runtime engine. They are kept as the record of that work and are re-measured in the final benchmark pass, which the owner scheduled for the end.

### Serializer, objects (`XmlSerializerObjectBenchmarks`, PR 6)

Each library reads the document it wrote and writes into a sink that is reused between calls. The models are in `CloudNimble.XmlEssentials.Performance/Models` and carry both libraries' attributes, so the documents have the same shape (the BCL ones are about 100 bytes longer because they declare `xsi` and `xsd`). The lists hold 1,000 items; the dictionary holds 100. The BCL serializer cannot map a record created through its constructor or a dictionary, so `Members` and `Dictionary` have no BCL row.

| Method | Category | Mean | Ratio | Allocated | Alloc ratio |
| --- | --- | --- | --- | --- | --- |
| Bcl_ReadCustomer | ReadCustomer | 8689 ns | 1.00 | 16496 B | 1.00 |
| Stj_ReadCustomer | ReadCustomer | 2993 ns | 0.34 | 3576 B | 0.22 |
| XmlEssentials_ReadCustomer | ReadCustomer | 3368 ns | 0.39 | 3168 B | 0.19 |
| Stj_ReadDictionary | ReadDictionary | 3696 ns |  | 13392 B |  |
| XmlEssentials_ReadDictionary | ReadDictionary | 4053 ns |  | 13392 B |  |
| Bcl_ReadDrawing | ReadDrawing | 209760 ns | 1.00 | 130866 B | 1.00 |
| Stj_ReadDrawing | ReadDrawing | 144158 ns | 0.69 | 117914 B | 0.90 |
| XmlEssentials_ReadDrawing | ReadDrawing | 114728 ns | 0.55 | 90793 B | 0.69 |
| Stj_ReadMembers | ReadMembers | 167605 ns |  | 240274 B |  |
| XmlEssentials_ReadMembers | ReadMembers | 170462 ns |  | 143802 B |  |
| Bcl_ReadMoney | ReadMoney | 268210 ns | 1.00 | 146251 B | 1.00 |
| Stj_ReadMoney | ReadMoney | 100397 ns | 0.37 | 89073 B | 0.61 |
| XmlEssentials_ReadMoney | ReadMoney | 91606 ns | 0.34 | 88601 B | 0.61 |
| Bcl_ReadOrder | ReadOrder | 2071 ns | 1.00 | 7368 B | 1.00 |
| Stj_ReadOrder | ReadOrder | 318 ns | 0.15 | 232 B | 0.03 |
| XmlEssentials_ReadOrder | ReadOrder | 457 ns | 0.22 | 232 B | 0.03 |
| Bcl_ReadOrders | ReadOrders | 771469 ns | 1.00 | 567319 B | 1.00 |
| Stj_ReadOrders | ReadOrders | 330520 ns | 0.43 | 249075 B | 0.44 |
| XmlEssentials_ReadOrders | ReadOrders | 433455 ns | 0.56 | 248603 B | 0.44 |
| Bcl_ReadReadings | ReadReadings | 346818 ns | 1.00 | 267715 B | 1.00 |
| Stj_ReadReadings | ReadReadings | 145485 ns | 0.42 | 105074 B | 0.39 |
| XmlEssentials_ReadReadings | ReadReadings | 201662 ns | 0.58 | 104602 B | 0.39 |
| Bcl_WriteCustomer | WriteCustomer | 4334 ns | 1.00 | 8648 B | 1.00 |
| Stj_WriteCustomer | WriteCustomer | 1493 ns | 0.34 | 312 B | 0.04 |
| XmlEssentials_WriteCustomer | WriteCustomer | 1681 ns | 0.39 | 0 B | 0.00 |
| Stj_WriteDictionary | WriteDictionary | 976 ns |  | 0 B |  |
| XmlEssentials_WriteDictionary | WriteDictionary | 1658 ns |  | 0 B |  |
| Bcl_WriteDrawing | WriteDrawing | 130754 ns | 1.00 | 30098 B | 1.00 |
| Stj_WriteDrawing | WriteDrawing | 86951 ns | 0.67 | 313 B | 0.01 |
| XmlEssentials_WriteDrawing | WriteDrawing | 69506 ns | 0.53 | 0 B | 0.00 |
| Stj_WriteMembers | WriteMembers | 56962 ns |  | 312 B |  |
| XmlEssentials_WriteMembers | WriteMembers | 68674 ns |  | 0 B |  |
| Bcl_WriteMoney | WriteMoney | 83452 ns | 1.00 | 48009 B | 1.000 |
| Stj_WriteMoney | WriteMoney | 55039 ns | 0.66 | 312 B | 0.006 |
| XmlEssentials_WriteMoney | WriteMoney | 63553 ns | 0.76 | 0 B | 0.000 |
| Bcl_WriteOrder | WriteOrder | 1030 ns | 1.00 | 8192 B | 1.00 |
| Stj_WriteOrder | WriteOrder | 187 ns | 0.18 | 0 B | 0.00 |
| XmlEssentials_WriteOrder | WriteOrder | 266 ns | 0.26 | 0 B | 0.00 |
| Bcl_WriteOrders | WriteOrders | 423885 ns | 1.00 | 214507 B | 1.000 |
| Stj_WriteOrders | WriteOrders | 202953 ns | 0.48 | 314 B | 0.001 |
| XmlEssentials_WriteOrders | WriteOrders | 234705 ns | 0.55 | 0 B | 0.000 |
| Bcl_WriteReadings | WriteReadings | 216607 ns | 1.00 | 131138 B | 1.000 |
| Stj_WriteReadings | WriteReadings | 109307 ns | 0.50 | 313 B | 0.002 |
| XmlEssentials_WriteReadings | WriteReadings | 132853 ns | 0.61 | 0 B | 0.000 |

| Category | What it holds |
| --- | --- |
| `Order` / `Orders` | One flat object of nine scalars (two strings, one with `&`; `int`, `long`, `bool`, enum, `decimal`, `double`, `DateTime`); a list of 1,000 |
| `Customer` | Two child objects, an unwrapped list of 20 objects, a wrapped list of 4 strings |
| `Readings` | 1,000 objects mapped as four attributes |
| `Money` | 1,000 elements with simple content and one attribute |
| `Members` | 1,000 records created through a four-parameter constructor |
| `Drawing` | 1,000 objects of three runtime types, told apart by element name |
| `Dictionary` | 100 integers keyed by name |

- **Every row with a BCL baseline is under it: 0.23 to 0.57 reading and 0.27 to 0.68 writing.** As PR 6 first worked, three rows were over (`WriteOrders` 1.21, `WriteReadings` 1.27, `ReadReadings` 1.51) and `ReadOrders` was 0.92.
- **Writing an object allocates nothing**, pinned by `Serialize_Objects_ToBufferWriter_DoNotAllocate` (elements, attributes, simple content, wrapped and unwrapped lists, arrays, derived types, dictionaries, a root list, a record). Reading allocates the objects and strings it returns: `Deserialize_Person_AllocatesOnlyTheObjectAndItsString`, `Deserialize_AttributeMapped_AllocatesOnlyTheObject`, `Deserialize_Price_AllocatesOnlyTheObjectAndItsString`. A type created through a constructor with parameters holds its values in an array its binding keeps between objects, and a value that needs decoding is decoded in the reader's scratch buffer, so neither allocates per object (see the fourth pass).
- **The scalar rows had slowed by about 15 ns when PR 6 first worked, and the object benchmarks are what showed it.** `XmlSerializerOptions` keeps the type info it returned last and checks it before the dictionary. PR 6 put the dictionary behind a lock, so that a type still being bound is not handed out, and stopped updating that slot on a dictionary hit. After a second type had been used, every root call took the lock: `ReadInt32` was 81 ns and `WriteInt32Buffer` 67 ns in the same process that had run other types, and 63 and 48 ns in a loop that used only `int`. A hit now becomes the type info returned last, unless a type is being bound at that moment. The scalar and sequence tables above are from the run after that fix.

#### What made the difference (first pass)

Times are 1,000 `Order` objects in a direct loop unless a row says otherwise.

| Step | Before | After |
| --- | --- | --- |
| Members read and written as their own type; object created first and set as it is read; members sorted into attributes, content, and elements once; the next expected member tried first; typed lists | read 710 µs, write 494 µs | read 538 µs, write 372 µs |
| Dates and times formatted by hand into the output (`XmlLexical.FormatDateTime` and friends) | `DateTime` 57 ns | 26 ns; write 322 µs |
| An attribute compared by its whole name when it has no prefix; mapped members looked for before the names the serializer reserves | `ReadReadings` 283 µs | 226 µs |
| `GetNil` scans the attribute records directly and skips unprefixed ones; `HandleNull` read once per type | `ReadReadings` 226 µs | 187 µs |
| Decoding a value copies whole runs between references; a decoded run followed by the end tag is parsed where it lies | string with `&` 102 ns | 73 ns |
| Dictionary converter typed by key and value | write 3.7 µs and 8856 B per 100 entries | 2.4 µs and 0 B |

- **Before.** The first version held every member's value in an `object?[]` with a `bool[]` beside it, boxed each value type, searched all members for each element, called the getter twice per member on write, created collections through `Activator` and `IList`, and read an `AsyncLocal` per member for the reference resolver.
- **Now.** `XmlBoundProperty<TDeclaring, TValue>` reads with `XmlTypeInfo<TValue>` and sets through a typed delegate. Whether each member has appeared is a stack-allocated span. `XmlCollectionBinding<TElement>` builds a `List<TElement>` and writes lists and arrays without an enumerator object. The reference resolver is looked at only when `ReferenceHandler` is set. A type created through a constructor with parameters still holds its values until the element ends, as STJ does.
- **Where the rest of the time was, before the second pass.** A hand-written loop over the reader that parses and sets the same members (what a source generator would emit) takes 434 µs for the orders against 517 µs, and 189 µs for the readings against 187 µs. The reader alone takes 267 µs of the orders. So the serializer's own share is small now, and the next gains are in the reader and in parsing: `decimal` costs 24 ns more than `int`, `Guid` 29 ns, `DateTime` 17 ns.
- **Per type before the second pass, 1,000 root items, ns per item (XmlEssentials / BCL / STJ).** Write: `int` 15 / 31 / 3, `bool` 17 / 30 / 2, `double` 51 / 68 / 36, `decimal` 26 / 47 / 18, `DateTime` 26 / 60 / 13, `string` 20 / 30 / 5, string with `&` 36 / 37 / 23, enum 20 / 31 / 3, `Guid` 14 / 44 / 3. Read: `int` 27 / 41 / 9, `double` 48 / 61 / 27, `decimal` 52 / 68 / 23, `DateTime` 45 / 71 / 15, `string` 39 / 144 / 16, string with `&` 73 / 170 / 51, enum 37 / 46 / 8, `Guid` 57 / 78 / 24.

#### Second pass: the element itself

After the first pass the serializer's own share was small, so the second pass went after what each member costs in the writer and the reader. Times are direct loops; rows not named are 1,000 `Order` objects.

| Step | Before | After |
| --- | --- | --- |
| A scalar member is written as a start tag built when the type is bound, the value, and an end tag built the same way, with no element frame (`Utf8XmlWriter.WriteLeafStart` / `WriteLeafEnd`) | write 316 µs; one `int` element 14.6 ns | 213 µs; 7.8 ns |
| An attribute member is written from ` name="` built when the type is bound (`WriteStartAttributeTrusted`); it is still recorded and checked for duplicates | `WriteReadings` 151 µs | 120 µs |
| A scalar element's text and end tag are read in one step (`Utf8XmlReader.TryReadLeafContent`) | one `int` element 27.9 ns; `ReadMembers` 202 µs; `ReadDrawing` 125 µs | 21.5 ns; 167 µs; 108 µs |
| `decimal` written as sign, digits, point, digits with at most 18 digits is parsed into 64 bits directly (`XmlLexical.TryParsePlainDecimal`) | 43 ns | 30 ns |
| A type created through a constructor holds its values in one array laid out in parameter order, and the constructor is called on that array | `ReadMembers` 303802 B, 167 µs | 247802 B, 159 µs |
| A member that is a built-in scalar and has no attributes is read without the nil check and one virtual call (interleaved runs: 430, 419, 421 without; 394, 394, 408, 388, 378 with) | read about 423 µs | about 392 µs |
| A struct's properties are read and set through delegates that take the struct by reference, instead of `PropertyInfo.GetValue` / `SetValue` (1,000 four-member structs) | read 145 µs, write 89 µs | read 118 µs, write 66 µs; the same shape as a class is 120 and 57 µs |

- **Why the leaf paths are safe.** A frame exists to remember an element's name, its namespaces, and whether it has content. A scalar member has a fixed name, no namespaces of its own, and exactly one value, so the writer has nothing to remember; the value is written as text of the parent and `WriteLeafEnd` puts back the one flag that changes (whether the parent holds text, which decides indentation). The name is an ASCII NCName checked when the tag is built; `MaxNameByteCount` and `MaxDepth` are still checked on every write. A member in a namespace, with a non-ASCII name, or whose converter is not built in takes the ordinary path. On the reader, `TryReadLeafContent` only accepts text that is plain in both XML versions (tab, blank, printable ASCII without `<`, `&`, `>`), with a byte that is not white space among the first eight, followed at once by an end tag that repeats the start tag's bytes and `>`. Anything else returns `false` before anything has been read, so every other shape still goes through `Read`.
- **`BCL` generates straight-line code per type and is not faster for it.** `XmlSerializationReaderILGen` emits a stack-allocated `Span<bool>` of members seen and a chain of comparisons of atomized names in declared order, which is the shape this converter already has without generated code.
- **Tried and not kept**, each measured on the same loop: calling the property's own getter and setter delegates through `Unsafe.As` instead of through a closure that casts (no change); a hand-written parse of the hyphenated GUID form (43 to 48 ns per element, slower than `Utf8Parser`).
- **Noise.** This machine moves a 400 µs loop by 5 to 8% between runs. A step is listed only if it was larger than that, or was confirmed by interleaved runs of both builds.
- The reader, writer, DOM, and real-document tables above were not regenerated: the full run after this pass had every one of those rows within noise of the table (the one row that looked different, DOM parse of `document.xml` at 26.5 µs, was 21.1 µs when rerun).

#### Third pass: the backlog

Each backlog item was either done, or measured and left with the reason.

| Item | Outcome | Before | After |
| --- | --- | --- | --- |
| `Nullable<T>` members skipped the leaf paths | **Done.** A nullable whose value has a built-in converter gets `XmlNullableValueConverter<T>`, itself a value converter, so `int?` reads and writes like `int`. A custom converter for `T` is still wrapped by `XmlNullableConverter<T>`. | 1,000 `int?`: write 17.8 ns each | 9.6 ns |
| Writing a nullable from a list boxed it | **Found here and fixed.** `value is null` on a type parameter is a box and a null test. The JIT normally removes the box for a `Nullable<T>`; with profile-guided inlining it did not, in one loop. `XmlConverterHelpers.IsNull` uses the comparer and cannot box. Pinned by `Serialize_NullableValues_ToBufferWriter_DoNotAllocate`, which warms up long enough for tier 1. | 24,000 B per 1,000 `int?` | 0 B |
| Writing a `TimeSpan` allocated a string | **Done.** `XmlLexical.FormatDuration` writes into the output. It also fixes three defects: a duration under one millisecond was written as `P`, sub-millisecond ticks were dropped, and `TimeSpan.MinValue` threw. A fraction now has no trailing zeros (`PT1.5S`, as `XmlConvert` writes it; it was `PT1.500S`). The parser now reads `TimeSpan.MinValue` back. | 42.3 ns, 152 B | 15.9 ns, 0 B |
| The reader's decode buffer was sized exactly | **Done.** It at least doubles. Pinned by `Drain_DecodedValuesThatKeepGrowing_AllocateBuffersThatDouble`. | 64 values growing from 5 to 68 bytes: 64 buffers | 5 buffers |
| A member named like another member's derived type | **Done.** `XmlObjectBinder.ValidateElementNames` throws `InvalidOperationException` when the type is bound, using the case rule the options ask for. | first or expected-next member won | rejected |
| `XmlReferenceScope` is an `AsyncLocal` | **Measured, not changed.** A thread-static field moved a Preserve write of 200 customers from 134 to 127 µs, inside the noise, against 50 µs with no handler. The cost is the resolver: a dictionary lookup, an id string, and a namespaced attribute per object. | | |
| Attribute reads (1.36 of STJ) | **Measured, not changed.** Reading the first eight bytes of an attribute value one at a time, as `ReadText` does for text, was slower (155 to 164 µs for `ReadReadings`): the plain-value search already stops at the closing quote. A hand-written loop over the reader is level with the serializer here, so what is left is the start-tag parser. | | |
| `Guid` and `double` parsing | **Measured, not changed.** Parsing alone is 11 ns for a GUID and 17 ns for a double with `Utf8Parser`, and 9 and 16 ns with the type's own UTF-8 `TryParse`. There are 2 ns to gain. | | |
| Text with a reference | **Measured, not changed.** One `&amp;` in a 35-byte string costs the reader 31 ns (a drain goes from 25 to 56 ns per element) and the serializer 16 ns more for the two-token path. The reader validates the reference in `ReadText` and decodes it again in `DecodeValueSpan`, with about eleven vectorized searches between them. Decoding in one pass is the fix, and it is a rewrite of the reader's most sensitive loop, so it is its own piece of work. | | |
| Boxing in constructor-created types | **Not done.** Removing it needs a typed constructor delegate, which means generated code (PR 7) or `System.Reflection.Emit`, and the second is ruled out for AOT. | | |

#### Fourth pass: the leftovers

Three items were left after the third pass. Times are direct loops against the commit before this pass, best of 15 rounds.

| Item | What changed | Before | After |
| --- | --- | --- | --- |
| Text with a reference was scanned twice | `ReadText` builds the value in the decode buffer as it scans. At the first reference or replaced line end it looks for the `<` that ends the token (one search for one byte), sizes the buffer for the whole token, and from then on copies each run and each replacement as it meets them. `DecodeValueSpan` no longer handles references and is left with CDATA, comments, and processing instructions. | one `&amp;` in 35 bytes: 47.7 ns per element; CR LF: 53.3 ns; eight references: 192 ns | 35.6 ns; 36.7 ns; 115 ns |
| The reader allocated on real documents | Measured first: every byte was a decode buffer or a stack that had outgrown its eight inline slots (the OpenDocument file has 38 attributes and 36 namespaces on one tag). A reader can be given a scratch buffer (two new constructors) and takes that memory from it before it allocates. `XmlDocument.Parse` passes the unused end of the buffer it rents; `XmlSerializer.Deserialize` passes 512 bytes of stack. | drain: 1440 / 6952 / 944 / 80 B (RSS, OpenDocument, WordprocessingML, RPM); DOM parse: 2720 / 7056 / 1048 / 184 B; `Deserialize<string>` with a reference: 152 B | drain with a 16 KB buffer: 0 B on all four, same time; DOM parse: 105 / 105 / 104 / 104 B in the benchmark, which is the document object; 88 B, the string |
| Constructor-created types boxed their value types | The values array is kept by the type's binding between objects (one reader at a time; a second thread or a nested object of the same type gets a new one). A value-type member holds its value in a box that stays in that array and is written through `Unsafe.Unbox`, because the constructor copies the value out. | 1,000 `Member(string, int, string, bool)`: 249,368 B; 1,000 records of three numbers: 176,600 B | 145,368 B (STJ: 240,272 B); 56,600 B (STJ: 161,072 B). Time unchanged. |

- **Raising the inline slots from 8 to 32 was tried and reverted.** It made the reader about 1 KB larger: `Deserialize<int>` went from 50.3 to 58.5 ns and the tiny drain from 56.7 to 61.0 ns, and it still did not cover the OpenDocument file.
- **A stack buffer in the reader's own frame costs 18 ns.** The reader is copied when it is constructed there. Allocated one frame up and passed down it costs nothing, which is how `XmlSerializer.Read` does it.
- **The DOM clears what the reader wrote in the rented buffer only when it wrote something.** Clearing unconditionally, and with a `finally`, made the tiny parse 113 to 130 ns in a direct loop. Guarded, it is 113 ns again.
- **A box is reused only where nothing can keep it.** The member must be a value type other than `Nullable<T>`, must not be a collection (its slot holds the builder), and every constructor parameter it fills must be of exactly its type. A parameter typed as `object` or an interface receives the box itself and may keep it, so such a member is boxed afresh each time (`KeptArgument_ConstructorKeepsWhatItWasGiven_TheNextReadDoesNotChangeIt`).
- **What the pass found.** XML 1.1 section 2.11 makes CR followed by NEL one line end. The reader produced two, in text, attribute values, CDATA, comments, and processing instructions, and counted two lines. NEL and LS alone in XML 1.1 text were treated as characters when deciding whether text was white space, although they are line ends by then. Both are fixed and pinned in `Utf8XmlReaderLineEndTests`.
- **Generated converters.** They could not create a type through a constructor with parameters (`new T()` was all they emitted), so a positional record in a context did not compile. They now choose the constructor by the reflection resolver's rules and call it with the typed locals they already read into, so there is no values array and no box (`GeneratedContext_RecordOfValueTypes_IsReadAllocatingOnlyTheRecord`). That test also showed 256 B per `long`, `double`, or `bool` member read: only `int`, `decimal`, and `string` had a direct path, and every other type asked the resolver for a new type info per value. The generated context now keeps the type info per type and options instance (`__GetInfo<T>`).
- **What is still allocated.** A raw reader with no scratch buffer allocates as before. A DOM parse of the RSS file (198 KB) averages 105 B in the benchmark, but a single parse can allocate a few kilobytes: its buffers are larger than the shared pool keeps per thread, so renting them sometimes allocates in the pool. That was so before this pass and is why `Parse_RealDocument_AllocatesOnlyTheDocument` covers the other three fixtures and not that one.
- Pinned by `Drain_RealDocumentWithScratch_AllocatesNothing`, `Parse_RealDocument_AllocatesOnlyTheDocument`, `Deserialize_StringThatNeedsDecoding_AllocatesOnlyTheString`, `Deserialize_RecordOfValueTypes_AllocatesOnlyTheRecord`, and `Deserialize_RecordWithAString_AllocatesOnlyTheRecordAndItsString`.

#### Fifth pass: a last look

The full suite was run on the checked-in code, the rows that looked different were rerun, and one change was made.

- **Nothing had regressed.** `DomParseCatalog` read 30.1 µs in the full run against 26.8 µs in the table; two reruns of that class gave 27.4 and 27.2 µs (0.53 of BCL both times). `CatalogDrain` read 20.2 µs with an error of 2.4 µs and 17.8 and 17.3 µs when rerun. Every other row was within the noise this machine has.
- **`ReadSequence` allocates 9672 B, 120 B more than the table above.** The reader state now carries each open element's prefix, so that an end tag in a later buffer can be checked against the prefix its start tag used: one array when the state is captured and one each time a reader resumes from it. This is per call, not per value, and it was attributed by reading the code, not by a probe.
- **A dictionary entry with a string key and a scalar value is written as a leaf element** (`XmlDictionaryConverter.WriteLeafEntry`), as a scalar member of an object already was. The key has just been shown to be an ASCII NCName, so the two tags are built on the stack from it and no element frame is pushed. It applies to keys of up to 32 characters; a longer key, a null value, a value that is not a built-in scalar, or a writer that is not inside an element takes the path it took before. `WriteDictionary` went from 2337 to 1658 ns in the benchmark (the two `WriteDictionary` rows above are from that later run, in which STJ was also faster than in the first; the other rows in the table were within noise and were not regenerated). In a direct loop, 100 `int` entries went from 2285 to 1651 ns and 100 `string` entries from 2633 to 1899 ns, 0 B before and after. The output is the same byte for byte, pinned by `Dictionary_KeysOfAnyLength_RoundTrip`, `Dictionary_Indented_PutsEachEntryOnItsOwnLine`, `Dictionary_NullValue_IsWrittenAsAnEmptyElement`, `Dictionary_EmptyValues_AreWrittenAsAStartAndAnEndTag`, `Dictionary_ObjectValues_AreWrittenUnderTheirKeys`, and `Dictionary_ConverterCalledAtDocumentLevel_WritesTheEntryAsTheDocumentElement`.
- **Looked at and left.** Enum and `DateTime` reads are 4 and 9 ns over `int` in the scalar table, which is the cost of comparing a name and of parsing twenty characters. The remaining distance to STJ on `WriteOrders`, `WriteMembers`, and `ReadReadings` is the per-element and per-attribute cost the earlier passes measured.

#### What the benchmarks and tests found

The object benchmarks were the first code to use several shapes, and the coverage gate then showed that many branches had only ever been reached by accident. Both turned up defects in PR 6 as first written; each now has a test in `XmlSerializerObjectMappingTests`.

- A list of a polymorphic type could not hold an item of the declared type itself: writing it threw `NotSupportedException`. It is written under the member name.
- A member declared as `IDictionary<TKey, TValue>` threw at bind time.
- A dictionary entry whose key is `Key` was written as `<Key>` and read back as the start of a pair. It is written in the pair form.
- A child of a mixed-content element named like the mixed member was read as an object instead of kept as an element.
- A type created through a constructor never registered its `id`, so a later `ref` to it failed.
- An exception thrown by a constructor arrived wrapped in `TargetInvocationException`. It is thrown as it is.
- An array root was named `Int32Array` while `SerializeAsyncEnumerable<int>` wrote `ArrayOfInt32`, so one could not read the other's document. Both are `ArrayOfInt` since [decision 70](24-doc-findings-resolution.md).

## Shipped after PR 4 (reader drain)

| Technique | Where BCL does it | What we did |
| --- | --- | --- |
| End tag equals start tag | `XmlTextReaderImpl` compares interned name references | `TryMatchStartTagName` compares the end tag to the start tag's qualified-name bytes in one `SequenceEqual`. A hit settles local name and prefix together: no name scan, no second local compare, no prefix compare, no namespace lookup. `TryMatchEndName` / `ReadName` remain for a different prefix on the same URI and for resumed elements. `EndPrefixMatchesStart` is gone. |
| Scope pop without copying | — | `PopNamespaceScope` reads the top binding's depth in place (`BindingDepthAt`) and returns on the first mismatch. Was two 40-byte `NamespaceBinding` copies per end tag. |
| Decode only when something needs decoding | `XmlTextReaderImpl.ParseText` tracks entity / EOL state while scanning | `ReadText` already stops at `&`, CR, and XML 1.1 line ends. It now remembers whether it hit one, and skips `DecodeValueSpan` (two more scans) when it did not. |
| One scan for attribute normalization | — | `DecodeAttributeValue` tests `&`, TAB, LF, CR with one `SearchValues` scan. Was four `IndexOf` calls per attribute. |

None of these touch the namespace-binding representation, so the interned-prefix idea below was not needed to get under 1.00.

## Shipped with PRs 2–3

- Writer: names of open elements on frames (as strings then; as UTF-8 in the name buffer since the final pass), complete leaf elements, `Ascii.FromUtf16` / `WriteRawByte`, `IBufferWriter.GetMemory`/`Advance`, `TryFormat` numbers, static newlines, `Reset` → 0 B, catalog write 0.76× BCL.
- Reader: binding-slot URI copy, 1-byte prefix lookup + last-prefix cache, matching end tags skip URI re-resolve. Namespaced drain 1.57× → 1.15× BCL, still 0 B.
- Coverage 100% line. Writer/reader conceptual docs and tokenizer benches.

## Shipped with PR 4 (DOM)

| Technique | Where STJ / BCL does it | What we did |
| --- | --- | --- |
| Rows in document order, no links | `JsonDocument.MetadataDb` / `DbRow` (12 bytes, `NumberOfRows`) | `XmlDbRow` is 32 bytes (was 68). Attributes follow their element, children follow the attributes, `EndIndex` skips a subtree. No first/last/next/parent indexes to maintain. |
| Offsets into an owned payload | `JsonDocument` keeps the UTF-8 and stores `Location` | `XmlDocument` owns one pooled copy of the payload. `Locate` records a tokenizer span by address; only entity-decoded values and interned URIs are appended to the extra buffer. Was 3–4 `AppendExtra` copies per node. |
| Pre-sized rows | `MetadataDb.CreateRented` (one token per 12 bytes) | Same estimate. Rows hold no content, so they are returned uncleared; content buffers are cleared on `Dispose`. |
| One pass per start tag | — | `xsi:nil` is detected while the attributes are captured (was a second walk through `GetNil`). Repeated namespace URIs reuse the previous copy. |
| UTF-8 vs UTF-16 name compare | `JsonElement.TextEquals` | `Utf8Equals`: equal lengths can only match as ASCII (`Ascii.Equals`), a shorter UTF-8 form never matches, the rest transcodes on the stack. Was a `string` per sibling scanned. |
| Typed getters on the bytes | `JsonElement.GetInt32` | A single text child is read in place. Mixed content is every text row in the element's row range, concatenated into a pooled scratch. Was `StringBuilder` → `string` → `byte[]`. |
| Leaf elements in one reservation | (the writer already did this for string names) | `WriteCompleteElementUtf8` writes `<n>v</n>` with one `Ensure` and no frame when the text needs no escaping and the name needs no new declaration. `WriteElementString(ReadOnlySpan<byte>, ReadOnlySpan<byte>)` uses it too. |
| One scan to decide "copy verbatim" | `XmlUtf8RawTextWriter` | `AsciiTextWriteChars` / `AsciiAttributeWriteChars`. Element text was two scans; attribute text had no fast path. |
| Namespace bindings without new arrays | — | `PushNs` reuses the popped slot's arrays when the bytes match, so a namespaced `WriteTo` after `Reset` is 0 B (was 72 B per declaration). |

0 B is asserted by `XmlDocumentAllocationTests` for navigation, enumeration, name lookup (ASCII and non-ASCII), the numeric / Boolean / date / GUID getters, and `WriteTo` (plain and namespaced). Members that return `string`, `byte[]`, or an `XmlNode` allocate that result.

### Standards work after the optimization (and what it cost)

Making the reader, writer, and DOM follow the specifications ([XML fidelity](02-xml-fidelity.md), [testing](14-testing.md)) added work to hot paths. Each addition was kept to a store or a branch, and the tables above are the result after all of it:

| Change | Hot-path cost | Kept cheap by |
| --- | --- | --- |
| `]]>` in text is an error | `>` is a text stop | No extra scan: it joins the existing `SearchValues` stop set, and only a `>` checks the two bytes before it. |
| Whitespace significance (mixed content, whitespace-only leaf, `xml:space`) | One flag write per start tag and per text token | Flags share the element-stack slot that already holds the prefix length. |
| Writer escapes TAB / LF / CR / NEL / LS so values round-trip | None on clean values | The verbatim-copy sets (`AsciiTextWriteChars`, `AsciiAttributeWriteChars`, `PlainTextChars`, `PlainAttributeChars`) are allow-lists; one `IndexOfAnyExcept` decides. The escaper now copies clean runs instead of walking rune by rune. Since decision 54 of [doc findings](24-doc-findings-resolution.md) the two text sets are chosen for each writer: when `NewLine` is CR LF, LF is left out of them (`AsciiTextWriteCharsNoLineFeed`, `PlainTextCharsNoLineFeed`), so text with a line break takes the escaping path. Not measured. |
| DOM writes namespace declarations as parsed | Two passes over an element's attribute rows | Leaf elements still take the one-reservation path; `RegisterNamespace` / `WriteRegisteredNamespace` split validation from output. |
| Typed getters share `XmlLexical` (XSD rules) | Was 1.11× on `DomNavigate` when first routed through it | A digits-only fast path in `TryParseInteger`; the nil check folded into the value lookup. Back to 0.83. |
| Escaped attribute values | Was one array per escaped attribute | One buffer per reader, reused from tag to tag. |

Bugs that surfaced while proving the above, all fixed with tests. The first two came from the optimization pass; the rest from the W3C suite, real documents, split-at-every-byte reads, exact-match assertions, and round-trip properties:

- `Utf8XmlReader`: an element in no namespace reported the namespace of the previous element at the same depth once that element's bindings were popped (the per-element slot was only written while a namespace was in scope). `_elementNsStored` keeps the unprefixed fast path for documents that never use a namespace.
- `XmlDocument`: a non-empty `xsi:nil` element was not pushed as a parent, so its end tag closed its parent instead.
- Reader, resume: after a resumed element closed, the next element at that depth read the closed element's saved name and namespace ("End tag does not match start tag").
- Reader, resume: a multi-byte character split across buffers inside a name, comment, PI, or CDATA was reported as invalid UTF-8.
- Reader: accepted names starting with a colon, a colon in a PI target, a PI target glued to its data, `XML` / `Xml` as a PI target, the `xmlns` prefix on an element, prefixes bound to the reserved `xml` / `xmlns` namespaces, a literal `]]>` in text, CDATA outside the root element, and `&#X…;`.
- Reader: the XML declaration was matched by substring search, so `<?xml garbage?>` passed and `version` was optional.
- Reader, default options: dropped whitespace between inline elements in mixed content (`a <b>b</b> <i>c</i>`) and the content of a whitespace-only leaf (`<c> </c>`); ignored `xml:space`.
- Reader: `LocalNameSpan` on a text, comment, or CDATA token returned the previous token's name.
- Writer: TAB / LF / CR in attribute values and CR in text were written raw and came back changed; control characters in a `string` value were written raw; `Indented` put whitespace inside text-only elements; comments, CDATA, and PIs were not checked for illegal characters; the `xmlns` element prefix and reserved-namespace declarations were accepted.
- DOM `WriteTo`: dropped namespace declarations that were only referenced from content (`xsi:type="p:T"`), dropped `xmlns=""`, reordered attributes, wrote `<e></e>` as `<e/>`, and dropped an attribute named `p:xmlns`.
- DOM getters: parsed values on their own and disagreed with the reader (whitespace, `+`, dates).
- Typed values: `DateTime` accepted any culture-invariant date (`March 5, 2020`); durations accepted `PT1D`.

Coverage is 100% line and 100% branch (7288 lines, 4590 branches after PR 6; 5786 and 3376 after PR 5; 4427 and 2890 after PR 4), with no test that exists only to reach a branch. `coverage-gate.cs` now fails below 100% branch coverage (it used to allow 80%).

## Backlog (do these next)

Work the list in order. Keep 0 B on drain/write. Do not grow the public API unless the bench cannot express the fast path (writer already has `WriteAttributeNumber` / complete elements).

### 1. Prefix resolution on the reader (done: real documents 0.71 to 0.87)

Real namespace-heavy documents were 1.27× and 1.79× BCL on drain. A sampling profile (`dotnet-trace`, the drain loop on each fixture) showed four separate costs, and only the first was the one this item predicted:

| Cost | What it was | What replaced it |
| --- | --- | --- |
| Prefix lookup | A 1-byte last-prefix cache that multi-byte prefixes never hit, then a scan of every in-scope binding comparing bytes, for each prefixed element and attribute | `LookupPrefixIndex`: an 8-slot direct-mapped cache from prefix to binding index, emptied when a binding is pushed or popped. The default namespace has its own slot (`_nsDefaultIndex`). No interning and no new arrays. |
| Attributes resolved twice or more | The duplicate check resolved each prefixed attribute, and `NamespaceUriSpan` resolved it again | `ResolveAttributeNamespaces` runs once per start tag and stores the binding index on `AttributeRecord.NsIndex`. The duplicate check compares indexes before it compares URIs. |
| Multi-byte text rescanned per character | After each non-ASCII character `ReadText` searched for the next `<` again, then decoded and checked one rune | `AdvanceAsciiTextRun` checks the whole run at once: allowed bytes (`TextRunChars`), `Utf8.IsValid`, and no U+FFFE / U+FFFF. A run that fails is left to `AdvanceChar`, which reports the exact error. This was the `.fodt` gap (it holds Markus Kuhn's UTF-8 demo text). |
| Start-tag overhead | Whitespace between attributes went through the full character validator; each attribute value was scanned four times (stop search, character check, whitespace check, decode-trigger check); names used a vectorized search for five-byte inputs | `SkipWhitespaceBytes` advances over space and tab directly. One `AttrPlainDouble` / `AttrPlainSingle` scan proves a value needs no check, no reference expansion, and no normalization. `ReadName` uses a byte loop. This was the WordprocessingML gap. |

The same lookup cost was on the writer (`LookupUri` scanned every binding for each prefixed name) and is fixed the same way (`_nsCacheIndexes`): `RealDomWrite` on the `.fodt` went from 1.22 to 0.83. `WriteElementText` / `WriteAttributeText` copy a valid multi-byte run in one piece (`WriteMultiByteRun`).

Not done: interned prefix handles on `NamespaceBinding`. The cache made them unnecessary.

### 2. Unprefixed catalog drain (done, 0.88×)

Closed by the end-tag and text work in "Shipped after PR 4". Remaining candidates if more is wanted: `ReadName` runs two vector scans for a prefixed name, and `AdvanceAsciiTextRun` re-scans the run that `IndexOfAny` just delimited. Do not add xmlns work to the unprefixed path.

### 3. Writer: interned NCNames for UTF-8 / span APIs

**Closed by the final pass, another way.** Every start tag puts its name in the name buffer, and the buffer goes back to where it was when the element ends, so it holds the names of the open elements and nothing else. An intern table is not needed.

### 4. Writer: `XmlEncodedText` in a dedicated bench (done: `EncodedNames` 19.3 µs against 20.8 µs for string names)

STJ benches often use `JsonEncodedText` for repeated property names. Add a category that writes the catalog with `XmlEncodedText.Encode("item")` etc., **in addition to** the string catalog. Do not replace the string catalog; that is the BCL-fair column.

### 5. Reader `Reset` / reuse

**Writer `Reset` shipped in PR 3** (benches reuse one writer at 0 B). `Utf8XmlReader` is a `ref struct`; reuse is “construct again on the same span,” which is already cheap. If a future mutable reader or sequence reader appears, give it `Reset` and a bench that reuses it.

**Done: per-reader allocations on real documents.** The split was measured (decode buffers, and stacks past their inline slots), and the reader takes that memory from a caller-supplied scratch buffer. See "Fourth pass: the leftovers".

### 6. DOM and serializer

**DOM shipped in PR 4** (`XmlDocumentBenchmarks`, table above). DOM parse time is now mostly the tokenizer (about 21 of 31 µs on the catalog), so any further reader work is also the next DOM parse win. The mutable `Nodes` tree has a bench that has not been run (`XmlNodeBenchmarks`); `XmlNode.Parse` goes through `XmlDocument`, keeps the parsed rows, and makes the objects of an element when it is first reached into (see "What was changed for the new rows").

**Scalar serializer benches shipped with PR 5** (`XmlSerializerBenchmarks`, table above): 0.05 to 0.25 of BCL. Open there:

- **Small elements, what is left.** The serializer now reads `<Int32>1</Int32>` in about 21 ns and writes it in about 8 ns as a leaf; through `Read` and `WriteStartElement` the reader and writer still spend about 20 and 12 ns on the tokens alone. The asynchronous iterator (about 11 ns per value) is now the largest single part of both sequence rows, and it is the price of `IAsyncEnumerable<T>`; STJ pays it too.
- **The reader is still 1064 bytes and construction is 17 ns.** What remains of construction is the caller clearing the local and the two stacks that must be cleared. Making reader state a value instead of a heap snapshot (as `JsonReaderState` is) would make resuming allocation-free; today `CurrentState` rebuilds every name and namespace as strings and costs about 49 ns.
- **A converter that cannot resume pays two reader copies per value when streamed** (the read-ahead). The built-in value converters do not. For object graphs (PR 6) decide whether converters become resumable, as STJ made them.
- **`ReadText` still validates multi-byte and entity-heavy text in small steps.** `sample_rss.xml` is under BCL now, but only CDATA and comments got the plain-run skip; check an entity-dense document with tiering off.

**Object benches shipped with PR 6** (`XmlSerializerObjectBenchmarks`, table above): 0.22 to 0.58 of BCL reading and 0.26 to 0.76 writing. Open there, largest first:

- **Done: text with a reference is decoded in one pass** (47.7 to 35.6 ns per element). See the fourth pass.
- **Done: constructor-created types no longer box their value types** on the reflection path, and generated converters call the constructor with typed locals and hold no array at all. See the fourth pass.
- **Attributes are the widest gap to STJ.** What is left is in the start-tag parser, which records, resolves, and checks each attribute. A short scalar scan of the value was tried and was slower.
- **Parsing is at the framework's floor for `Guid` and `double`** (11 and 17 ns of `Utf8Parser`). `DateTime` and enums are 13 and 10 ns over `int` in our own code and have not been profiled.
- **Preserve is 2.6 times a plain write** (134 against 50 µs for 200 customers). Each object is looked up twice, once by the member that holds it and once by its own converter; one lookup would do.

The remaining object-graph columns (first call, source-generated context) come with PR 7 and PR 9. Follow [15](15-benchmarks.md): cache BCL `XmlSerializer`, same object graph, STJ as sidecar. Apply this same process (probe allocs, official BCL source, coverage, table).

## Files that matter

| Area | Files |
| --- | --- |
| Writer hot path | `Utf8XmlWriter.cs`, `XmlWriterFrame.cs`, `XmlWriterOptions.cs` |
| Reader hot path | `Utf8XmlReader.cs`, `Utf8XmlReader.Parsing.cs`, `Utf8XmlReader.Namespaces.cs`, `NamespaceBinding.cs` |
| DOM hot path | `XmlDocument.cs`, `XmlElement.cs`, `XmlDbRow.cs` |
| Typed values | `XmlLexical.cs`, `XmlDeclarationSyntax.cs` |
| Serializer hot path | `XmlSerializer.cs`, `Serialization/XmlTypeInfoOfT.cs`, `Serialization/XmlValueConverterOfT.cs`, `Serialization/XmlConverterHelpers.cs`, `Serialization/XmlWriterCache.cs`, `Serialization/XmlEnumConverterOfT.cs`, `XmlStreamBuffer.cs`, `Serialization/XmlObjectConverterOfT.cs`, `Serialization/XmlBoundPropertyOfT.cs`, `Serialization/XmlCollectionBindingOfT.cs`, `Serialization/XmlDictionaryConverterOfT.cs`, `Serialization/XmlPolymorphism.cs` |
| Benches | `Utf8XmlWriterBenchmarks.cs`, `Utf8XmlReaderBenchmarks.cs`, `XmlDocumentBenchmarks.cs`, `RealDocumentBenchmarks.cs`, `XmlSerializerBenchmarks.cs`, `XmlSerializerSequenceBenchmarks.cs`, `XmlSerializerObjectBenchmarks.cs`, `XmlSerializerGraphBenchmarks.cs`, `Utf8XmlWriterStyleBenchmarks.cs`, `Utf8XmlReaderFlowBenchmarks.cs`, `XmlNodeBenchmarks.cs`, `XmlSerializerFeatureBenchmarks.cs`, `XmlSerializerSourceGenBenchmarks.cs`, `XmlSerializerObjectSequenceBenchmarks.cs`, `Models/`, `Program.cs` |
| Allocations | `Utf8XmlWriterAllocationTests.cs`, `Utf8XmlReaderAllocationTests.cs`, `XmlDocumentAllocationTests.cs`, `XmlSerializerAllocationTests.cs` |
| Coverage | `src/coverage-gate.cs`, `artifacts/coverage/coverage.cobertura.xml` |

## What not to do

- Triple-scan ASCII names (`IsValid` + `GetBytes` + copy). One pass.
- `Encoding.UTF8.GetBytes` on every name or newline in the ctor.
- `ToString("G17")` for doubles on the writer hot path.
- Growing a 16 KB staging buffer when the destination is `IBufferWriter<byte>`.
- Claiming 0 B because ArrayPool is warm without a `GetAllocatedBytesForCurrentThread` test after `Reset`/drain warmup.
- Reporting benches as `~30µs / 0.76×`. Use the table.
- Decompiling `System.Private.Xml`. Use the GitHub tag.

## After the doc findings (2026-11-24, net11.0 ShortRun, Hyper-V)

The tables above are from before the [doc findings resolution](24-doc-findings-resolution.md). The first full run after it showed the serializer's write side 10 to 30% slower, and six new benchmark classes showed problems the old rows never reached. This section records what was found, what was changed, and the numbers of the last run. Cells are time / allocated bytes for XmlEssentials; the last column is the ratio to the BCL in the last run. A ShortRun moves by a few percent from run to run, and so do the BCL rows.

### What had slowed down, and why

| Cause (found with `dotnet-trace` against the commit before the work) | Fix |
| --- | --- |
| The writer's set of plain text characters became a field, chosen from `NewLine`. A `SearchValues` in a field is searched through a virtual call; a static one is inlined. Every run of element text paid it. | `IndexOfEscapedText` branches on `_newLineIsCrLf` and searches a static set in each branch. |
| `XmlSerializerOptions.ToWriterOptions` and `ToReaderOptions` built a struct through setters that now validate, on every call. | Both are copied once, when the options are read-only. |
| `SerializeAsyncEnumerable` found the name of the array element through attribute reflection on every call, and each value went through a reference scope held in an `AsyncLocal`. | The name is kept on the type info. The scope is a thread-static, and is not touched at all with no reference handler. |
| A polymorphic item went through `ResolveWrite(ref object)`, and `XmlDerivedBinding` was a struct copied for each item. | A value that names its element has a path of its own (`WriteNamed`); the binding is a class. |
| On Windows every line break in text is now written as CR LF (decision 54), which took the escaping path. | Text whose only special characters are line breaks is copied run by run (`TryWriteLinesAsCrLf`). What is left of this cost is the work the setting asks for. |

### Rows that existed before

| Test | Before (2026-11-23) | First run after | Last run | XE ÷ BCL |
| --- | --- | --- | --- | --- |
| CatalogWrite | 23,343 ns / 0 B | 22,753 ns / 0 B | 22,279 ns / 0 B | 0.68 |
| TinyWrite | 55 ns / 0 B | 59 ns / 0 B | 59 ns / 0 B | 0.18 |
| CatalogDrain | 18,737 ns / 0 B | 20,826 ns / 0 B | 20,510 ns / 0 B | 0.79 |
| CatalogGetInt32 | 19,168 ns / 0 B | 19,881 ns / 0 B | 20,696 ns / 0 B | 0.69 |
| NamespacedDrain | 15,507 ns / 0 B | 16,539 ns / 0 B | 15,644 ns / 0 B | 0.75 |
| TinyDrain | 69 ns / 0 B | 67 ns / 0 B | 71 ns / 0 B | 0.23 |
| RealDrain, document.xml | 14,700 ns / 944 B | 14,450 ns / 944 B | 14,600 ns / 944 B | 0.78 |
| RealDrain, .fodt | 50,830 ns / 6,952 B | 49,140 ns / 6,952 B | 49,520 ns / 6,952 B | 0.63 |
| RealDrain, rpm_primary.xml | 20,790 ns / 80 B | 17,370 ns / 80 B | 17,500 ns / 80 B | 0.57 |
| RealDrain, sample_rss.xml | 83,850 ns / 1,441 B | 81,850 ns / 1,441 B | 82,200 ns / 1,441 B | 0.45 |
| RealDomParse, document.xml | 21,180 ns / 104 B | 25,380 ns / 104 B | 21,580 ns / 104 B | 0.45 |
| RealDomParse, .fodt | 68,560 ns / 105 B | 66,700 ns / 105 B | 70,260 ns / 104 B | 0.39 |
| RealDomParse, rpm_primary.xml | 25,310 ns / 104 B | 27,780 ns / 104 B | 25,290 ns / 104 B | 0.37 |
| RealDomParse, sample_rss.xml | 112,880 ns / 105 B | 114,440 ns / 105 B | 114,210 ns / 105 B | 0.36 |
| RealDomWrite, document.xml | 22,200 ns / 0 B | 21,950 ns / 0 B | 21,770 ns / 0 B | 0.76 |
| RealDomWrite, .fodt | 71,450 ns / 0 B | 73,430 ns / 0 B | 72,440 ns / 0 B | 0.76 |
| RealDomWrite, rpm_primary.xml | 21,270 ns / 0 B | 24,060 ns / 0 B | 23,920 ns / 0 B | 0.75 |
| RealDomWrite, sample_rss.xml | 80,340 ns / 0 B | 91,780 ns / 0 B | 93,240 ns / 0 B | 0.64 |
| DomNavigate | 2,801 ns / 0 B | 2,817 ns / 0 B | 2,894 ns / 0 B | 0.83 |
| DomParseCatalog | 26,826 ns / 104 B | 28,466 ns / 104 B | 28,912 ns / 104 B | 0.55 |
| DomParseNamespaced | 21,709 ns / 104 B | 22,059 ns / 104 B | 22,110 ns / 104 B | 0.53 |
| DomParseTiny | 120 ns / 104 B | 124 ns / 104 B | 117 ns / 104 B | 0.19 |
| DomWrite | 15,628 ns / 0 B | 16,196 ns / 0 B | 16,044 ns / 0 B | 0.58 |
| WriteDateTime | 68 ns / 104 B | 78 ns / 104 B | 72 ns / 104 B | 0.21 |
| WriteEnum | 64 ns / 96 B | 77 ns / 96 B | 66 ns / 96 B | 0.18 |
| WriteInt32 | 56 ns / 88 B | 69 ns / 80 B | 62 ns / 80 B | 0.19 |
| WriteInt32Buffer | 48 ns / 0 B | 52 ns / 0 B | 49 ns / 0 B | 0.15 |
| WriteInt32String | 65 ns / 144 B | 91 ns / 136 B | 68 ns / 136 B | 0.20 |
| WriteNullable | 78 ns / 144 B | 103 ns / 144 B | 82 ns / 144 B | 0.14 |
| WriteString | 90 ns / 128 B | 114 ns / 128 B | 95 ns / 128 B | 0.28 |
| ReadDateTime | 71 ns / 0 B | 66 ns / 0 B | 65 ns / 0 B | 0.06 |
| ReadEnum | 59 ns / 0 B | 62 ns / 0 B | 62 ns / 0 B | 0.08 |
| ReadInt32 | 55 ns / 0 B | 56 ns / 0 B | 55 ns / 0 B | 0.05 |
| ReadInt32Stream | 68 ns / 0 B | 78 ns / 0 B | 73 ns / 0 B | 0.07 |
| ReadString | 122 ns / 96 B | 124 ns / 96 B | 123 ns / 96 B | 0.10 |
| ReadSequence | 31,877 ns / 9,552 B | 40,144 ns / 9,432 B | 36,324 ns / 9,440 B | 0.87 |
| WriteSequence | 21,519 ns / 928 B | 28,309 ns / 1,432 B | 21,344 ns / 936 B | 0.66 |
| ReadCustomer | 3,368 ns / 3,168 B | 3,622 ns / 3,168 B | 3,597 ns / 3,168 B | 0.42 |
| ReadDictionary | 4,053 ns / 13,392 B | 4,444 ns / 13,392 B | 4,184 ns / 13,392 B | |
| ReadDrawing | 114,728 ns / 90,793 B | 138,864 ns / 90,794 B | 123,837 ns / 90,794 B | 0.58 |
| ReadMembers | 170,462 ns / 143,802 B | 186,086 ns / 143,802 B | 183,875 ns / 143,802 B | |
| ReadMoney | 91,606 ns / 88,601 B | 102,031 ns / 88,601 B | 98,102 ns / 88,601 B | 0.37 |
| ReadOrder | 457 ns / 232 B | 468 ns / 232 B | 484 ns / 232 B | 0.23 |
| ReadOrders | 433,455 ns / 248,603 B | 438,252 ns / 248,604 B | 426,870 ns / 248,600 B | 0.56 |
| ReadReadings | 201,662 ns / 104,602 B | 192,565 ns / 104,602 B | 189,728 ns / 104,602 B | 0.55 |
| WriteCustomer | 1,681 ns / 0 B | 1,874 ns / 0 B | 1,725 ns / 0 B | 0.41 |
| WriteDictionary | 1,658 ns / 0 B | 1,720 ns / 0 B | 1,662 ns / 0 B | |
| WriteDrawing | 69,506 ns / 0 B | 90,589 ns / 0 B | 69,309 ns / 0 B | 0.53 |
| WriteMembers | 68,674 ns / 0 B | 73,971 ns / 0 B | 66,431 ns / 0 B | |
| WriteMoney | 63,553 ns / 0 B | 61,166 ns / 0 B | 59,243 ns / 0 B | 0.72 |
| WriteOrder | 266 ns / 0 B | 302 ns / 0 B | 268 ns / 0 B | 0.26 |
| WriteOrders | 234,705 ns / 0 B | 256,569 ns / 0 B | 246,862 ns / 0 B | 0.59 |
| WriteReadings | 132,853 ns / 0 B | 144,602 ns / 0 B | 135,312 ns / 0 B | 0.66 |

Every row is under the BCL, and every allocation is at or under what it was. Still above the 2026-11-23 time by more than a ShortRun moves:

- **RealDomWrite of `sample_rss.xml` (+16%) and `rpm_primary.xml` (+12%).** Both hold many text nodes with line breaks, which are now written as CR LF on Windows. Against the commit before the work, on the same day, the rest of the DOM write is level.
- **The object reads `ReadDrawing`, `ReadMembers`, `ReadMoney`, `ReadCustomer` (+5 to +8%) and `ReadSequence` (+14%).** The read loops now keep the member being read for the path of an error (decision 61). `ReadChildren` was split so that the loop itself has no handler in it, which got part of it back. Not profiled further.
- **`CatalogDrain` and `DomParseCatalog` (+8 to +9%).** Run against the commit before the work on the same day, both are level (17.1 against 17.3 µs, 25.6 against 25.4 µs), so this is the machine and not the code.

### Rows that are new

| Test | First run | Last run | BCL | STJ |
| --- | --- | --- | --- | --- |
| WideElement1000 | 758,910 ns / 73,327 B | 24,360 ns / 73,320 B | 67,470 ns / 344,281 B | |
| WideElement5000 | 21,432,310 ns / 590,174 B | 152,030 ns / 590,000 B | 1,669,610 ns / 1,755,817 B | |
| ResumedDrain | 218,210 ns / 31,138 B | 222,190 ns / 31,138 B | 264,430 ns / 27,604 B | |
| Skip | 183,830 ns / 42 B | 183,120 ns / 42 B | 198,010 ns / 27,738 B | |
| TrySkip, resumed | 201,140 ns / 17,466 B | 201,330 ns / 17,466 B | 198,010 ns / 27,738 B | |
| IndentedWrite | 34,276 ns / 0 B | 31,572 ns / 0 B | 44,457 ns / 13,784 B | 14,239 ns / 0 B |
| NewLineText, CR LF | 129,174 ns / 753,602 B | 101,933 ns / 0 B | 308,617 ns / 7,332 B | |
| NewLineText, CR LF, XML 1.1 | 130,697 ns / 753,602 B | 104,294 ns / 0 B | 308,617 ns / 7,332 B | |
| NewLineText, LF | 25,879 ns / 0 B | 25,503 ns / 0 B | | |
| AttributesPerLine | 25,958 ns / 0 B | 27,921 ns / 0 B | 26,634 ns / 7,384 B | |
| RawValue, checked | 22,476 ns / 14,784 B | 20,291 ns / 0 B | 6,639 ns / 7,328 B | 9,443 ns / 0 B |
| RawValue, not checked | 90 ns / 0 B | 89 ns / 0 B | 6,639 ns / 7,328 B | 53 ns / 0 B |
| StringSegments | 8,530 ns / 0 B | 7,712 ns / 0 B | 30,569 ns / 7,328 B | 54,883 ns / 0 B |
| Base64Segments | 1,805 ns / 0 B | 1,815 ns / 0 B | 27,264 ns / 9,448 B | 1,786 ns / 0 B |
| EncodedNames | 25,994 ns / 0 B | 19,316 ns / 0 B | 32,085 ns / 13,728 B | 11,771 ns / 0 B |
| StringNames | 21,004 ns / 0 B | 20,838 ns / 0 B | 32,085 ns / 13,728 B | |
| ParseTree | 97,630 ns / 310,016 B | 38,140 ns / 87,542 B | 53,100 ns / 135,301 B | 17,700 ns / 48,937 B |
| ChangeTree | 172,530 ns / 458,895 B | 113,960 ns / 257,198 B | 117,090 ns / 195,727 B | 70,050 ns / 226,540 B |
| ReadFeed | 124,840 ns / 153,585 B | 118,934 ns / 153,584 B | 167,477 ns / 197,882 B | |
| WriteFeed | 72,915 ns / 0 B | 66,849 ns / 0 B | 84,445 ns / 15,009 B | |
| ReadCustomers | 260,137 ns / 266,668 B | 252,588 ns / 266,668 B | | 209,794 ns / 254,338 B |
| ReadCustomers, Preserve | 493,807 ns / 494,468 B | 462,219 ns / 494,396 B | | 353,439 ns / 385,516 B |
| WriteCustomers | 121,642 ns / 0 B | 114,994 ns / 0 B | | 106,568 ns / 313 B |
| WriteCustomers, IgnoreCycles | 150,556 ns / 434 B | 134,923 ns / 362 B | | 116,365 ns / 425 B |
| WriteCustomers, Preserve | 291,195 ns / 148,652 B | 239,599 ns / 148,578 B | | 165,221 ns / 150,666 B |
| ReadTaggedDrawing | 206,891 ns / 117,474 B | 184,018 ns / 90,794 B | | 144,870 ns / 117,914 B |
| WriteTaggedDrawing | 102,255 ns / 0 B | 89,730 ns / 0 B | | 81,183 ns / 313 B |
| ReadTypedDrawing | 280,776 ns / 122,818 B | 261,165 ns / 90,800 B | 236,895 ns / 228,570 B | |
| WriteTypedDrawing | 147,488 ns / 0 B | 117,460 ns / 0 B | 161,822 ns / 30,506 B | |
| ReadCustomer, Bcl preset | 2,415 ns / 1,320 B | 2,402 ns / 1,320 B | 4,632 ns / 10,704 B | |
| WriteCustomer, Bcl preset | 1,031 ns / 0 B | 972 ns / 0 B | 2,103 ns / 8,224 B | |
| ReadOrderSequence | 822,700 ns / 240,440 B | 482,400 ns / 246,620 B | | 445,100 ns / 235,220 B |
| WriteOrderSequence | 277,200 ns / 1,540 B | 254,100 ns / 940 B | | 233,600 ns / 690 B |
| Source generation, read (default) | 3,970 ns / 3,168 B | 3,458 ns / 3,168 B | | 2,874 ns / 3,576 B |
| Source generation, write (default) | 1,422 ns / 0 B | 1,450 ns / 0 B | | 852 ns / 0 B |

### What was changed for the new rows

- **Wide elements.** `CheckDuplicateNamespacedAttributes` compared every pair. A start tag with more than 16 attributes now puts each in a hash table by its local name (`CheckDuplicateAttributesByName`), on the stack up to 128 attributes and rented beyond.
- **Long escaped text.** `WriteStringValue` transcoded a whole string into an array before escaping it. It is now transcoded a part at a time into 1,024 bytes of stack (`WriteEscapedChars`), which never cuts a surrogate pair. `Reset_WriteLongEscapedText_DoesNotAllocate` pins 0 B.
- **Raw values.** The document a raw value is read in to check it is built in a pooled buffer the writer keeps. `Reset_WriteValidatedRawValue_DoesNotAllocate` pins 0 B. The time that is left is one pass of the reader over the value.
- **Encoded names.** The `XmlEncodedText` overloads of `WriteElementNumber` and `WriteElementString` write the element whole, as the string overloads do, and copy the name where a string name is transcoded.
- **Discriminators** are compared as the bytes the attribute holds, with bytes each derived binding keeps, as System.Text.Json compares `$type`. A known discriminator is never made into a string.
- **`id`, `ref` and `xsi:type`** are written from UTF-8 literals; the string overloads no longer clear 576 bytes of stack for each name; `XmlPreserveReferenceResolver.GetReference` is one look in its table.
- **Streamed objects are resumable**, the design System.Text.Json has ([serializer](07-api-serializer.md)). `XmlResumableReader` reads an object into the frames of an `XmlReadStack`, and when a block ends inside a value the frames stay and the read goes on in the next block: nothing is read twice, and no setter or constructor runs twice. It reads what `XmlObjectBinding.IsResumable` allows: objects of the built-in converter whose members are built-in scalars, such objects, or collections of either. Anything else (a polymorphic or populated member, mixed content, text, extension data, a dictionary, a converter of the caller's, a reference handler, top-level values) is read once all of it has arrived, which `Utf8XmlReader.IsElementInBlock` now finds out by counting tags where it read the value twice.
- **The mutable tree is made on demand**, as `JsonNode.Parse` makes it ([DOM](06-api-dom.md)). `XmlNode.Parse` and `ParseValue` keep the parsed document, out of the pool, and an element makes its attributes and children when they are first asked for. An element nothing has looked at is written and copied from the parsed rows. `XmlElement.CloneNode()` on a document the caller owns still copies everything, because that document may be disposed. Besides that: the node lists hold an array and not a `List<T>`, the attribute list is made when it is first asked for, and the names of a tree share their strings.

### What is still behind

- **`RawValue`, checked** is three times the BCL, which does not check, and twice System.Text.Json. Checking is reading; only a faster reader changes it.
- **`ChangeTree` allocates more than the BCL** (257 KB against 196 KB) while taking about the same time.
- **`Preserve`** writes at 2.1 times a plain write, where System.Text.Json pays 1.55 times. An id is a string for each object in both.
- **`ReadTypedDrawing`** is 1.10 times the BCL. The serializer declares the `xsi` prefix on every element that has `xsi:type`, where the BCL declares it once on the document element, so the document it reads is half as long again. Declaring it once is a change to what is written, and is not made here.
- **`ReadOrderSequence`** is 1.08 times System.Text.Json.

## Final pass (2026-11-25, net11.0, Hyper-V)

The goal of this pass was every row under the BCL and under System.Text.Json. The numbers below are from a direct loop over the benchmark methods (each method warmed, then five interleaved rounds, minimum taken), not from BenchmarkDotNet: the full suite was not run in this pass. It was run afterwards: see [Full run](#full-run-2026-11-25-net110-shortrun-hyper-v), which is the current standing. A direct loop moves by about 5% between runs, so a row within 5% of its target is not settled until BenchmarkDotNet says so. Two rows were confirmed with a filtered BenchmarkDotNet run: `AttributesPerLine` 12,568 ns against 24,321 (0.52), and `RawValue` checked 18,784 ns against 12,438 for the BCL and 9,432 for System.Text.Json.

### What was found

A hand-written loop over each writer, producing the same documents the serializers produce, split the cost. `Utf8XmlWriter` took 12 ns for a small element where `Utf8JsonWriter` took 4 ns for a property, and the serializer on top of it added 20 ns per object where System.Text.Json's adds 58. The same split for reading put the two serializer layers level and the tokenizer at 240 ns per order against 144. So the work went into the writer and the reader, and into letting the serializer ask each of them for a whole element at once.

| Finding | What replaced it |
| --- | --- |
| The state of the element being written (start tag open, has content, has text, attribute count) was in its frame, looked up on every call | Four fields of the writer. Only the innermost element has such state. A frame keeps the name for the end tag and what to go back to |
| **The name buffer was never given back.** Every start tag and every attribute added its name for the life of the document, so a writer's memory grew with what it wrote | A frame records the length of the buffer when its element starts, and the buffer goes back to it when the element ends. `NameBuffer_HoldsOnlyTheNamesOfOpenElements` pins it |
| A name given as a string was read three or four times: checked, copied to the name buffer, compared, transcoded to the output | Checked as it is copied (`XmlChars.TryCopyShortAsciiNcName`), to the output and the name buffer in one pass. A frame holds no string, so no reference is stored for a start tag |
| Copying bytes into one buffer and reading them straight back out stalls: the first attempt at the row above made byte names 2.5 ns slower | The output and the name buffer are each written from the caller's name, never from each other |
| An element with one value was `<name>`, the value, `</name>` in three reservations, and a number was formatted on the stack and copied | One reservation (`BeginElementValue` / `EndElementValue`), formatted in place. All 28 `WriteElementNumber` overloads are one line each over `WriteElementFormatted` |
| A scalar member was three writer calls and three virtual calls | `XmlFormattedValueConverter<T>` (21 built-in converters): a longest length and a `Format` into a span. A member, a collection item, a root value, a dictionary entry and an attribute are each one reservation (`BeginLeaf` / `EndLeaf`, `BeginAttribute` / `EndAttribute`). Plain ASCII strings take the same path |
| Dates were formatted a digit at a time, with a division for each | Two digits from a table (`XmlLexical.WriteTwoDigits`): a `DateTime` element went from 15.2 to 10.4 ns, against 13.6 for System.Text.Json |
| A root call reset the cached writer twice and gave its name buffer back to the pool and rented it again | `Detach` resets once and keeps the name buffer; the declaration is written in one reservation. `WriteOrder` 245 to 195 ns, `WriteInt32Buffer` 49 to 32 ns |
| An asynchronous sequence allocated a writer per call | One spare writer for calls that cross an `await` (`XmlWriterCache.RentSpare`): 856 B to 184 B per call. The loop does not await a value that is ready |
| A member that holds one scalar was three tokens, with the element pushed, its namespace worked out, and all of it taken back two tokens later | `Utf8XmlReader.TryReadExpectedLeaf`: the serializer knows which member comes next, so two comparisons and one search read `<Id>42</Id>`. Used for members, collection items, streamed values and the resumable reader. It reads nothing when the input is any other shape |
| The usual start tag and the usual attribute paid for the unusual one: a helper call per blank, a stack buffer for references in every text node and attribute | A plain `<name>` is found with one loop; the reference buffer is in a method of its own; attribute records are passed by reference |
| A reader state turned every open element name and namespace into a string, and a resumed reader turned them back into bytes | The state holds UTF-8, a resumed reader uses its arrays as they are, and a state taken with the same elements open reuses them. `ResumedDrain` 31,136 B to 17,040 B; `TrySkip`, resumed, 17,464 B to 6,504 B |

The hot paths no longer depend on profile-guided inlining to be joined up: the small helpers are marked for inlining and their rare paths are methods of their own. With `DOTNET_TieredPGO=0` an element still costs about 17 ns to write against 10 with it, and a read of the orders is about 45% slower. That is what a Native AOT caller sees, and it is the largest thing left.

### Where each row stands

Time / allocated bytes for XmlEssentials; the ratio is to the target named.

| Test | Before | After | Target | Ratio |
| --- | --- | --- | --- | --- |
| CatalogWrite | 22,279 ns / 0 B | 15,132 ns / 0 B | BCL 27,247 | 0.56 |
| TinyWrite | 59 ns / 0 B | 33 ns / 0 B | BCL 286 | 0.12 |
| AttributesPerLine | 27,921 ns / 0 B | 12,071 ns / 0 B | BCL 19,976 | 0.60 |
| EncodedNames | 19,316 ns / 0 B | 13,809 ns / 0 B | STJ 10,890 | 1.27 |
| IndentedWrite | 31,572 ns / 0 B | 20,687 ns / 0 B | STJ 13,065 | 1.58 |
| RawValue, checked | 20,291 ns / 0 B | 16,028 ns / 0 B | STJ 11,136 | 1.44 |
| RawValue, not checked | 89 ns / 0 B | 86 ns / 0 B | STJ 56 | 1.54 |
| CatalogDrain | 20,510 ns / 0 B | 15,070 ns / 0 B | BCL 21,380 | 0.70 |
| ResumedDrain | 222,190 ns / 31,138 B | 175,174 ns / 17,040 B | BCL 233,407 / 27,600 B | 0.75 |
| TrySkip, resumed | 201,330 ns / 17,466 B | 167,427 ns / 6,504 B | BCL 173,651 | 0.96 |
| DomParseCatalog | 28,912 ns / 104 B | 23,983 ns / 104 B | BCL 47,914 | 0.50 |
| DomWrite | 16,044 ns / 0 B | 13,676 ns / 0 B | BCL 23,117 | 0.59 |
| ParseTree | 38,140 ns / 87,542 B | 29,215 ns / 87,544 B | STJ 17,134 / 48,936 B | 1.71 |
| ChangeTree | 113,960 ns / 257,198 B | 90,423 ns / 257,192 B | BCL 99,097 / 195,728 B | 0.91 time, 1.31 bytes |
| WriteInt32 | 62 ns / 80 B | 41 ns / 80 B | STJ 24 / 32 B | 1.71 |
| WriteString | 95 ns / 128 B | 72 ns / 128 B | STJ 56 / 80 B | 1.29 |
| ReadInt32 | 55 ns / 0 B | 53 ns / 0 B | STJ 24 | 2.21 |
| ReadString | 123 ns / 96 B | 115 ns / 96 B | STJ 74 | 1.55 |
| WriteOrder | 268 ns / 0 B | 195 ns / 0 B | STJ 160 | 1.22 |
| WriteOrders | 246,862 ns / 0 B | 185,336 ns / 0 B | STJ 186,791 | 0.99 |
| WriteCustomer | 1,725 ns / 0 B | 1,161 ns / 0 B | STJ 1,284 | 0.90 |
| WriteDrawing | 69,309 ns / 0 B | 54,350 ns / 0 B | STJ 70,970 | 0.77 |
| WriteMembers | 66,431 ns / 0 B | 44,680 ns / 0 B | STJ 47,762 | 0.94 |
| WriteMoney | 59,243 ns / 0 B | 43,952 ns / 0 B | STJ 44,664 | 0.98 |
| WriteReadings | 135,312 ns / 0 B | 119,475 ns / 0 B | STJ 95,922 | 1.25 |
| WriteDictionary | 1,662 ns / 0 B | 1,390 ns / 0 B | STJ 933 | 1.49 |
| ReadOrder | 484 ns / 232 B | 299 ns / 232 B | STJ 283 | 1.06 |
| ReadOrders | 426,870 ns / 248,600 B | 267,337 ns / 248,600 B | STJ 298,753 | 0.89 |
| ReadCustomer | 3,597 ns / 3,168 B | 2,112 ns / 3,168 B | STJ 2,573 | 0.82 |
| ReadDrawing | 123,837 ns / 90,794 B | 82,705 ns / 90,792 B | STJ 119,381 | 0.69 |
| ReadMembers | 183,875 ns / 143,802 B | 111,082 ns / 143,800 B | STJ 150,554 | 0.74 |
| ReadMoney | 98,102 ns / 88,601 B | 78,956 ns / 88,600 B | STJ 74,421 | 1.06 |
| ReadReadings | 189,728 ns / 104,602 B | 142,828 ns / 104,600 B | STJ 127,215 | 1.12 |
| ReadDictionary | 4,184 ns / 13,392 B | 3,566 ns / 13,392 B | STJ 3,191 | 1.12 |
| ReadCustomers | 252,588 ns / 266,668 B | 159,526 ns / 266,664 B | STJ 185,934 / 254,336 B | 0.86 time, 1.05 bytes |
| ReadCustomers, Preserve | 462,219 ns / 494,396 B | 328,259 ns / 494,392 B | STJ 318,220 / 385,512 B | 1.03 time, 1.28 bytes |
| WriteCustomers | 114,994 ns / 0 B | 91,506 ns / 0 B | STJ 92,866 | 0.99 |
| WriteCustomers, IgnoreCycles | 134,923 ns / 362 B | 110,352 ns / 360 B | STJ 100,847 | 1.09 |
| WriteCustomers, Preserve | 239,599 ns / 148,578 B | 194,098 ns / 148,576 B | STJ 152,228 | 1.28 |
| ReadTaggedDrawing | 184,018 ns / 90,794 B | 118,398 ns / 90,792 B | STJ 132,937 | 0.89 |
| WriteTaggedDrawing | 89,730 ns / 0 B | 73,899 ns / 0 B | STJ 71,840 | 1.03 |
| ReadTypedDrawing | 261,165 ns / 90,800 B | 172,592 ns / 90,792 B | BCL 209,855 | 0.82 |
| Source generation, read | 3,458 ns / 3,168 B | 1,959 ns / 3,168 B | STJ 2,447 | 0.80 |
| Source generation, write | 1,450 ns / 0 B | 1,008 ns / 0 B | STJ 776 | 1.30 |
| ReadSequence | 36,324 ns / 9,440 B | 20,017 ns / 9,224 B | STJ 24,916 / 8,968 B | 0.80 time, 1.03 bytes |
| WriteSequence | 21,344 ns / 936 B | 13,849 ns / 184 B | STJ 12,856 / 376 B | 1.08 time, 0.49 bytes |
| ReadOrderSequence | 482,400 ns / 246,620 B | 304,221 ns / 240,888 B | STJ 362,648 / 235,216 B | 0.84 time, 1.02 bytes |
| WriteOrderSequence | 254,100 ns / 940 B | 195,655 ns / 184 B | STJ 200,070 / 688 B | 0.98 |

**Every row with a BCL baseline was under it on time in the direct loop.** The full run that followed found one that is not (`ChangeTree` against `XDocument`), and that the BCL's `RawValue` row checks nothing. `ReadTypedDrawing` (1.10) and `AttributesPerLine` (1.05) were the two that were over, and `ResumedDrain` now allocates less than the BCL. One BCL comparison is still over: `ChangeTree` allocates 257 KB against 196 KB.

### What is still over System.Text.Json, and why

- **Rows where the document is the cost.** `<int>42</int>` after a declaration is 55 bytes where JSON writes `42`. `WriteInt32` is 0.7 ns per byte written against 12 for System.Text.Json, and it allocates more because what it returns is longer. The scalar root rows, `EncodedNames` (13.8 µs for about 14 KB against 10.9 µs for about 10 KB, so about 1.0 against 1.1 ns per byte), `WriteSequence` and `WriteDictionary` are of this kind. They can be brought closer and not under.
- **`IndentedWrite` was not a like-for-like row.** The XML side wrote a comment before each item and the JSON side wrote none. Since 2026-11-25 the JSON side writes the same comment (`Utf8JsonWriter.WriteCommentValue`); the row above was measured before that. The full run has it at 16.0 µs for System.Text.Json and 21.5 µs for XmlEssentials.
- **The object rows gave System.Text.Json a writer the benchmark reuses**, and gave XmlEssentials the overload that takes a buffer writer and rents a writer for each call. That was the 35 ns between `WriteOrder` and one order of `WriteOrders`. Since 2026-11-25 `XmlSerializerObjectBenchmarks` writes through a reused `Utf8XmlWriter` (`Reset`, `Serialize(writer, value)`, `Flush`), as the JSON side does; the two shapes write the same bytes. The rows above were measured before that; the full run has the rows as they are now.
- **Attributes.** `WriteReadings` and `ReadReadings` are four attributes per element. A third of the write is formatting a `double`, which is the same code in both libraries. What is left is that each attribute is recorded and compared with the others on the element, which JSON has no need to do for a property.
- **`RawValue`, checked** is one pass of the reader over the fragment, so it is as fast as the reader is.
- **`Preserve`** is as it was: an id string and a table entry for each object.
- **`ParseTree` and `ChangeTree`** were not worked on in this pass beyond what the reader and writer gave them. Their rows were measured before the node lists were given a version and an element's lists were put in place with a compare-and-exchange (2026-11-25, [DOM](06-api-dom.md)); the full run shows no cost from either.
- **Source generation, write.** Generated code writes a scalar member as three calls (`WriteLeafStart`, the value, `WriteLeafEnd`). The one-call form needs no new public member: `Utf8XmlWriter.WriteElementNumber`, `WriteElementString` and `WriteElementBoolean` take a UTF-8 name. It was tried on 2026-11-25 and taken out again. `Write_Default` went from 1,003 ns to about 970 ns in the direct loop, which is inside the noise of that harness, and the path of an error changed: a value with an illegal character was reported at `/Order/Lines/Line/Label` by the generated fast path and at `/Order/Lines/Line` by the metadata and reflection paths (`ExceptionPaths_AreTheSameFromAGeneratedContext`). So no helper was added to `XmlMetadataServices`. What the fast path still pays for is elsewhere: an object member is written with a name given as a string (`WriteStartElement("Billing")`), and a nested object goes through its type info and converter (`XmlMetadataServices.Write<T>`) before it reaches its own generated method.

## Full run (2026-11-25, net11.0 ShortRun, Hyper-V)

One BenchmarkDotNet run of the whole suite (229 benchmarks, 26 minutes 45 seconds), made after the final pass, after the two benchmark changes recorded under it, and after the node lists were given a version and concurrent reads were made safe ([DOM](06-api-dom.md)). **This table is the current standing.** The sections above keep the tables of their own runs. The documentation (`benchmarks.mdx`, the cards of `index.mdx`, the cost table of `reflection-vs-source-generation.mdx`, the README) was regenerated from this run.

Time / allocated bytes. The fifth column is the BCL time over ours (above 1 is faster than the BCL). The sixth is our time over System.Text.Json's (under 1 is faster than it), marked where the difference is inside the sum of the two error columns: a ShortRun is three iterations, and its error is wide.

| Test | BCL | STJ | XmlEssentials | BCL ÷ XmlEssentials | XmlEssentials ÷ STJ |
| --- | --- | --- | --- | --- | --- |
| Word document (WordprocessingML, 11 KB) | 19.7 µs / 25,144 B |  | 12.9 µs / 944 B | 1.54 |  |
| RPM package metadata (21 KB) | 31.3 µs / 20,064 B |  | 15.3 µs / 80 B | 2.04 |  |
| OpenDocument text (57 KB) | 79.8 µs / 56,017 B |  | 43.9 µs / 6,952 B | 1.82 |  |
| RSS feed (198 KB) | 191.9 µs / 47,137 B |  | 82.4 µs / 1,441 B | 2.33 |  |
| Word document, scratch buffer |  |  | 12.8 µs / 0 B |  |  |
| RPM package metadata, scratch buffer |  |  | 15.2 µs / 0 B |  |  |
| OpenDocument text, scratch buffer |  |  | 43.3 µs / 0 B |  |  |
| RSS feed, scratch buffer |  |  | 83.0 µs / 0 B |  |  |
| Word document (11 KB) | 52.9 µs / 127,176 B |  | 17.0 µs / 104 B | 3.12 |  |
| RPM package metadata (21 KB) | 72.8 µs / 170,953 B |  | 21.3 µs / 104 B | 3.42 |  |
| OpenDocument text (57 KB) | 198.4 µs / 353,889 B |  | 59.1 µs / 104 B | 3.36 |  |
| RSS feed (198 KB) | 345.4 µs / 709,083 B |  | 108.8 µs / 105 B | 3.18 |  |
| Word document (11 KB) | 28.8 µs / 11,032 B |  | 20.6 µs / 0 B | 1.40 |  |
| RPM package metadata (21 KB) | 32.6 µs / 8,016 B |  | 21.5 µs / 0 B | 1.51 |  |
| OpenDocument text (57 KB) | 97.7 µs / 23,273 B |  | 68.0 µs / 0 B | 1.44 |  |
| RSS feed (198 KB) | 148.8 µs / 8,017 B |  | 89.6 µs / 0 B | 1.66 |  |
| Write the tiny document | 326 ns / 7,328 B |  | 33 ns / 0 B | 9.89 |  |
| Read the tiny document | 325 ns / 2,472 B |  | 63 ns / 0 B | 5.18 |  |
| Parse the tiny document into a DOM | 645 ns / 5,176 B |  | 114 ns / 104 B | 5.65 |  |
| Write the catalog | 33.4 µs / 13,728 B |  | 16.4 µs / 0 B | 2.04 |  |
| Read the catalog | 25.9 µs / 15,088 B |  | 17.3 µs / 0 B | 1.50 |  |
| Read the catalog and parse its integers | 30.4 µs / 35,808 B |  | 18.6 µs / 0 B | 1.63 |  |
| Read the catalog with namespaces | 21.0 µs / 15,200 B |  | 14.6 µs / 0 B | 1.44 |  |
| Parse the catalog into a DOM | 55.8 µs / 136,128 B |  | 25.8 µs / 104 B | 2.16 |  |
| Parse the catalog with namespaces into a DOM | 44.6 µs / 110,760 B |  | 20.0 µs / 104 B | 2.24 |  |
| Navigate the parsed catalog | 3.56 µs / 0 B |  | 2.84 µs / 0 B | 1.25 |  |
| Write the parsed catalog | 28.3 µs / 7,368 B |  | 15.5 µs / 0 B | 1.83 |  |
| Read a catalog from a stream, in blocks | 261.1 µs / 27,603 B |  | 190.6 µs / 17,042 B | 1.37 |  |
| Skip the content of every item in a catalog | 197.7 µs / 27,738 B |  | 172.4 µs / 42 B | 1.15 |  |
| Read one element with 1,000 attributes | 69.4 µs / 344,281 B |  | 16.1 µs / 73,320 B | 4.30 |  |
| Read one element with 5,000 attributes | 1.67 ms / 1,755,804 B |  | 119.2 µs / 589,936 B | 14.03 |  |
| Read the same input as one block |  |  | 194.3 µs / 114 B |  |  |
| TrySkip across blocks |  |  | 187.9 µs / 6,506 B |  |  |
| Indented output | 44.7 µs / 13,784 B | 16.0 µs / 0 B | 21.5 µs / 0 B | 2.08 | 1.35 |
| Indented, one attribute per line | 26.3 µs / 7,384 B |  | 15.8 µs / 0 B | 1.66 |  |
| Names encoded once and reused | 32.2 µs / 13,728 B | 11.9 µs / 0 B | 14.8 µs / 0 B | 2.18 | 1.24 |
| 60 KB of binary as Base64, in segments | 27.5 µs / 9,448 B | 1.68 µs / 0 B | 1.78 µs / 0 B | 15.50 | 1.06 (within error) |
| One long text value, in segments | 31.9 µs / 7,328 B | 50.6 µs / 0 B | 7.56 µs / 0 B | 4.21 | 0.15 |
| Text with line breaks, rewritten as CR LF | 310.1 µs / 7,332 B |  | 103.6 µs / 0 B | 2.99 |  |
| Same names, given as strings |  |  | 14.4 µs / 0 B |  |  |
| Text with line breaks, CR LF, XML 1.1 |  |  | 104.1 µs / 0 B |  |  |
| Text with line breaks, written as they are |  |  | 24.8 µs / 0 B |  |  |
| Raw fragment, checked |  | 9.55 µs / 0 B | 17.5 µs / 0 B |  | 1.83 |
| Raw fragment, not checked | 6.75 µs / 7,328 B | 54 ns / 0 B | 82 ns / 0 B | 82.40 | 1.51 |
| Parse into a tree, against XmlDocument | 54.6 µs / 135,296 B | 18.2 µs / 48,936 B | 35.5 µs / 87,544 B | 1.54 | 1.95 |
| Parse into a tree, against XDocument | 52.9 µs / 105,049 B | 18.2 µs / 48,936 B | 35.5 µs / 87,544 B | 1.49 | 1.95 |
| Parse, change, and write, against XmlDocument | 119.6 µs / 195,729 B | 71.3 µs / 226,537 B | 108.4 µs / 257,193 B | 1.10 | 1.52 |
| Parse, change, and write, against XDocument | 92.2 µs / 136,297 B | 71.3 µs / 226,537 B | 108.4 µs / 257,193 B | 0.85 | 1.52 |
| Read an int | 1.07 µs / 5,968 B | 27 ns / 0 B | 56 ns / 0 B | 19.06 | 2.06 |
| Read an int from a stream | 1.07 µs / 5,968 B |  | 68 ns / 0 B | 15.62 |  |
| Read a DateTime | 1.10 µs / 6,040 B |  | 74 ns / 0 B | 14.99 |  |
| Read an enum | 828 ns / 4,480 B |  | 62 ns / 0 B | 13.27 |  |
| Read a string | 1.25 µs / 6,120 B | 77 ns / 96 B | 125 ns / 96 B | 9.96 | 1.63 |
| Write an int to a buffer | 345 ns / 7,648 B |  | 35 ns / 0 B | 10.00 |  |
| Write an int to a byte array | 333 ns / 7,728 B | 28 ns / 32 B | 49 ns / 80 B | 6.78 | 1.76 |
| Write an int to a string | 352 ns / 7,784 B | 32 ns / 32 B | 58 ns / 136 B | 6.08 | 1.84 |
| Write a string | 360 ns / 7,888 B | 66 ns / 80 B | 83 ns / 128 B | 4.32 | 1.27 |
| Write a DateTime | 368 ns / 7,784 B |  | 58 ns / 104 B | 6.36 |  |
| Write an enum | 365 ns / 7,744 B |  | 52 ns / 96 B | 7.00 |  |
| Write a nullable int | 604 ns / 8,024 B |  | 75 ns / 144 B | 8.03 |  |
| Read an order | 2.14 µs / 7,368 B | 311 ns / 232 B | 363 ns / 232 B | 5.90 | 1.17 |
| Write an order | 1.05 µs / 8,192 B | 179 ns / 0 B | 207 ns / 0 B | 5.10 | 1.15 |
| Read a customer | 8.75 µs / 16,496 B | 2.97 µs / 3,576 B | 2.67 µs / 3,168 B | 3.28 | 0.90 (within error) |
| Write a customer | 4.26 µs / 8,648 B | 1.43 µs / 312 B | 1.33 µs / 0 B | 3.21 | 0.93 (within error) |
| Read 1,000 orders | 771.2 µs / 567,312 B | 325.8 µs / 249,076 B | 320.3 µs / 248,604 B | 2.41 | 0.98 (within error) |
| Write 1,000 orders | 425.1 µs / 214,508 B | 202.1 µs / 314 B | 201.1 µs / 0 B | 2.11 | 1.00 (within error) |
| Read 1,000 objects mapped as attributes | 353.1 µs / 267,716 B | 156.2 µs / 105,074 B | 157.2 µs / 104,602 B | 2.25 | 1.01 (within error) |
| Write 1,000 objects mapped as attributes | 209.4 µs / 131,138 B | 108.2 µs / 313 B | 110.3 µs / 0 B | 1.90 | 1.02 (within error) |
| Read 1,000 objects of three derived types | 208.6 µs / 130,866 B | 142.6 µs / 117,914 B | 100.4 µs / 90,793 B | 2.08 | 0.70 |
| Write 1,000 objects of three derived types | 131.2 µs / 30,098 B | 79.8 µs / 313 B | 61.5 µs / 0 B | 2.13 | 0.77 |
| Read 1,000 objects with a decimal amount | 262.4 µs / 146,252 B | 88.9 µs / 89,073 B | 87.9 µs / 88,601 B | 2.98 | 0.99 (within error) |
| Write 1,000 objects with a decimal amount | 82.6 µs / 48,009 B | 50.4 µs / 312 B | 47.2 µs / 0 B | 1.75 | 0.94 (within error) |
| Read 1,000 positional records |  | 170.1 µs / 240,274 B | 141.7 µs / 143,802 B |  | 0.83 (within error) |
| Write 1,000 positional records |  | 56.3 µs / 312 B | 50.6 µs / 0 B |  | 0.90 |
| Read a dictionary of 100 integers |  | 3.48 µs / 13,392 B | 4.10 µs / 13,392 B |  | 1.18 (within error) |
| Write a dictionary of 100 integers |  | 928 ns / 0 B | 1.79 µs / 0 B |  | 1.93 |
| Read a customer written by System.Xml, Bcl preset | 4.65 µs / 10,704 B |  | 1.66 µs / 1,320 B | 2.80 |  |
| Write a customer as System.Xml writes it, Bcl preset | 2.27 µs / 8,224 B |  | 747 ns / 0 B | 3.04 |  |
| Read a feed of 200 episodes in four namespaces | 169.0 µs / 197,882 B |  | 122.5 µs / 153,585 B | 1.38 |  |
| Write a feed of 200 episodes in four namespaces | 85.0 µs / 15,009 B |  | 64.2 µs / 0 B | 1.32 |  |
| Read 1,000 objects marked with xsi:type | 234.0 µs / 228,570 B |  | 197.8 µs / 90,798 B | 1.18 |  |
| Write 1,000 objects marked with xsi:type | 164.9 µs / 30,506 B |  | 96.1 µs / 0 B | 1.72 |  |
| Read 200 customers |  | 206.8 µs / 254,338 B | 195.2 µs / 266,666 B |  | 0.94 (within error) |
| Write 200 customers |  | 107.2 µs / 313 B | 101.4 µs / 0 B |  | 0.95 |
| Read 200 customers, references preserved |  | 366.3 µs / 385,516 B | 368.8 µs / 494,396 B |  | 1.01 (within error) |
| Write 200 customers, references preserved |  | 167.1 µs / 150,666 B | 199.6 µs / 148,578 B |  | 1.19 |
| Write 200 customers, cycles ignored |  | 116.1 µs / 425 B | 112.4 µs / 360 B |  | 0.97 (within error) |
| Read 1,000 objects with a type discriminator |  | 150.8 µs / 117,914 B | 133.5 µs / 90,794 B |  | 0.89 (within error) |
| Write 1,000 objects with a type discriminator |  | 79.6 µs / 313 B | 76.7 µs / 0 B |  | 0.96 (within error) |
| Read 1,000 integers as a stream | 42.4 µs / 61,152 B | 26.5 µs / 8,968 B | 21.0 µs / 9,224 B | 2.02 | 0.79 |
| Write 1,000 integers as a stream | 33.4 µs / 30,408 B | 13.7 µs / 376 B | 18.5 µs / 184 B | 1.80 | 1.36 |
| Read 1,000 orders as a stream |  | 462.6 µs / 235,220 B | 340.3 µs / 240,892 B |  | 0.74 |
| Write 1,000 orders as a stream |  | 225.5 µs / 690 B | 214.1 µs / 186 B |  | 0.95 (within error) |
| Read a customer | 8.67 µs / 16,496 B | 2.87 µs / 3,576 B | 2.64 µs / 3,168 B | 3.29 | 0.92 |
| Read, source generated |  |  | 2.59 µs / 3,168 B |  |  |
| Read, first call |  |  | 621.0 µs / 82,432 B |  |  |
| Write a customer | 4.30 µs / 8,648 B | 1.41 µs / 312 B | 1.42 µs / 0 B | 3.04 | 1.01 (within error) |
| Write, source generated |  |  | 1.21 µs / 0 B |  |  |
| Write, first call |  |  | 570.7 µs / 79,248 B |  |  |
| Read, default mode |  | 2.88 µs / 3,576 B | 2.54 µs / 3,168 B |  | 0.88 (within error) |
| Read, metadata mode |  | 2.83 µs / 3,576 B | 2.56 µs / 3,168 B |  | 0.91 |
| Read, reflection |  |  | 2.66 µs / 3,168 B |  |  |
| Write, default mode |  | 856 ns / 0 B | 1.07 µs / 0 B |  | 1.25 |
| Write, serialization mode |  | 853 ns / 0 B | 1.12 µs / 0 B |  | 1.31 |
| Write, metadata mode |  | 1.41 µs / 312 B | 1.31 µs / 0 B |  | 0.93 |
| Write, default mode, other options |  |  | 1.34 µs / 0 B |  |  |
| Write, reflection |  |  | 1.38 µs / 0 B |  |  |
| Read an int: reader alone |  |  | 42 ns / 0 B |  |  |
| Read an int: serializer |  |  | 52 ns / 0 B |  |  |
| Read an int: serializer, type info supplied |  |  | 49 ns / 0 B |  |  |
| Read an int with a declaration: reader alone |  |  | 48 ns / 0 B |  |  |
| Read an int with a declaration: serializer |  |  | 63 ns / 0 B |  |  |

### Against the BCL

Counted over every row where the BCL does the same work: **67 comparisons, faster in 66, 3.00 times faster as a geometric mean** (median 2.25, range 0.85 to 19.1). Allocation: **85.1% less as a mean of each row's reduction**, over the 66 rows where the BCL allocates; 68.4% less when the bytes of all rows are summed (6,846,332 against 2,162,447), because the large rows return the objects they read; 0 B in 32 of the 67.

- The tree rows count twice, against `XmlDocument` and against `XDocument`.
- **The raw rows are not counted.** `XmlWriter.WriteRaw` checks nothing, so the row the earlier sections compared with it, `RawValue` checked, was never like for like. Unchecked against unchecked is 82 ns against 6.75 µs, with the BCL transcoding a string; that is too unlike to put in an average either.
- **The sequence rows are not counted.** The BCL has no streamed read or write, and its rows are an array read and written whole.
- **The one row the BCL wins is `ChangeTree` against `XDocument`** (0.85, which is itself inside the error of the two rows; the documentation gives it to the BCL). Both `ChangeTree` rows are the only ones that allocate more than the BCL (257 KB against 196 KB and 136 KB).
- The smallest wins are `ChangeTree` against `XmlDocument` (1.10, inside the error), `Skip` (1.15) and `ReadTypedDrawing` (1.18).
- An earlier readout of this run paired the BCL's `NewLineText` row, which writes CR LF, with our LF row (12.5). The pair is the CR LF row: 3.0.

### Against System.Text.Json

47 rows have a System.Text.Json figure: **ahead in 10, inside the error in 19, behind in 18.** We allocate the same or less in 38.

- **Ahead:** the derived-type drawings (0.70 read, 0.77 write), `ReadOrderSequence` (0.74), `ReadSequence` (0.79), `StringSegments` (0.15), `WriteMembers`, `WriteCustomers`, the graph read, and the metadata-mode rows of the generator.
- **Inside the error:** the customer, the 1,000 orders, the readings, the money rows and the tagged drawings, read and written; `ReadMembers`; `ReadCustomers` with and without `Preserve`; `IgnoreCycles`; `WriteOrderSequence`; `Base64Segments`; `ReadDictionary`.
- **Behind:** the scalar roots (1.27 to 2.06), `ParseTree` (1.95) and `ChangeTree` (1.52), `WriteDictionary` (1.93), `RawValue` checked (1.83) and unchecked (1.51), `WriteSequence` (1.36), `IndentedWrite` (1.35), generated write in the default and serialization modes (1.25 and 1.31), `EncodedNames` (1.24), `WriteCustomers` with `Preserve` (1.19), and one order read (1.17) and written (1.15).

### What this run settled, and what it opened

- **The node-list version and the compare-and-exchange cost nothing that shows.** `ParseTree` was 38,140 ns and `ChangeTree` 113,960 ns in the run of 2026-11-24; they are 35,500 ns and 108,400 ns.
- **`IndentedWrite` is like for like** since the JSON side writes the same comment: 21.5 µs against 16.0 µs.
- **The object rows use a reused writer on both sides.** `WriteOrder` is 207 ns against 179 ns.
- **Open: `WriteDictionary` is 1,792 ns.** It was 1,662 ns in the run of 2026-11-24 and about 1,390 ns in the direct loop of the final pass. The benchmark was moved to a reused writer in between. The cause has not been looked for.
- **Open: `WriteSequence` is 18.5 µs against 13.7 µs** (1.36), where the direct loop gave 1.08.
- The two first-call rows have an error larger than their mean. Three iterations cannot measure a call that happens once.

## XPath against System.Xml.XPath (2026-11-26, net11.0 ShortRun, not profiled)

`XPathBenchmarks` (Performance project) evaluates seven expressions, each compiled once, over a catalog of 1000 items parsed once: `XmlDocument` here, `XPathDocument` (the read-only store of `System.Xml.XPath`) on the other side. `GlobalSetup` evaluates every expression on both sides and throws when the results differ; they did not. BenchmarkDotNet 0.15.8, `Job.ShortRun` in process, Linux Ubuntu 24.04, Intel Xeon 2.30 GHz, 4 cores, .NET 11.0.0. ShortRun has three iterations, so the error column is wide.

| Category | Expression | System.Xml.XPath | XmlEssentials | Ratio | Allocated (BCL / ours) |
| --- | --- | --- | --- | --- | --- |
| XPathAncestor | `//qty[. = '7']/ancestor::item/@id` | 31.7 us | 160.3 us | 5.10 | 26160 B / 259897 B |
| XPathCount | `count(//qty)` | 8.4 us | 84.8 us | 10.17 | 416 B / 171649 B |
| XPathFilter | `/catalog/item[@category = 'b']/name` | 97.0 us | 108.5 us | 1.12 | 96904 B / 111009 B |
| XPathLast | `string(/catalog/item[last()]/name)` | 38.5 us | 24.8 us | 0.65 | 89728 B / 16784 B |
| XPathPredicates | `count(//item[qty > 50][@category = 'a'])` | 225.7 us | 253.0 us | 1.12 | 156112 B / 319257 B |
| XPathStrings | `count(//name[contains(., '5') and starts-with(., 'item')])` | 42.6 us | 218.3 us | 5.13 | 31520 B / 283561 B |
| XPathSum | `sum(//item/@price)` | 79.0 us | 146.5 us | 1.88 | 48481 B / 179689 B |

No profile has been taken, so no cause is stated here. The first evaluator was written for correctness; the next step is `dotnet-trace collect --profile cpu-sampling` on `XPathCount` and `XPathStrings`, as the process above says.

---

[Index](README.md) · [Previous: PR plan](22-pr-plan.md) · [Next: Doc findings resolution](24-doc-findings-resolution.md)
