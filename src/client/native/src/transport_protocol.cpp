#include "transport_protocol.hpp"

#include <bcrypt.h>

namespace {
constexpr uint32_t framing_major = 1;
constexpr uint32_t protocol_major = 1;
constexpr uint32_t protocol_minor = 0;
constexpr uint16_t type_protocol_hello = 20;
constexpr uint16_t type_protocol_hello_ack = 21;
constexpr uint16_t type_heartbeat = 42;
constexpr uint16_t type_transport_binding = 46;
constexpr uint16_t type_transport_binding_ack = 47;
constexpr char binding_domain[] = "RSP-TRANSPORT-BINDING-V1";
constexpr char binding_algorithm[] = "ecdsa-p256-sha256-p1363";

void append_u16_be(std::vector<uint8_t>& output, uint16_t value) {
  output.push_back(static_cast<uint8_t>(value >> 8));
  output.push_back(static_cast<uint8_t>(value));
}

void append_u32_be(std::vector<uint8_t>& output, uint32_t value) {
  output.push_back(static_cast<uint8_t>(value >> 24));
  output.push_back(static_cast<uint8_t>(value >> 16));
  output.push_back(static_cast<uint8_t>(value >> 8));
  output.push_back(static_cast<uint8_t>(value));
}

void append_u64_be(std::vector<uint8_t>& output, uint64_t value) {
  for (int shift = 56; shift >= 0; shift -= 8) output.push_back(static_cast<uint8_t>(value >> shift));
}

uint16_t read_u16_be(const uint8_t* data) {
  return static_cast<uint16_t>(static_cast<uint16_t>(data[0]) << 8 | data[1]);
}

uint32_t read_u32_be(const uint8_t* data) {
  return static_cast<uint32_t>(data[0]) << 24 | static_cast<uint32_t>(data[1]) << 16 |
      static_cast<uint32_t>(data[2]) << 8 | data[3];
}

uint64_t read_u64_be(const uint8_t* data) {
  uint64_t value = 0;
  for (size_t index = 0; index < 8; ++index) value = value << 8 | data[index];
  return value;
}

void append_varint(std::vector<uint8_t>& output, uint64_t value) {
  while (value >= 0x80) {
    output.push_back(static_cast<uint8_t>(value) | 0x80);
    value >>= 7;
  }
  output.push_back(static_cast<uint8_t>(value));
}

void append_key(std::vector<uint8_t>& output, uint32_t field, uint32_t wire) {
  append_varint(output, static_cast<uint64_t>(field) << 3 | wire);
}

void append_varint_field(std::vector<uint8_t>& output, uint32_t field, uint64_t value) {
  if (value == 0) return;
  append_key(output, field, 0);
  append_varint(output, value);
}

void append_bytes_field(std::vector<uint8_t>& output, uint32_t field, const uint8_t* data, size_t length) {
  if (length == 0) return;
  append_key(output, field, 2);
  append_varint(output, length);
  output.insert(output.end(), data, data + length);
}

void append_string_field(std::vector<uint8_t>& output, uint32_t field, const std::string& value) {
  append_bytes_field(output, field, reinterpret_cast<const uint8_t*>(value.data()), value.size());
}

bool read_varint(const uint8_t*& current, const uint8_t* end, uint64_t& value) {
  value = 0;
  for (uint32_t shift = 0; shift < 64 && current < end; shift += 7) {
    const uint8_t byte = *current++;
    value |= static_cast<uint64_t>(byte & 0x7f) << shift;
    if ((byte & 0x80) == 0) return true;
  }
  return false;
}

bool read_length_delimited(const uint8_t*& current, const uint8_t* end, const uint8_t*& value, size_t& length) {
  uint64_t encoded_length = 0;
  if (!read_varint(current, end, encoded_length) || encoded_length > static_cast<uint64_t>(end - current)) return false;
  value = current;
  length = static_cast<size_t>(encoded_length);
  current += length;
  return true;
}

bool skip_value(const uint8_t*& current, const uint8_t* end, uint32_t wire) {
  uint64_t ignored = 0;
  const uint8_t* bytes = nullptr;
  size_t length = 0;
  switch (wire) {
    case 0: return read_varint(current, end, ignored);
    case 1: if (end - current < 8) return false; current += 8; return true;
    case 2: return read_length_delimited(current, end, bytes, length);
    case 5: if (end - current < 4) return false; current += 4; return true;
    default: return false;
  }
}

bool assign_string(const uint8_t* value, size_t length, size_t maximum, std::string& output) {
  if (length == 0 || length > maximum || std::find(value, value + length, 0) != value + length) return false;
  output.assign(reinterpret_cast<const char*>(value), length);
  return true;
}

std::optional<uint32_t> scope_number(const std::string& scope) {
  static const std::map<std::string, uint32_t> values{
      {"VIEW_SCREEN", 1}, {"CONTROL_POINTER", 2}, {"CONTROL_KEYBOARD", 3},
      {"SYNC_CLIPBOARD_TEXT_HOST_TO_OPERATOR", 4}, {"SYNC_CLIPBOARD_TEXT_OPERATOR_TO_HOST", 5},
      {"TRANSFER_FILE_HOST_TO_OPERATOR", 6}, {"TRANSFER_FILE_OPERATOR_TO_HOST", 7}, {"CHAT", 8},
      {"SWITCH_MONITOR", 9}, {"REQUEST_REBOOT", 10}, {"RECONNECT_AFTER_REBOOT", 11}, {"UNATTENDED_SESSION", 12}};
  const auto found = values.find(scope);
  return found == values.end() ? std::nullopt : std::optional<uint32_t>(found->second);
}

std::optional<std::string> scope_name(uint64_t value) {
  static const std::array<const char*, 13> names{"", "VIEW_SCREEN", "CONTROL_POINTER", "CONTROL_KEYBOARD",
      "SYNC_CLIPBOARD_TEXT_HOST_TO_OPERATOR", "SYNC_CLIPBOARD_TEXT_OPERATOR_TO_HOST", "TRANSFER_FILE_HOST_TO_OPERATOR",
      "TRANSFER_FILE_OPERATOR_TO_HOST", "CHAT", "SWITCH_MONITOR", "REQUEST_REBOOT", "RECONNECT_AFTER_REBOOT",
      "UNATTENDED_SESSION"};
  if (value == 0 || value >= names.size()) return std::nullopt;
  return std::string(names[static_cast<size_t>(value)]);
}

void append_canonical_string(std::vector<uint8_t>& output, const std::string& value) {
  append_u32_be(output, static_cast<uint32_t>(value.size()));
  output.insert(output.end(), value.begin(), value.end());
}

std::vector<uint8_t> canonical_binding(const parsed_transport_binding& binding) {
  std::vector<uint8_t> output;
  append_canonical_string(output, binding_domain);
  append_canonical_string(output, binding.binding_id);
  append_canonical_string(output, binding.session_id);
  append_canonical_string(output, binding.sender_peer_id);
  append_u32_be(output, static_cast<uint32_t>(binding.sender_role));
  append_u64_be(output, binding.transport_epoch);
  append_u64_be(output, binding.permission_revision);
  auto scopes = binding.scopes;
  std::sort(scopes.begin(), scopes.end());
  append_u32_be(output, static_cast<uint32_t>(scopes.size()));
  for (const auto& scope : scopes) append_canonical_string(output, scope);
  output.insert(output.end(), binding.local_fingerprint.begin(), binding.local_fingerprint.end());
  output.insert(output.end(), binding.remote_fingerprint.begin(), binding.remote_fingerprint.end());
  output.insert(output.end(), binding.authorization_context.begin(), binding.authorization_context.end());
  append_canonical_string(output, binding.key_id);
  return output;
}

std::string random_uuid() {
  std::array<uint8_t, 16> bytes{};
  BCryptGenRandom(nullptr, bytes.data(), static_cast<ULONG>(bytes.size()), BCRYPT_USE_SYSTEM_PREFERRED_RNG);
  bytes[6] = static_cast<uint8_t>((bytes[6] & 0x0f) | 0x40);
  bytes[8] = static_cast<uint8_t>((bytes[8] & 0x3f) | 0x80);
  constexpr char hex[] = "0123456789abcdef";
  std::string value;
  for (size_t index = 0; index < bytes.size(); ++index) {
    if (index == 4 || index == 6 || index == 8 || index == 10) value.push_back('-');
    value.push_back(hex[bytes[index] >> 4]);
    value.push_back(hex[bytes[index] & 0xf]);
  }
  return value;
}

std::vector<uint8_t> make_envelope(rs_transport_t* transport, uint16_t body_type, const std::vector<uint8_t>& body) {
  std::vector<uint8_t> envelope;
  append_varint_field(envelope, 1, protocol_major);
  append_varint_field(envelope, 2, protocol_minor);
  append_string_field(envelope, 3, random_uuid());
  append_string_field(envelope, 4, transport->session_id);
  append_string_field(envelope, 5, transport->local_peer_id);
  append_varint_field(envelope, 6, transport->binding.local_role);
  append_varint_field(envelope, 7, transport->transport_epoch);
  append_varint_field(envelope, 8, transport->binding.permission_revision);
  append_varint_field(envelope, 9, transport->control_outgoing_sequence);
  append_varint_field(envelope, 10, monotonic_nanoseconds());
  append_bytes_field(envelope, body_type, body.data(), body.size());
  return envelope;
}

std::vector<uint8_t> frame_envelope(rs_transport_t* transport, uint16_t type, const std::vector<uint8_t>& body) {
  transport->control_outgoing_sequence++;
  const auto envelope = make_envelope(transport, type, body);
  std::vector<uint8_t> frame;
  frame.insert(frame.end(), {'R', 'S', 'P', '1'});
  append_u16_be(frame, framing_major);
  append_u16_be(frame, type);
  append_u16_be(frame, 0);
  append_u16_be(frame, 0);
  append_u64_be(frame, transport->control_outgoing_sequence);
  append_u32_be(frame, static_cast<uint32_t>(envelope.size()));
  frame.insert(frame.end(), envelope.begin(), envelope.end());
  return frame;
}

bool parse_envelope(const uint8_t* data, size_t length, uint16_t hinted_type, parsed_control_frame& output, std::string& error) {
  const uint8_t* current = data;
  const uint8_t* end = data + length;
  while (current < end) {
    uint64_t key = 0;
    if (!read_varint(current, end, key)) { error = "SIGNAL_PROTOCOL_INVALID"; return false; }
    const uint32_t field = static_cast<uint32_t>(key >> 3);
    const uint32_t wire = static_cast<uint32_t>(key & 7);
    uint64_t value = 0;
    const uint8_t* bytes = nullptr;
    size_t bytes_length = 0;
    if (wire == 0 && (field == 1 || field == 2 || (field >= 6 && field <= 10))) {
      if (!read_varint(current, end, value)) { error = "SIGNAL_PROTOCOL_INVALID"; return false; }
      if (field == 1) output.protocol_major = static_cast<uint32_t>(value);
      else if (field == 2) output.protocol_minor = static_cast<uint32_t>(value);
      else if (field == 6) output.sender_role = static_cast<rs_peer_role_v1>(value);
      else if (field == 7) output.transport_epoch = value;
      else if (field == 8) output.permission_revision = value;
      else if (field == 9) output.envelope_sequence = value;
      continue;
    }
    if (wire == 2 && (field == 4 || field == 5 || field == hinted_type)) {
      if (!read_length_delimited(current, end, bytes, bytes_length)) { error = "SIGNAL_PROTOCOL_INVALID"; return false; }
      if (field == 4 && !assign_string(bytes, bytes_length, 128, output.session_id)) return false;
      if (field == 5 && !assign_string(bytes, bytes_length, 128, output.sender_peer_id)) return false;
      if (field == hinted_type) output.body.assign(bytes, bytes + bytes_length);
      continue;
    }
    if (!skip_value(current, end, wire)) { error = "SIGNAL_PROTOCOL_INVALID"; return false; }
  }
  if (output.protocol_major != protocol_major || output.body.empty()) { error = "PROTOCOL_VERSION_UNSUPPORTED"; return false; }
  return true;
}
}

