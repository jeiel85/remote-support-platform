#!/usr/bin/env python3
from __future__ import annotations

import csv
import hashlib
import json
import re
import sys
import tempfile
from pathlib import Path
from typing import Any, Iterable

import yaml
from grpc_tools import protoc
from jsonschema import Draft202012Validator, FormatChecker
from openapi_spec_validator import validate_spec
from pglast import parse_sql

HTTP_METHODS = {"get", "post", "put", "patch", "delete"}
TEXT_SUFFIXES = {".md", ".yaml", ".yml", ".json", ".proto", ".sql", ".h", ".ps1", ".csv"}
BUNDLE_DIRECTORIES = {f"{index:02d}-{name}" for index, name in enumerate([
    "product", "architecture", "protocol", "client", "backend", "security",
    "quality", "delivery", "operations", "templates", "references", "bootstrap",
])}
BUNDLE_ROOT_FILES = {
    "DOCUMENT_INDEX.md", "FINAL_AUDIT_REPORT.md", "IMPLEMENTATION_READINESS.md",
    "README.md", "START_HERE.md",
}


def iter_bundle_files(root: Path) -> Iterable[Path]:
    for path in root.rglob("*"):
        if not path.is_file() or path.name == "MANIFEST.json":
            continue
        relative = path.relative_to(root)
        if relative.parts[0] in BUNDLE_DIRECTORIES or relative.as_posix() in BUNDLE_ROOT_FILES:
            yield path


def fail(message: str) -> None:
    raise AssertionError(message)


def walk_refs(value: Any) -> list[str]:
    refs: list[str] = []
    if isinstance(value, dict):
        for key, child in value.items():
            if key == "$ref" and isinstance(child, str):
                refs.append(child)
            refs.extend(walk_refs(child))
    elif isinstance(value, list):
        for child in value:
            refs.extend(walk_refs(child))
    return refs


def resolve_json_pointer(document: Any, pointer: str) -> Any:
    if not pointer.startswith("#/"):
        fail(f"Only internal references are permitted in this check: {pointer}")
    current = document
    for part in pointer[2:].split("/"):
        part = part.replace("~1", "/").replace("~0", "~")
        if not isinstance(current, dict) or part not in current:
            fail(f"Unresolved JSON pointer: {pointer}")
        current = current[part]
    return current


def extract_openapi_scopes(spec: dict[str, Any]) -> set[str]:
    return set(spec["components"]["schemas"]["Scope"]["enum"])


def find_named_blocks(text: str, keyword: str) -> list[tuple[str, str]]:
    blocks: list[tuple[str, str]] = []
    pattern = re.compile(rf"\b{re.escape(keyword)}\s+([A-Za-z_][A-Za-z0-9_]*)\s*\{{")
    for match in pattern.finditer(text):
        depth = 1
        index = match.end()
        while index < len(text) and depth:
            if text[index] == "{":
                depth += 1
            elif text[index] == "}":
                depth -= 1
            index += 1
        if depth:
            fail(f"Unclosed {keyword} block: {match.group(1)}")
        blocks.append((match.group(1), text[match.end(): index - 1]))
    return blocks


def remove_nested_type_blocks(body: str) -> str:
    chars = list(body)
    pattern = re.compile(r"\b(?:message|enum)\s+[A-Za-z_][A-Za-z0-9_]*\s*\{")
    for match in list(pattern.finditer(body)):
        depth = 1
        index = match.end()
        while index < len(body) and depth:
            if body[index] == "{":
                depth += 1
            elif body[index] == "}":
                depth -= 1
            index += 1
        for i in range(match.start(), min(index, len(chars))):
            chars[i] = " "
    return "".join(chars)


