# Test Naming Convention

## Standard Pattern

```
Should_[Action]_When_[Condition]
```

Every test method in both `TurboHttp.Tests` and `TurboHttp.StreamTests` must follow this pattern.

### Parts

| Part | Description | Rules |
|------|-------------|-------|
| `Should` | Fixed prefix | Always present, exactly `Should` |
| `Action` | What the SUT does or produces | PascalCase, verb-led (e.g. `Parse200Ok`, `ReturnFalse`, `ThrowException`) |
| `When` | Fixed separator | Always present, exactly `When` |
| `Condition` | The input, state, or scenario | PascalCase, describes the trigger (e.g. `StatusLineIsValid`, `InputIsNull`) |

### Examples

```csharp
// Good
Should_Parse200Ok_When_StatusLineIsValid()
Should_ReturnFalse_When_FrameHeaderHasZeroBytes()
Should_ThrowInvalidHeader_When_NoColon()
Should_TrimWhitespace_When_HeaderValueHasExtraSpaces()

// Bad — old patterns being replaced
FrameHeader_ZeroBytes_ReturnsFalse()        // → Should_ReturnFalse_When_FrameHeaderHasZeroBytes()
Test_9112_StatusLine_200Ok()                 // → Should_Parse200Ok_When_StatusLineIsValid()
NullInput_ReturnsNull()                      // → Should_ReturnNull_When_InputIsNull()
IsCacheable_200_True()                       // → Should_BeTrue_When_StatusIs200()
H2HP_001_Method_Get_Is_Static_Indexed()      // → Should_UseStaticIndex_When_MethodIsGet()
```

## Edge Cases

### 1. Theory methods with `[InlineData]`

Use a general condition; the specific values come from the data attributes.

```csharp
[Theory]
[InlineData(200)]
[InlineData(301)]
public void Should_BeCacheable_When_StatusCodeIsCacheable(int statusCode)
```

### 2. Multiple assertions in one test

Name after the primary assertion or the overall behaviour being verified.

```csharp
// Verifies both URI and method are preserved
Should_PreserveUriAndMethod_When_BuildingConditionalRequest()
```

### 3. Negative / error tests

Use `Throw` + exception type or `ReturnError` as the action.

```csharp
Should_ThrowHpackException_When_DynamicTableOverflows()
Should_ReturnProtocolError_When_StreamIdIsEven()
```

### 4. Async tests

Same naming pattern. The `async Task` return type is orthogonal to the name.

```csharp
public async Task Should_CompleteWithResponse_When_SingleRequestSent()
```

### 5. Boolean result tests

Use `BeTrue` / `BeFalse` or a descriptive action instead of bare `ReturnTrue`.

```csharp
Should_BeTrue_When_MethodIsIdempotent()
Should_BeFalse_When_RequestHasNoStoreDirective()
```

### 6. Collection / count tests

Describe the expected outcome concretely.

```csharp
Should_ContainExactly61Entries_When_QueryingStaticTable()
Should_HaveSizeZero_When_DynamicTableIsEmpty()
```

### 7. Void / side-effect tests

Name after the observable side effect.

```csharp
Should_RemoveEntry_When_Invalidated()
Should_EvictLruEntry_When_MaxEntriesExceeded()
```

### 8. Fragmentation / partial data tests

Describe the fragmentation scenario in the condition.

```csharp
Should_ReassembleResponse_When_BodySplitAcrossMultipleFrames()
Should_BufferIncompleteFrame_When_TcpSegmentSplitsMidHeader()
```

### 9. Long conditions

Keep it readable. Abbreviate only when the abbreviation is universally understood in the project (e.g. `Http10`, `Http20`, `Lru`). Do not use acronyms that are not established in the codebase.

### 10. DisplayName attribute

`DisplayName` carries the RFC tag and human-readable description. It is **never** changed during renames. The method name and `DisplayName` serve different purposes:

- **Method name**: machine-readable, consistent pattern for tooling and filtering
- **DisplayName**: human-readable, RFC-tagged for compliance tracing

## Constraints

- **FR-2**: `DisplayName` attributes are NEVER changed
- **FR-8**: No `DisplayName` attributes are changed
- **FR-9**: No test logic or assertions are changed
- Only the method name is modified; everything else stays identical