std::vector<uint8_t> make_transport_binding_frame(rs_transport_t* transport) {
  parsed_transport_binding binding;
  binding.binding_id = random_uuid();
  binding.session_id = transport->session_id;
  binding.sender_peer_id = transport->local_peer_id;
  binding.sender_role = transport->binding.local_role;
  binding.transport_epoch = transport->transport_epoch;
  binding.permission_revision = transport->binding.permission_revision;
  binding.scopes = transport->binding.scopes;
  binding.local_fingerprint = transport->local_dtls_fingerprint;
  binding.remote_fingerprint = transport->remote_dtls_fingerprint;
  binding.authorization_context = transport->binding.authorization_context;
  binding.key_id = transport->binding.local_key_id;
  binding.canonicalization_version = binding_domain;
  binding.signature_algorithm = binding_algorithm;
  std::array<uint8_t, 32> canonical_hash{};
  const auto canonical = canonical_binding(binding);
  if (!sha256_bytes(canonical.data(), canonical.size(), canonical_hash) ||
      !sign_p256_sha256(transport->binding, canonical_hash, binding.signature)) return {};
  std::vector<uint8_t> body;
  append_string_field(body, 1, binding.binding_id);
  append_string_field(body, 2, binding.session_id);
  append_string_field(body, 3, binding.sender_peer_id);
  append_varint_field(body, 4, binding.sender_role);
  append_varint_field(body, 5, binding.transport_epoch);
  append_varint_field(body, 6, binding.permission_revision);
  for (const auto& scope : binding.scopes) {
    const auto number = scope_number(scope);
    if (!number.has_value()) return {};
    append_varint_field(body, 7, *number);
  }
  append_bytes_field(body, 8, binding.local_fingerprint.data(), binding.local_fingerprint.size());
  append_bytes_field(body, 9, binding.remote_fingerprint.data(), binding.remote_fingerprint.size());
  append_bytes_field(body, 10, binding.authorization_context.data(), binding.authorization_context.size());
  append_string_field(body, 11, binding.key_id);
  append_bytes_field(body, 12, binding.signature.data(), binding.signature.size());
  append_string_field(body, 13, binding.canonicalization_version);
  append_string_field(body, 14, binding.signature_algorithm);
  sha256_bytes(body.data(), body.size(), transport->local_binding_hash);
  transport->local_binding_id = binding.binding_id;
  return frame_envelope(transport, type_transport_binding, body);
}

