#include "native_internal.hpp"
#include "transport_protocol.hpp"

#include <cctype>
#include <limits>
#include <memory>

namespace {
constexpr char control_label[] = "rsp.control.v1";
constexpr uint16_t type_protocol_hello = 20;
constexpr uint16_t type_protocol_hello_ack = 21;
constexpr uint16_t type_heartbeat = 42;
constexpr uint16_t type_transport_binding = 46;
constexpr uint16_t type_transport_binding_ack = 47;
constexpr uint32_t absolute_max_message_bytes = 262'144;

std::string copy_string(rs_string_view_v1 value, size_t maximum = 1024) {
  if (value.data == nullptr || value.length == 0 || value.length > maximum ||
      std::find(value.data, value.data + value.length, '\0') != value.data + value.length) return {};
  return std::string(value.data, value.length);
}

void emit_error(rs_transport_t* transport, rs_status_v1 status, const char* code, const std::string& detail) {
  set_last_error(transport->runtime, status, code, detail);
  if (transport->runtime->callbacks.on_error != nullptr) {
    const std::string stable_code(code);
    transport->runtime->callbacks.on_error(transport->runtime->callbacks.user_context, status, string_view(stable_code));
  }
}

void emit_state(rs_transport_t* transport, rs_transport_state_v1 state, const char* reason) {
  if (transport->runtime->callbacks.on_transport_state != nullptr) {
    const std::string stable_reason(reason == nullptr ? "" : reason);
    transport->runtime->callbacks.on_transport_state(
        transport->runtime->callbacks.user_context, state, string_view(stable_reason));
  }
}

void emit_binding_state(rs_transport_t* transport, rs_transport_binding_state_v1 state, const char* reason) {
  if (transport->runtime->callbacks.on_transport_binding_state != nullptr) {
    const std::string stable_reason(reason == nullptr ? "" : reason);
    transport->runtime->callbacks.on_transport_binding_state(
        transport->runtime->callbacks.user_context, state, string_view(stable_reason));
  }
}

void fail_transport(rs_transport_t* transport, const char* code, const std::string& detail) {
  int peer = -1;
  bool notify = false;
  {
    std::scoped_lock lock(transport->mutex);
    if (transport->state != RS_TRANSPORT_STATE_FAILED && transport->state != RS_TRANSPORT_STATE_CLOSED) {
      transport->state = RS_TRANSPORT_STATE_FAILED;
      transport->content_ready = false;
      peer = transport->peer_connection;
      notify = true;
    }
  }
  if (!notify) return;
  emit_error(transport, RS_STATUS_PROTOCOL_ERROR, code, detail);
  emit_binding_state(transport, RS_TRANSPORT_BINDING_FAILED, code);
  emit_state(transport, RS_TRANSPORT_STATE_FAILED, code);
  if (peer >= 0) rtcClosePeerConnection(peer);
  transport->state_changed.notify_all();
}

bool parse_sha256_fingerprint(const std::string& sdp, std::array<uint8_t, 32>& output) {
  constexpr char prefix[] = "a=fingerprint:sha-256 ";
  bool found = false;
  size_t line_start = 0;
  while (line_start < sdp.size()) {
    const size_t line_end = sdp.find_first_of("\r\n", line_start);
    std::string line = sdp.substr(line_start, line_end == std::string::npos ? std::string::npos : line_end - line_start);
    std::string lower = line;
    std::transform(lower.begin(), lower.end(), lower.begin(), [](unsigned char value) {
      return static_cast<char>(std::tolower(value));
    });
    if (lower.rfind(prefix, 0) == 0) {
      const std::string value = line.substr(sizeof(prefix) - 1);
      if (value.size() != 95) return false;
      std::array<uint8_t, 32> parsed{};
      for (size_t index = 0; index < parsed.size(); ++index) {
        if (index != 0 && value[index * 3 - 1] != ':') return false;
        const auto hex = [](char c) -> int {
          if (c >= '0' && c <= '9') return c - '0';
          if (c >= 'a' && c <= 'f') return c - 'a' + 10;
          if (c >= 'A' && c <= 'F') return c - 'A' + 10;
          return -1;
        };
        const size_t offset = index * 3;
        const int high = hex(value[offset]);
        const int low = hex(value[offset + 1]);
        if (high < 0 || low < 0) return false;
        parsed[index] = static_cast<uint8_t>((high << 4) | low);
      }
      if (found && parsed != output) return false;
      output = parsed;
      found = true;
    }
    if (line_end == std::string::npos) break;
    line_start = line_end + 1;
    if (line_start < sdp.size() && sdp[line_end] == '\r' && sdp[line_start] == '\n') ++line_start;
  }
  return found;
}

std::string percent_encode(const std::string& value) {
  constexpr char hex[] = "0123456789ABCDEF";
  std::string encoded;
  for (const unsigned char c : value) {
    if (std::isalnum(c) || c == '-' || c == '_' || c == '.' || c == '~') encoded.push_back(static_cast<char>(c));
    else {
      encoded.push_back('%');
      encoded.push_back(hex[c >> 4]);
      encoded.push_back(hex[c & 0x0f]);
    }
  }
  return encoded;
}

std::string make_ice_uri(const std::string& url, const std::string& username, const std::string& credential) {
  std::string lower = url;
  std::transform(lower.begin(), lower.end(), lower.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
  const size_t scheme_length = lower.rfind("turns:", 0) == 0 ? 6 : lower.rfind("turn:", 0) == 0 ? 5 : 0;
  if (scheme_length == 0 || username.empty()) return url;
  if (url.find('@', scheme_length) != std::string::npos) return url;
  return url.substr(0, scheme_length) + percent_encode(username) + ":" + percent_encode(credential) + "@" +
      url.substr(scheme_length);
}

std::string rtc_string(int id, int (*getter)(int, char*, int)) {
  const int required = getter(id, nullptr, 0);
  if (required <= 0 || required > 16'384) return {};
  std::vector<char> buffer(static_cast<size_t>(required));
  if (getter(id, buffer.data(), required) < 0) return {};
  return std::string(buffer.data());
}

void update_route(rs_transport_t* transport) {
  std::array<char, 4096> local{};
  std::array<char, 4096> remote{};
  if (rtcGetSelectedCandidatePair(transport->peer_connection, local.data(), static_cast<int>(local.size()),
          remote.data(), static_cast<int>(remote.size())) < 0) return;
  std::string pair = std::string(local.data()) + " " + remote.data();
  std::transform(pair.begin(), pair.end(), pair.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
  const bool relay = pair.find(" typ relay") != std::string::npos;
  const bool tcp = pair.find(" tcp ") != std::string::npos || pair.find(" tcptype ") != std::string::npos;
  transport->route_class.store(relay ? transport->configured_relay_route :
      (tcp ? RS_ROUTE_CLASS_DIRECT_TCP : RS_ROUTE_CLASS_DIRECT_UDP));
}

void notify_channel_state(rs_transport_t* transport, uint32_t channel_id, const std::string& label,
    rs_data_channel_state_v1 state) {
  if (transport->runtime->callbacks.on_data_channel_state != nullptr) {
    transport->runtime->callbacks.on_data_channel_state(
        transport->runtime->callbacks.user_context, channel_id, string_view(label), state);
  }
}

bool send_control_frame(rs_transport_t* transport, int rtc_id, const std::vector<uint8_t>& frame) {
  if (rtc_id < 0 || frame.empty() || frame.size() > static_cast<size_t>((std::numeric_limits<int>::max)())) return false;
  if (rtcSendMessage(rtc_id, reinterpret_cast<const char*>(frame.data()), static_cast<int>(frame.size())) < 0) return false;
  transport->bytes_sent.fetch_add(frame.size());
  return true;
}

void maybe_start_protocol(rs_transport_t* transport);

void maybe_send_binding(rs_transport_t* transport) {
  std::vector<uint8_t> frame;
  int rtc_id = -1;
  {
    std::scoped_lock lock(transport->mutex);
    if (transport->closing || transport->state == RS_TRANSPORT_STATE_FAILED || transport->local_binding_sent ||
        !transport->have_local_fingerprint || !transport->have_remote_fingerprint ||
        transport->control_channel_id == RS_DATA_CHANNEL_ID_INVALID) return;
    const auto channel = transport->channels.find(transport->control_channel_id);
    if (channel == transport->channels.end() || !channel->second.open) return;
    frame = make_transport_binding_frame(transport);
    if (frame.empty()) {
      rtc_id = -2;
    } else {
      rtc_id = channel->second.rtc_id;
      transport->local_binding_sent = true;
    }
  }
  if (rtc_id == -2) {
    fail_transport(transport, "TRANSPORT_BINDING_SIGN_FAILED", "The reciprocal transport binding could not be signed.");
    return;
  }
  emit_binding_state(transport, RS_TRANSPORT_BINDING_LOCAL_SENT, "TRANSPORT_BINDING_LOCAL_SENT");
#if defined(RS_ENABLE_TEST_FAULT_INJECTION)
  if ((transport->flags & 0x20000000u) != 0 && frame.size() > 25) frame[25] = 2;
#endif
  if (!send_control_frame(transport, rtc_id, frame)) {
    fail_transport(transport, "TRANSPORT_CONTROL_SEND_FAILED", "The signed transport binding could not be sent.");
    return;
  }
#if defined(RS_ENABLE_TEST_FAULT_INJECTION)
  if ((transport->flags & 0x40000000u) != 0 && frame.size() > 24) {
    auto replay = frame;
    uint64_t sequence = 0;
    {
      std::scoped_lock lock(transport->mutex);
      sequence = ++transport->control_outgoing_sequence;
    }
    for (size_t index = 0; index < 8; ++index) replay[12 + index] = static_cast<uint8_t>(sequence >> (56 - index * 8));
    size_t position = 24;
    while (position < replay.size()) {
      uint64_t key = 0;
      uint32_t shift = 0;
      while (position < replay.size() && shift < 64) {
        const uint8_t byte = replay[position++];
        key |= static_cast<uint64_t>(byte & 0x7f) << shift;
        if ((byte & 0x80) == 0) break;
        shift += 7;
      }
      const uint32_t field = static_cast<uint32_t>(key >> 3);
      const uint32_t wire = static_cast<uint32_t>(key & 7);
      if (field == 9 && wire == 0 && sequence < 128 && position < replay.size()) {
        replay[position] = static_cast<uint8_t>(sequence);
        break;
      }
      if (wire == 0) { while (position < replay.size() && (replay[position++] & 0x80) != 0) {} }
      else if (wire == 1) position += 8;
      else if (wire == 2) {
        uint64_t size = 0; shift = 0;
        while (position < replay.size() && shift < 64) {
          const uint8_t byte = replay[position++]; size |= static_cast<uint64_t>(byte & 0x7f) << shift;
          if ((byte & 0x80) == 0) break;
          shift += 7;
        }
        position += static_cast<size_t>(size);
      } else if (wire == 5) position += 4;
      else break;
    }
    send_control_frame(transport, rtc_id, replay);
  }
#endif
}

void mark_content_ready(rs_transport_t* transport) {
  bool start_worker = false;
  {
    std::scoped_lock lock(transport->mutex);
    if (!transport->content_ready && transport->protocol_hello_received && transport->protocol_hello_acked &&
        transport->local_binding_acked && transport->remote_binding_verified) {
      transport->content_ready = true;
      start_worker = true;
    }
  }
  if (!start_worker) return;
  transport->heartbeat_worker = std::jthread([transport](std::stop_token stop) {
    while (!stop.stop_requested()) {
      for (int index = 0; index < 20 && !stop.stop_requested(); ++index) std::this_thread::sleep_for(std::chrono::milliseconds(100));
      if (stop.stop_requested()) break;
      std::vector<uint8_t> frame;
      int rtc_id = -1;
      {
        std::scoped_lock lock(transport->mutex);
        const auto channel = transport->channels.find(transport->control_channel_id);
        if (!transport->content_ready || transport->closing || channel == transport->channels.end() || !channel->second.open) continue;
        transport->last_heartbeat_nonce++;
        transport->last_heartbeat_sent_ns = monotonic_nanoseconds();
        frame = make_heartbeat_frame(transport, transport->last_heartbeat_nonce);
        rtc_id = channel->second.rtc_id;
      }
      if (!send_control_frame(transport, rtc_id, frame)) {
        fail_transport(transport, "TRANSPORT_HEARTBEAT_FAILED", "The reliable control heartbeat could not be sent.");
        break;
      }
    }
  });
}

void maybe_start_protocol(rs_transport_t* transport) {
  std::vector<uint8_t> frame;
  int rtc_id = -1;
  bool notify_binding = false;
  {
    std::scoped_lock lock(transport->mutex);
    if (!transport->local_binding_acked || !transport->remote_binding_verified || transport->protocol_hello_sent) return;
    const auto channel = transport->channels.find(transport->control_channel_id);
    if (channel == transport->channels.end() || !channel->second.open) return;
    if (!transport->binding_complete_notified) {
      transport->binding_complete_notified = true;
      notify_binding = true;
    }
    frame = make_protocol_hello_frame(transport);
    transport->protocol_hello_sent = true;
    rtc_id = channel->second.rtc_id;
  }
  if (notify_binding) emit_binding_state(transport, RS_TRANSPORT_BINDING_VERIFIED, "TRANSPORT_BINDING_VERIFIED");
  if (!send_control_frame(transport, rtc_id, frame)) {
    fail_transport(transport, "TRANSPORT_CONTROL_SEND_FAILED", "Protocol capability exchange could not be sent.");
  }
}

void process_control_message(rs_transport_t* transport, const uint8_t* data, size_t length) {
  parsed_control_frame frame;
  std::string error;
  if (!parse_control_frame(data, length, transport->max_data_message_bytes, frame, error)) {
    fail_transport(transport, error.empty() ? "SIGNAL_PROTOCOL_INVALID" : error.c_str(), "Malformed control-channel frame.");
    return;
  }
  {
    std::scoped_lock lock(transport->mutex);
    if (frame.channel_sequence != transport->control_incoming_sequence + 1 ||
        frame.session_id != transport->session_id || frame.sender_peer_id != transport->binding.remote_peer_id ||
        frame.sender_role != transport->binding.remote_role || frame.transport_epoch != transport->transport_epoch ||
        frame.permission_revision != transport->binding.permission_revision) {
      error = "TRANSPORT_CONTROL_CONTEXT_MISMATCH";
    } else {
      transport->control_incoming_sequence = frame.channel_sequence;
    }
  }
  if (!error.empty()) {
    fail_transport(transport, error.c_str(), "Control-channel sequence or authorization context did not match.");
    return;
  }

  if (frame.message_type == type_transport_binding) {
    parsed_transport_binding binding;
    std::array<uint8_t, 32> binding_hash{};
    if (!parse_transport_binding(frame.body, binding, error) ||
        !verify_transport_binding(transport, binding, frame.body, binding_hash, error)) {
      fail_transport(transport, error.empty() ? "TRANSPORT_BINDING_INVALID" : error.c_str(), "Remote DTLS transport binding validation failed.");
      return;
    }
    std::vector<uint8_t> ack;
    int rtc_id = -1;
    {
      std::scoped_lock lock(transport->mutex);
      if (!transport->seen_remote_binding_ids.insert(binding.binding_id).second) {
        error = "TRANSPORT_BINDING_REPLAYED";
      } else {
        transport->remote_binding_verified = true;
        const auto channel = transport->channels.find(transport->control_channel_id);
        if (channel != transport->channels.end()) rtc_id = channel->second.rtc_id;
        ack = make_transport_binding_ack_frame(transport, binding.binding_id, true, "", binding_hash);
      }
    }
    if (!error.empty()) {
      fail_transport(transport, error.c_str(), "A signed transport binding identifier was replayed.");
      return;
    }
    emit_binding_state(transport, RS_TRANSPORT_BINDING_REMOTE_VERIFIED, "TRANSPORT_BINDING_REMOTE_VERIFIED");
    if (!send_control_frame(transport, rtc_id, ack)) {
      fail_transport(transport, "TRANSPORT_CONTROL_SEND_FAILED", "Transport-binding acknowledgement could not be sent.");
      return;
    }
    maybe_start_protocol(transport);
    return;
  }

  if (frame.message_type == type_transport_binding_ack) {
    std::string binding_id;
    std::string reason;
    bool verified = false;
    std::array<uint8_t, 32> binding_hash{};
    if (!parse_binding_ack(frame.body, binding_id, verified, reason, binding_hash, error)) {
      fail_transport(transport, error.empty() ? "TRANSPORT_BINDING_ACK_INVALID" : error.c_str(), "Malformed transport-binding acknowledgement.");
      return;
    }
    {
      std::scoped_lock lock(transport->mutex);
      if (!transport->local_binding_sent || !verified || binding_id != transport->local_binding_id ||
          binding_hash != transport->local_binding_hash) error = "TRANSPORT_BINDING_ACK_MISMATCH";
      else transport->local_binding_acked = true;
    }
    if (!error.empty()) {
      fail_transport(transport, error.c_str(), reason.empty() ? "Remote peer rejected the transport binding." : reason);
      return;
    }
    maybe_start_protocol(transport);
    return;
  }

  if (frame.message_type == type_protocol_hello) {
    uint32_t remote_maximum = 0;
    bool bound = false;
    {
      std::scoped_lock lock(transport->mutex);
      bound = transport->local_binding_acked && transport->remote_binding_verified;
    }
    if (!bound || !parse_protocol_hello(frame.body, remote_maximum, error)) {
      fail_transport(transport, bound ? (error.empty() ? "PROTOCOL_HELLO_INVALID" : error.c_str()) : "TRANSPORT_PRECONTENT_VIOLATION",
          "Capability exchange arrived before reciprocal transport binding or was invalid.");
      return;
    }
    std::vector<uint8_t> ack;
    int rtc_id = -1;
    {
      std::scoped_lock lock(transport->mutex);
      transport->max_data_message_bytes = (std::min)(transport->max_data_message_bytes, remote_maximum);
      transport->protocol_hello_received = true;
      const auto channel = transport->channels.find(transport->control_channel_id);
      if (channel != transport->channels.end()) rtc_id = channel->second.rtc_id;
      ack = make_protocol_hello_ack_frame(transport, true, nullptr);
    }
    if (!send_control_frame(transport, rtc_id, ack)) {
      fail_transport(transport, "TRANSPORT_CONTROL_SEND_FAILED", "Protocol acknowledgement could not be sent.");
      return;
    }
    mark_content_ready(transport);
    return;
  }

  if (frame.message_type == type_protocol_hello_ack) {
    bool accepted = false;
    std::string rejection;
    if (!parse_protocol_hello_ack(frame.body, accepted, rejection, error) || !accepted) {
      fail_transport(transport, error.empty() ? "PROTOCOL_HELLO_REJECTED" : error.c_str(), rejection);
      return;
    }
    {
      std::scoped_lock lock(transport->mutex);
      if (!transport->protocol_hello_sent) error = "TRANSPORT_PRECONTENT_VIOLATION";
      else transport->protocol_hello_acked = true;
    }
    if (!error.empty()) {
      fail_transport(transport, error.c_str(), "Unexpected capability acknowledgement.");
      return;
    }
    mark_content_ready(transport);
    return;
  }

  if (frame.message_type == type_heartbeat) {
    bool ready = false;
    {
      std::scoped_lock lock(transport->mutex);
      ready = transport->content_ready;
    }
    if (!ready) fail_transport(transport, "TRANSPORT_PRECONTENT_VIOLATION", "Heartbeat arrived before capability exchange completed.");
    return;
  }
  fail_transport(transport, "SIGNAL_PROTOCOL_INVALID", "Unsupported message type on the reserved control channel.");
}

void RTC_API on_channel_open(int id, void* pointer) {
  auto* transport = static_cast<rs_transport_t*>(pointer);
  uint32_t channel_id = RS_DATA_CHANNEL_ID_INVALID;
  std::string label;
  bool control = false;
  {
    std::scoped_lock lock(transport->mutex);
    const auto found_id = transport->channel_by_rtc_id.find(id);
    if (found_id == transport->channel_by_rtc_id.end()) return;
    auto found = transport->channels.find(found_id->second);
    if (found == transport->channels.end()) return;
    found->second.open = true;
    channel_id = found->first;
    label = found->second.label;
    control = found->second.internal_control;
  }
  notify_channel_state(transport, channel_id, label, RS_DATA_CHANNEL_STATE_OPEN);
  if (control) maybe_send_binding(transport);
}

void RTC_API on_channel_closed(int id, void* pointer) {
  auto* transport = static_cast<rs_transport_t*>(pointer);
  uint32_t channel_id = RS_DATA_CHANNEL_ID_INVALID;
  std::string label;
  bool control = false;
  {
    std::scoped_lock lock(transport->mutex);
    const auto found_id = transport->channel_by_rtc_id.find(id);
    if (found_id == transport->channel_by_rtc_id.end()) return;
    auto found = transport->channels.find(found_id->second);
    if (found == transport->channels.end()) return;
    found->second.open = false;
    channel_id = found->first;
    label = found->second.label;
    control = found->second.internal_control;
  }
  notify_channel_state(transport, channel_id, label, RS_DATA_CHANNEL_STATE_CLOSED);
  if (control) fail_transport(transport, "TRANSPORT_CONTROL_CHANNEL_CLOSED", "The reliable control channel closed unexpectedly.");
}

void RTC_API on_channel_error(int, const char* error, void* pointer) {
  auto* transport = static_cast<rs_transport_t*>(pointer);
  fail_transport(transport, "TRANSPORT_CHANNEL_ERROR", error == nullptr ? "WebRTC channel error." : error);
}

void RTC_API on_buffered_amount_low(int id, void* pointer) {
  auto* transport = static_cast<rs_transport_t*>(pointer);
  uint32_t channel_id = RS_DATA_CHANNEL_ID_INVALID;
  {
    std::scoped_lock lock(transport->mutex);
    const auto found = transport->channel_by_rtc_id.find(id);
    if (found != transport->channel_by_rtc_id.end()) channel_id = found->second;
  }
  if (channel_id != RS_DATA_CHANNEL_ID_INVALID && transport->runtime->callbacks.on_buffered_amount_low != nullptr) {
    const int amount = rtcGetBufferedAmount(id);
    transport->runtime->callbacks.on_buffered_amount_low(transport->runtime->callbacks.user_context,
        channel_id, amount < 0 ? 0 : static_cast<uint64_t>(amount));
  }
}

void RTC_API on_channel_message(int id, const char* message, int size, void* pointer) {
  auto* transport = static_cast<rs_transport_t*>(pointer);
  if (message == nullptr || size == 0) {
    fail_transport(transport, "SIGNAL_PROTOCOL_INVALID", "Empty data-channel message.");
    return;
  }
  const size_t length = size < 0 ? std::strlen(message) : static_cast<size_t>(size);
  uint32_t channel_id = RS_DATA_CHANNEL_ID_INVALID;
  bool control = false;
  bool ready = false;
  {
    std::scoped_lock lock(transport->mutex);
    const auto found_id = transport->channel_by_rtc_id.find(id);
    if (found_id == transport->channel_by_rtc_id.end()) return;
    const auto found = transport->channels.find(found_id->second);
    if (found == transport->channels.end()) return;
    channel_id = found->first;
    control = found->second.internal_control;
    ready = transport->content_ready;
  }
  transport->bytes_received.fetch_add(length);
  if (length > transport->max_data_message_bytes) {
    fail_transport(transport, "TRANSPORT_MESSAGE_TOO_LARGE", "Data-channel message exceeded the negotiated limit.");
    return;
  }
  if (control) {
    if (size < 0) {
      fail_transport(transport, "SIGNAL_PROTOCOL_INVALID", "Control-channel messages must be binary.");
      return;
    }
    process_control_message(transport, reinterpret_cast<const uint8_t*>(message), length);
    return;
  }
  if (!ready) {
    fail_transport(transport, "TRANSPORT_PRECONTENT_VIOLATION", "Application data arrived before reciprocal binding and capability exchange.");
    return;
  }
  if (transport->runtime->callbacks.on_data_message != nullptr) {
    rs_data_message_v1 data{};
    data.struct_size = sizeof(data);
    data.channel_id = channel_id;
    data.binary = size >= 0 ? 1u : 0u;
    data.payload = {reinterpret_cast<const uint8_t*>(message), static_cast<uint32_t>(length)};
    transport->runtime->callbacks.on_data_message(transport->runtime->callbacks.user_context, &data);
  }
}

bool register_channel(rs_transport_t* transport, int rtc_id, const std::string& label, bool internal,
    uint32_t& product_id) {
  if (rtc_id < 0) return false;
  {
    std::scoped_lock lock(transport->mutex);
    product_id = transport->next_channel_id++;
    transport_channel channel;
    channel.product_id = product_id;
    channel.rtc_id = rtc_id;
    channel.label = label;
    channel.internal_control = internal;
    transport->channels.emplace(product_id, std::move(channel));
    transport->channel_by_rtc_id.emplace(rtc_id, product_id);
    if (internal) transport->control_channel_id = product_id;
  }
  rtcSetUserPointer(rtc_id, transport);
  if (rtcSetOpenCallback(rtc_id, on_channel_open) < 0 || rtcSetClosedCallback(rtc_id, on_channel_closed) < 0 ||
      rtcSetErrorCallback(rtc_id, on_channel_error) < 0 || rtcSetMessageCallback(rtc_id, on_channel_message) < 0 ||
      rtcSetBufferedAmountLowThreshold(rtc_id, static_cast<int>(transport->buffered_amount_low_threshold_bytes)) < 0 ||
      rtcSetBufferedAmountLowCallback(rtc_id, on_buffered_amount_low) < 0) return false;
  notify_channel_state(transport, product_id, label, RS_DATA_CHANNEL_STATE_CONNECTING);
  if (rtcIsOpen(rtc_id)) on_channel_open(rtc_id, transport);
  return true;
}

void RTC_API on_incoming_data_channel(int, int data_channel, void* pointer) {
  auto* transport = static_cast<rs_transport_t*>(pointer);
  const std::string label = rtc_string(data_channel, rtcGetDataChannelLabel);
  if (label.empty() || label == control_label) {
    rtcClose(data_channel);
    rtcDeleteDataChannel(data_channel);
    if (label == control_label) fail_transport(transport, "TRANSPORT_DUPLICATE_CONTROL_CHANNEL", "A second reserved control channel was offered.");
    return;
  }
  uint32_t channel_id = 0;
  if (!register_channel(transport, data_channel, label, false, channel_id)) {
    rtcClose(data_channel);
    fail_transport(transport, "TRANSPORT_CHANNEL_NEGOTIATION_FAILED", "Incoming data channel could not be registered.");
  }
}

void append_nal(std::vector<uint8_t>& access_unit, const uint8_t* data, size_t length, bool& key_frame) {
  if (data == nullptr || length == 0 || access_unit.size() + length + 4 > 16u * 1024u * 1024u) return;
  access_unit.insert(access_unit.end(), {0, 0, 0, 1});
  access_unit.insert(access_unit.end(), data, data + length);
  if ((data[0] & 0x1f) == 5) key_frame = true;
}

bool depacketize_h264(rs_transport_t* transport, const uint8_t* packet, size_t length,
    std::vector<uint8_t>& completed, bool& key_frame, uint32_t& timestamp) {
  if (length < 12 || (packet[0] >> 6) != 2) return false;
  const size_t csrc_count = packet[0] & 0x0f;
  size_t offset = 12 + csrc_count * 4;
  if (offset > length) return false;
  if ((packet[0] & 0x10) != 0) {
    if (offset + 4 > length) return false;
    const size_t words = static_cast<size_t>(packet[offset + 2] << 8 | packet[offset + 3]);
    offset += 4 + words * 4;
    if (offset > length) return false;
  }
  size_t payload_end = length;
  if ((packet[0] & 0x20) != 0) {
    const uint8_t padding = packet[length - 1];
    if (padding == 0 || padding > payload_end - offset) return false;
    payload_end -= padding;
  }
  if (offset >= payload_end) return false;
  const uint16_t sequence = static_cast<uint16_t>(packet[2] << 8 | packet[3]);
  const uint32_t rtp_timestamp = static_cast<uint32_t>(packet[4]) << 24 | static_cast<uint32_t>(packet[5]) << 16 |
      static_cast<uint32_t>(packet[6]) << 8 | packet[7];
  const bool marker = (packet[1] & 0x80) != 0;
  const uint8_t* payload = packet + offset;
  const size_t payload_length = payload_end - offset;
  if (transport->incoming_h264_have_sequence && sequence != transport->incoming_h264_expected_sequence) {
    transport->incoming_h264_access_unit.clear();
    transport->incoming_h264_key_frame = false;
  }
  transport->incoming_h264_have_sequence = true;
  transport->incoming_h264_expected_sequence = static_cast<uint16_t>(sequence + 1);
  if (!transport->incoming_h264_access_unit.empty() && rtp_timestamp != transport->incoming_h264_timestamp) {
    transport->incoming_h264_access_unit.clear();
    transport->incoming_h264_key_frame = false;
  }
  transport->incoming_h264_timestamp = rtp_timestamp;
  const uint8_t nal_type = payload[0] & 0x1f;
  if (nal_type >= 1 && nal_type <= 23) {
    append_nal(transport->incoming_h264_access_unit, payload, payload_length, transport->incoming_h264_key_frame);
  } else if (nal_type == 24) {
    size_t position = 1;
    while (position + 2 <= payload_length) {
      const size_t nal_length = static_cast<size_t>(payload[position] << 8 | payload[position + 1]);
      position += 2;
      if (nal_length == 0 || position + nal_length > payload_length) return false;
      append_nal(transport->incoming_h264_access_unit, payload + position, nal_length, transport->incoming_h264_key_frame);
      position += nal_length;
    }
    if (position != payload_length) return false;
  } else if (nal_type == 28) {
    if (payload_length < 2) return false;
    const bool start = (payload[1] & 0x80) != 0;
    const bool end = (payload[1] & 0x40) != 0;
    if (start) {
      const uint8_t reconstructed = static_cast<uint8_t>((payload[0] & 0xe0) | (payload[1] & 0x1f));
      append_nal(transport->incoming_h264_access_unit, &reconstructed, 1, transport->incoming_h264_key_frame);
      transport->incoming_h264_access_unit.insert(transport->incoming_h264_access_unit.end(), payload + 2, payload + payload_length);
    } else {
      if (transport->incoming_h264_access_unit.empty()) return false;
      transport->incoming_h264_access_unit.insert(transport->incoming_h264_access_unit.end(), payload + 2, payload + payload_length);
    }
    if (end && !marker) return true;
  } else {
    return true;
  }
  if (!marker || transport->incoming_h264_access_unit.empty()) return true;
  completed.swap(transport->incoming_h264_access_unit);
  key_frame = transport->incoming_h264_key_frame;
  timestamp = rtp_timestamp;
  transport->incoming_h264_key_frame = false;
  return true;
}

void RTC_API on_track_message(int, const char* message, int size, void* pointer) {
  auto* transport = static_cast<rs_transport_t*>(pointer);
  if (message == nullptr || size <= 0) return;
  std::vector<uint8_t> completed;
  bool key_frame = false;
  uint32_t timestamp = 0;
  bool ready = false;
  uint64_t frame_id = 0;
  {
    std::scoped_lock lock(transport->mutex);
    ready = transport->content_ready;
    if (ready && depacketize_h264(transport, reinterpret_cast<const uint8_t*>(message), static_cast<size_t>(size),
        completed, key_frame, timestamp) && !completed.empty()) frame_id = transport->next_remote_frame_id++;
  }
  transport->bytes_received.fetch_add(static_cast<size_t>(size));
  if (!ready || completed.empty() || transport->internal_decoder == nullptr) return;
  rs_encoded_frame_v1 frame{};
  frame.struct_size = sizeof(frame);
  frame.frame_id = frame_id;
  frame.rtp_timestamp_90khz = timestamp;
  frame.monotonic_timestamp_ns = monotonic_nanoseconds();
  frame.frame_kind = key_frame ? RS_FRAME_KIND_KEY : RS_FRAME_KIND_DELTA;
  frame.codec = RS_CODEC_H264;
  frame.bytes = {completed.data(), static_cast<uint32_t>(completed.size())};
  frame.stream_format = RS_H264_STREAM_FORMAT_ANNEX_B;
  const rs_status_v1 status = rs_decoder_submit_h264(transport->internal_decoder, &frame);
  if (status == RS_STATUS_OK) transport->video_frames_received.fetch_add(1);
  else if (status != RS_STATUS_INVALID_STATE) emit_error(transport, status, "TRANSPORT_VIDEO_DECODE_FAILED", "Remote H.264 access unit could not be decoded.");
}

void configure_receiving_track(rs_transport_t* transport, int track) {
  rtcSetUserPointer(track, transport);
  rtcSetMessageCallback(track, on_track_message);
  rtcSetErrorCallback(track, on_channel_error);
}

void RTC_API on_incoming_track(int, int track, void* pointer) {
  auto* transport = static_cast<rs_transport_t*>(pointer);
  {
    std::scoped_lock lock(transport->mutex);
    if (track != transport->video_track) transport->additional_track_ids.push_back(track);
  }
  configure_receiving_track(transport, track);
}

void RTC_API on_pli(int, void* pointer) {
  auto* transport = static_cast<rs_transport_t*>(pointer);
  if (transport->internal_encoder != nullptr) rs_encoder_request_keyframe(transport->internal_encoder);
  if (transport->runtime->callbacks.on_transport_video_feedback != nullptr) {
    transport->runtime->callbacks.on_transport_video_feedback(transport->runtime->callbacks.user_context,
        transport->target_bitrate_bps.load(), transport->target_fps.load(), 1);
  }
}

void RTC_API on_remb(int, unsigned int bitrate, void* pointer) {
  auto* transport = static_cast<rs_transport_t*>(pointer);
  const uint32_t bounded = (std::max)(250'000u, (std::min)(bitrate, 50'000'000u));
  transport->target_bitrate_bps.store(bounded);
  if (transport->internal_encoder != nullptr) rs_encoder_set_rate(transport->internal_encoder, bounded, transport->target_fps.load());
  if (transport->runtime->callbacks.on_transport_video_feedback != nullptr) {
    transport->runtime->callbacks.on_transport_video_feedback(
        transport->runtime->callbacks.user_context, bounded, transport->target_fps.load(), 0);
  }
}

void RTC_API on_local_description(int, const char* sdp, const char* type, void* pointer) {
  auto* transport = static_cast<rs_transport_t*>(pointer);
  if (sdp == nullptr || type == nullptr) {
    fail_transport(transport, "TRANSPORT_SDP_INVALID", "WebRTC produced an empty local description.");
    return;
  }
  std::array<uint8_t, 32> fingerprint{};
  if (!parse_sha256_fingerprint(sdp, fingerprint)) {
    fail_transport(transport, "TRANSPORT_DTLS_FINGERPRINT_INVALID", "Local SDP did not contain one unambiguous SHA-256 DTLS fingerprint.");
    return;
  }
  rs_sdp_type_v1 description_type = std::strcmp(type, "offer") == 0 ? RS_SDP_TYPE_OFFER :
      std::strcmp(type, "answer") == 0 ? RS_SDP_TYPE_ANSWER : RS_SDP_TYPE_UNKNOWN;
  {
    std::scoped_lock lock(transport->mutex);
    transport->local_description = sdp;
    transport->local_description_set = true;
    transport->local_dtls_fingerprint = fingerprint;
    transport->have_local_fingerprint = true;
  }
  if (transport->runtime->callbacks.on_local_description != nullptr) {
    const std::string value(sdp);
    rs_session_description_v1 description{sizeof(description), description_type, string_view(value)};
    transport->runtime->callbacks.on_local_description(transport->runtime->callbacks.user_context, &description);
  }
  maybe_send_binding(transport);
}

void RTC_API on_local_candidate(int, const char* candidate, const char* mid, void* pointer) {
  auto* transport = static_cast<rs_transport_t*>(pointer);
  if (candidate == nullptr || mid == nullptr || transport->runtime->callbacks.on_local_ice_candidate == nullptr) return;
  const std::string candidate_value(candidate);
  const std::string mid_value(mid);
  rs_ice_candidate_v1 value{};
  value.struct_size = sizeof(value);
  value.sdp_mid_utf8 = string_view(mid_value);
  value.sdp_mline_index = -1;
  value.candidate_utf8 = string_view(candidate_value);
  transport->runtime->callbacks.on_local_ice_candidate(transport->runtime->callbacks.user_context, &value);
}

void RTC_API on_peer_state(int, rtcState rtc_state, void* pointer) {
  auto* transport = static_cast<rs_transport_t*>(pointer);
  rs_transport_state_v1 state = RS_TRANSPORT_STATE_NEW;
  const char* reason = "TRANSPORT_NEW";
  switch (rtc_state) {
    case RTC_CONNECTING: state = RS_TRANSPORT_STATE_CONNECTING; reason = "TRANSPORT_CONNECTING"; break;
    case RTC_CONNECTED: state = RS_TRANSPORT_STATE_CONNECTED; reason = "TRANSPORT_CONNECTED"; break;
    case RTC_DISCONNECTED: state = RS_TRANSPORT_STATE_DISCONNECTED; reason = "TRANSPORT_DISCONNECTED"; break;
    case RTC_FAILED: state = RS_TRANSPORT_STATE_FAILED; reason = "TRANSPORT_ICE_FAILED"; break;
    case RTC_CLOSED: state = RS_TRANSPORT_STATE_CLOSED; reason = "TRANSPORT_CLOSED"; break;
    default: break;
  }
  {
    std::scoped_lock lock(transport->mutex);
    if (transport->state == RS_TRANSPORT_STATE_FAILED && state == RS_TRANSPORT_STATE_CLOSED) return;
    transport->state = state;
  }
  if (state == RS_TRANSPORT_STATE_CONNECTED) update_route(transport);
  emit_state(transport, state, reason);
  transport->state_changed.notify_all();
}

void RTC_API on_encoded_video(void* pointer, const rs_encoded_frame_v1* frame) {
  if (pointer != nullptr && frame != nullptr) rs_transport_submit_encoded_video(static_cast<rs_transport_t*>(pointer), frame);
}

bool valid_binding_options(const rs_transport_binding_options_v1* input) {
  return input != nullptr && input->struct_size >= sizeof(rs_transport_binding_options_v1) &&
      input->remote_peer_id_utf8.data != nullptr && input->remote_peer_id_utf8.length > 0 &&
      input->permission_revision > 0 && input->granted_scopes_utf8 != nullptr && input->granted_scope_count > 0 &&
      input->granted_scope_count <= 16 && input->authorization_context_sha256.data != nullptr &&
      input->authorization_context_sha256.length == 32 && input->local_private_key_p256.data != nullptr &&
      input->local_private_key_p256.length == 32 && input->local_public_key_uncompressed_p256.data != nullptr &&
      input->local_public_key_uncompressed_p256.length == 65 && input->remote_public_key_uncompressed_p256.data != nullptr &&
      input->remote_public_key_uncompressed_p256.length == 65 && input->local_role != RS_PEER_ROLE_UNKNOWN &&
      input->remote_role != RS_PEER_ROLE_UNKNOWN && input->local_role != input->remote_role;
}

bool copy_binding(rs_transport_t* transport, const rs_transport_binding_options_v1* input) {
  transport->binding.remote_peer_id = copy_string(input->remote_peer_id_utf8, 128);
  transport->binding.local_role = input->local_role;
  transport->binding.remote_role = input->remote_role;
  transport->binding.permission_revision = input->permission_revision;
  transport->binding.local_key_id = copy_string(input->local_key_id_utf8, 128);
  transport->binding.remote_key_id = copy_string(input->remote_key_id_utf8, 128);
  if (transport->binding.remote_peer_id.empty() || transport->binding.local_key_id.empty() || transport->binding.remote_key_id.empty()) return false;
  for (uint32_t index = 0; index < input->granted_scope_count; ++index) {
    std::string scope = copy_string(input->granted_scopes_utf8[index], 64);
    if (scope.empty()) return false;
    transport->binding.scopes.push_back(std::move(scope));
  }
  std::sort(transport->binding.scopes.begin(), transport->binding.scopes.end());
  if (std::adjacent_find(transport->binding.scopes.begin(), transport->binding.scopes.end()) != transport->binding.scopes.end()) return false;
  std::memcpy(transport->binding.authorization_context.data(), input->authorization_context_sha256.data, 32);
  std::memcpy(transport->binding.local_private_key.data(), input->local_private_key_p256.data, 32);
  std::memcpy(transport->binding.local_public_key.data(), input->local_public_key_uncompressed_p256.data, 65);
  std::memcpy(transport->binding.remote_public_key.data(), input->remote_public_key_uncompressed_p256.data, 65);
  transport->binding.private_key_locked = VirtualLock(
      transport->binding.local_private_key.data(), transport->binding.local_private_key.size()) != FALSE;
  if (!transport->binding.private_key_locked) return false;
  std::array<uint8_t, 32> proof{};
  std::array<uint8_t, 64> signature{};
  proof[0] = 0xa5;
  return sign_p256_sha256(transport->binding, proof, signature) &&
      verify_p256_sha256(transport->binding.local_public_key, proof, signature.data(), signature.size());
}

void wipe_binding(rs_transport_t* transport) {
  SecureZeroMemory(transport->binding.local_private_key.data(), transport->binding.local_private_key.size());
  if (transport->binding.private_key_locked) {
    VirtualUnlock(transport->binding.local_private_key.data(), transport->binding.local_private_key.size());
    transport->binding.private_key_locked = false;
  }
}
}

extern "C" {
rs_status_v1 RS_CALL rs_transport_create(rs_runtime_handle runtime, const rs_transport_options_v1* options,
    rs_transport_handle* out_transport) {
  if (runtime == nullptr || options == nullptr || out_transport == nullptr ||
      options->struct_size < sizeof(rs_transport_options_v1) || !valid_binding_options(options->binding) ||
      options->transport_epoch == 0 || options->ice_server_count > 8 ||
      (options->ice_server_count != 0 && options->ice_servers == nullptr)) return RS_STATUS_INVALID_ARGUMENT;
  *out_transport = nullptr;
  try {
    auto transport = std::make_unique<rs_transport_t>();
    transport->runtime = runtime;
    transport->session_id = copy_string(options->session_id_utf8, 128);
    transport->local_peer_id = copy_string(options->local_peer_id_utf8, 128);
    transport->transport_epoch = options->transport_epoch;
    transport->video_input_mode = options->video_input_mode;
    transport->max_data_message_bytes = options->max_data_message_bytes == 0 ? 64 * 1024 : options->max_data_message_bytes;
    transport->buffered_amount_low_threshold_bytes = options->buffered_amount_low_threshold_bytes == 0 ? 256 * 1024 :
        options->buffered_amount_low_threshold_bytes;
    transport->flags = options->flags;
    transport->created_ns = monotonic_nanoseconds();
    if (transport->session_id.empty() || transport->local_peer_id.empty() ||
        transport->max_data_message_bytes < 1024 || transport->max_data_message_bytes > absolute_max_message_bytes ||
        (options->video_input_mode != RS_VIDEO_INPUT_MODE_ENCODED_H264 &&
         options->video_input_mode != RS_VIDEO_INPUT_MODE_D3D11_TEXTURE) || !copy_binding(transport.get(), options->binding)) {
      wipe_binding(transport.get());
      return RS_STATUS_INVALID_ARGUMENT;
    }

    std::vector<std::string> ice_uris;
    for (uint32_t server_index = 0; server_index < options->ice_server_count; ++server_index) {
      const auto& server = options->ice_servers[server_index];
      if (server.struct_size < sizeof(rs_ice_server_v1) || server.url_count == 0 || server.url_count > 8 || server.urls == nullptr) {
        wipe_binding(transport.get());
        return RS_STATUS_INVALID_ARGUMENT;
      }
      const std::string username = server.username_utf8.length == 0 ? std::string() : copy_string(server.username_utf8, 512);
      const std::string credential = server.credential_utf8.length == 0 ? std::string() : copy_string(server.credential_utf8, 512);
      for (uint32_t url_index = 0; url_index < server.url_count; ++url_index) {
        const std::string url = copy_string(server.urls[url_index], 2048);
        if (url.empty()) { wipe_binding(transport.get()); return RS_STATUS_INVALID_ARGUMENT; }
        std::string lower = url;
        std::transform(lower.begin(), lower.end(), lower.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
        if (lower.rfind("turns:", 0) == 0) transport->configured_relay_route = RS_ROUTE_CLASS_TURN_TLS;
        else if (lower.rfind("turn:", 0) == 0 && lower.find("transport=tcp") != std::string::npos &&
                 transport->configured_relay_route != RS_ROUTE_CLASS_TURN_TLS) transport->configured_relay_route = RS_ROUTE_CLASS_TURN_TCP;
        ice_uris.push_back(make_ice_uri(url, username, credential));
      }
    }
    std::vector<const char*> ice_pointers;
    ice_pointers.reserve(ice_uris.size());
    for (const auto& uri : ice_uris) ice_pointers.push_back(uri.c_str());
    rtcConfiguration configuration{};
    configuration.iceServers = ice_pointers.empty() ? nullptr : ice_pointers.data();
    configuration.iceServersCount = static_cast<int>(ice_pointers.size());
    configuration.certificateType = RTC_CERTIFICATE_ECDSA;
    configuration.iceTransportPolicy = RTC_TRANSPORT_POLICY_ALL;
    configuration.enableIceTcp = true;
    configuration.disableAutoNegotiation = true;
    configuration.forceMediaTransport = true;
    configuration.maxMessageSize = static_cast<int>(transport->max_data_message_bytes);
    transport->peer_connection = rtcCreatePeerConnection(&configuration);
    if (transport->peer_connection < 0) {
      wipe_binding(transport.get());
      return RS_STATUS_NOT_SUPPORTED;
    }
    rtcSetUserPointer(transport->peer_connection, transport.get());
    if (rtcSetLocalDescriptionCallback(transport->peer_connection, on_local_description) < 0 ||
        rtcSetLocalCandidateCallback(transport->peer_connection, on_local_candidate) < 0 ||
        rtcSetStateChangeCallback(transport->peer_connection, on_peer_state) < 0 ||
        rtcSetDataChannelCallback(transport->peer_connection, on_incoming_data_channel) < 0 ||
        rtcSetTrackCallback(transport->peer_connection, on_incoming_track) < 0) {
      rtcDeletePeerConnection(transport->peer_connection);
      wipe_binding(transport.get());
      return RS_STATUS_INTERNAL_ERROR;
    }

    rtcDataChannelInit control{};
    control.negotiated = true;
    control.manualStream = true;
    control.stream = 0;
    const int control_rtc_id = rtcCreateDataChannelEx(transport->peer_connection, control_label, &control);
    uint32_t control_product_id = 0;
    if (!register_channel(transport.get(), control_rtc_id, control_label, true, control_product_id)) {
      rtcDeletePeerConnection(transport->peer_connection);
      wipe_binding(transport.get());
      return RS_STATUS_INTERNAL_ERROR;
    }

    rtcTrackInit track{};
    track.direction = transport->binding.local_role == RS_PEER_ROLE_HOST ? RTC_DIRECTION_SENDONLY : RTC_DIRECTION_RECVONLY;
    track.codec = RTC_CODEC_H264;
    track.payloadType = 96;
    track.ssrc = static_cast<uint32_t>(monotonic_nanoseconds());
    track.mid = "video";
    track.name = "screen";
    track.msid = "remote-support";
    track.trackId = "screen-video";
    track.profile = "42e01f";
    transport->video_track = rtcAddTrackEx(transport->peer_connection, &track);
    if (transport->video_track < 0) {
      rtcDeletePeerConnection(transport->peer_connection);
      wipe_binding(transport.get());
      return RS_STATUS_NOT_SUPPORTED;
    }
    rtcSetUserPointer(transport->video_track, transport.get());
    if (transport->binding.local_role == RS_PEER_ROLE_HOST) {
      rtcPacketizerInit packetizer{};
      packetizer.ssrc = track.ssrc;
      packetizer.cname = "remote-support";
      packetizer.payloadType = 96;
      packetizer.clockRate = 90'000;
      packetizer.maxFragmentSize = 1200;
      packetizer.nalSeparator = RTC_NAL_SEPARATOR_START_SEQUENCE;
      if (rtcSetH264Packetizer(transport->video_track, &packetizer) < 0 ||
          rtcChainRtcpSrReporter(transport->video_track) < 0 || rtcChainRtcpNackResponder(transport->video_track, 512) < 0 ||
          rtcChainPliHandler(transport->video_track, on_pli) < 0 || rtcChainRembHandler(transport->video_track, on_remb) < 0) {
        rtcDeletePeerConnection(transport->peer_connection);
        wipe_binding(transport.get());
        return RS_STATUS_NOT_SUPPORTED;
      }
    } else {
      configure_receiving_track(transport.get(), transport->video_track);
      rs_decoder_options_v1 decoder_options{};
      decoder_options.struct_size = sizeof(decoder_options);
      decoder_options.codec = RS_CODEC_H264;
      decoder_options.stream_format = RS_H264_STREAM_FORMAT_ANNEX_B;
      decoder_options.max_width = 3840;
      decoder_options.max_height = 2160;
      decoder_options.output_queue_capacity = 3;
      decoder_options.allow_software_fallback = 1;
      if (rs_decoder_create(runtime, &decoder_options, &transport->internal_decoder) == RS_STATUS_OK) {
        transport->internal_decoder->remote_transport_output = true;
      }
    }
    *out_transport = transport.release();
    return RS_STATUS_OK;
  } catch (...) {
    set_last_error(runtime, RS_STATUS_INTERNAL_ERROR, "TRANSPORT_CREATE_FAILED", "Unexpected transport allocation failure.");
    return RS_STATUS_INTERNAL_ERROR;
  }
}

rs_status_v1 RS_CALL rs_transport_create_offer(rs_transport_handle transport, uint32_t ice_restart) {
  if (transport == nullptr || ice_restart > 1) return RS_STATUS_INVALID_ARGUMENT;
  {
    std::scoped_lock lock(transport->mutex);
    if (transport->closing || transport->state == RS_TRANSPORT_STATE_FAILED) return RS_STATUS_INVALID_STATE;
  }
  return rtcSetLocalDescription(transport->peer_connection, "offer") < 0 ? RS_STATUS_PROTOCOL_ERROR : RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_transport_create_answer(rs_transport_handle transport) {
  if (transport == nullptr) return RS_STATUS_INVALID_ARGUMENT;
  {
    std::scoped_lock lock(transport->mutex);
    if (transport->closing || !transport->remote_description_set) return RS_STATUS_INVALID_STATE;
  }
  return rtcSetLocalDescription(transport->peer_connection, "answer") < 0 ? RS_STATUS_PROTOCOL_ERROR : RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_transport_set_remote_description(rs_transport_handle transport,
    const rs_session_description_v1* description) {
  if (transport == nullptr || description == nullptr || description->struct_size < sizeof(rs_session_description_v1)) return RS_STATUS_INVALID_ARGUMENT;
  const std::string sdp = copy_string(description->sdp_utf8, 1024 * 1024);
  const char* type = description->type == RS_SDP_TYPE_OFFER ? "offer" : description->type == RS_SDP_TYPE_ANSWER ? "answer" : nullptr;
  std::array<uint8_t, 32> fingerprint{};
  if (sdp.empty() || type == nullptr || !parse_sha256_fingerprint(sdp, fingerprint)) return RS_STATUS_INVALID_ARGUMENT;
#if defined(RS_ENABLE_TEST_FAULT_INJECTION)
  if ((transport->flags & 0x80000000u) != 0) fingerprint[0] ^= 0x80;
#endif
  if (rtcSetRemoteDescription(transport->peer_connection, sdp.c_str(), type) < 0) {
    emit_error(transport, RS_STATUS_PROTOCOL_ERROR, "TRANSPORT_REMOTE_SDP_REJECTED", "Remote SDP was rejected by the WebRTC stack.");
    return RS_STATUS_PROTOCOL_ERROR;
  }
  {
    std::scoped_lock lock(transport->mutex);
    transport->remote_description = sdp;
    transport->remote_description_set = true;
    transport->remote_dtls_fingerprint = fingerprint;
    transport->have_remote_fingerprint = true;
  }
  maybe_send_binding(transport);
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_transport_add_remote_ice_candidate(rs_transport_handle transport,
    const rs_ice_candidate_v1* candidate) {
  if (transport == nullptr || candidate == nullptr || candidate->struct_size < sizeof(rs_ice_candidate_v1)) return RS_STATUS_INVALID_ARGUMENT;
  const std::string value = copy_string(candidate->candidate_utf8, 8192);
  const std::string mid = copy_string(candidate->sdp_mid_utf8, 256);
  {
    std::scoped_lock lock(transport->mutex);
    if (!transport->remote_description_set) return RS_STATUS_INVALID_STATE;
  }
  return value.empty() || mid.empty() || rtcAddRemoteCandidate(transport->peer_connection, value.c_str(), mid.c_str()) < 0 ?
      RS_STATUS_PROTOCOL_ERROR : RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_transport_submit_encoded_video(rs_transport_handle transport, const rs_encoded_frame_v1* frame) {
  if (transport == nullptr || frame == nullptr || frame->struct_size < sizeof(rs_encoded_frame_v1) ||
      frame->codec != RS_CODEC_H264 || frame->stream_format != RS_H264_STREAM_FORMAT_ANNEX_B ||
      frame->bytes.data == nullptr || frame->bytes.length == 0 || frame->bytes.length > 16u * 1024u * 1024u) return RS_STATUS_INVALID_ARGUMENT;
  int track = -1;
  {
    std::scoped_lock lock(transport->mutex);
    if (!transport->content_ready || transport->binding.local_role != RS_PEER_ROLE_HOST || transport->closing) return RS_STATUS_INVALID_STATE;
    track = transport->video_track;
  }
  if (frame->rtp_timestamp_90khz != 0) rtcSetTrackRtpTimestamp(track, static_cast<uint32_t>(frame->rtp_timestamp_90khz));
  if (rtcSendMessage(track, reinterpret_cast<const char*>(frame->bytes.data), static_cast<int>(frame->bytes.length)) < 0) return RS_STATUS_WOULD_BLOCK;
  transport->bytes_sent.fetch_add(frame->bytes.length);
  transport->video_bytes_sent.fetch_add(frame->bytes.length);
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_transport_submit_d3d11_video(rs_transport_handle transport, const rs_frame_info_v1* frame) {
  if (transport == nullptr || frame == nullptr || frame->struct_size < sizeof(rs_frame_info_v1) || frame->d3d11_texture == nullptr) return RS_STATUS_INVALID_ARGUMENT;
  {
    std::scoped_lock lock(transport->mutex);
    if (!transport->content_ready || transport->binding.local_role != RS_PEER_ROLE_HOST ||
        transport->video_input_mode != RS_VIDEO_INPUT_MODE_D3D11_TEXTURE) return RS_STATUS_INVALID_STATE;
    if (transport->internal_encoder == nullptr) {
      rs_encoder_options_v1 options{};
      options.struct_size = sizeof(options);
      options.width = frame->width;
      options.height = frame->height;
      options.target_fps = transport->target_fps.load();
      options.target_bitrate_bps = transport->target_bitrate_bps.load();
      options.max_bitrate_bps = 50'000'000;
      options.codec = RS_CODEC_H264;
      options.quality_profile = RS_QUALITY_PROFILE_BALANCED;
      options.frame_queue_capacity = 3;
      options.prefer_hardware = 1;
      options.allow_software_fallback = 1;
      options.max_keyframe_interval_ms = 2'000;
      const rs_status_v1 status = rs_encoder_create(transport->runtime, &options, &transport->internal_encoder);
      if (status != RS_STATUS_OK) return status;
      transport->internal_encoder->output_callback = on_encoded_video;
      transport->internal_encoder->output_callback_context = transport;
    }
  }
  return rs_encoder_submit_d3d11_frame(transport->internal_encoder, frame);
}

rs_status_v1 RS_CALL rs_transport_set_video_rate(rs_transport_handle transport, uint32_t target_bitrate_bps, uint32_t target_fps) {
  if (transport == nullptr || target_bitrate_bps < 100'000 || target_bitrate_bps > 50'000'000 || target_fps == 0 || target_fps > 120) return RS_STATUS_INVALID_ARGUMENT;
  transport->target_bitrate_bps.store(target_bitrate_bps);
  transport->target_fps.store(target_fps);
  return transport->internal_encoder == nullptr ? RS_STATUS_OK : rs_encoder_set_rate(transport->internal_encoder, target_bitrate_bps, target_fps);
}

rs_status_v1 RS_CALL rs_transport_open_data_channel(rs_transport_handle transport,
    const rs_data_channel_options_v1* options, uint32_t* out_channel_id) {
  if (transport == nullptr || options == nullptr || out_channel_id == nullptr ||
      options->struct_size < sizeof(rs_data_channel_options_v1)) return RS_STATUS_INVALID_ARGUMENT;
  *out_channel_id = RS_DATA_CHANNEL_ID_INVALID;
  const std::string label = copy_string(options->label_utf8, 128);
  if (label.empty() || label == control_label || (options->max_retransmits >= 0 && options->max_packet_lifetime_ms >= 0) ||
      options->negotiated_id > 65534) return RS_STATUS_INVALID_ARGUMENT;
  rtcDataChannelInit init{};
  init.reliability.unordered = options->ordered == 0;
  init.reliability.unreliable = options->max_retransmits >= 0 || options->max_packet_lifetime_ms >= 0;
  if (options->max_retransmits >= 0) init.reliability.maxRetransmits = static_cast<unsigned int>(options->max_retransmits);
  if (options->max_packet_lifetime_ms >= 0) init.reliability.maxPacketLifeTime = static_cast<unsigned int>(options->max_packet_lifetime_ms);
  init.negotiated = options->negotiated != 0;
  init.manualStream = options->negotiated_id >= 0;
  init.stream = options->negotiated_id < 0 ? 0 : static_cast<uint16_t>(options->negotiated_id);
  const int rtc_id = rtcCreateDataChannelEx(transport->peer_connection, label.c_str(), &init);
  uint32_t product_id = 0;
  if (!register_channel(transport, rtc_id, label, false, product_id)) return RS_STATUS_PROTOCOL_ERROR;
  *out_channel_id = product_id;
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_transport_send_data(rs_transport_handle transport, const rs_data_message_v1* message) {
  if (transport == nullptr || message == nullptr || message->struct_size < sizeof(rs_data_message_v1) ||
      message->payload.data == nullptr || message->payload.length == 0 || message->payload.length > transport->max_data_message_bytes) return RS_STATUS_INVALID_ARGUMENT;
  int rtc_id = -1;
  {
    std::scoped_lock lock(transport->mutex);
    const auto found = transport->channels.find(message->channel_id);
    if (!transport->content_ready || found == transport->channels.end() || found->second.internal_control || !found->second.open) return RS_STATUS_INVALID_STATE;
    rtc_id = found->second.rtc_id;
  }
  const int size = message->binary != 0 ? static_cast<int>(message->payload.length) : -1;
  if (message->binary == 0 && std::find(message->payload.data, message->payload.data + message->payload.length, 0) !=
      message->payload.data + message->payload.length) return RS_STATUS_INVALID_ARGUMENT;
  std::string text;
  const char* data = reinterpret_cast<const char*>(message->payload.data);
  if (size < 0) { text.assign(data, message->payload.length); data = text.c_str(); }
  if (rtcSendMessage(rtc_id, data, size) < 0) return RS_STATUS_WOULD_BLOCK;
  transport->bytes_sent.fetch_add(message->payload.length);
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_transport_close_data_channel(rs_transport_handle transport, uint32_t channel_id) {
  if (transport == nullptr) return RS_STATUS_INVALID_ARGUMENT;
  int rtc_id = -1;
  std::string label;
  {
    std::scoped_lock lock(transport->mutex);
    const auto found = transport->channels.find(channel_id);
    if (found == transport->channels.end() || found->second.internal_control) return RS_STATUS_INVALID_ARGUMENT;
    rtc_id = found->second.rtc_id;
    label = found->second.label;
  }
  notify_channel_state(transport, channel_id, label, RS_DATA_CHANNEL_STATE_CLOSING);
  return rtcClose(rtc_id) < 0 ? RS_STATUS_PROTOCOL_ERROR : RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_transport_get_stats(rs_transport_handle transport, rs_transport_stats_v1* out_stats) {
  if (transport == nullptr || out_stats == nullptr || out_stats->struct_size < sizeof(rs_transport_stats_v1)) return RS_STATUS_INVALID_ARGUMENT;
  rs_transport_stats_v1 stats{};
  stats.struct_size = sizeof(stats);
  stats.rtt_ms = transport->heartbeat_rtt_ms.load();
  stats.available_outgoing_bitrate_bps = transport->target_bitrate_bps.load();
  const uint64_t elapsed_ns = (std::max)(uint64_t{1}, monotonic_nanoseconds() - transport->created_ns);
  stats.actual_video_bitrate_bps = transport->video_bytes_sent.load() * 8'000'000'000ULL / elapsed_ns;
  stats.bytes_sent = transport->bytes_sent.load();
  stats.bytes_received = transport->bytes_received.load();
  stats.route_class = transport->route_class.load();
  {
    std::scoped_lock lock(transport->mutex);
    for (const auto& [id, channel] : transport->channels) {
      (void)id;
      const int amount = rtcGetBufferedAmount(channel.rtc_id);
      if (amount > 0) stats.data_channel_buffered_bytes += static_cast<uint64_t>(amount);
    }
  }
  *out_stats = stats;
  return RS_STATUS_OK;
}

rs_status_v1 RS_CALL rs_transport_close(rs_transport_handle transport) {
  if (transport == nullptr) return RS_STATUS_INVALID_ARGUMENT;
  int peer = -1;
  {
    std::scoped_lock lock(transport->mutex);
    if (transport->closing) return RS_STATUS_CLOSED;
    transport->closing = true;
    transport->content_ready = false;
    peer = transport->peer_connection;
  }
  if (transport->heartbeat_worker.joinable()) {
    transport->heartbeat_worker.request_stop();
    transport->heartbeat_worker.join();
  }
  if (peer >= 0) rtcClosePeerConnection(peer);
  return RS_STATUS_OK;
}

void RS_CALL rs_transport_destroy(rs_transport_handle transport) {
  if (transport == nullptr) return;
  rs_transport_close(transport);
  std::vector<int> channels;
  std::vector<int> tracks;
  {
    std::scoped_lock lock(transport->mutex);
    for (const auto& [product_id, channel] : transport->channels) {
      (void)product_id;
      channels.push_back(channel.rtc_id);
    }
    if (transport->video_track >= 0) tracks.push_back(transport->video_track);
    tracks.insert(tracks.end(), transport->additional_track_ids.begin(), transport->additional_track_ids.end());
  }
  for (const int id : channels) {
    rtcSetUserPointer(id, nullptr);
    rtcSetOpenCallback(id, nullptr);
    rtcSetClosedCallback(id, nullptr);
    rtcSetErrorCallback(id, nullptr);
    rtcSetMessageCallback(id, nullptr);
    rtcSetBufferedAmountLowCallback(id, nullptr);
    rtcDeleteDataChannel(id);
  }
  for (const int id : tracks) {
    rtcSetUserPointer(id, nullptr);
    rtcSetOpenCallback(id, nullptr);
    rtcSetClosedCallback(id, nullptr);
    rtcSetErrorCallback(id, nullptr);
    rtcSetMessageCallback(id, nullptr);
    rtcDeleteTrack(id);
  }
  if (transport->internal_encoder != nullptr) rs_encoder_destroy(transport->internal_encoder);
  if (transport->internal_decoder != nullptr) rs_decoder_destroy(transport->internal_decoder);
  if (transport->peer_connection >= 0) {
    rtcSetUserPointer(transport->peer_connection, nullptr);
    rtcSetLocalDescriptionCallback(transport->peer_connection, nullptr);
    rtcSetLocalCandidateCallback(transport->peer_connection, nullptr);
    rtcSetStateChangeCallback(transport->peer_connection, nullptr);
    rtcSetDataChannelCallback(transport->peer_connection, nullptr);
    rtcSetTrackCallback(transport->peer_connection, nullptr);
    rtcDeletePeerConnection(transport->peer_connection);
    transport->peer_connection = -1;
  }
  wipe_binding(transport);
  delete transport;
}
}
