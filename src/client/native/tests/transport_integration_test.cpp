#include "remote_support_native.h"

#include <array>
#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstring>
#include <mutex>
#include <string>
#include <thread>
#include <utility>
#include <vector>

namespace {
struct description_record { rs_sdp_type_v1 type{}; std::string sdp; };
struct candidate_record { std::string mid; std::string candidate; };

struct peer_context {
  std::mutex mutex;
  std::vector<description_record> descriptions;
  std::vector<candidate_record> candidates;
  std::atomic<bool> binding_verified{};
  std::atomic<bool> failed{};
  std::atomic<uint32_t> application_channel{RS_DATA_CHANNEL_ID_INVALID};
  std::atomic<uint32_t> received_messages{};
  std::string received_payload;
};

void RS_CALL on_description(void* pointer, const rs_session_description_v1* description) {
  auto* context = static_cast<peer_context*>(pointer);
  std::scoped_lock lock(context->mutex);
  context->descriptions.push_back({description->type,
      std::string(description->sdp_utf8.data, description->sdp_utf8.length)});
}

void RS_CALL on_candidate(void* pointer, const rs_ice_candidate_v1* candidate) {
  auto* context = static_cast<peer_context*>(pointer);
  std::scoped_lock lock(context->mutex);
  context->candidates.push_back({
      std::string(candidate->sdp_mid_utf8.data, candidate->sdp_mid_utf8.length),
      std::string(candidate->candidate_utf8.data, candidate->candidate_utf8.length)});
}

void RS_CALL on_binding(void* pointer, rs_transport_binding_state_v1 state, rs_string_view_v1) {
  auto* context = static_cast<peer_context*>(pointer);
  if (state == RS_TRANSPORT_BINDING_VERIFIED) context->binding_verified.store(true);
  if (state == RS_TRANSPORT_BINDING_FAILED) context->failed.store(true);
}

void RS_CALL on_state(void* pointer, rs_transport_state_v1 state, rs_string_view_v1) {
  if (state == RS_TRANSPORT_STATE_FAILED) static_cast<peer_context*>(pointer)->failed.store(true);
}

void RS_CALL on_channel(void* pointer, uint32_t channel_id, rs_string_view_v1 label,
    rs_data_channel_state_v1 state) {
  if (state != RS_DATA_CHANNEL_STATE_OPEN) return;
  auto* context = static_cast<peer_context*>(pointer);
  const std::string value(label.data, label.length);
  if (value == "integration.reliable") context->application_channel.store(channel_id);
}

void RS_CALL on_message(void* pointer, const rs_data_message_v1* message) {
  auto* context = static_cast<peer_context*>(pointer);
  {
    std::scoped_lock lock(context->mutex);
    context->received_payload.assign(reinterpret_cast<const char*>(message->payload.data), message->payload.length);
  }
  context->received_messages.fetch_add(1);
}

void RS_CALL on_error(void* pointer, rs_status_v1, rs_string_view_v1) {
  static_cast<peer_context*>(pointer)->failed.store(true);
}

rs_callbacks_v1 callbacks_for(peer_context* context) {
  rs_callbacks_v1 callbacks{};
  callbacks.struct_size = sizeof(callbacks);
  callbacks.user_context = context;
  callbacks.on_error = on_error;
  callbacks.on_transport_state = on_state;
  callbacks.on_local_description = on_description;
  callbacks.on_local_ice_candidate = on_candidate;
  callbacks.on_data_channel_state = on_channel;
  callbacks.on_data_message = on_message;
  callbacks.on_transport_binding_state = on_binding;
  return callbacks;
}

bool take_description(peer_context& context, description_record& output) {
  std::scoped_lock lock(context.mutex);
  if (context.descriptions.empty()) return false;
  output = std::move(context.descriptions.front());
  context.descriptions.erase(context.descriptions.begin());
  return true;
}

bool take_candidate(peer_context& context, candidate_record& output) {
  std::scoped_lock lock(context.mutex);
  if (context.candidates.empty()) return false;
  output = std::move(context.candidates.front());
  context.candidates.erase(context.candidates.begin());
  return true;
}

bool apply_description(rs_transport_handle target, const description_record& record) {
  rs_session_description_v1 description{};
  description.struct_size = sizeof(description);
  description.type = record.type;
  description.sdp_utf8 = {record.sdp.data(), static_cast<uint32_t>(record.sdp.size())};
  return rs_transport_set_remote_description(target, &description) == RS_STATUS_OK;
}

bool apply_candidate(rs_transport_handle target, const candidate_record& record) {
  rs_ice_candidate_v1 candidate{};
  candidate.struct_size = sizeof(candidate);
  candidate.sdp_mid_utf8 = {record.mid.data(), static_cast<uint32_t>(record.mid.size())};
  candidate.sdp_mline_index = -1;
  candidate.candidate_utf8 = {record.candidate.data(), static_cast<uint32_t>(record.candidate.size())};
  return rs_transport_add_remote_ice_candidate(target, &candidate) == RS_STATUS_OK;
}

struct peer {
  peer_context context;
  rs_runtime_handle runtime{};
  rs_transport_handle transport{};
  rs_peer_key_pair_v1 keys{sizeof(keys), {}, {}};
};

bool create_runtime(peer& value) {
  rs_runtime_options_v1 options{};
  options.struct_size = sizeof(options);
  options.requested_abi_major = RS_NATIVE_ABI_MAJOR;
  options.requested_abi_minor = RS_NATIVE_ABI_MINOR;
  const auto callbacks = callbacks_for(&value.context);
  return rs_runtime_create(&options, &callbacks, &value.runtime) == RS_STATUS_OK &&
      rs_runtime_generate_peer_key_pair(value.runtime, &value.keys) == RS_STATUS_OK;
}

bool create_transport(peer& local, const peer& remote, const char* local_id, const char* remote_id,
    rs_peer_role_v1 local_role, rs_peer_role_v1 remote_role, const std::array<uint8_t, 32>& authorization,
    uint32_t flags = 0, uint64_t epoch = 11, uint32_t scope_count = 3) {
  const rs_string_view_v1 scopes[]{{"VIEW_SCREEN", 11}, {"CONTROL_POINTER", 15}, {"CONTROL_KEYBOARD", 16}};
  rs_transport_binding_options_v1 binding{};
  binding.struct_size = sizeof(binding);
  binding.remote_peer_id_utf8 = {remote_id, static_cast<uint32_t>(std::strlen(remote_id))};
  binding.local_role = local_role;
  binding.remote_role = remote_role;
  binding.permission_revision = 7;
  binding.granted_scopes_utf8 = scopes;
  binding.granted_scope_count = scope_count;
  binding.authorization_context_sha256 = {authorization.data(), static_cast<uint32_t>(authorization.size())};
  binding.local_private_key_p256 = {local.keys.private_key_p256, 32};
  binding.local_public_key_uncompressed_p256 = {local.keys.public_key_uncompressed_p256, 65};
  binding.remote_public_key_uncompressed_p256 = {remote.keys.public_key_uncompressed_p256, 65};
  binding.local_key_id_utf8 = {local_id, static_cast<uint32_t>(std::strlen(local_id))};
  binding.remote_key_id_utf8 = {remote_id, static_cast<uint32_t>(std::strlen(remote_id))};
  rs_transport_options_v1 options{};
  options.struct_size = sizeof(options);
  options.session_id_utf8 = {"integration-session", 19};
  options.local_peer_id_utf8 = {local_id, static_cast<uint32_t>(std::strlen(local_id))};
  options.transport_epoch = epoch;
  options.video_input_mode = RS_VIDEO_INPUT_MODE_ENCODED_H264;
  options.max_data_message_bytes = 64 * 1024;
  options.buffered_amount_low_threshold_bytes = 32 * 1024;
  options.flags = flags;
  options.binding = &binding;
  return rs_transport_create(local.runtime, &options, &local.transport) == RS_STATUS_OK;
}

void destroy_peer(peer& value) {
  rs_transport_destroy(value.transport);
  value.transport = nullptr;
  rs_runtime_destroy(value.runtime);
  value.runtime = nullptr;
}
}