std::vector<uint8_t> make_transport_binding_ack_frame(rs_transport_t* transport,
    const std::string& binding_id, bool verified, const char* reason, const std::array<uint8_t, 32>& binding_hash) {
  std::vector<uint8_t> body;
  append_string_field(body, 1, binding_id);
  append_varint_field(body, 2, verified ? 1 : 0);
  append_string_field(body, 3, reason == nullptr ? std::string() : std::string(reason));
  append_bytes_field(body, 4, binding_hash.data(), binding_hash.size());
  return frame_envelope(transport, type_transport_binding_ack, body);
}

std::vector<uint8_t> make_protocol_hello_frame(rs_transport_t* transport) {
  std::vector<uint8_t> body;
  append_string_field(body, 1, "remote-support-native/0.4");
  append_string_field(body, 2, "windows");
  append_string_field(body, 3, "x64");
  append_varint_field(body, 4, transport->binding.local_role);
  append_string_field(body, 5, "H264");
  append_string_field(body, 6, "transport-binding-v1");
  for (const auto& scope : transport->binding.scopes) {
    const auto number = scope_number(scope);
    if (number.has_value()) append_varint_field(body, 7, *number);
  }
  append_varint_field(body, 8, transport->max_data_message_bytes);
  append_varint_field(body, 9, 1'048'576);
  return frame_envelope(transport, type_protocol_hello, body);
}

