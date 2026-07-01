#include "native_internal.hpp"

#include <bcrypt.h>

namespace {
struct algorithm_handle {
  BCRYPT_ALG_HANDLE value{};
  ~algorithm_handle() { if (value != nullptr) BCryptCloseAlgorithmProvider(value, 0); }
};

struct key_handle {
  BCRYPT_KEY_HANDLE value{};
  ~key_handle() { if (value != nullptr) BCryptDestroyKey(value); }
};

bool open_ecdsa(algorithm_handle& algorithm) {
  return BCryptOpenAlgorithmProvider(&algorithm.value, BCRYPT_ECDSA_P256_ALGORITHM, nullptr, 0) >= 0;
}

bool import_public_key(const std::array<uint8_t, 65>& public_key, key_handle& key) {
  if (public_key[0] != 0x04) return false;
  algorithm_handle algorithm;
  if (!open_ecdsa(algorithm)) return false;
  std::array<uint8_t, sizeof(BCRYPT_ECCKEY_BLOB) + 64> blob{};
  auto* header = reinterpret_cast<BCRYPT_ECCKEY_BLOB*>(blob.data());
  header->dwMagic = BCRYPT_ECDSA_PUBLIC_P256_MAGIC;
  header->cbKey = 32;
  std::memcpy(blob.data() + sizeof(BCRYPT_ECCKEY_BLOB), public_key.data() + 1, 64);
  return BCryptImportKeyPair(algorithm.value, nullptr, BCRYPT_ECCPUBLIC_BLOB, &key.value,
      blob.data(), static_cast<ULONG>(blob.size()), 0) >= 0;
}

bool import_private_key(const transport_binding_material& material, key_handle& key) {
  if (material.local_public_key[0] != 0x04) return false;
  algorithm_handle algorithm;
  if (!open_ecdsa(algorithm)) return false;
  std::array<uint8_t, sizeof(BCRYPT_ECCKEY_BLOB) + 96> blob{};
  auto* header = reinterpret_cast<BCRYPT_ECCKEY_BLOB*>(blob.data());
  header->dwMagic = BCRYPT_ECDSA_PRIVATE_P256_MAGIC;
  header->cbKey = 32;
  std::memcpy(blob.data() + sizeof(BCRYPT_ECCKEY_BLOB), material.local_public_key.data() + 1, 64);
  std::memcpy(blob.data() + sizeof(BCRYPT_ECCKEY_BLOB) + 64, material.local_private_key.data(), 32);
  const bool imported = BCryptImportKeyPair(algorithm.value, nullptr, BCRYPT_ECCPRIVATE_BLOB, &key.value,
      blob.data(), static_cast<ULONG>(blob.size()), 0) >= 0;
  SecureZeroMemory(blob.data(), blob.size());
  return imported;
}
}

bool generate_p256_key_pair(rs_peer_key_pair_v1* output) {
  if (output == nullptr || output->struct_size < sizeof(rs_peer_key_pair_v1)) return false;
  algorithm_handle algorithm;
  key_handle key;
  if (!open_ecdsa(algorithm) || BCryptGenerateKeyPair(algorithm.value, &key.value, 256, 0) < 0 ||
      BCryptFinalizeKeyPair(key.value, 0) < 0) return false;
  ULONG required = 0;
  if (BCryptExportKey(key.value, nullptr, BCRYPT_ECCPRIVATE_BLOB, nullptr, 0, &required, 0) < 0 ||
      required != sizeof(BCRYPT_ECCKEY_BLOB) + 96) return false;
  std::vector<uint8_t> blob(required);
  if (BCryptExportKey(key.value, nullptr, BCRYPT_ECCPRIVATE_BLOB, blob.data(), required, &required, 0) < 0) return false;
  const auto* header = reinterpret_cast<const BCRYPT_ECCKEY_BLOB*>(blob.data());
  if (header->dwMagic != BCRYPT_ECDSA_PRIVATE_P256_MAGIC || header->cbKey != 32) return false;
  output->public_key_uncompressed_p256[0] = 0x04;
  std::memcpy(output->public_key_uncompressed_p256 + 1, blob.data() + sizeof(BCRYPT_ECCKEY_BLOB), 64);
  std::memcpy(output->private_key_p256, blob.data() + sizeof(BCRYPT_ECCKEY_BLOB) + 64, 32);
  SecureZeroMemory(blob.data(), blob.size());
  return true;
}

bool sha256_bytes(const uint8_t* data, size_t length, std::array<uint8_t, 32>& output) {
  if ((data == nullptr && length != 0) || length > ULONG_MAX) return false;
  algorithm_handle algorithm;
  if (BCryptOpenAlgorithmProvider(&algorithm.value, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0) return false;
  return BCryptHash(algorithm.value, nullptr, 0, const_cast<PUCHAR>(data), static_cast<ULONG>(length),
      output.data(), static_cast<ULONG>(output.size())) >= 0;
}

bool sign_p256_sha256(const transport_binding_material& material, const std::array<uint8_t, 32>& hash,
    std::array<uint8_t, 64>& signature) {
  key_handle key;
  if (!import_private_key(material, key)) return false;
  ULONG written = 0;
  return BCryptSignHash(key.value, nullptr, const_cast<PUCHAR>(hash.data()), static_cast<ULONG>(hash.size()),
      signature.data(), static_cast<ULONG>(signature.size()), &written, 0) >= 0 && written == signature.size();
}

bool verify_p256_sha256(const std::array<uint8_t, 65>& public_key, const std::array<uint8_t, 32>& hash,
    const uint8_t* signature, size_t signature_length) {
  if (signature == nullptr || signature_length != 64) return false;
  key_handle key;
  if (!import_public_key(public_key, key)) return false;
  return BCryptVerifySignature(key.value, nullptr, const_cast<PUCHAR>(hash.data()), static_cast<ULONG>(hash.size()),
      const_cast<PUCHAR>(signature), static_cast<ULONG>(signature_length), 0) >= 0;
}

extern "C" rs_status_v1 RS_CALL rs_runtime_generate_peer_key_pair(
    rs_runtime_handle runtime, rs_peer_key_pair_v1* out_key_pair) {
  if (runtime == nullptr || out_key_pair == nullptr || out_key_pair->struct_size < sizeof(rs_peer_key_pair_v1)) {
    return RS_STATUS_INVALID_ARGUMENT;
  }
  return generate_p256_key_pair(out_key_pair) ? RS_STATUS_OK : RS_STATUS_INTERNAL_ERROR;
}
