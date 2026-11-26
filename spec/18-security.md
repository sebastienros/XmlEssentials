# Security & privacy considerations

Part of the [XmlEssentials spec](README.md).

| Threat | Severity | Mitigation |
| --- | --- | --- |
| XXE / external entity | **Critical** | DTD is a hard error. No entity resolver, no network fetch, no file fetch. |
| Billion laughs (entity expansion) | **Critical** | No general entities. Five predefined only. |
| Deeply nested elements | High | `MaxDepth` default 64. `0` means default, not unlimited. |
| Attribute / name / text bombs | High | `MaxAttributeCount` (includes xmlns*), `MaxNameByteCount`, `MaxTextByteCount` (text, CDATA, attribute values, processing instruction data), `MaxCommentByteCount`. `0` on these means table default. |
| Unbounded stream deserialize | High | `MaxDocumentByteCount` `0` = unbounded **by choice**. When set, enforce **while** buffering, not after. Callers who leave 0 accept memory DoS. |
| Quadratic well-formedness | Medium | Namespace stack is O(depth × decls); skip-validation writer path documented as unsafe for untrusted output. |
| SSRF via `xsi:schemaLocation` | Low | We do not fetch schemas. Attribute is data. |
| Sensitive data in XML | Medium | Library does not log payload values. Callers' problem. |
| HashDoS on extension data keys | Low | `XmlName` dictionary uses ordinal string hash (framework). |
| XPath expression the caller did not write | High | An expression is code, and a fixed one costs a fixed power of the document (`//a[//a[//a]]`). `XPathOptions.MaxOperations` bounds the rows the steps look at plus the string-values read in one evaluation; `0`, the default, is no limit **by choice**, as for `MaxDocumentByteCount`. `XPathOptions.MaxDepth` (default 64) bounds the nesting the compiler and the evaluator recurse on. A caller who evaluates an expression it did not write sets `MaxOperations`. See [XPath](27-xpath.md#evaluation). |

No authentication, no PII store, no telemetry. Schema files are compile-time AdditionalFiles only; the generator must not fetch URLs. `<!DOCTYPE` in instance **or** schema documents is `XmlException` / XMLESS010. `WriteRawValue(..., skipInputValidation: true)` is the unsafe hatch; document it as "do not use on untrusted input".

Privacy: none. The library does not phone home.

DoS limits: [XML fidelity](02-xml-fidelity.md#dos-limits-defaults). DTD rejection: [XML fidelity](02-xml-fidelity.md#not-in-v1-errors-not-silent-skip).

---

[Index](README.md) · [Previous: Alternatives](17-alternatives.md) · [Next: Operability](19-operability.md)