std::vector<uint8_t> make_protocol_hello_ack_frame(rs_transport_t* transport, bool accepted, const char* rejection_code) {
  std::vector<uint8_t> body;
  append_varint_field(body, 1, accepted ? 1 : 0);
  append_varint_field(body, 2, protocol_major);
  append_varint_field(body, 3, protocol_minor);
  append_string_field(body, 4, "transport-binding-v1");
  append_varint_field(body, 5, transport->max_data_message_bytes);
  append_varint_field(body, 6, 1'048'576);
  if (rejection_code != nullptr) append_string_field(body, 7, rejection_code);
  return frame_envelope(transport, type_protocol_hello_ack, body);
}

std::vector<uint8_t> make_heartbeat_frame(rs_transport_t* transport, uint64_t nonce) {
  std::vector<uint8_t> body;
  append_varint_field(body, 1, transport->control_incoming_sequence);
  append_varint_field(body, 3, transport->binding.permission_revision);
  append_varint_field(body, 4, nonce);
  append_varint_field(body, 5, transport->heartbeat_rtt_ms.load());
  return frame_envelope(transport, type_heartbeat, body);
}

bool parse_control_frame(const uint8_t* data, size_t length, uint32_t maximum_bytes,
    parsed_control_frame& output, std::string& error) {
  if (data == nullptr || length < 24 || length > maximum_bytes || std::memcmp(data, "RSP1", 4) != 0 ||
      read_u16_be(data + 4) != framing_major || read_u16_be(data + 10) != 0) {
    error = length > maximum_bytes ? "TRANSPORT_MESSAGE_TOO_LARGE" : "SIGNAL_PROTOCOL_INVALID";
    return false;
  }
  output.message_type = read_u16_be(data + 6);
  output.channel_sequence = read_u64_be(data + 12);
  const uint32_t payload_length = read_u32_be(data + 20);
  if (payload_length != length - 24 || output.channel_sequence == 0) { error = "SIGNAL_PROTOCOL_INVALID"; return false; }
  if (!parse_envelope(data + 24, payload_length, output.message_type, output, error)) return false;
  if (output.envelope_sequence != output.channel_sequence) { error = "SIGNAL_PROTOCOL_INVALID"; return false; }
  return true;
}