int main(int argc, char** argv) {
  peer host;
  peer oper;
  if (!create_runtime(host) || !create_runtime(oper)) return 1;
  std::array<uint8_t, 32> authorization{};
  for (size_t index = 0; index < authorization.size(); ++index) authorization[index] = static_cast<uint8_t>(index + 1);
  const std::string mode = argc > 1 ? argv[1] : "normal";
  auto operator_authorization = authorization;
  uint32_t host_flags = 0;
  uint64_t operator_epoch = 11;
  uint32_t operator_scope_count = 3;
  if (mode == "authorization-mismatch") operator_authorization[0] ^= 0xff;
  else if (mode == "scope-mismatch") operator_scope_count = 2;
  else if (mode == "epoch-mismatch") operator_epoch = 12;
  else if (mode == "fingerprint-mismatch") host_flags = 0x80000000u;
  else if (mode == "binding-replay") host_flags = 0x40000000u;
  else if (mode == "protocol-version") host_flags = 0x20000000u;
  else if (mode != "normal") return 15;
  const bool expect_failure = mode != "normal";
  if (!create_transport(host, oper, "host-peer", "operator-peer", RS_PEER_ROLE_HOST, RS_PEER_ROLE_OPERATOR,
          authorization, host_flags) ||
      !create_transport(oper, host, "operator-peer", "host-peer", RS_PEER_ROLE_OPERATOR, RS_PEER_ROLE_HOST,
          operator_authorization, 0, operator_epoch, operator_scope_count)) return 2;
  if (rs_transport_create_offer(host.transport, 0) != RS_STATUS_OK) return 3;

  bool operator_answered = false;
  bool host_has_remote = false;
  bool operator_has_remote = false;
  std::vector<candidate_record> host_candidates;
  std::vector<candidate_record> operator_candidates;
  const auto binding_deadline = std::chrono::steady_clock::now() + std::chrono::seconds(15);
  while ((!host.context.binding_verified.load() || !oper.context.binding_verified.load()) &&
      std::chrono::steady_clock::now() < binding_deadline && !host.context.failed.load() && !oper.context.failed.load()) {
    description_record description;
    while (take_description(host.context, description)) {
      if (!apply_description(oper.transport, description)) return 4;
      operator_has_remote = true;
      if (description.type == RS_SDP_TYPE_OFFER && !operator_answered) {
        if (rs_transport_create_answer(oper.transport) != RS_STATUS_OK) return 5;
        operator_answered = true;
      }
    }
    while (take_description(oper.context, description)) {
      if (!apply_description(host.transport, description)) return 6;
      host_has_remote = true;
    }
    candidate_record candidate;
    while (take_candidate(host.context, candidate)) host_candidates.push_back(std::move(candidate));
    while (take_candidate(oper.context, candidate)) operator_candidates.push_back(std::move(candidate));
    if (operator_has_remote) {
      for (const auto& value : host_candidates) if (!apply_candidate(oper.transport, value)) return 7;
      host_candidates.clear();
    }
    if (host_has_remote) {
      for (const auto& value : operator_candidates) if (!apply_candidate(host.transport, value)) return 8;
      operator_candidates.clear();
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(5));
  }
  if (expect_failure) {
    const bool failed = host.context.failed.load() || oper.context.failed.load();
    destroy_peer(host);
    destroy_peer(oper);
    return failed ? 0 : 16;
  }
  if (!host.context.binding_verified.load() || !oper.context.binding_verified.load() ||
      host.context.failed.load() || oper.context.failed.load()) return 9;

  rs_data_channel_options_v1 channel{};
  channel.struct_size = sizeof(channel);
  channel.label_utf8 = {"integration.reliable", 20};
  channel.ordered = 1;
  channel.max_retransmits = -1;
  channel.max_packet_lifetime_ms = -1;
  channel.negotiated_id = -1;
  uint32_t host_channel = RS_DATA_CHANNEL_ID_INVALID;
  if (rs_transport_open_data_channel(host.transport, &channel, &host_channel) != RS_STATUS_OK) return 10;
  const auto channel_deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
  while ((host.context.application_channel.load() == RS_DATA_CHANNEL_ID_INVALID ||
          oper.context.application_channel.load() == RS_DATA_CHANNEL_ID_INVALID) &&
      std::chrono::steady_clock::now() < channel_deadline) std::this_thread::sleep_for(std::chrono::milliseconds(5));
  if (host.context.application_channel.load() != host_channel ||
      oper.context.application_channel.load() == RS_DATA_CHANNEL_ID_INVALID) return 11;

  const std::string payload = "actual-dtls-bound-data";
  rs_data_message_v1 message{};
  message.struct_size = sizeof(message);
  message.channel_id = host_channel;
  message.binary = 1;
  message.payload = {reinterpret_cast<const uint8_t*>(payload.data()), static_cast<uint32_t>(payload.size())};
  if (rs_transport_send_data(host.transport, &message) != RS_STATUS_OK) return 12;
  std::vector<uint8_t> oversized(64 * 1024 + 1, 0x5a);
  rs_data_message_v1 oversized_message{};
  oversized_message.struct_size = sizeof(oversized_message);
  oversized_message.channel_id = host_channel;
  oversized_message.binary = 1;
  oversized_message.payload = {oversized.data(), static_cast<uint32_t>(oversized.size())};
  if (rs_transport_send_data(host.transport, &oversized_message) != RS_STATUS_INVALID_ARGUMENT) return 17;
  const auto message_deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
  while (oper.context.received_messages.load() == 0 && std::chrono::steady_clock::now() < message_deadline)
    std::this_thread::sleep_for(std::chrono::milliseconds(5));
  {
    std::scoped_lock lock(oper.context.mutex);
    if (oper.context.received_payload != payload) return 13;
  }

  rs_transport_stats_v1 stats{};
  stats.struct_size = sizeof(stats);
  if (rs_transport_get_stats(host.transport, &stats) != RS_STATUS_OK || stats.bytes_sent < payload.size() ||
      stats.route_class == RS_ROUTE_CLASS_UNKNOWN) return 14;
  destroy_peer(host);
  destroy_peer(oper);
  return 0;
}