def validate_proto(proto: str, source: str) -> tuple[set[str], int]:
    if 'syntax = "proto3";' not in proto:
        fail(f"{source}: proto3 syntax declaration missing")
    enum_values: dict[str, set[str]] = {}
    for name, body in find_named_blocks(proto, "enum"):
        numbers: dict[int, str] = {}
        names: set[str] = set()
        for value_name, value_number in re.findall(r"^\s*([A-Z][A-Z0-9_]*)\s*=\s*(-?\d+)\s*;", body, re.M):
            number = int(value_number)
            if value_name in names:
                fail(f"{source}: duplicate enum name {name}.{value_name}")
            if number in numbers:
                fail(f"{source}: duplicate enum number {name}={number} ({numbers[number]}, {value_name})")
            names.add(value_name)
            numbers[number] = value_name
        if not names:
            fail(f"{source}: empty/unparsed enum {name}")
        enum_values[name] = names

    message_count = 0
    for name, body in find_named_blocks(proto, "message"):
        message_count += 1
        direct = remove_nested_type_blocks(body)
        numbers: dict[int, str] = {}
        names: set[str] = set()
        field_pattern = re.compile(r"^\s*(?:repeated\s+|optional\s+)?[.A-Za-z_][.A-Za-z0-9_<>]*\s+([a-zA-Z_][A-Za-z0-9_]*)\s*=\s*(\d+)\s*(?:\[[^\]]*\])?\s*;", re.M)
        for field_name, field_number in field_pattern.findall(direct):
            number = int(field_number)
            if field_name in names:
                fail(f"{source}: duplicate field name {name}.{field_name}")
            if number in numbers:
                fail(f"{source}: duplicate field number {name}={number} ({numbers[number]}, {field_name})")
            if number <= 0 or 19000 <= number <= 19999:
                fail(f"{source}: prohibited field number {name}.{field_name}={number}")
            names.add(field_name)
            numbers[number] = field_name
    return enum_values.get("CapabilityScope", set()), message_count


def normalize_proto_scopes(values: set[str]) -> set[str]:
    return {value for value in values if value != "CAPABILITY_SCOPE_UNSPECIFIED"}


def extract_sql_states(sql: str) -> set[str]:
    table = re.search(r"create\s+table\s+support_sessions\s*\((.*?)\n\);", sql, re.S | re.I)
    if not table:
        fail("Missing support_sessions table")
    match = re.search(r"state\s+text\s+not\s+null\s+check\s*\(state\s+in\s*\((.*?)\)\)", table.group(1), re.S | re.I)
    if not match:
        fail("Missing SQL session state constraint")
    return set(re.findall(r"'([A-Z_]+)'", match.group(1)))



def extract_sql_scopes(sql: str) -> set[str]:
    match = re.search(r"requested_scopes\s*<@\s*array\[(.*?)\]::text\[\]", sql, re.S | re.I)
    if not match:
        fail("Missing SQL requested scope constraint")
    return set(re.findall(r"'([A-Z_]+)'", match.group(1)))


def parse_requirement_ids(root: Path) -> tuple[list[str], dict[str, str]]:
    texts = []
    for rel in ["00-product/functional-requirements.md", "00-product/nonfunctional-requirements.md"]:
        texts.append((root / rel).read_text(encoding="utf-8"))
    combined = "\n".join(texts)
    pairs = re.findall(r"\|\s*((?:FR|NFR)-[A-Z]+-\d+)\s*\|\s*(.*?)\s*\|", combined)
    ids = [item[0] for item in pairs]
    if len(ids) != len(set(ids)):
        fail("Duplicate requirement ID")
    return ids, dict(pairs)


def iter_semicolon_paths(value: str) -> Iterable[str]:
    for item in value.split(";"):
        item = item.strip()
        if item:
            yield item