bool parse_transport_binding(const std::vector<uint8_t>& body, parsed_transport_binding& output, std::string& error) {
  const uint8_t* current = body.data();
  const uint8_t* end = current + body.size();
  while (current < end) {
    uint64_t key = 0;
    if (!read_varint(current, end, key)) return false;
    const uint32_t field = static_cast<uint32_t>(key >> 3);
    const uint32_t wire = static_cast<uint32_t>(key & 7);
    uint64_t number = 0;
    const uint8_t* bytes = nullptr;
    size_t length = 0;
    if (wire == 0 && field >= 4 && field <= 7) {
      if (!read_varint(current, end, number)) return false;
      if (field == 4) output.sender_role = static_cast<rs_peer_role_v1>(number);
      else if (field == 5) output.transport_epoch = number;
      else if (field == 6) output.permission_revision = number;
      else { const auto scope = scope_name(number); if (!scope.has_value()) return false; output.scopes.push_back(*scope); }
      continue;
    }
    if (wire == 2 && field >= 1 && field <= 14) {
      if (!read_length_delimited(current, end, bytes, length)) return false;
      if (field == 1 && !assign_string(bytes, length, 64, output.binding_id)) return false;
      else if (field == 2 && !assign_string(bytes, length, 128, output.session_id)) return false;
      else if (field == 3 && !assign_string(bytes, length, 128, output.sender_peer_id)) return false;
      else if (field >= 8 && field <= 10) {
        if (length != 32) return false;
        auto* target = field == 8 ? output.local_fingerprint.data() : field == 9 ? output.remote_fingerprint.data() : output.authorization_context.data();
        std::memcpy(target, bytes, 32);
      } else if (field == 11 && !assign_string(bytes, length, 128, output.key_id)) return false;
      else if (field == 12) { if (length != 64) return false; std::memcpy(output.signature.data(), bytes, 64); }
      else if (field == 13 && !assign_string(bytes, length, 64, output.canonicalization_version)) return false;
      else if (field == 14 && !assign_string(bytes, length, 64, output.signature_algorithm)) return false;
      continue;
    }
    if (!skip_value(current, end, wire)) return false;
  }
  if (output.binding_id.empty() || output.session_id.empty() || output.sender_peer_id.empty() || output.key_id.empty()) {
    error = "TRANSPORT_BINDING_INVALID"; return false;
  }
  return true;
}

