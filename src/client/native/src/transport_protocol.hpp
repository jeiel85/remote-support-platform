#pragma once

#include "native_internal.hpp"

struct parsed_transport_binding {
  std::string binding_id;
  std::string session_id;
  std::string sender_peer_id;
  rs_peer_role_v1 sender_role{RS_PEER_ROLE_UNKNOWN};
  uint64_t transport_epoch{};
  uint64_t permission_revision{};
  std::vector<std::string> scopes;
  std::array<uint8_t, 32> local_fingerprint{};
  std::array<uint8_t, 32> remote_fingerprint{};
  std::array<uint8_t, 32> authorization_context{};
  std::string key_id;
  std::array<uint8_t, 64> signature{};
  std::string canonicalization_version;
  std::string signature_algorithm;
};

struct parsed_control_frame {
  uint16_t message_type{};
  uint64_t channel_sequence{};
  uint32_t protocol_major{};
  uint32_t protocol_minor{};
  std::string session_id;
  std::string sender_peer_id;
  rs_peer_role_v1 sender_role{RS_PEER_ROLE_UNKNOWN};
  uint64_t transport_epoch{};
  uint64_t permission_revision{};
  uint64_t envelope_sequence{};
  std::vector<uint8_t> body;
};

struct parsed_permission_state {
  uint64_t revision{};
  std::vector<std::string> active_scopes;
  std::vector<std::string> revoked_scopes;
  uint64_t effective_at_reliable_input_sequence{};
  std::string reason_code;
};

std::vector<uint8_t> make_transport_binding_frame(rs_transport_t* transport);
std::vector<uint8_t> make_transport_binding_ack_frame(rs_transport_t* transport,
    const std::string& binding_id, bool verified, const char* reason, const std::array<uint8_t, 32>& binding_hash);
std::vector<uint8_t> make_protocol_hello_frame(rs_transport_t* transport);
std::vector<uint8_t> make_protocol_hello_ack_frame(rs_transport_t* transport, bool accepted, const char* rejection_code);
std::vector<uint8_t> make_heartbeat_frame(rs_transport_t* transport, uint64_t nonce);
std::vector<uint8_t> make_permission_state_frame(rs_transport_t* transport,
    const std::vector<std::string>& active_scopes, const std::vector<std::string>& revoked_scopes,
    uint64_t effective_at_reliable_input_sequence, const std::string& reason_code);
bool parse_control_frame(const uint8_t* data, size_t length, uint32_t maximum_bytes,
    parsed_control_frame& output, std::string& error);
bool parse_transport_binding(const std::vector<uint8_t>& body, parsed_transport_binding& output, std::string& error);
bool verify_transport_binding(rs_transport_t* transport, const parsed_transport_binding& binding,
    const std::vector<uint8_t>& serialized_body, std::array<uint8_t, 32>& binding_hash, std::string& error);
bool parse_binding_ack(const std::vector<uint8_t>& body, std::string& binding_id, bool& verified,
    std::string& reason, std::array<uint8_t, 32>& binding_hash, std::string& error);
bool parse_protocol_hello(const std::vector<uint8_t>& body, uint32_t& maximum_message_bytes, std::string& error);
bool parse_protocol_hello_ack(const std::vector<uint8_t>& body, bool& accepted, std::string& rejection_code, std::string& error);
bool parse_permission_state(const std::vector<uint8_t>& body, parsed_permission_state& output, std::string& error);
