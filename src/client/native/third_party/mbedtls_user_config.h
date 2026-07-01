#pragma once

/* Required by WebRTC media transport (RFC 5764). Keep the base Mbed TLS
   configuration and append only the feature libdatachannel needs. */
#define MBEDTLS_SSL_DTLS_SRTP