bool verify_transport_binding(rs_transport_t* transport, const parsed_transport_binding& binding,
    const std::vector<uint8_t>& serialized_body, std::array<uint8_t, 32>& binding_hash, std::string& error) {
  if (binding.session_id != transport->session_id || binding.sender_peer_id != transport->binding.remote_peer_id ||
      binding.sender_role != transport->binding.remote_role || binding.transport_epoch != transport->transport_epoch ||
      binding.permission_revision != transport->binding.permission_revision || binding.authorization_context != transport->binding.authorization_context ||
      binding.local_fingerprint != transport->remote_dtls_fingerprint || binding.remote_fingerprint != transport->local_dtls_fingerprint ||
      binding.key_id != transport->binding.remote_key_id || binding.canonicalization_version != binding_domain ||
      binding.signature_algorithm != binding_algorithm) {
    error = "TRANSPORT_BINDING_CONTEXT_MISMATCH"; return false;
  }
  auto received_scopes = binding.scopes;
  auto expected_scopes = transport->binding.scopes;
  std::sort(received_scopes.begin(), received_scopes.end());
  std::sort(expected_scopes.begin(), expected_scopes.end());
  if (received_scopes != expected_scopes) { error = "TRANSPORT_BINDING_SCOPE_MISMATCH"; return false; }
  std::array<uint8_t, 32> canonical_hash{};
  const auto canonical = canonical_binding(binding);
  if (!sha256_bytes(canonical.data(), canonical.size(), canonical_hash) ||
      !verify_p256_sha256(transport->binding.remote_public_key, canonical_hash, binding.signature.data(), binding.signature.size())) {
    error = "TRANSPORT_BINDING_SIGNATURE_INVALID"; return false;
  }
  if (!sha256_bytes(serialized_body.data(), serialized_body.size(), binding_hash)) { error = "TRANSPORT_BINDING_HASH_FAILED"; return false; }
  return true;
}

bool parse_binding_ack(const std::vector<uint8_t>& body, std::string& binding_id, bool& verified,
    std::string& reason, std::array<uint8_t, 32>& binding_hash, std::string& error) {
  const uint8_t* current = body.data(); const uint8_t* end = current + body.size();
  while (current < end) {
    uint64_t key = 0; if (!read_varint(current, end, key)) return false;
    const uint32_t field = static_cast<uint32_t>(key >> 3); const uint32_t wire = static_cast<uint32_t>(key & 7);
    if (field == 2 && wire == 0) { uint64_t value = 0; if (!read_varint(current, end, value)) return false; verified = value != 0; continue; }
    if ((field == 1 || field == 3 || field == 4) && wire == 2) {
      const uint8_t* bytes = nullptr; size_t length = 0; if (!read_length_delimited(current, end, bytes, length)) return false;
      if (field == 1 && !assign_string(bytes, length, 64, binding_id)) return false;
      else if (field == 3 && length != 0 && !assign_string(bytes, length, 128, reason)) return false;
      else if (field == 4) { if (length != 32) return false; std::memcpy(binding_hash.data(), bytes, 32); }
      continue;
    }
    if (!skip_value(current, end, wire)) return false;
  }
  if (binding_id.empty()) { error = "TRANSPORT_BINDING_ACK_INVALID"; return false; }
  return true;
}

bool parse_protocol_hello(const std::vector<uint8_t>& body, uint32_t& maximum_message_bytes, std::string& error) {
  const uint8_t* current = body.data(); const uint8_t* end = current + body.size();
  while (current < end) {
    uint64_t key = 0; if (!read_varint(current, end, key)) return false;
    const uint32_t field = static_cast<uint32_t>(key >> 3); const uint32_t wire = static_cast<uint32_t>(key & 7);
    if (field == 8 && wire == 0) { uint64_t value = 0; if (!read_varint(current, end, value) || value > 262'144) return false; maximum_message_bytes = static_cast<uint32_t>(value); continue; }
    if (!skip_value(current, end, wire)) return false;
  }
  if (maximum_message_bytes < 1024) { error = "PROTOCOL_LIMIT_INVALID"; return false; }
  return true;
}

bool parse_protocol_hello_ack(const std::vector<uint8_t>& body, bool& accepted, std::string& rejection_code, std::string& error) {
  uint32_t major = 0;
  const uint8_t* current = body.data(); const uint8_t* end = current + body.size();
  while (current < end) {
    uint64_t key = 0; if (!read_varint(current, end, key)) return false;
    const uint32_t field = static_cast<uint32_t>(key >> 3); const uint32_t wire = static_cast<uint32_t>(key & 7);
    if ((field == 1 || field == 2) && wire == 0) { uint64_t value = 0; if (!read_varint(current, end, value)) return false; if (field == 1) accepted = value != 0; else major = static_cast<uint32_t>(value); continue; }
    if (field == 7 && wire == 2) { const uint8_t* bytes = nullptr; size_t length = 0; if (!read_length_delimited(current, end, bytes, length) || !assign_string(bytes, length, 128, rejection_code)) return false; continue; }
    if (!skip_value(current, end, wire)) return false;
  }
  if (major != protocol_major) { error = "PROTOCOL_VERSION_UNSUPPORTED"; return false; }
  return true;
}
