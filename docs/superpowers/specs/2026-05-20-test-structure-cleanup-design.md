# Test Folder Restructuring

**Date:** 2026-05-20
**Scope:** Mirror source project structure changes in TurboHTTP.Tests
**Approach:** git mv + namespace updates, single commit

## Moves from Server/ to top-level

| From | To | Count |
|------|----|-------|
| Server/Context/*.cs | Context/ | 10 |
| Server/Binding/*.cs | Routing/Binding/ | 9 |
| Server/Routing/*.cs | Routing/ | 6 |
| Server/EndpointResolverSpec.cs | Routing/ | 1 |
| Server/Lifecycle/ListenerActorConnectionLimitSpec.cs | Streams/Stages/Lifecycle/ | 1 |

## Moves within Streams/

| From | To | Count |
|------|----|-------|
| Streams/ConnectionShapeSpec.cs | Streams/Stages/Client/ | 1 |
| Streams/RequestEnricherUriSpec.cs | Streams/Stages/Client/ | 1 |
| Streams/Pipe*Spec.cs | Streams/Stages/ | 4 |
| Streams/Stages/HandlerBidiStageSpec.cs | Streams/Stages/Server/ | 1 |
| Streams/Stages/EndpointDispatchCachingSpec.cs | Streams/Stages/Routing/ | 1 |

## Namespace changes

Each moved file gets namespace updated to match new folder path.

## Out of scope

No changes to Protocol/, Features/, Client/, Diagnostics/, Internal/ test folders.