def main() -> int:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else ".").resolve()
    if not (root / "README.md").exists():
        fail(f"Not a bundle root: {root}")

    required_contracts = [
        "02-protocol/openapi/openapi.yaml",
        "02-protocol/protobuf/remote_support.proto",
        "02-protocol/ipc/service_ipc.proto",
        "02-protocol/native/remote_support_native.h",
        "04-backend/database-schema.sql",
        "07-delivery/acceptance-test-cases.csv",
        "07-delivery/traceability/requirements-traceability.csv",
    ]
    for rel in required_contracts:
        if not (root / rel).is_file():
            fail(f"Missing required contract: {rel}")

    json_ids: set[str] = set()
    bundle_files = list(iter_bundle_files(root))
    for path in (path for path in bundle_files if path.suffix.lower() == ".json"):
        document = json.loads(path.read_text(encoding="utf-8"))
        if isinstance(document, dict) and "$schema" in document:
            Draft202012Validator.check_schema(document)
        if isinstance(document, dict) and "$id" in document:
            schema_id = document["$id"]
            if schema_id in json_ids:
                fail(f"Duplicate JSON Schema $id: {schema_id}")
            json_ids.add(schema_id)
    for path in (path for path in bundle_files if path.suffix.lower() in {".yaml", ".yml"}):
        yaml.safe_load(path.read_text(encoding="utf-8"))

    schema_pairs = [
        ("02-protocol/schemas/client-config.schema.json", "09-templates/client-config.example.json"),
        ("02-protocol/schemas/appsettings.schema.json", "09-templates/appsettings.example.json"),
        ("02-protocol/schemas/policy-document.schema.json", "09-templates/policy-document.example.json"),
        ("02-protocol/schemas/audit-event.schema.json", "09-templates/audit-event.example.json"),
        ("02-protocol/schemas/update-manifest.schema.json", "09-templates/update-manifest.example.json"),
        ("02-protocol/schemas/update-root.schema.json", "09-templates/update-root.example.json"),
    ]
    for schema_name, example_name in schema_pairs:
        schema = json.loads((root / schema_name).read_text(encoding="utf-8"))
        example = json.loads((root / example_name).read_text(encoding="utf-8"))
        errors = list(Draft202012Validator(schema, format_checker=FormatChecker()).iter_errors(example))
        if errors:
            fail(f"Schema validation failed for {example_name}: {errors[0].message}")

    spec = yaml.safe_load((root / "02-protocol/openapi/openapi.yaml").read_text(encoding="utf-8"))
    if not str(spec.get("openapi", "")).startswith("3.1."):
        fail("OpenAPI must use the 3.1 line")
    validate_spec(spec)
    operation_ids: set[str] = set()
    for path_name, path_item in spec["paths"].items():
        path_params = set(re.findall(r"\{([^}]+)\}", path_name))
        for method, operation in path_item.items():
            if method.lower() not in HTTP_METHODS:
                continue
            op_id = operation.get("operationId")
            if not op_id:
                fail(f"Missing operationId: {method.upper()} {path_name}")
            if op_id in operation_ids:
                fail(f"Duplicate operationId: {op_id}")
            operation_ids.add(op_id)
            responses = operation.get("responses") or {}
            if not responses:
                fail(f"Missing responses: {op_id}")
            if not any(str(code).startswith("2") for code in responses):
                fail(f"Missing success response: {op_id}")
            effective_security = operation.get("security", spec.get("security"))
            if effective_security is None:
                fail(f"Security policy must be explicit or inherited: {op_id}")
            declared_params: set[str] = set()
            for parameter in [*(path_item.get("parameters") or []), *(operation.get("parameters") or [])]:
                if isinstance(parameter, dict) and "$ref" in parameter:
                    parameter = resolve_json_pointer(spec, parameter["$ref"])
                if isinstance(parameter, dict) and parameter.get("in") == "path":
                    declared_params.add(parameter.get("name"))
            if path_params != declared_params:
                fail(f"Path parameter mismatch for {op_id}: template={path_params}, declared={declared_params}")
    for ref in walk_refs(spec):
        resolve_json_pointer(spec, ref)
    required_operations = {
        "createManagedSession", "pollManagedSessionRequests", "decideManagedHostSession", "createPeerAuthorizationChallenge",
        "createDeviceCredentialChallenge", "exchangeDeviceCredential", "rotateDeviceKey",
        "createInvitation", "acceptInvitation", "patchMembership", "requestTenantDataExport",
        "getTenantDataExport", "requestTenantClosure", "getUpdateRoot"
    }
    if not required_operations.issubset(operation_ids):
        fail(f"OpenAPI missing final-audit operations: {required_operations-operation_ids}")
    for path_item in spec["paths"].values():
        for method, operation in path_item.items():
            if method not in HTTP_METHODS:
                continue
            for requirement in operation.get("security", spec.get("security", [])):
                if isinstance(requirement, dict) and ("peerDpopAccessToken" in requirement or "deviceDpopAccessToken" in requirement) and "dpopProof" not in requirement:
                    fail(f"Sender-constrained token missing dpopProof: {operation.get('operationId')}")
    if "HOST_PENDING" not in set(spec["components"]["schemas"]["SessionState"]["enum"]):
        fail("OpenAPI missing HOST_PENDING")

    missing_links: list[tuple[Path, str]] = []
    link_pattern = re.compile(r"\[[^\]]*\]\(([^)]+)\)")
    for path in (path for path in bundle_files if path.suffix.lower() == ".md"):
        text = path.read_text(encoding="utf-8")
        for target in link_pattern.findall(text):
            clean = target.split("#", 1)[0]
            if not clean or "://" in clean or clean.startswith("mailto:"):
                continue
            if not (path.parent / clean).resolve().exists():
                missing_links.append((path.relative_to(root), target))
    if missing_links:
        fail(f"Missing Markdown link: {missing_links[0]}")

    openapi_scopes = extract_openapi_scopes(spec)
    peer_proto_path = root / "02-protocol/protobuf/remote_support.proto"
    ipc_proto_path = root / "02-protocol/ipc/service_ipc.proto"
    proto_scopes_raw, peer_message_count = validate_proto(peer_proto_path.read_text(encoding="utf-8"), peer_proto_path.relative_to(root).as_posix())
    _, ipc_message_count = validate_proto(ipc_proto_path.read_text(encoding="utf-8"), ipc_proto_path.relative_to(root).as_posix())
    with tempfile.TemporaryDirectory(prefix="rsp-proto-") as out_dir:
        result = protoc.main([
            "protoc",
            f"-I{peer_proto_path.parent}",
            f"-I{ipc_proto_path.parent}",
            f"--python_out={out_dir}",
            str(peer_proto_path),
            str(ipc_proto_path),
        ])
        if result != 0:
            fail(f"Protobuf compilation failed with exit code {result}")
    proto_scopes = normalize_proto_scopes(proto_scopes_raw)
    if openapi_scopes != proto_scopes:
        fail(f"Scope mismatch: OpenAPI-only={openapi_scopes-proto_scopes}, Proto-only={proto_scopes-openapi_scopes}")

    api_states = set(spec["components"]["schemas"]["SessionState"]["enum"])
    sql = (root / "04-backend/database-schema.sql").read_text(encoding="utf-8")
    parsed_sql = parse_sql(sql)
    if not parsed_sql:
        fail("PostgreSQL schema contains no statements")
    sql_states = extract_sql_states(sql)
    if api_states != sql_states:
        fail(f"State mismatch: API-only={api_states-sql_states}, SQL-only={sql_states-api_states}")
    sql_scopes = extract_sql_scopes(sql)
    policy_schema = json.loads((root / "02-protocol/schemas/policy-document.schema.json").read_text(encoding="utf-8"))
    policy_scopes = set(policy_schema["properties"]["rules"]["items"]["properties"]["scopes"]["items"]["enum"] )
    if openapi_scopes != sql_scopes or openapi_scopes != policy_scopes:
        fail(f"Scope contract mismatch: API={openapi_scopes}, SQL={sql_scopes}, Policy={policy_scopes}")
    sql_tables = set(re.findall(r"create\s+table\s+(?:if\s+not\s+exists\s+)?([a-z_][a-z0-9_]*)", sql, re.I))
    required_tables = {"tenants", "users", "memberships", "devices", "device_keys", "device_credential_challenges", "tenant_invitations", "data_export_requests", "tenant_closure_requests", "policies", "policy_versions", "support_sessions", "session_participants", "audit_events", "update_releases", "outbox_messages", "idempotency_records"}
    if not required_tables.issubset(sql_tables):
        fail(f"Database missing required tables: {required_tables - sql_tables}")

    requirement_ids, requirement_text = parse_requirement_ids(root)
    requirement_set = set(requirement_ids)
    trace_path = root / "07-delivery/traceability/requirements-traceability.csv"
    with trace_path.open(encoding="utf-8", newline="") as handle:
        trace_rows = list(csv.DictReader(handle))
    allowed_release_trains = {"ATTENDED_GA", "MANAGED_HOST", "UNATTENDED_GA", "ENTERPRISE_POST_GA", "ALL_RELEASES"}
    traced_ids = [row["id"] for row in trace_rows]
    if len(traced_ids) != len(set(traced_ids)):
        fail("Duplicate traceability requirement ID")
    if requirement_set != set(traced_ids):
        fail(f"Traceability mismatch: missing={requirement_set-set(traced_ids)}, extra={set(traced_ids)-requirement_set}")
    for row in trace_rows:
        if row["requirement"] != requirement_text[row["id"]]:
            fail(f"Traceability text drift: {row['id']}")
        for field in ["design_refs", "goal_refs"]:
            for rel in iter_semicolon_paths(row[field]):
                if not (root / rel).exists():
                    fail(f"Traceability {field} path missing for {row['id']}: {rel}")
        if row.get("priority") not in {"P0", "P1", "P2"}:
            fail(f"Invalid priority for {row['id']}: {row.get('priority')}")
        if row.get("release_train") not in allowed_release_trains:
            fail(f"Invalid release train for {row['id']}: {row.get('release_train')}")
        if row["acceptance_test"] != "AT-" + row["id"]:
            fail(f"Acceptance test ID mismatch in traceability: {row['id']}")

    cases_path = root / "07-delivery/acceptance-test-cases.csv"
    with cases_path.open(encoding="utf-8", newline="") as handle:
        case_rows = list(csv.DictReader(handle))
    case_req_ids = [row["requirement_id"] for row in case_rows]
    test_ids = [row["test_id"] for row in case_rows]
    if len(case_req_ids) != len(set(case_req_ids)) or len(test_ids) != len(set(test_ids)):
        fail("Duplicate acceptance case/test ID")
    if set(case_req_ids) != requirement_set:
        fail(f"Acceptance coverage mismatch: missing={requirement_set-set(case_req_ids)}, extra={set(case_req_ids)-requirement_set}")
    for row in case_rows:
        if row.get("release_train") != next(item["release_train"] for item in trace_rows if item["id"] == row["requirement_id"]):
            fail(f"Release train mismatch: {row['requirement_id']}")
        if row["test_id"] != "AT-" + row["requirement_id"]:
            fail(f"Acceptance ID mismatch: {row['requirement_id']}")
        if len(row.get("level", "").strip()) < 3:
            fail(f"Acceptance field too weak: {row['test_id']} level")
        for field in ["preconditions", "procedure", "pass_criteria", "required_evidence", "automation_expectation"]:
            if len(row.get(field, "").strip()) < 20:
                fail(f"Acceptance field too weak: {row['test_id']} {field}")
    catalog = (root / "07-delivery/acceptance-test-catalog.md").read_text(encoding="utf-8")
    for test_id in test_ids:
        if f"### {test_id} —" not in catalog:
            fail(f"Acceptance catalog missing test: {test_id}")

    header = (root / "02-protocol/native/remote_support_native.h").read_text(encoding="utf-8")
    # Every delivery goal must exist and be referenced by at least one requirement.
    goal_paths = {f"07-delivery/goals/goal-{i:02d}-" for i in range(1, 15)}
    all_goal_refs = {rel for row in trace_rows for rel in iter_semicolon_paths(row["goal_refs"])}
    for prefix in goal_paths:
        matches = [p for p in root.glob(prefix + "*.md")]
        if len(matches) != 1:
            fail(f"Expected one goal file for prefix {prefix}, found {len(matches)}")
        rel = matches[0].relative_to(root).as_posix()
        if rel not in all_goal_refs:
            fail(f"Goal has no mapped requirement: {rel}")

    peer_proto_text = peer_proto_path.read_text(encoding="utf-8")
    for required_message in ["message TransportBinding", "message TransportBindingAck", "RSP-TRANSPORT-BINDING-V1"]:
        if required_message not in peer_proto_text:
            fail(f"Peer protocol missing transport-binding contract: {required_message}")
    for required_sql in ["unique (id, tenant_id)", "tenant_invitations", "data_export_requests", "tenant_closure_requests", "device_credential_challenges", "CREDENTIAL_REFRESH", "KEY_ROTATION", "HOST_PENDING", "foreign key (device_id, tenant_id)", "foreign key (session_id, tenant_id)"]:
        if required_sql not in sql:
            fail(f"Database missing tenant/managed-host invariant: {required_sql}")

    header = (root / "02-protocol/native/remote_support_native.h").read_text(encoding="utf-8")
    required_symbols = {
        "rs_runtime_create", "rs_runtime_enumerate_displays", "rs_capture_create", "rs_encoder_create",
        "rs_runtime_enumerate_encoders", "rs_encoder_reconfigure", "rs_decoder_create",
        "rs_decoder_submit_h264", "rs_renderer_create", "rs_renderer_set_transform",
        "rs_transport_create", "rs_transport_create_offer", "rs_transport_set_remote_description",
        "rs_transport_open_data_channel", "rs_transport_send_data", "rs_input_injector_create",
        "rs_input_release_all",
    }
    missing_symbols = {symbol for symbol in required_symbols if not re.search(rf"\b{re.escape(symbol)}\s*\(", header)}
    if missing_symbols:
        fail(f"Native ABI missing required symbols: {missing_symbols}")

    forbidden = re.compile(r"\b(TODO|TBD|FIXME|CHANGEME|REPLACE_ME)\b")
    for path in bundle_files:
        if path.suffix.lower() in TEXT_SUFFIXES:
            match = forbidden.search(path.read_text(encoding="utf-8"))
            if match:
                fail(f"Unresolved placeholder {match.group(1)} in {path.relative_to(root)}")

    manifest_path = root / "MANIFEST.json"
    if manifest_path.exists():
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        listed = {entry["path"]: entry for entry in manifest["files"]}
        actual = {p.relative_to(root).as_posix(): p for p in bundle_files}
        if set(listed) != set(actual):
            fail(f"Manifest file set mismatch: missing={set(actual)-set(listed)}, extra={set(listed)-set(actual)}")
        if manifest.get("fileCount") != len(actual):
            fail("Manifest fileCount mismatch")
        for rel, path in actual.items():
            payload = path.read_bytes()
            entry = listed[rel]
            if len(payload) != entry["bytes"]:
                fail(f"Manifest byte mismatch: {rel}")
            if hashlib.sha256(payload).hexdigest() != entry["sha256"]:
                fail(f"Manifest hash mismatch: {rel}")

    print(f"Bundle validation passed: {root}")
    print(f"OpenAPI paths/operations: {len(spec['paths'])}/{len(operation_ids)}")
    print(f"Peer/IPC Protobuf messages: {peer_message_count}/{ipc_message_count}")
    print(f"Requirements and acceptance cases: {len(requirement_ids)}")
    print(f"Scopes synchronized across API/peer/SQL/policy: {len(openapi_scopes)}")
    print(f"Session states synchronized: {len(api_states)}")
    print(f"Database tables detected: {len(sql_tables)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
