# Package Manifest: SolidPOS V1.1-08 Terminal & Device Management Hardening

- **Phase:** V1.1-08 Terminal & Device Management Hardening
- **Baseline SHA:** `d44eff7db399f2df5452eba5eab99ad79e16a39b`
- **Target Branch:** `lucilferChanges/fastandrun`
- **schemaVersion:** 4
- schemaVersion: 4
- **syncContract:** schema_version_4
- syncContract: schema_version_4
- **Authoritative Boundary Enforcement:** Active terminal check via `TerminalValidationMiddleware` rejecting revoked and disabled terminals with 401 Unauthorized.
- **Tenant / Store Isolation:** Mandatory tenant-scoped store check (`StoreExistsAsync`), PostgreSQL RLS enforcement, and strict foreign store assignment rejection.

## Modified and Added Files
1. `contracts/openapi/solidpos-api-v1.openapi.yaml` (Documented heartbeat, remote config, store assignment, disable, enable, and device health endpoints)
2. `database/postgresql/021_terminal_device_management_hardening.sql` (Additive migration adding device_health and remote_config_metadata columns)
3. `scripts/apply-postgresql-migrations.ps1` (Registered migration 021 in local runner)
4. `scripts/apply-postgresql-migrations.sh` (Registered migration 021 in shell runner)
5. `src/PosServer/SolidPOS.PosServer.Contracts/Terminals/TerminalDeviceHealthDto.cs` (Device telemetry DTO)
6. `src/PosServer/SolidPOS.PosServer.Contracts/Terminals/TerminalRemoteConfigMetadata.cs` (Remote configuration metadata DTO)
7. `src/PosServer/SolidPOS.PosServer.Contracts/Terminals/TerminalDetailResponse.cs` (Complete terminal detail response)
8. `src/PosServer/SolidPOS.PosServer.Contracts/Terminals/AssignTerminalStoreRequest.cs` (Store assignment request)
9. `src/PosServer/SolidPOS.PosServer.Contracts/Terminals/TerminalHeartbeatRequest.cs` (Heartbeat request contract)
10. `src/PosServer/SolidPOS.PosServer.Contracts/Terminals/TerminalHeartbeatResponse.cs` (Heartbeat response contract)
11. `src/PosServer/SolidPOS.PosServer.Contracts/Terminals/UpdateTerminalRemoteConfigRequest.cs` (Config update request)
12. `src/PosServer/SolidPOS.PosServer.Application/Terminals/ITerminalRepository.cs` (Added terminal query, store assignment, disable, enable, heartbeat, and health repository methods)
13. `src/PosServer/SolidPOS.PosServer.Application/Terminals/ITerminalEnrollmentService.cs` (Added management service abstractions)
14. `src/PosServer/SolidPOS.PosServer.Infrastructure/Terminals/PostgreSqlTerminalRepository.cs` (Implemented new SQL operations with RLS tenant context)
15. `src/PosServer/SolidPOS.PosServer.Infrastructure/Terminals/TerminalEnrollmentService.cs` (Implemented tenant isolation validation, sync change emission, and lifecycle rules)
16. `src/PosServer/SolidPOS.PosServer.Api/Endpoints/TerminalEndpoints.cs` (Exposed GET details, POST store, POST disable, POST enable, GET health, GET/PUT remote config)
17. `src/PosServer/SolidPOS.PosServer.Api/Endpoints/TerminalRuntimeEndpoints.cs` (Exposed POST heartbeat, GET remote config)
18. `tests/SolidPOS.PosServer.ContractTests/OpenApiContractTests.cs` (Added disable/enable to body exclusion list)
19. `tests/SolidPOS.PosServer.UnitTests/Terminals/TerminalValidationMiddlewareTests.cs` (Validation middleware boundary enforcement tests)
20. `tests/SolidPOS.PosServer.UnitTests/Terminals/TerminalEnrollmentServiceTests.cs` (Unit tests for all 7 hardening capabilities)
21. `.github/workflows/solidpos-ci.yml` (Added V1.1-08 static contract validation step)
22. `scripts/v1.1/validate-v1.1-08-terminal-device-management-hardening.ps1` (Phase validator script)
23. `SOLIDPOS_V1_1_08_TERMINAL_DEVICE_MANAGEMENT_HARDENING.md` (Technical documentation)
24. `V1_1_08_PACKAGE_MANIFEST.md` (Manifest)
25. `V1_1_08_VALIDATION_COMMANDS.md` (Verification commands)
